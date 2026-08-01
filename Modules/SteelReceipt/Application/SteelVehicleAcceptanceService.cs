using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Application;
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
    IGoodsReceiptPolicyService receiptPolicyService,
    IAuditLogWriter audit) : ISteelVehicleAcceptanceService
{
    private IGenericRepository<SteelVehicleAcceptance> Acceptances => uow.Repository<SteelVehicleAcceptance>();
    private IGenericRepository<SteelVehicleAcceptedPlate> AcceptedPlates => uow.Repository<SteelVehicleAcceptedPlate>();
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
        bool canManageVehicleAcceptance,
        CancellationToken ct = default)
    {
        var acceptance = await Acceptances.Query()
            .Where(x => x.VehicleCheckInId == vehicleCheckInId)
            .OrderByDescending(x => x.AcceptedAtUtc)
            .FirstOrDefaultAsync(ct);
        return acceptance is null
            ? null
            : await BuildResultAsync(acceptance, false, canManageVehicleAcceptance, ct);
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
                    return await BuildResultAsync(replay, true, canManageVehicleAcceptance: true, token);

                var knownSlots = request.Slots
                    .Where(x => x.IdentityStatus == SteelPlateIdentityStatus.Known)
                    .ToList();
                var plateRequests = knownSlots.ToDictionary(x => x.PlanLineId!.Value);
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
                var receiptPolicy = await receiptPolicyService.GetAsync(branch, token);
                if (HasSelectedPlateConflict(lines))
                    throw AppException.Conflict("Seçilen levhalardan biri daha önce kabul edilmiş, kontrol edilmiş veya mal kabule aktarılmış.");

                foreach (var line in lines)
                    ApplyVersion(line.RowVersion, plateRequests[line.Id].RowVersion!);

                await UserWarehouseAccessService.EnsureAsync(
                    uow, actor, branch, lines.Select(x => x.TargetWarehouseId), token);

                var supplierIds = lines.Select(x => x.Plan.SupplierId).Distinct().ToArray();
                if (supplierIds.Length > 1)
                    throw AppException.BadRequest("Tek araç kabul işleminde seçilen levhalar aynı tedarikçiye ait olmalıdır.");
                var existingVehicleCustomerId = request.Vehicle.Id.HasValue
                    ? await uow.Repository<VehicleCheckInHeader>().Query()
                        .Where(x => x.Id == request.Vehicle.Id.Value)
                        .Select(x => x.CustomerId)
                        .FirstOrDefaultAsync(token)
                    : null;
                var effectiveCustomerId = request.Vehicle.CustomerId ?? existingVehicleCustomerId;
                if (supplierIds.Length == 1 && effectiveCustomerId.HasValue && effectiveCustomerId.Value != supplierIds[0])
                    throw AppException.BadRequest("Araçtaki tedarikçi ile seçilen SAC planının tedarikçisi uyuşmuyor.");

                var vehicleRequest = request.Vehicle with
                {
                    CustomerId = effectiveCustomerId
                        ?? (supplierIds.Length == 1 ? supplierIds[0] : null)
                };
                var vehicleDetail = await vehicleCheckIns.SaveAsync(vehicleRequest, actor, token);
                var vehicle = await uow.Repository<VehicleCheckInHeader>().FindByIdAsync(vehicleDetail.Header.Id, true, token)
                    ?? throw AppException.NotFound("Araç giriş kaydı bulunamadı.");

                if (vehicle.Status == VehicleCheckInStatus.Cancelled)
                    throw AppException.Conflict("İptal edilmiş araç girişine SAC levhası eklenemez.");

                var existingSlotCount = await AcceptedPlates.CountAsync(
                    x => x.VehicleCheckInId == vehicle.Id, token);
                EnsureTargetSlotCount(
                    existingSlotCount, request.Slots.Count, vehicle.SteelSheetCount);

                var plans = lines.Select(x => x.Plan).DistinctBy(x => x.Id).ToList();

                var overrideLocationIds = plateRequests.Values
                    .Where(x => x.ReceivingLocationId is > 0)
                    .Select(x => x.ReceivingLocationId!.Value)
                    .Distinct()
                    .ToArray();
                if (overrideLocationIds.Length > 0)
                {
                    var locations = await uow.Repository<WarehouseLocation>().Query()
                        .Where(x => overrideLocationIds.Contains(x.Id))
                        .ToDictionaryAsync(x => x.Id, token);
                    if (locations.Count != overrideLocationIds.Length)
                        throw AppException.BadRequest("Seçilen kabul raflarından biri bulunamadı.");

                    foreach (var line in lines)
                    {
                        var overrideLocationId = plateRequests[line.Id].ReceivingLocationId;
                        if (overrideLocationId is not > 0)
                            continue;

                        var location = locations[overrideLocationId.Value];
                        var locationPolicy = GoodsReceiptLocationPolicy.ResolveSelectionPolicy(
                            receiptPolicy.BlockPutawayUntilQualityDecision);
                        if (!GoodsReceiptLocationPolicy.IsAllowedForReceiptLine(
                                locationPolicy,
                                location,
                                line.TargetWarehouseId,
                                requiresQuality: false,
                                receiptPolicy.BlockPutawayUntilQualityDecision)
                            || location.IsQuarantine
                            || location.LocationType is LocationTypes.Virtual or LocationTypes.Shipping)
                            throw AppException.BadRequest(
                                $"{line.DCode}: {GoodsReceiptOperationsService.LocationPolicyError(GoodsReceiptLocationSelectionPolicy.AnyActiveWarehouseLocation)}");
                    }
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
                    PlateCount = request.Slots.Count,
                    TotalAcceptedQuantity = lines.Sum(x => x.ExpectedQuantity),
                    Status = ResolveAcceptanceStatus(
                        request.Slots.Any(x => x.IdentityStatus == SteelPlateIdentityStatus.Unknown)),
                    AcceptedAtUtc = DateTimeOffset.UtcNow,
                    AcceptedBy = actor,
                    Note = Clean(request.Note, 1000),
                    CreatedBy = actor,
                    CreatedDate = DateTime.UtcNow
                };
                await Acceptances.AddAsync(acceptance, token);
                await uow.SaveChangesAsync(token);

                for (var index = 0; index < request.Slots.Count; index++)
                {
                    var slot = request.Slots[index];
                    await AcceptedPlates.AddAsync(new SteelVehicleAcceptedPlate
                    {
                        BranchCode = branch,
                        VehicleCheckInId = vehicle.Id,
                        VehicleAcceptanceId = acceptance.Id,
                        SequenceNo = index + 1,
                        IdentityStatus = slot.IdentityStatus,
                        PlanLineId = slot.PlanLineId,
                        CreatedBy = actor,
                        CreatedDate = DateTime.UtcNow
                    }, token);
                }

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
                    if (plate.ReceivingLocationId is > 0)
                        line.ReceivingLocationId = plate.ReceivingLocationId.Value;
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

                var newUnknownCount = request.Slots.Count(
                    x => x.IdentityStatus == SteelPlateIdentityStatus.Unknown);
                var existingUnknownCount = await AcceptedPlates.Query()
                    .CountAsync(x => x.VehicleCheckInId == vehicle.Id
                        && x.IdentityStatus == SteelPlateIdentityStatus.Unknown, token);
                var vehicleUnknownCount = existingUnknownCount + newUnknownCount;
                vehicle.Status = vehicleUnknownCount > 0
                    ? VehicleCheckInStatus.ContainsUnknownPlates
                    : VehicleCheckInStatus.Completed;
                vehicle.UpdatedBy = actor;
                vehicle.UpdatedDate = DateTime.UtcNow;

                foreach (var plan in plans)
                {
                    plan.VehicleCheckInId = vehicle.Id;
                    plan.UpdatedBy = actor;
                }

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
                        UnknownCount = vehicleUnknownCount,
                        LineIds = lineIds,
                        PlanIds = plans.Select(x => x.Id).ToArray()
                    },
                    ChangedFields: ["Vehicle", "VehicleImages", "SteelLines", "AcceptedPlates", "PlateImages", "ReceivingLocations"]),
                    token);

                return await BuildResultAsync(
                    acceptance, false, canManageVehicleAcceptance: true, token);
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

    public async Task<AcceptedSteelPlateRow> ResolveUnknownPlateAsync(
        long acceptedPlateId,
        ResolveUnknownPlateRequest request,
        IReadOnlyList<SteelPlateImageUpload> plateImages,
        long actor,
        CancellationToken ct = default)
    {
        if (request.PlanLineId <= 0)
            throw AppException.BadRequest("Eşleştirilecek SAC satırı zorunludur.");
        if (plateImages.Count is < 1 or > 5)
            throw AppException.BadRequest("Bilinmeyen levhayı eşleştirmek için 1-5 levha görseli zorunludur.");
        if (plateImages.Any(x => x.PlanLineId != request.PlanLineId))
            throw AppException.BadRequest("Levha görseli seçilen SAC satırıyla uyuşmuyor.");
        if (plateImages.Any(x => x.Length is <= 0 or > 8_388_608))
            throw AppException.BadRequest("Her levha görseli dolu ve en fazla 8 MB olmalıdır.");
        if (plateImages.Any(x => (x.ContentType ?? string.Empty).ToLowerInvariant() is not
                ("image/jpeg" or "image/png" or "image/webp")))
            throw AppException.BadRequest("Levha görseli yalnızca JPG, PNG veya WEBP formatında olabilir.");

        var storedFiles = new List<StoredFile>();
        try
        {
            return await uow.ExecuteInTransactionAsync(async token =>
            {
                var acceptedPlate = await AcceptedPlates.Query(tracking: true)
                    .Include(x => x.VehicleAcceptance)
                    .FirstOrDefaultAsync(x => x.Id == acceptedPlateId, token)
                    ?? throw AppException.NotFound("Bilinmeyen levha slotu bulunamadı.");
                if (acceptedPlate.IdentityStatus != SteelPlateIdentityStatus.Unknown || acceptedPlate.PlanLineId.HasValue)
                    throw AppException.Conflict("Bu levha slotu daha önce eşleştirilmiş.");
                ApplyVersion(acceptedPlate.RowVersion, request.RowVersion);

                var acceptance = acceptedPlate.VehicleAcceptance;

                var line = await Lines.Query(tracking: true)
                    .Include(x => x.Plan)
                    .FirstOrDefaultAsync(x => x.Id == request.PlanLineId, token)
                    ?? throw AppException.NotFound("Eşleştirilecek SAC levhası bulunamadı.");
                if (line.BranchCode != acceptance.BranchCode || line.Plan.BranchCode != acceptance.BranchCode)
                    throw AppException.BadRequest("Seçilen SAC levhası araç kabulünün şubesiyle uyuşmuyor.");
                if (HasSelectedPlateConflict([line]))
                    throw AppException.Conflict("Seçilen levha daha önce kabul edilmiş, kontrol edilmiş veya mal kabule aktarılmış.");
                ApplyVersion(line.RowVersion, request.PlanLineRowVersion);

                var vehicle = await uow.Repository<VehicleCheckInHeader>().FindByIdAsync(
                    acceptance.VehicleCheckInId, true, token)
                    ?? throw AppException.NotFound("Araç giriş kaydı bulunamadı.");
                if (vehicle.CustomerId.HasValue && vehicle.CustomerId.Value != line.Plan.SupplierId)
                    throw AppException.BadRequest("Araçtaki tedarikçi ile seçilen SAC planının tedarikçisi uyuşmuyor.");
                if (!vehicle.CustomerId.HasValue)
                {
                    vehicle.CustomerId = line.Plan.SupplierId;
                    vehicle.CustomerCodeSnapshot = line.Plan.SupplierCodeSnapshot;
                    vehicle.CustomerNameSnapshot = line.Plan.SupplierNameSnapshot;
                }

                await UserWarehouseAccessService.EnsureAsync(
                    uow, actor, acceptance.BranchCode, [line.TargetWarehouseId], token);

                if (request.ReceivingLocationId is > 0)
                {
                    var location = await uow.Repository<WarehouseLocation>().FindByIdAsync(
                        request.ReceivingLocationId.Value, false, token)
                        ?? throw AppException.BadRequest("Seçilen kabul rafı bulunamadı.");
                    var receiptPolicy = await receiptPolicyService.GetAsync(acceptance.BranchCode, token);
                    var locationPolicy = GoodsReceiptLocationPolicy.ResolveSelectionPolicy(
                        receiptPolicy.BlockPutawayUntilQualityDecision);
                    if (!GoodsReceiptLocationPolicy.IsAllowedForReceiptLine(
                            locationPolicy,
                            location,
                            line.TargetWarehouseId,
                            requiresQuality: false,
                            receiptPolicy.BlockPutawayUntilQualityDecision)
                        || location.IsQuarantine
                        || location.LocationType is LocationTypes.Virtual or LocationTypes.Shipping)
                        throw AppException.BadRequest(
                            $"{line.DCode}: {GoodsReceiptOperationsService.LocationPolicyError(GoodsReceiptLocationSelectionPolicy.AnyActiveWarehouseLocation)}");
                    line.ReceivingLocationId = location.Id;
                }

                var now = DateTimeOffset.UtcNow;
                foreach (var image in plateImages)
                {
                    var upload = new SteelReceiptAttachmentUpload(
                        image.Content,
                        image.FileName,
                        image.ContentType,
                        image.Length);
                    var path = await plateImageStorage.SaveAsync(line.Id, upload, token);
                    storedFiles.Add(new(plateImageStorage.Delete, path));
                    await PlateImages.AddAsync(new SteelReceiptInspectionAttachment
                    {
                        BranchCode = acceptance.BranchCode,
                        PlanLineId = line.Id,
                        FileName = PrivateUploadFileName.ForDisplay(image.FileName),
                        ContentType = image.ContentType,
                        StoragePath = path,
                        Caption = $"Bilinmeyen levha eşleştirme kanıtı · {vehicle.PlateNo}",
                        FileSize = image.Length,
                        CreatedBy = actor,
                        CreatedDate = DateTime.UtcNow
                    }, token);
                }

                line.VehicleAcceptanceId = acceptance.Id;
                line.ArrivedQuantity = line.ExpectedQuantity;
                line.ApprovedQuantity = line.ExpectedQuantity;
                line.RejectedQuantity = 0;
                line.ArrivalStatus = SteelArrivalStatus.Arrived;
                line.InspectionStatus = SteelInspectionStatus.Approved;
                line.RejectReason = null;
                line.InspectionNote = Clean(request.Note, 1000);
                line.InspectedBy = actor;
                line.InspectedAtUtc = now;
                line.UpdatedBy = actor;
                line.UpdatedDate = DateTime.UtcNow;

                acceptedPlate.IdentityStatus = SteelPlateIdentityStatus.Resolved;
                acceptedPlate.PlanLineId = line.Id;
                acceptedPlate.ResolvedAtUtc = now;
                acceptedPlate.ResolvedBy = actor;
                acceptedPlate.UpdatedBy = actor;
                acceptedPlate.UpdatedDate = DateTime.UtcNow;

                line.Plan.VehicleCheckInId = vehicle.Id;
                line.Plan.UpdatedBy = actor;
                line.Plan.UpdatedDate = DateTime.UtcNow;

                var hasOtherUnknowns = await AcceptedPlates.Query()
                    .AnyAsync(x => x.VehicleAcceptanceId == acceptance.Id
                        && x.Id != acceptedPlate.Id
                        && x.IdentityStatus == SteelPlateIdentityStatus.Unknown, token);
                var hasOtherVehicleUnknowns = await AcceptedPlates.Query()
                    .AnyAsync(x => x.VehicleCheckInId == acceptance.VehicleCheckInId
                        && x.Id != acceptedPlate.Id
                        && x.IdentityStatus == SteelPlateIdentityStatus.Unknown, token);
                acceptance.Status = ResolveAcceptanceStatus(hasOtherUnknowns);
                acceptance.TotalAcceptedQuantity += line.ExpectedQuantity;
                acceptance.UpdatedBy = actor;
                acceptance.UpdatedDate = DateTime.UtcNow;
                vehicle.Status = hasOtherVehicleUnknowns
                    ? VehicleCheckInStatus.ContainsUnknownPlates
                    : VehicleCheckInStatus.Completed;
                vehicle.UpdatedBy = actor;
                vehicle.UpdatedDate = DateTime.UtcNow;

                await uow.SaveChangesAsync(token);
                await RefreshPlanAsync(line.Plan, token);
                await audit.WriteAsync(new(
                    "steel-receipt.vehicle-acceptance.resolve-unknown",
                    nameof(SteelVehicleAcceptedPlate),
                    acceptedPlate.Id.ToString(),
                    "Succeeded",
                    "steel-receipt",
                    OldValues: new { IdentityStatus = SteelPlateIdentityStatus.Unknown, PlanLineId = (long?)null },
                    NewValues: new
                    {
                        acceptedPlate.IdentityStatus,
                        acceptedPlate.PlanLineId,
                        acceptedPlate.ResolvedAtUtc,
                        acceptedPlate.ResolvedBy,
                        acceptance.VehicleCheckInId,
                        ImageCount = plateImages.Count
                    },
                    ChangedFields: ["IdentityStatus", "PlanLineId", "ResolvedAtUtc", "ResolvedBy", "PlateImages"]),
                    token);

                acceptedPlate.PlanLine = line;
                var attachmentRows = await PlateImages.Query()
                    .Where(x => x.PlanLineId == line.Id)
                    .OrderByDescending(x => x.CreatedDate)
                    .Select(x => new SteelReceiptAttachmentRow(
                        x.Id,
                        x.PlanLineId,
                        x.FileName,
                        x.ContentType,
                        $"/api/steel-receipts/attachments/{x.Id}/file",
                        x.Caption,
                        x.FileSize,
                        x.CreatedBy,
                        x.CreatedDate))
                    .ToListAsync(token);
                return ToRow(acceptedPlate, acceptance.AcceptedAtUtc, canResolve: false, attachmentRows);
            }, ct, IsolationLevel.Serializable);
        }
        catch (DbUpdateConcurrencyException)
        {
            CleanupStoredFiles(storedFiles);
            throw AppException.Conflict("Bilinmeyen levha veya SAC satırı başka bir kullanıcı tarafından değiştirildi. Listeyi yenileyin.");
        }
        catch (DbUpdateException)
        {
            CleanupStoredFiles(storedFiles);
            throw AppException.Conflict("Seçilen SAC levhası başka bir kabul kaydına bağlandı. Aday listesini yenileyin.");
        }
        catch
        {
            CleanupStoredFiles(storedFiles);
            throw;
        }
    }

    private static void CleanupStoredFiles(List<StoredFile> storedFiles)
    {
        for (var index = storedFiles.Count - 1; index >= 0; index--)
        {
            try { storedFiles[index].Delete(storedFiles[index].Path); }
            catch { /* Database rollback is authoritative; orphan cleanup can be retried operationally. */ }
        }
    }

    private async Task<CompleteSteelVehicleAcceptanceResult> BuildResultAsync(
        SteelVehicleAcceptance acceptance,
        bool replayed,
        bool canManageVehicleAcceptance,
        CancellationToken ct)
    {
        var vehicle = await vehicleCheckIns.GetAsync(acceptance.VehicleCheckInId, ct);
        var entities = await AcceptedPlates.Query()
            .Include(x => x.VehicleAcceptance)
            .Include(x => x.PlanLine)
            .ThenInclude(x => x!.Plan)
            .Include(x => x.PlanLine)
            .ThenInclude(x => x!.Attachments)
            .Where(x => x.VehicleCheckInId == acceptance.VehicleCheckInId)
            .OrderBy(x => x.VehicleAcceptance.AcceptedAtUtc)
            .ThenBy(x => x.VehicleAcceptanceId)
            .ThenBy(x => x.SequenceNo)
            .ToListAsync(ct);
        var plates = entities.Select((entity, index) =>
        {
            var canResolve = entity.IdentityStatus == SteelPlateIdentityStatus.Unknown
                && canManageVehicleAcceptance;
            return ToRow(
                entity,
                entity.VehicleAcceptance.AcceptedAtUtc,
                canResolve,
                MapAttachments(entity.PlanLine),
                sequenceNo: index + 1);
        }).ToList();
        var unknownCount = plates.Count(x => x.IdentityStatus == nameof(SteelPlateIdentityStatus.Unknown));
        return new(
            acceptance.Id,
            replayed,
            vehicle,
            plates,
            unknownCount,
            unknownCount > 0,
            plates.Any(x => x.CanResolve));
    }

    internal static SteelVehicleAcceptanceStatus ResolveAcceptanceStatus(bool hasUnknownPlates) =>
        hasUnknownPlates
            ? SteelVehicleAcceptanceStatus.PartiallyIdentified
            : SteelVehicleAcceptanceStatus.Completed;

    private static AcceptedSteelPlateRow ToRow(
        SteelVehicleAcceptedPlate acceptedPlate,
        DateTimeOffset acceptedAtUtc,
        bool canResolve,
        IReadOnlyList<SteelReceiptAttachmentRow> attachments,
        int? sequenceNo = null)
    {
        var line = acceptedPlate.PlanLine;
        return new(
            acceptedPlate.Id,
            sequenceNo ?? acceptedPlate.SequenceNo,
            acceptedPlate.IdentityStatus.ToString(),
            acceptedPlate.PlanLineId,
            line?.PlanId,
            line?.Plan.ImportReferenceNo,
            line?.DCode,
            line?.StockCodeSnapshot,
            line?.SupplierSerialNo,
            line?.ApprovedQuantity,
            line?.UnitCode,
            line?.ReceivingLocationId,
            line?.InspectedAtUtc ?? acceptedAtUtc,
            Convert.ToBase64String(acceptedPlate.RowVersion),
            canResolve,
            line is null
                ? null
                : new AcceptedSteelPlatePlanLineSummary(
                    line.Id,
                    line.PlanId,
                    line.StockCodeSnapshot,
                    line.StockNameSnapshot),
            attachments);
    }

    private static IReadOnlyList<SteelReceiptAttachmentRow> MapAttachments(SteelReceiptPlanLine? line) =>
        line?.Attachments
            .OrderByDescending(x => x.CreatedDate)
            .Select(ToAttachmentRow)
            .ToList()
        ?? [];

    private static SteelReceiptAttachmentRow ToAttachmentRow(SteelReceiptInspectionAttachment attachment) =>
        new(
            attachment.Id,
            attachment.PlanLineId,
            attachment.FileName,
            attachment.ContentType,
            $"/api/steel-receipts/attachments/{attachment.Id}/file",
            attachment.Caption,
            attachment.FileSize,
            attachment.CreatedBy,
            attachment.CreatedDate);

    private async Task RefreshPlanAsync(SteelReceiptPlan plan, CancellationToken ct)
    {
        var states = await Lines.Query()
            .Where(x => x.PlanId == plan.Id)
            .Select(x => new { x.InspectionStatus, x.ConversionStatus })
            .ToListAsync(ct);
        plan.Status = SteelReceiptPlanStatusRules.Resolve(
            states.Select(x => new SteelReceiptPlanStatusRules.LineState(x.InspectionStatus, x.ConversionStatus)));
        plan.UpdatedDate = DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
    }

    internal static void ValidateRequest(
        CompleteSteelVehicleAcceptanceRequest request,
        IReadOnlyList<VehicleImageUpload> vehicleImages,
        IReadOnlyList<SteelPlateImageUpload> plateImages)
    {
        if (request.IdempotencyKey == Guid.Empty)
            throw AppException.BadRequest("Idempotency anahtarı zorunludur.");
        if (request.Slots.Count is < 1 or > 50)
            throw AppException.BadRequest("Tek araç kabulünde 1-50 levha slotu seçilebilir.");
        if (request.Slots.Any(x => x.IdentityStatus is not (SteelPlateIdentityStatus.Known or SteelPlateIdentityStatus.Unknown)))
            throw AppException.BadRequest("Kabul sırasında levha durumu Known veya Unknown olmalıdır.");
        var knownSlots = request.Slots.Where(x => x.IdentityStatus == SteelPlateIdentityStatus.Known).ToList();
        if (knownSlots.Any(x => x.PlanLineId is null or <= 0 || string.IsNullOrWhiteSpace(x.RowVersion)))
            throw AppException.BadRequest("Bilinen levhalarda SAC satırı ve eşzamanlılık bilgisi zorunludur.");
        if (request.Slots.Any(x => x.IdentityStatus == SteelPlateIdentityStatus.Unknown && x.PlanLineId.HasValue))
            throw AppException.BadRequest("Bilinmeyen levha slotu bir SAC satırına bağlı olamaz.");
        if (knownSlots.Select(x => x.PlanLineId).Distinct().Count() != knownSlots.Count)
            throw AppException.BadRequest("Aynı SAC levhası birden fazla kez seçilemez.");
        if (vehicleImages.Count > 10)
            throw AppException.BadRequest("Bir araç kabulünde en fazla 10 araç görseli yüklenebilir.");
        if (plateImages.Count > 100)
            throw AppException.BadRequest("Bir araç kabulünde en fazla 100 levha görseli yüklenebilir.");
        if (vehicleImages.Sum(x => x.Length) + plateImages.Sum(x => x.Length) > 120_000_000)
            throw AppException.BadRequest("Araç kabulündeki toplam görsel boyutu 120 MB sınırını aşamaz.");
        if (plateImages.Any(x => !knownSlots.Any(p => p.PlanLineId == x.PlanLineId)))
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

    internal static bool HasSelectedPlateConflict(
        IEnumerable<SteelReceiptPlanLine> selectedLines) =>
        selectedLines.Any(line =>
            line.VehicleAcceptanceId.HasValue
            || line.ArrivalStatus != SteelArrivalStatus.Expected
            || line.InspectionStatus != SteelInspectionStatus.Pending
            || line.ConversionStatus != SteelReceiptConversionStatus.NotCreated);

    internal static void EnsureTargetSlotCount(
        int existingSlotCount,
        int newSlotCount,
        int targetSteelSheetCount)
    {
        if (existingSlotCount + newSlotCount != targetSteelSheetCount)
            throw AppException.BadRequest(
                $"Araç hedef adedi {targetSteelSheetCount}; mevcut {existingSlotCount} aktif slota "
                + $"{newSlotCount} yeni slot eklendiğinde hedef adetle eşleşmelidir.");
    }

    private sealed record StoredFile(Action<string> Delete, string Path);
}
