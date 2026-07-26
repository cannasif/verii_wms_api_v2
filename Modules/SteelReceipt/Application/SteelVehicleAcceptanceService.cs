using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Modules.VehicleCheckIn.Application;
using verii_wms_api_v2.Modules.VehicleCheckIn.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Files;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.SteelReceipt.Application;

public sealed class SteelVehicleAcceptanceService(
    IUnitOfWork uow,
    IVehicleCheckInService vehicleCheckIns,
    IVehicleCheckInImageStorage vehicleImageStorage,
    ISteelReceiptAttachmentStorage plateImageStorage,
    IAuditLogWriter audit) : ISteelVehicleAcceptanceService
{
    private IGenericRepository<SteelVehicleAcceptance> Acceptances => uow.Repository<SteelVehicleAcceptance>();
    private IGenericRepository<SteelReceiptPlan> Plans => uow.Repository<SteelReceiptPlan>();
    private IGenericRepository<SteelReceiptPlanLine> Lines => uow.Repository<SteelReceiptPlanLine>();
    private IGenericRepository<VehicleCheckInImage> VehicleImages => uow.Repository<VehicleCheckInImage>();
    private IGenericRepository<SteelReceiptInspectionAttachment> PlateImages => uow.Repository<SteelReceiptInspectionAttachment>();

    public async Task<PagedResponse<SteelVehicleAcceptanceCandidateRow>> GetCandidatesPagedAsync(
        string branchCode,
        PagedRequest request,
        CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        var query =
            from line in Lines.Query()
            join plan in Plans.Query() on line.PlanId equals plan.Id
            join warehouse in uow.Repository<WarehouseEntity>().Query() on line.TargetWarehouseId equals warehouse.Id
            join location in uow.Repository<WarehouseLocation>().Query() on line.ReceivingLocationId equals location.Id
            where line.BranchCode == branch
                  && line.VehicleAcceptanceId == null
                  && line.ArrivalStatus == SteelArrivalStatus.Expected
                  && line.InspectionStatus == SteelInspectionStatus.Pending
                  && line.ConversionStatus == SteelReceiptConversionStatus.NotCreated
                  && plan.Status != SteelReceiptPlanStatus.Cancelled
            select new SteelVehicleAcceptanceCandidateRow(
                line.Id,
                line.PlanId,
                plan.ImportReferenceNo,
                plan.SourceFileName,
                line.LineNo,
                line.DCode,
                line.NetsisOrderNo,
                line.StockCodeSnapshot,
                line.StockNameSnapshot,
                line.SupplierSerialNo,
                line.SecondarySerialNo,
                line.CombinedSize,
                line.MaterialGrade,
                line.HeatNumber,
                line.CertificateNumber,
                line.ExpectedQuantity,
                line.UnitCode,
                line.TargetWarehouseId,
                warehouse.WarehouseCode,
                warehouse.WarehouseName,
                line.ReceivingLocationId,
                location.Code,
                location.Name,
                line.Attachments.Count,
                Convert.ToBase64String(line.RowVersion));

        var search = request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.DCode.Contains(search)
                || x.SupplierSerialNo.Contains(search)
                || (x.SecondarySerialNo != null && x.SecondarySerialNo.Contains(search))
                || x.StockCode.Contains(search)
                || (x.StockName != null && x.StockName.Contains(search))
                || x.ImportReferenceNo.Contains(search)
                || x.SourceFileName.Contains(search)
                || (x.NetsisOrderNo != null && x.NetsisOrderNo.Contains(search)));
        }

        return await query
            .ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(SteelVehicleAcceptanceCandidateRow.LineNo))
            .ToPagedResponseAsync(request, ct);
    }

    public async Task<CompleteSteelVehicleAcceptanceResult?> GetLatestByVehicleAsync(
        long vehicleCheckInId,
        CancellationToken ct = default)
    {
        var acceptance = await Acceptances.Query()
            .Where(x => x.VehicleCheckInId == vehicleCheckInId)
            .OrderByDescending(x => x.AcceptedAtUtc)
            .FirstOrDefaultAsync(ct);
        return acceptance is null ? null : await BuildResultAsync(acceptance, false, ct);
    }

    public async Task<CompleteSteelVehicleAcceptanceResult> CompleteAsync(
        CompleteSteelVehicleAcceptanceRequest request,
        IReadOnlyList<VehicleImageUpload> vehicleImages,
        IReadOnlyList<SteelPlateImageUpload> plateImages,
        long actor,
        CancellationToken ct = default)
    {
        ValidateRequest(request, vehicleImages, plateImages);
        var storedFiles = new List<StoredFile>();

        try
        {
            return await uow.ExecuteInTransactionAsync(async token =>
            {
                var replay = await Acceptances.Query()
                    .FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, token);
                if (replay is not null)
                    return await BuildResultAsync(replay, true, token);

                var plateRequests = request.Plates.ToDictionary(x => x.PlanLineId);
                var lineIds = plateRequests.Keys.ToArray();
                var lines = await Lines.Query(tracking: true)
                    .Include(x => x.Plan)
                    .Include(x => x.Attachments)
                    .Where(x => lineIds.Contains(x.Id))
                    .OrderBy(x => x.Id)
                    .ToListAsync(token);

                if (lines.Count != lineIds.Length)
                    throw AppException.BadRequest("Seçilen SAC levhalarından biri bulunamadı.");

                var branch = NormalizeBranch(request.Vehicle.BranchCode);
                if (lines.Any(x => x.BranchCode != branch || x.Plan.BranchCode != branch))
                    throw AppException.BadRequest("Seçilen SAC levhaları araç girişinin şubesiyle uyuşmuyor.");
                if (lines.Any(x => x.VehicleAcceptanceId.HasValue
                                   || x.ArrivalStatus != SteelArrivalStatus.Expected
                                   || x.InspectionStatus != SteelInspectionStatus.Pending
                                   || x.ConversionStatus != SteelReceiptConversionStatus.NotCreated))
                    throw AppException.Conflict("Seçilen levhalardan biri daha önce kabul edilmiş, kontrol edilmiş veya mal kabule aktarılmış.");

                foreach (var line in lines)
                    ApplyVersion(line.RowVersion, plateRequests[line.Id].RowVersion);

                var supplierIds = lines.Select(x => x.Plan.SupplierId).Distinct().ToArray();
                if (supplierIds.Length != 1)
                    throw AppException.BadRequest("Tek araç kabul işleminde seçilen levhalar aynı tedarikçiye ait olmalıdır.");
                if (request.Vehicle.CustomerId.HasValue && request.Vehicle.CustomerId.Value != supplierIds[0])
                    throw AppException.BadRequest("Araçtaki tedarikçi ile seçilen SAC planının tedarikçisi uyuşmuyor.");

                var vehicleRequest = request.Vehicle with
                {
                    CustomerId = request.Vehicle.CustomerId ?? supplierIds[0],
                    SteelSheetCount = lines.Count
                };
                var vehicleDetail = await vehicleCheckIns.SaveAsync(vehicleRequest, actor, token);
                var vehicle = await uow.Repository<VehicleCheckInHeader>().FindByIdAsync(vehicleDetail.Header.Id, true, token)
                    ?? throw AppException.NotFound("Araç giriş kaydı bulunamadı.");

                if (vehicle.Status is VehicleCheckInStatus.Completed or VehicleCheckInStatus.Cancelled)
                    throw AppException.Conflict("Bu araç girişinin SAC kabul işlemi daha önce tamamlanmış veya iptal edilmiş.");

                var plans = lines.Select(x => x.Plan).DistinctBy(x => x.Id).ToList();
                if (plans.Any(x => x.VehicleCheckInId.HasValue && x.VehicleCheckInId != vehicle.Id))
                    throw AppException.Conflict("Seçilen SAC planlarından biri farklı bir araç girişiyle ilişkilidir.");

                var locationIds = plateRequests.Values.Select(x => x.ReceivingLocationId).Distinct().ToArray();
                var locations = await uow.Repository<WarehouseLocation>().Query()
                    .Where(x => locationIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, token);
                if (locations.Count != locationIds.Length)
                    throw AppException.BadRequest("Seçilen kabul raflarından biri bulunamadı.");

                foreach (var line in lines)
                {
                    var location = locations[plateRequests[line.Id].ReceivingLocationId];
                    if (!location.IsActive
                        || location.IsQuarantine
                        || location.WarehouseId != line.TargetWarehouseId
                        || location.LocationType is LocationTypes.Virtual or LocationTypes.Shipping)
                        throw AppException.BadRequest($"{line.DCode} için seçilen kabul rafı uygun değil.");
                }

                var imagesByLine = plateImages.GroupBy(x => x.PlanLineId)
                    .ToDictionary(x => x.Key, x => x.ToList());
                foreach (var line in lines)
                {
                    var existingImageCount = line.Attachments.Count(x => !x.IsDeleted);
                    var newImageCount = imagesByLine.GetValueOrDefault(line.Id)?.Count ?? 0;
                    if (existingImageCount + newImageCount == 0)
                        throw AppException.BadRequest($"{line.DCode} levhası için en az bir görsel zorunludur.");
                }

                var existingVehicleImageCount = await VehicleImages.CountAsync(x => x.HeaderId == vehicle.Id, token);
                if (existingVehicleImageCount + vehicleImages.Count == 0)
                    throw AppException.BadRequest("Araç kabulü için en az bir araç görseli zorunludur.");

                var acceptance = new SteelVehicleAcceptance
                {
                    BranchCode = branch,
                    IdempotencyKey = request.IdempotencyKey,
                    VehicleCheckInId = vehicle.Id,
                    PlateCount = lines.Count,
                    TotalAcceptedQuantity = lines.Sum(x => x.ExpectedQuantity),
                    Status = SteelVehicleAcceptanceStatus.Completed,
                    AcceptedAtUtc = DateTimeOffset.UtcNow,
                    AcceptedBy = actor,
                    Note = Clean(request.Note, 1000),
                    CreatedBy = actor,
                    CreatedDate = DateTime.UtcNow
                };
                await Acceptances.AddAsync(acceptance, token);
                await uow.SaveChangesAsync(token);

                var nextVehicleImageOrder =
                    (await VehicleImages.Query().Where(x => x.HeaderId == vehicle.Id)
                        .MaxAsync(x => (int?)x.SortOrder, token) ?? 0) + 1;
                foreach (var upload in vehicleImages)
                {
                    var path = await vehicleImageStorage.SaveAsync(vehicle.Id, upload, token);
                    storedFiles.Add(new(vehicleImageStorage.Delete, path));
                    await VehicleImages.AddAsync(new VehicleCheckInImage
                    {
                        BranchCode = branch,
                        HeaderId = vehicle.Id,
                        FileName = PrivateUploadFileName.ForDisplay(upload.FileName),
                        ContentType = upload.ContentType,
                        StoragePath = path,
                        FileSize = upload.Length,
                        SortOrder = nextVehicleImageOrder++,
                        CreatedBy = actor,
                        CreatedDate = DateTime.UtcNow
                    }, token);
                }

                foreach (var image in plateImages)
                {
                    var line = lines.First(x => x.Id == image.PlanLineId);
                    var upload = new SteelReceiptAttachmentUpload(
                        image.Content,
                        image.FileName,
                        image.ContentType,
                        image.Length);
                    var path = await plateImageStorage.SaveAsync(line.Id, upload, token);
                    storedFiles.Add(new(plateImageStorage.Delete, path));
                    await PlateImages.AddAsync(new SteelReceiptInspectionAttachment
                    {
                        BranchCode = branch,
                        PlanLineId = line.Id,
                        FileName = PrivateUploadFileName.ForDisplay(image.FileName),
                        ContentType = image.ContentType,
                        StoragePath = path,
                        Caption = $"Araç kabul kanıtı · {vehicle.PlateNo}",
                        FileSize = image.Length,
                        CreatedBy = actor,
                        CreatedDate = DateTime.UtcNow
                    }, token);
                }

                var acceptedAt = acceptance.AcceptedAtUtc;
                foreach (var line in lines)
                {
                    var plate = plateRequests[line.Id];
                    line.VehicleAcceptanceId = acceptance.Id;
                    line.ReceivingLocationId = plate.ReceivingLocationId;
                    line.ArrivedQuantity = line.ExpectedQuantity;
                    line.ApprovedQuantity = line.ExpectedQuantity;
                    line.RejectedQuantity = 0;
                    line.ArrivalStatus = SteelArrivalStatus.Arrived;
                    line.InspectionStatus = SteelInspectionStatus.Approved;
                    line.RejectReason = null;
                    line.InspectionNote = Clean(plate.Note, 1000);
                    line.InspectedBy = actor;
                    line.InspectedAtUtc = acceptedAt;
                    line.UpdatedBy = actor;
                    line.UpdatedDate = DateTime.UtcNow;
                }

                foreach (var plan in plans)
                {
                    plan.VehicleCheckInId = vehicle.Id;
                    plan.UpdatedBy = actor;
                    plan.UpdatedDate = DateTime.UtcNow;
                }

                vehicle.Status = VehicleCheckInStatus.Completed;
                vehicle.UpdatedBy = actor;
                vehicle.UpdatedDate = DateTime.UtcNow;
                await uow.SaveChangesAsync(token);

                foreach (var plan in plans)
                    await RefreshPlanAsync(plan, token);

                await audit.WriteAsync(new(
                    "steel-receipt.vehicle-acceptance.complete",
                    nameof(SteelVehicleAcceptance),
                    acceptance.Id.ToString(),
                    "Succeeded",
                    "steel-receipt",
                    NewValues: new
                    {
                        acceptance.VehicleCheckInId,
                        vehicle.PlateNo,
                        acceptance.PlateCount,
                        acceptance.TotalAcceptedQuantity,
                        LineIds = lineIds,
                        PlanIds = plans.Select(x => x.Id).ToArray()
                    },
                    ChangedFields: ["Vehicle", "VehicleImages", "SteelLines", "PlateImages", "ReceivingLocations"]),
                    token);

                return await BuildResultAsync(acceptance, false, token);
            }, ct, IsolationLevel.Serializable);
        }
        catch (Exception exception)
        {
            for (var index = storedFiles.Count - 1; index >= 0; index--)
            {
                try { storedFiles[index].Delete(storedFiles[index].Path); }
                catch { /* Database rollback is authoritative; orphan cleanup can be retried operationally. */ }
            }
            if (exception is DbUpdateConcurrencyException)
                throw AppException.Conflict("Araç veya SAC levhalarından biri başka bir kullanıcı tarafından değiştirildi. Aday listesini yenileyip tekrar deneyin.");
            throw;
        }
    }

    private async Task<CompleteSteelVehicleAcceptanceResult> BuildResultAsync(
        SteelVehicleAcceptance acceptance,
        bool replayed,
        CancellationToken ct)
    {
        var vehicle = await vehicleCheckIns.GetAsync(acceptance.VehicleCheckInId, ct);
        var plates = await (
            from line in Lines.Query()
            join plan in Plans.Query() on line.PlanId equals plan.Id
            where line.VehicleAcceptanceId == acceptance.Id
            orderby line.LineNo
            select new AcceptedSteelPlateRow(
                line.Id,
                line.PlanId,
                plan.ImportReferenceNo,
                line.DCode,
                line.StockCodeSnapshot,
                line.SupplierSerialNo,
                line.ApprovedQuantity,
                line.UnitCode,
                line.ReceivingLocationId,
                line.InspectedAtUtc ?? acceptance.AcceptedAtUtc))
            .ToListAsync(ct);
        return new(acceptance.Id, replayed, vehicle, plates);
    }

    private async Task RefreshPlanAsync(SteelReceiptPlan plan, CancellationToken ct)
    {
        var states = await Lines.Query()
            .Where(x => x.PlanId == plan.Id)
            .Select(x => new { x.InspectionStatus, x.ConversionStatus })
            .ToListAsync(ct);
        plan.Status = states.All(x => x.ConversionStatus == SteelReceiptConversionStatus.Created)
            ? SteelReceiptPlanStatus.Converted
            : states.Any(x => x.ConversionStatus == SteelReceiptConversionStatus.Created)
                ? SteelReceiptPlanStatus.PartiallyConverted
                : states.Any(x => x.InspectionStatus is SteelInspectionStatus.Approved or SteelInspectionStatus.PartiallyApproved)
                    ? SteelReceiptPlanStatus.ReadyForReceipt
                    : states.Any(x => x.InspectionStatus != SteelInspectionStatus.Pending)
                        ? SteelReceiptPlanStatus.InspectionInProgress
                        : SteelReceiptPlanStatus.Imported;
        plan.UpdatedDate = DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
    }

    private static void ValidateRequest(
        CompleteSteelVehicleAcceptanceRequest request,
        IReadOnlyList<VehicleImageUpload> vehicleImages,
        IReadOnlyList<SteelPlateImageUpload> plateImages)
    {
        if (request.IdempotencyKey == Guid.Empty)
            throw AppException.BadRequest("Idempotency anahtarı zorunludur.");
        if (request.Plates.Count is < 1 or > 50)
            throw AppException.BadRequest("Tek araç kabulünde 1-50 SAC levhası seçilebilir.");
        if (request.Plates.Select(x => x.PlanLineId).Distinct().Count() != request.Plates.Count)
            throw AppException.BadRequest("Aynı SAC levhası birden fazla kez seçilemez.");
        if (request.Vehicle.SteelSheetCount != request.Plates.Count)
            throw AppException.BadRequest("Sac levha adedi ile seçilen levha sayısı aynı olmalıdır.");
        if (vehicleImages.Count > 10)
            throw AppException.BadRequest("Bir araç kabulünde en fazla 10 araç görseli yüklenebilir.");
        if (plateImages.Count > 100)
            throw AppException.BadRequest("Bir araç kabulünde en fazla 100 levha görseli yüklenebilir.");
        if (vehicleImages.Sum(x => x.Length) + plateImages.Sum(x => x.Length) > 120_000_000)
            throw AppException.BadRequest("Araç kabulündeki toplam görsel boyutu 120 MB sınırını aşamaz.");
        if (plateImages.Any(x => !request.Plates.Any(p => p.PlanLineId == x.PlanLineId)))
            throw AppException.BadRequest("Görseli gönderilen SAC levhası kabul listesinde bulunmuyor.");
        if (plateImages.GroupBy(x => x.PlanLineId).Any(x => x.Count() > 5))
            throw AppException.BadRequest("Bir SAC levhasına en fazla 5 görsel eklenebilir.");
    }

    private static void ApplyVersion(byte[] current, string supplied)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(supplied); }
        catch { throw AppException.BadRequest("Geçersiz eşzamanlılık bilgisi."); }
        if (!current.SequenceEqual(expected))
            throw AppException.Conflict("SAC levhası başka bir kullanıcı tarafından değiştirildi. Aday listesini yenileyin.");
    }

    private static string NormalizeBranch(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();

    private static string? Clean(string? value, int max)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return normalized.Length <= max ? normalized : normalized[..max];
    }

    private sealed record StoredFile(Action<string> Delete, string Path);
}
