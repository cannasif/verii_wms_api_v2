using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Packing.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Packing.Application;

public sealed record PackingPrintPayload(long JobId,long HandlingUnitId,string HandlingUnitNo,string? Sscc,long? PrinterDefinitionId,int Copies);
public sealed record PackingScaleGatewayResult(decimal GrossWeight,bool IsStable,string? RawPayload);

public interface IPackingDeviceGateway
{
    Task PrintAsync(PackingPrintPayload payload,CancellationToken ct);
    Task<PackingScaleGatewayResult> ReadScaleAsync(string deviceCode,CancellationToken ct);
}

public sealed class HttpPackingDeviceGateway(IHttpClientFactory clients,IConfiguration configuration) : IPackingDeviceGateway
{
    public async Task PrintAsync(PackingPrintPayload payload,CancellationToken ct)
    {
        var endpoint=configuration["PackingDevices:PrinterBridgeUrl"];
        if(string.IsNullOrWhiteSpace(endpoint))throw new AppException(StatusCodes.Status503ServiceUnavailable,"Yazıcı köprüsü yapılandırılmamış. PackingDevices:PrinterBridgeUrl secret değerini tanımlayın.");
        using var response=await clients.CreateClient(nameof(HttpPackingDeviceGateway)).PostAsJsonAsync(endpoint,payload,ct);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException($"Yazıcı köprüsü HTTP {(int)response.StatusCode} döndürdü.");
    }
    public async Task<PackingScaleGatewayResult> ReadScaleAsync(string deviceCode,CancellationToken ct)
    {
        var endpoint=configuration["PackingDevices:ScaleBridgeUrl"];
        if(string.IsNullOrWhiteSpace(endpoint))throw new AppException(StatusCodes.Status503ServiceUnavailable,"Terazi köprüsü yapılandırılmamış. PackingDevices:ScaleBridgeUrl secret değerini tanımlayın.");
        var separator=endpoint.Contains('?')?'&':'?';
        using var response=await clients.CreateClient(nameof(HttpPackingDeviceGateway)).GetAsync($"{endpoint}{separator}deviceCode={Uri.EscapeDataString(deviceCode)}",ct);
        if(!response.IsSuccessStatusCode)throw new AppException(StatusCodes.Status503ServiceUnavailable,$"Terazi köprüsüne ulaşılamadı (HTTP {(int)response.StatusCode}).");
        var raw=await response.Content.ReadAsStringAsync(ct);
        var value=JsonSerializer.Deserialize<ScaleBridgeResponse>(raw,new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new InvalidOperationException("Terazi yanıtı okunamadı.");
        if(value.GrossWeight<=0)throw new InvalidOperationException("Terazi sıfır veya negatif değer döndürdü.");
        return new(value.GrossWeight,value.IsStable,raw.Length>2000?raw[..2000]:raw);
    }
    private sealed record ScaleBridgeResponse(decimal GrossWeight,bool IsStable);
}

public interface IPackingDeviceService
{
    Task<PackingPrintJobRow> EnqueueAsync(long handlingUnitId,Guid idempotencyKey,int copies,long actor,CancellationToken ct);
    Task<PagedResponse<PackingPrintJobRow>> GetJobsAsync(PagedRequest request,CancellationToken ct);
    Task<ScaleReadingDto> ReadScaleAsync(long handlingUnitId,Guid idempotencyKey,long actor,CancellationToken ct);
}

public sealed class PackingDeviceService(IUnitOfWork uow,IPackingDeviceGateway gateway) : IPackingDeviceService
{
    private IGenericRepository<PackingPrintJob> Jobs=>uow.Repository<PackingPrintJob>();
    private IGenericRepository<PackingScaleReading> Readings=>uow.Repository<PackingScaleReading>();
    private IGenericRepository<HandlingUnit> Units=>uow.Repository<HandlingUnit>();
    private IGenericRepository<PackingStation> Stations=>uow.Repository<PackingStation>();

    public async Task<PackingPrintJobRow> EnqueueAsync(long handlingUnitId,Guid key,int copies,long actor,CancellationToken ct)
    {
        if(key==Guid.Empty)throw AppException.BadRequest("IdempotencyKey zorunludur.");
        if(copies is <1 or >20)throw AppException.BadRequest("Kopya sayısı 1-20 arasında olmalıdır.");
        var replay=await Jobs.FirstOrDefaultAsync(x=>x.IdempotencyKey==key,false,ct);if(replay is not null)return Map(replay);
        var unit=await Units.Query().Include(x=>x.Session).FirstOrDefaultAsync(x=>x.Id==handlingUnitId,ct)??throw AppException.NotFound("Paket bulunamadı.");
        if(unit.Status==HandlingUnitStatus.Open)throw AppException.Conflict("Etiket yalnızca kapatılmış veya serbest bırakılmış paket için basılabilir.");
        var station=await Stations.FindByIdAsync(unit.Session.PackingStationId,false,ct)??throw AppException.NotFound("Paketleme istasyonu bulunamadı.");
        var job=new PackingPrintJob{BranchCode=unit.BranchCode,HandlingUnitId=unit.Id,PackingStationId=station.Id,PrinterDefinitionId=station.PrinterDefinitionId,Copies=copies,IdempotencyKey=key,RequestedAtUtc=DateTimeOffset.UtcNow,CreatedBy=actor};
        await Jobs.AddAsync(job,ct);await uow.SaveChangesAsync(ct);
        job.PayloadJson=JsonSerializer.Serialize(new PackingPrintPayload(job.Id,unit.Id,unit.HandlingUnitNo,unit.Sscc,station.PrinterDefinitionId,copies));
        await uow.SaveChangesAsync(ct);return Map(job);
    }
    public Task<PagedResponse<PackingPrintJobRow>> GetJobsAsync(PagedRequest request,CancellationToken ct)
    {
        var search=request.LegacySearch?.Trim();
        var q=Jobs.Query().Where(x=>string.IsNullOrWhiteSpace(search)||(x.LastError!=null&&x.LastError.Contains(search)))
            .Select(x=>new PackingPrintJobRow(x.Id,x.HandlingUnitId,x.PackingStationId,x.PrinterDefinitionId,x.Status,x.Copies,x.AttemptCount,x.RequestedAtUtc,x.CompletedAtUtc,x.LastError))
            .ApplyAdvancedFilters(request).ApplySort(request,nameof(PackingPrintJobRow.RequestedAtUtc));
        return q.ToPagedResponseAsync(request,ct);
    }
    public async Task<ScaleReadingDto> ReadScaleAsync(long handlingUnitId,Guid key,long actor,CancellationToken ct)
    {
        if(key==Guid.Empty)throw AppException.BadRequest("IdempotencyKey zorunludur.");
        var prior=await Readings.FirstOrDefaultAsync(x=>x.IdempotencyKey==key,false,ct);
        if(prior is not null)return Map(prior);
        var unit=await Units.Query().Include(x=>x.Session).FirstOrDefaultAsync(x=>x.Id==handlingUnitId,ct)??throw AppException.NotFound("Paket bulunamadı.");
        var station=await Stations.FindByIdAsync(unit.Session.PackingStationId,false,ct)??throw AppException.NotFound("Paketleme istasyonu bulunamadı.");
        if(string.IsNullOrWhiteSpace(station.ScaleDeviceCode))throw AppException.Conflict("İstasyona terazi cihaz kodu tanımlanmamış.");
        var result=await gateway.ReadScaleAsync(station.ScaleDeviceCode,ct);
        if(!result.IsStable)throw AppException.Conflict("Terazi değeri stabil değil; yük sabitlendikten sonra yeniden deneyin.");
        var row=new PackingScaleReading{BranchCode=unit.BranchCode,PackingStationId=station.Id,HandlingUnitId=unit.Id,DeviceCode=station.ScaleDeviceCode,GrossWeight=result.GrossWeight,IsStable=true,IdempotencyKey=key,CapturedAtUtc=DateTimeOffset.UtcNow,RawPayload=result.RawPayload,CreatedBy=actor};
        await Readings.AddAsync(row,ct);await uow.SaveChangesAsync(ct);return Map(row);
    }
    private static PackingPrintJobRow Map(PackingPrintJob x)=>new(x.Id,x.HandlingUnitId,x.PackingStationId,x.PrinterDefinitionId,x.Status,x.Copies,x.AttemptCount,x.RequestedAtUtc,x.CompletedAtUtc,x.LastError);
    private static ScaleReadingDto Map(PackingScaleReading x)=>new(x.Id,x.PackingStationId,x.HandlingUnitId,x.DeviceCode,x.GrossWeight,x.IsStable,x.CapturedAtUtc);
}

public interface IPackingPrintQueueJobRunner { Task DispatchPendingAsync(CancellationToken ct); }
public sealed class PackingPrintQueueJobRunner(IUnitOfWork uow,IPackingDeviceGateway gateway,ILogger<PackingPrintQueueJobRunner> logger) : IPackingPrintQueueJobRunner
{
    public async Task DispatchPendingAsync(CancellationToken ct)
    {
        var jobs=await uow.Repository<PackingPrintJob>().Query(true)
            .Where(x=>(x.Status==PackingPrintJobStatus.Pending||x.Status==PackingPrintJobStatus.Failed)&&x.AttemptCount<5&&(!x.NextAttemptAtUtc.HasValue||x.NextAttemptAtUtc<=DateTimeOffset.UtcNow))
            .OrderBy(x=>x.RequestedAtUtc).Take(20).ToListAsync(ct);
        foreach(var job in jobs)
        {
            try
            {
                job.Status=PackingPrintJobStatus.Processing;job.ProcessingStartedAtUtc=DateTimeOffset.UtcNow;job.AttemptCount++;await uow.SaveChangesAsync(ct);
                var payload=JsonSerializer.Deserialize<PackingPrintPayload>(job.PayloadJson)??throw new InvalidOperationException("Yazdırma iş yükü geçersiz.");
                await gateway.PrintAsync(payload,ct);
                job.Status=PackingPrintJobStatus.Completed;job.CompletedAtUtc=DateTimeOffset.UtcNow;job.NextAttemptAtUtc=null;job.LastError=null;
            }
            catch(Exception ex) when(ex is not OperationCanceledException)
            {
                job.Status=PackingPrintJobStatus.Failed;job.LastError=ex.Message.Length>2000?ex.Message[..2000]:ex.Message;job.NextAttemptAtUtc=DateTimeOffset.UtcNow.AddMinutes(Math.Pow(2,job.AttemptCount));logger.LogError(ex,"Packing print job {JobId} failed.",job.Id);
            }
            await uow.SaveChangesAsync(ct);
        }
    }
}
