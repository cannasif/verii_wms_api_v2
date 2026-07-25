using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Packing.Domain;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Packing.Application;

public sealed record PackingSourceHeaderData(string DocumentNo,long WarehouseId,long? CustomerId,string? CustomerCode);
public sealed record PackingSourceLineData(long Id,int LineNo,long StockId,string StockCode,string? StockName,long? YapCodeId,string? YapCode,string UnitCode,decimal PickedQuantity,decimal PackedQuantity,StockTrackingType TrackingType);

public interface IPackingSourceAdapter
{
    PackingSourceType SourceType { get; }
    Task<PackingSourceHeaderData> GetHeaderAsync(long headerId,long warehouseId,CancellationToken ct);
    Task<IReadOnlyList<PackingSourceLineOption>> GetLinesAsync(long headerId,CancellationToken ct);
    Task<PackingSourceLineData> GetLineAsync(long headerId,long lineId,bool tracking,CancellationToken ct);
    Task ApplyPackedDeltaAsync(long headerId,long lineId,decimal delta,string? lotNo,string? serialNo,string handlingUnitNo,long actor,CancellationToken ct);
}

public sealed class PackingSourceAdapterResolver(IEnumerable<IPackingSourceAdapter> adapters)
{
    public IPackingSourceAdapter Resolve(PackingSourceType sourceType) =>
        adapters.FirstOrDefault(x=>x.SourceType==sourceType)
        ?? throw AppException.BadRequest($"{sourceType} kaynak tipi operasyonel paketleme için desteklenmiyor.");
}

public sealed class WarehouseOutboundPackingSourceAdapter(IUnitOfWork uow) : IPackingSourceAdapter
{
    public PackingSourceType SourceType=>PackingSourceType.WarehouseOutbound;
    private IGenericRepository<WarehouseOutboundHeader> Headers=>uow.Repository<WarehouseOutboundHeader>();
    private IGenericRepository<WarehouseOutboundLine> Lines=>uow.Repository<WarehouseOutboundLine>();

    public async Task<PackingSourceHeaderData> GetHeaderAsync(long id,long warehouseId,CancellationToken ct)
    {
        var x=await Headers.FindByIdAsync(id,false,ct)??throw AppException.NotFound("Ambar çıkış/sevk emri bulunamadı.");
        if(x.SourceWarehouseId!=warehouseId)throw AppException.BadRequest("Kaynak depo paketleme deposuyla aynı olmalıdır.");
        if(x.Status is not (WarehouseOutboundStatus.Picked or WarehouseOutboundStatus.Packing or WarehouseOutboundStatus.Packed))throw AppException.Conflict("Yalnızca toplanmış veya paketleme aşamasındaki emir paketlenebilir.");
        return new(x.DocumentNo,x.SourceWarehouseId,x.CustomerId,x.CustomerCodeSnapshot);
    }
    public async Task<IReadOnlyList<PackingSourceLineOption>> GetLinesAsync(long id,CancellationToken ct)=>await Lines.Query().Where(x=>x.WarehouseOutboundHeaderId==id&&x.PickedQuantity>x.PackedQuantity).OrderBy(x=>x.LineNo).Select(MapOption).ToListAsync(ct);
    public async Task<PackingSourceLineData> GetLineAsync(long headerId,long lineId,bool tracking,CancellationToken ct)
    {
        var q=Lines.Query(tracking).Where(x=>x.Id==lineId&&x.WarehouseOutboundHeaderId==headerId);
        if(tracking)q=q.Include(x=>x.Trackings).Include(x=>x.Header).ThenInclude(x=>x.Lines);
        var x=await q.FirstOrDefaultAsync(ct)??throw AppException.BadRequest("Kaynak ambar çıkış satırı bulunamadı.");
        return Map(x);
    }
    public async Task ApplyPackedDeltaAsync(long headerId,long lineId,decimal delta,string? lot,string? serial,string hu,long actor,CancellationToken ct)
    {
        var x=await Lines.Query(true).Include(l=>l.Trackings).Include(l=>l.Header).ThenInclude(h=>h.Lines).FirstOrDefaultAsync(l=>l.Id==lineId&&l.WarehouseOutboundHeaderId==headerId,ct)??throw AppException.BadRequest("Kaynak ambar çıkış satırı bulunamadı.");
        ValidateDelta(x.PickedQuantity,x.PackedQuantity,x.LoadedQuantity,delta);
        ApplyTrackingDelta(x.TrackingType,x.Trackings,delta,lot,serial,hu);
        x.PackedQuantity+=delta;x.Status=x.PackedQuantity>=x.PickedQuantity?WarehouseOutboundLineStatus.Packed:WarehouseOutboundLineStatus.Picked;x.UpdatedBy=actor;x.UpdatedDate=DateTime.UtcNow;
        x.Header.Status=x.Header.Lines.All(l=>(l.Id==x.Id?x.PackedQuantity:l.PackedQuantity)>=l.PickedQuantity)?WarehouseOutboundStatus.Packed:WarehouseOutboundStatus.Packing;
    }
    private static readonly System.Linq.Expressions.Expression<Func<WarehouseOutboundLine,PackingSourceLineOption>> MapOption=x=>new(x.Id,x.LineNo,x.StockCodeSnapshot,x.StockNameSnapshot,x.YapCodeSnapshot,x.UnitCode,x.PickedQuantity,x.PackedQuantity,x.PickedQuantity-x.PackedQuantity,x.TrackingType.ToString());
    private static PackingSourceLineData Map(WarehouseOutboundLine x)=>new(x.Id,x.LineNo,x.StockId,x.StockCodeSnapshot,x.StockNameSnapshot,x.YapCodeId,x.YapCodeSnapshot,x.UnitCode,x.PickedQuantity,x.PackedQuantity,x.TrackingType);
    private static void ApplyTrackingDelta(StockTrackingType type,ICollection<WarehouseOutboundTracking> rows,decimal delta,string? lot,string? serial,string hu)
    {
        if(type==StockTrackingType.None&&rows.Count==0)return;
        var row=rows.FirstOrDefault(x=>Same(x.LotNo,lot)&&Same(x.SerialNo,serial))??throw AppException.Conflict("Seri/lot, toplama takip kaydıyla eşleşmiyor.");
        ValidateDelta(row.PickedQuantity,row.PackedQuantity,row.LoadedQuantity,delta);
        if(delta>0&&!string.IsNullOrWhiteSpace(row.HandlingUnitNo)&&!Same(row.HandlingUnitNo,hu))throw AppException.Conflict("Seri/lot başka bir pakete bağlı.");
        row.PackedQuantity+=delta;row.HandlingUnitNo=row.PackedQuantity==0?null:hu;row.UpdatedDate=DateTime.UtcNow;
    }
    internal static void ValidateDelta(decimal picked,decimal packed,decimal irreversible,decimal delta)
    {
        var result=packed+delta;
        if(result<irreversible)throw AppException.Conflict("Yüklenmiş veya sevk edilmiş miktar paketten çıkarılamaz.");
        if(result<0||result>picked)throw AppException.Conflict("Paketlenen miktar, sıfır ile toplanan miktar arasında olmalıdır.");
    }
    internal static bool Same(string? a,string? b)=>string.Equals(a?.Trim(),b?.Trim(),StringComparison.OrdinalIgnoreCase);
}

public sealed class ShipmentPackingSourceAdapter(IUnitOfWork uow) : IPackingSourceAdapter
{
    public PackingSourceType SourceType=>PackingSourceType.Shipment;
    private IGenericRepository<ShipmentHeader> Headers=>uow.Repository<ShipmentHeader>();
    private IGenericRepository<ShipmentLine> Lines=>uow.Repository<ShipmentLine>();
    public async Task<PackingSourceHeaderData> GetHeaderAsync(long id,long warehouseId,CancellationToken ct)
    {
        var x=await Headers.FindByIdAsync(id,false,ct)??throw AppException.NotFound("Sevk emri bulunamadı.");
        if(x.SourceWarehouseId!=warehouseId)throw AppException.BadRequest("Sevk deposu paketleme deposuyla aynı olmalıdır.");
        if(x.Status is not (ShipmentStatus.Picked or ShipmentStatus.Packing or ShipmentStatus.Packed))throw AppException.Conflict("Yalnızca toplanmış veya paketleme aşamasındaki sevk paketlenebilir.");
        return new(x.DocumentNo,x.SourceWarehouseId,x.CustomerId,x.CustomerCodeSnapshot);
    }
    public async Task<IReadOnlyList<PackingSourceLineOption>> GetLinesAsync(long id,CancellationToken ct)=>await Lines.Query().Where(x=>x.ShipmentHeaderId==id&&x.PickedQuantity>x.PackedQuantity).OrderBy(x=>x.LineNo).Select(x=>new PackingSourceLineOption(x.Id,x.LineNo,x.StockCodeSnapshot,x.StockNameSnapshot,x.YapCodeSnapshot,x.UnitCode,x.PickedQuantity,x.PackedQuantity,x.PickedQuantity-x.PackedQuantity,x.TrackingType.ToString())).ToListAsync(ct);
    public async Task<PackingSourceLineData> GetLineAsync(long headerId,long lineId,bool tracking,CancellationToken ct)
    {
        var q=Lines.Query(tracking).Where(x=>x.Id==lineId&&x.ShipmentHeaderId==headerId);if(tracking)q=q.Include(x=>x.Trackings).Include(x=>x.Header).ThenInclude(x=>x.Lines);
        var x=await q.FirstOrDefaultAsync(ct)??throw AppException.BadRequest("Kaynak sevk satırı bulunamadı.");
        return new(x.Id,x.LineNo,x.StockId,x.StockCodeSnapshot,x.StockNameSnapshot,x.YapCodeId,x.YapCodeSnapshot,x.UnitCode,x.PickedQuantity,x.PackedQuantity,x.TrackingType);
    }
    public async Task ApplyPackedDeltaAsync(long headerId,long lineId,decimal delta,string? lot,string? serial,string hu,long actor,CancellationToken ct)
    {
        var x=await Lines.Query(true).Include(l=>l.Trackings).Include(l=>l.Header).ThenInclude(h=>h.Lines).FirstOrDefaultAsync(l=>l.Id==lineId&&l.ShipmentHeaderId==headerId,ct)??throw AppException.BadRequest("Kaynak sevk satırı bulunamadı.");
        WarehouseOutboundPackingSourceAdapter.ValidateDelta(x.PickedQuantity,x.PackedQuantity,x.LoadedQuantity,delta);
        if(x.TrackingType!=StockTrackingType.None||x.Trackings.Count>0){var row=x.Trackings.FirstOrDefault(t=>WarehouseOutboundPackingSourceAdapter.Same(t.LotNo,lot)&&WarehouseOutboundPackingSourceAdapter.Same(t.SerialNo,serial))??throw AppException.Conflict("Seri/lot, toplama takip kaydıyla eşleşmiyor.");WarehouseOutboundPackingSourceAdapter.ValidateDelta(row.PickedQuantity,row.PackedQuantity,row.LoadedQuantity,delta);if(delta>0&&!string.IsNullOrWhiteSpace(row.HandlingUnitNo)&&!WarehouseOutboundPackingSourceAdapter.Same(row.HandlingUnitNo,hu))throw AppException.Conflict("Seri/lot başka bir pakete bağlı.");row.PackedQuantity+=delta;row.HandlingUnitNo=row.PackedQuantity==0?null:hu;row.UpdatedDate=DateTime.UtcNow;}
        x.PackedQuantity+=delta;x.Status=x.PackedQuantity>=x.PickedQuantity?ShipmentLineStatus.Packed:ShipmentLineStatus.Picked;x.UpdatedBy=actor;x.UpdatedDate=DateTime.UtcNow;
        x.Header.Status=x.Header.Lines.All(l=>(l.Id==x.Id?x.PackedQuantity:l.PackedQuantity)>=l.PickedQuantity)?ShipmentStatus.Packed:ShipmentStatus.Packing;
    }
}

public sealed class WarehouseTransferPackingSourceAdapter(IUnitOfWork uow) : IPackingSourceAdapter
{
    public PackingSourceType SourceType=>PackingSourceType.WarehouseTransfer;
    private IGenericRepository<WarehouseTransferHeader> Headers=>uow.Repository<WarehouseTransferHeader>();
    private IGenericRepository<WarehouseTransferLine> Lines=>uow.Repository<WarehouseTransferLine>();
    public async Task<PackingSourceHeaderData> GetHeaderAsync(long id,long warehouseId,CancellationToken ct)
    {
        var x=await Headers.FindByIdAsync(id,false,ct)??throw AppException.NotFound("Depolar arası transfer emri bulunamadı.");
        if(x.SourceWarehouseId!=warehouseId)throw AppException.BadRequest("Transfer kaynak deposu paketleme deposuyla aynı olmalıdır.");
        if(x.Status is not (WarehouseTransferStatus.Picked or WarehouseTransferStatus.PartiallyPicked))throw AppException.Conflict("Yalnızca toplama aşaması tamamlanan transfer paketlenebilir.");
        return new(x.DocumentNo,x.SourceWarehouseId,null,null);
    }
    public async Task<IReadOnlyList<PackingSourceLineOption>> GetLinesAsync(long id,CancellationToken ct)=>await Lines.Query().Where(x=>x.WtHeaderId==id&&x.PickedQuantity>x.PackedQuantity).OrderBy(x=>x.LineNo).Select(x=>new PackingSourceLineOption(x.Id,x.LineNo,x.StockCodeSnapshot,x.StockNameSnapshot,x.YapCodeSnapshot,x.UnitCode,x.PickedQuantity,x.PackedQuantity,x.PickedQuantity-x.PackedQuantity,x.TrackingType.ToString())).ToListAsync(ct);
    public async Task<PackingSourceLineData> GetLineAsync(long headerId,long lineId,bool tracking,CancellationToken ct)
    {
        var q=Lines.Query(tracking).Where(x=>x.Id==lineId&&x.WtHeaderId==headerId);if(tracking)q=q.Include(x=>x.Trackings);
        var x=await q.FirstOrDefaultAsync(ct)??throw AppException.BadRequest("Kaynak transfer satırı bulunamadı.");
        return new(x.Id,x.LineNo,x.StockId,x.StockCodeSnapshot,x.StockNameSnapshot,x.YapCodeId,x.YapCodeSnapshot,x.UnitCode,x.PickedQuantity,x.PackedQuantity,x.TrackingType);
    }
    public async Task ApplyPackedDeltaAsync(long headerId,long lineId,decimal delta,string? lot,string? serial,string hu,long actor,CancellationToken ct)
    {
        var x=await Lines.Query(true).Include(l=>l.Trackings).FirstOrDefaultAsync(l=>l.Id==lineId&&l.WtHeaderId==headerId,ct)??throw AppException.BadRequest("Kaynak transfer satırı bulunamadı.");
        WarehouseOutboundPackingSourceAdapter.ValidateDelta(x.PickedQuantity,x.PackedQuantity,x.ShippedQuantity,delta);
        if(x.TrackingType!=StockTrackingType.None||x.Trackings.Count>0){var row=x.Trackings.FirstOrDefault(t=>WarehouseOutboundPackingSourceAdapter.Same(t.LotNo,lot)&&WarehouseOutboundPackingSourceAdapter.Same(t.SerialNo,serial))??throw AppException.Conflict("Seri/lot, transfer takip kaydıyla eşleşmiyor.");WarehouseOutboundPackingSourceAdapter.ValidateDelta(row.PickedQuantity,row.PackedQuantity,row.ShippedQuantity,delta);if(delta>0&&!string.IsNullOrWhiteSpace(row.HandlingUnitNo)&&!WarehouseOutboundPackingSourceAdapter.Same(row.HandlingUnitNo,hu))throw AppException.Conflict("Seri/lot başka bir pakete bağlı.");row.PackedQuantity+=delta;row.HandlingUnitNo=row.PackedQuantity==0?null:hu;row.UpdatedDate=DateTime.UtcNow;}
        x.PackedQuantity+=delta;x.UpdatedBy=actor;x.UpdatedDate=DateTime.UtcNow;
    }
}
