using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.GeneratorProduction.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.GeneratorProduction.Application;

public sealed partial class GeneratorProductionService
{
    public async Task<GeneratorProductRow> SaveProductAsync(
        long? id, SaveGeneratorProductRequest request, long userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 80)
            throw AppException.BadRequest("Ürün kodu zorunludur ve en fazla 80 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 250)
            throw AppException.BadRequest("Ürün adı zorunludur ve en fazla 250 karakter olabilir.");
        if (request.Routes.Count == 0 || request.Routes.Select(x => x.PartType).Distinct().Count() != request.Routes.Count)
            throw AppException.BadRequest("Ürün için her bileşen türünde en fazla bir rota ve en az bir rota seçilmelidir.");

        var routeIds = request.Routes.Select(x => x.RouteId).Distinct().ToArray();
        var routes = await uow.Repository<GeneratorProductionRoute>().Query()
            .Where(x => routeIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
        if (routes.Count != routeIds.Length || request.Routes.Any(x => !routes.TryGetValue(x.RouteId, out var route) || route.PartType != x.PartType))
            throw AppException.BadRequest("Ürün rota eşleştirmesinde pasif, bulunamayan veya bileşen türü uyuşmayan rota var.");

        StockEntity? stock = null;
        if (request.ProducedStockId.HasValue)
            stock = await uow.Repository<StockEntity>().FindByIdAsync(request.ProducedStockId.Value, false, ct)
                ?? throw AppException.BadRequest("Üretilen stok kartı bulunamadı.");

        var repository = uow.Repository<GeneratorProductionProduct>();
        var entity = id.HasValue
            ? await repository.Query(true).Include(x => x.Routes).FirstOrDefaultAsync(x => x.Id == id.Value, ct)
                ?? throw AppException.NotFound("Jeneratör ürün tanımı bulunamadı.")
            : new GeneratorProductionProduct { CreatedBy = userId };
        if (id.HasValue && (string.IsNullOrWhiteSpace(request.RowVersion) || !entity.RowVersion.SequenceEqual(DecodeRowVersion(request.RowVersion))))
            throw AppException.Conflict("Ürün tanımı başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");

        var code = request.Code.Trim().ToUpperInvariant();
        if (await repository.AnyAsync(x => x.Code == code && x.Id != entity.Id, ct))
            throw AppException.Conflict("Bu jeneratör ürün kodu zaten kullanılıyor.");
        var before = id.HasValue ? MapProduct(entity) : null;
        entity.Code = code;
        entity.Name = request.Name.Trim();
        entity.GeneratorType = Clean(request.GeneratorType);
        entity.ProducedStockId = stock?.Id;
        entity.ProducedStockCodeSnapshot = stock?.ErpStockCode ?? Clean(request.ProducedStockCode);
        entity.Description = Clean(request.Description);
        entity.IsActive = request.IsActive;
        entity.UpdatedBy = userId;
        entity.UpdatedDate = DateTime.UtcNow;
        if (!id.HasValue) await repository.AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);

        var requestedByPart = request.Routes.ToDictionary(x => x.PartType);
        foreach (var current in entity.Routes.Where(x => !requestedByPart.ContainsKey(x.PartType)))
        {
            current.IsDeleted = true; current.DeletedBy = userId; current.DeletedDate = DateTime.UtcNow;
        }
        foreach (var input in request.Routes)
        {
            var current = entity.Routes.FirstOrDefault(x => x.PartType == input.PartType);
            if (current is null)
            {
                current = new GeneratorProductionProductRoute
                {
                    ProductId = entity.Id, PartType = input.PartType, RouteId = input.RouteId,
                    IsActive = true, CreatedBy = userId
                };
                await uow.Repository<GeneratorProductionProductRoute>().AddAsync(current, ct);
            }
            else
            {
                current.RouteId = input.RouteId; current.IsActive = true; current.UpdatedBy = userId; current.UpdatedDate = DateTime.UtcNow;
            }
        }
        await uow.SaveChangesAsync(ct);
        var result = await GetProductRowAsync(entity.Id, ct);
        await audit.WriteAsync(new AuditLogWriteEntry(id.HasValue ? "Update" : "Create", nameof(GeneratorProductionProduct), entity.Id.ToString(), "Success", "GeneratorProduction",
            OldValues: before, NewValues: result), ct);
        return result;
    }

    public async Task DeleteProductAsync(long id, long userId, CancellationToken ct = default)
    {
        var entity = await uow.Repository<GeneratorProductionProduct>().FindByIdAsync(id, true, ct)
            ?? throw AppException.NotFound("Jeneratör ürün tanımı bulunamadı.");
        if (await Projects.AnyAsync(x => x.ProductId == id, ct))
            throw AppException.Conflict("Projelerde kullanılan ürün tanımı silinemez; pasif duruma alınabilir.");
        entity.IsDeleted = true; entity.DeletedBy = userId; entity.DeletedDate = DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry("Delete", nameof(GeneratorProductionProduct), id.ToString(), "Success", "GeneratorProduction"), ct);
    }

    public async Task<GeneratorStationCapabilityRow> SaveStationCapabilityAsync(
        long? id, SaveGeneratorStationCapabilityRequest request, long userId, CancellationToken ct = default)
    {
        if (request.EfficiencyPercent is < 1 or > 300 || request.SetupMinutes is < 0 or > 10_080)
            throw AppException.BadRequest("İstasyon verimliliği %1-%300, hazırlık süresi 0-10080 dakika arasında olmalıdır.");
        var product = await uow.Repository<GeneratorProductionProduct>().FindByIdAsync(request.ProductId, false, ct)
            ?? throw AppException.BadRequest("Jeneratör ürün tanımı bulunamadı.");
        if (!product.IsActive) throw AppException.Conflict("Pasif ürün için istasyon yeteneği tanımlanamaz.");
        var operation = await uow.Repository<GeneratorProductionRouteOperation>().Query()
            .Include(x => x.Route).FirstOrDefaultAsync(x => x.Id == request.RouteOperationId && x.IsActive, ct)
            ?? throw AppException.BadRequest("Aktif rota operasyonu bulunamadı.");
        var station = await uow.Repository<GeneratorProductionStation>().FindByIdAsync(request.StationId, false, ct)
            ?? throw AppException.BadRequest("İstasyon bulunamadı.");
        if (!station.IsActive) throw AppException.Conflict("Pasif istasyon ürün yeteneğine bağlanamaz.");
        if (!await uow.Repository<GeneratorProductionProductRoute>().AnyAsync(
                x => x.ProductId == product.Id && x.RouteId == operation.RouteId && x.IsActive, ct))
            throw AppException.BadRequest("Operasyon, ürünün seçili rotalarından birine ait değil.");

        var repository = uow.Repository<GeneratorProductionStationCapability>();
        var entity = id.HasValue
            ? await repository.FindByIdAsync(id.Value, true, ct) ?? throw AppException.NotFound("İstasyon yeteneği bulunamadı.")
            : new GeneratorProductionStationCapability { CreatedBy = userId };
        CheckRowVersion(entity.RowVersion, request.RowVersion, id.HasValue, "İstasyon yeteneği");
        if (await repository.AnyAsync(x => x.ProductId == request.ProductId && x.RouteOperationId == request.RouteOperationId
                && x.StationId == request.StationId && x.Id != entity.Id, ct))
            throw AppException.Conflict("Bu ürün, operasyon ve istasyon yeteneği zaten tanımlı.");

        if (request.IsPrimary)
        {
            var otherPrimaries = await repository.Query(true)
                .Where(x => x.ProductId == request.ProductId && x.RouteOperationId == request.RouteOperationId && x.IsPrimary && x.Id != entity.Id)
                .ToListAsync(ct);
            foreach (var other in otherPrimaries) { other.IsPrimary = false; other.UpdatedBy = userId; other.UpdatedDate = DateTime.UtcNow; }
        }
        entity.ProductId = request.ProductId; entity.RouteOperationId = request.RouteOperationId; entity.StationId = request.StationId;
        entity.IsPrimary = request.IsPrimary; entity.EfficiencyPercent = request.EfficiencyPercent; entity.SetupMinutes = request.SetupMinutes;
        entity.IsActive = request.IsActive; entity.UpdatedBy = userId; entity.UpdatedDate = DateTime.UtcNow;
        if (!id.HasValue) await repository.AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);
        var result = await GetStationCapabilityRowAsync(entity.Id, ct);
        await audit.WriteAsync(new AuditLogWriteEntry(id.HasValue ? "Update" : "Create", nameof(GeneratorProductionStationCapability), entity.Id.ToString(), "Success", "GeneratorProduction", NewValues: result), ct);
        return result;
    }

    public Task DeleteStationCapabilityAsync(long id, long userId, CancellationToken ct = default) =>
        SoftDeleteDefinitionAsync<GeneratorProductionStationCapability>(id, userId, "İstasyon yeteneği", ct);

    public async Task<GeneratorOperationMaterialRow> SaveOperationMaterialAsync(
        long? id, SaveGeneratorOperationMaterialRequest request, long userId, CancellationToken ct = default)
    {
        if (request.QuantityPerUnit <= 0 || request.WasteRate is < 0 or > 100 || request.NeedOffsetMinutes is < -10_080 or > 10_080)
            throw AppException.BadRequest("Malzeme miktarı, fire oranı veya ihtiyaç zamanı geçersiz.");
        var product = await uow.Repository<GeneratorProductionProduct>().FindByIdAsync(request.ProductId, false, ct)
            ?? throw AppException.BadRequest("Jeneratör ürün tanımı bulunamadı.");
        var operation = await uow.Repository<GeneratorProductionRouteOperation>().FindByIdAsync(request.RouteOperationId, false, ct)
            ?? throw AppException.BadRequest("Rota operasyonu bulunamadı.");
        if (!await uow.Repository<GeneratorProductionProductRoute>().AnyAsync(x => x.ProductId == product.Id && x.RouteId == operation.RouteId && x.IsActive, ct))
            throw AppException.BadRequest("Operasyon, ürünün seçili rotalarından birine ait değil.");
        var stock = await uow.Repository<StockEntity>().FindByIdAsync(request.StockId, false, ct)
            ?? throw AppException.BadRequest("Stok kartı bulunamadı.");
        var warehouse = await uow.Repository<WarehouseEntity>().FindByIdAsync(request.WarehouseId, false, ct)
            ?? throw AppException.BadRequest("Kaynak depo bulunamadı.");
        YapCodeEntity? yapCode = null;
        if (request.YapCodeId.HasValue)
            yapCode = await uow.Repository<YapCodeEntity>().FindByIdAsync(request.YapCodeId.Value, false, ct)
                ?? throw AppException.BadRequest("Konfigürasyon kodu bulunamadı.");
        var unit = string.IsNullOrWhiteSpace(request.UnitCode) ? stock.BaseUnitCode : request.UnitCode.Trim().ToUpperInvariant();
        if (unit.Length > 20) throw AppException.BadRequest("Birim kodu en fazla 20 karakter olabilir.");

        var repository = uow.Repository<GeneratorProductionOperationMaterial>();
        var entity = id.HasValue
            ? await repository.FindByIdAsync(id.Value, true, ct) ?? throw AppException.NotFound("Operasyon malzeme tanımı bulunamadı.")
            : new GeneratorProductionOperationMaterial { CreatedBy = userId };
        CheckRowVersion(entity.RowVersion, request.RowVersion, id.HasValue, "Operasyon malzemesi");
        if (await repository.AnyAsync(x => x.ProductId == request.ProductId && x.RouteOperationId == request.RouteOperationId
                && x.StockId == request.StockId && x.YapCodeId == request.YapCodeId && x.WarehouseId == request.WarehouseId
                && x.UnitCode == unit && x.Id != entity.Id, ct))
            throw AppException.Conflict("Bu operasyon malzemesi aynı depo ve boyutlarla zaten tanımlı.");
        entity.ProductId = request.ProductId; entity.RouteOperationId = request.RouteOperationId; entity.StockId = stock.Id;
        entity.YapCodeId = yapCode?.Id; entity.WarehouseId = warehouse.Id; entity.StockCodeSnapshot = stock.ErpStockCode;
        entity.StockNameSnapshot = stock.StockName; entity.UnitCode = unit; entity.QuantityPerUnit = request.QuantityPerUnit;
        entity.WasteRate = request.WasteRate; entity.NeedOffsetMinutes = request.NeedOffsetMinutes; entity.IsMandatory = request.IsMandatory;
        entity.UpdatedBy = userId; entity.UpdatedDate = DateTime.UtcNow;
        if (!id.HasValue) await repository.AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);
        var result = await GetOperationMaterialRowAsync(entity.Id, ct);
        await audit.WriteAsync(new AuditLogWriteEntry(id.HasValue ? "Update" : "Create", nameof(GeneratorProductionOperationMaterial), entity.Id.ToString(), "Success", "GeneratorProduction", NewValues: result), ct);
        return result;
    }

    public Task DeleteOperationMaterialAsync(long id, long userId, CancellationToken ct = default) =>
        SoftDeleteDefinitionAsync<GeneratorProductionOperationMaterial>(id, userId, "Operasyon malzemesi", ct);

    public async Task<GeneratorScheduleRow> UpdateOperationScheduleAsync(
        long operationId, UpdateGeneratorOperationScheduleRequest request, long userId, CancellationToken ct = default)
    {
        var policy = await GetRequiredPolicyEntityAsync(false, ct);
        RequireReason(request.Reason, "Manuel planlama gerekçesi", policy.MinimumPlanReasonLength);
        await uow.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
        var operation = await uow.Repository<GeneratorProductionOperation>().Query(true)
            .Include(x => x.Project).Include(x => x.Station).Include(x => x.RouteOperation).ThenInclude(x => x.Route)
            .FirstOrDefaultAsync(x => x.Id == operationId, ct)
            ?? throw AppException.NotFound("Jeneratör üretim operasyonu bulunamadı.");
        if (!operation.RowVersion.SequenceEqual(DecodeRowVersion(request.RowVersion)))
            throw AppException.Conflict("Operasyon başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");
        if (operation.Status is not (GeneratorOperationStatus.Planned or GeneratorOperationStatus.Ready))
            throw AppException.Conflict("Yalnızca başlamamış operasyonların planı elle değiştirilebilir.");
        var start = AsUtc(request.PlannedStartAtUtc); var end = AsUtc(request.PlannedEndAtUtc);
        if (end <= start) throw AppException.BadRequest("Plan bitişi başlangıçtan sonra olmalıdır.");
        var station = await uow.Repository<GeneratorProductionStation>().FindByIdAsync(request.StationId, false, ct)
            ?? throw AppException.BadRequest("İstasyon bulunamadı.");
        if (!station.IsActive) throw AppException.Conflict("Pasif istasyona operasyon planlanamaz.");
        if (operation.Project.ProductId.HasValue && !await uow.Repository<GeneratorProductionStationCapability>().AnyAsync(
                x => x.ProductId == operation.Project.ProductId && x.RouteOperationId == operation.RouteOperationId
                    && x.StationId == station.Id && x.IsActive, ct))
            throw AppException.Conflict("Seçilen istasyon bu ürün ve operasyon için yetkili değil.");
        await ValidateManualStationAvailabilityAsync(operation, station, start, end, policy, ct);
        await ValidateManualDependenciesAsync(operation.Id, start, end, ct);
        var materialCheck = await CheckOperationMaterialAvailabilityAsync(operation, start, policy.InboundQualityBufferDays, ct);
        if (policy.RequireMaterialAvailabilityToStart && materialCheck.HasShortage)
            throw AppException.Conflict(materialCheck.Message ?? "Seçilen başlangıç zamanında zorunlu malzeme kullanılabilir değil.");

        var before = new { operation.StationId, operation.PlannedStartAtUtc, operation.PlannedEndAtUtc, operation.IsScheduleLocked };
        operation.StationId = station.Id; operation.Station = station; operation.PlannedStartAtUtc = start; operation.PlannedEndAtUtc = end;
        operation.IsScheduleLocked = request.IsLocked; operation.ManualScheduleReason = request.Reason.Trim(); operation.ManualScheduledBy = userId;
        operation.ManualScheduledAtUtc = DateTime.UtcNow; operation.HasMaterialShortage = materialCheck.HasShortage;
        operation.UpdatedBy = userId; operation.UpdatedDate = DateTime.UtcNow;
        var revision = new GeneratorProductionPlanRevision
        {
            ProjectId = operation.ProjectId, ActionType = request.IsLocked ? "ManualScheduleLocked" : "ManualScheduleChanged",
            Reason = request.Reason.Trim(), PreviousPlanJson = JsonSerializer.Serialize(before),
            NewPlanJson = JsonSerializer.Serialize(new { operation.Id, StationId = station.Id, PlannedStartAtUtc = start, PlannedEndAtUtc = end, request.IsLocked }),
            OccurredAtUtc = DateTime.UtcNow, ActorUserId = userId, CreatedBy = userId
        };
        await uow.Repository<GeneratorProductionPlanRevision>().AddAsync(revision, ct);
        await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry("ManualSchedule", nameof(GeneratorProductionOperation), operation.Id.ToString(), "Success", "GeneratorProduction",
            request.Reason, OldValues: before, NewValues: new { operation.StationId, operation.PlannedStartAtUtc, operation.PlannedEndAtUtc, operation.IsScheduleLocked }), ct);
        await uow.CommitTransactionAsync(ct);
        return ToScheduleRow(operation);
        }
        catch
        {
            await uow.RollbackTransactionAsync(ct);
            throw;
        }
    }

    private async Task ValidateProductSelectionAsync(long? productId, CancellationToken ct)
    {
        if (!productId.HasValue) return;
        if (!await uow.Repository<GeneratorProductionProduct>().AnyAsync(x => x.Id == productId.Value && x.IsActive, ct))
            throw AppException.BadRequest("Seçilen jeneratör ürün tanımı aktif değil veya bulunamadı.");
    }

    private async Task<IReadOnlyList<GeneratorProductRow>> GetProductRowsAsync(CancellationToken ct)
    {
        var entities = await uow.Repository<GeneratorProductionProduct>().Query()
            .Include(x => x.Routes).ThenInclude(x => x.Route).OrderBy(x => x.Code).ToListAsync(ct);
        return entities.Select(MapProduct).ToArray();
    }

    private async Task<GeneratorProductRow> GetProductRowAsync(long id, CancellationToken ct)
    {
        var entity = await uow.Repository<GeneratorProductionProduct>().Query()
            .Include(x => x.Routes).ThenInclude(x => x.Route).FirstAsync(x => x.Id == id, ct);
        return MapProduct(entity);
    }

    private static GeneratorProductRow MapProduct(GeneratorProductionProduct x) => new(
        x.Id, x.Code, x.Name, x.GeneratorType, x.ProducedStockId, x.ProducedStockCodeSnapshot, x.Description, x.IsActive,
        x.Routes.Where(r => r.IsActive).OrderBy(r => r.PartType).Select(r => new GeneratorProductRouteRow(r.PartType, r.RouteId, r.Route.Code, r.Route.Name)).ToArray(),
        Convert.ToBase64String(x.RowVersion));

    private async Task<IReadOnlyList<GeneratorStationCapabilityRow>> GetStationCapabilityRowsAsync(CancellationToken ct) =>
        await uow.Repository<GeneratorProductionStationCapability>().Query().OrderBy(x => x.Product.Code).ThenBy(x => x.RouteOperation.Sequence).ThenByDescending(x => x.IsPrimary)
            .Select(x => new GeneratorStationCapabilityRow(x.Id, x.ProductId, x.Product.Code, x.RouteOperationId, x.RouteOperation.OperationCode,
                x.RouteOperation.OperationName, x.StationId, x.Station.Code, x.Station.Name, x.IsPrimary, x.EfficiencyPercent,
                x.SetupMinutes, x.IsActive, Convert.ToBase64String(x.RowVersion))).ToListAsync(ct);

    private async Task<GeneratorStationCapabilityRow> GetStationCapabilityRowAsync(long id, CancellationToken ct) =>
        await uow.Repository<GeneratorProductionStationCapability>().Query().Where(x => x.Id == id)
            .Select(x => new GeneratorStationCapabilityRow(x.Id, x.ProductId, x.Product.Code, x.RouteOperationId, x.RouteOperation.OperationCode,
                x.RouteOperation.OperationName, x.StationId, x.Station.Code, x.Station.Name, x.IsPrimary, x.EfficiencyPercent,
                x.SetupMinutes, x.IsActive, Convert.ToBase64String(x.RowVersion))).SingleAsync(ct);

    private async Task<IReadOnlyList<GeneratorOperationMaterialRow>> GetOperationMaterialRowsAsync(CancellationToken ct) =>
        await OperationMaterialRows().OrderBy(x => x.ProductCode).ThenBy(x => x.OperationCode).ThenBy(x => x.StockCode).ToListAsync(ct);

    private async Task<GeneratorOperationMaterialRow> GetOperationMaterialRowAsync(long id, CancellationToken ct) =>
        await OperationMaterialRows().SingleAsync(x => x.Id == id, ct);

    private IQueryable<GeneratorOperationMaterialRow> OperationMaterialRows() =>
        uow.Repository<GeneratorProductionOperationMaterial>().Query().Select(x => new GeneratorOperationMaterialRow(
            x.Id, x.ProductId, x.Product.Code, x.RouteOperationId, x.RouteOperation.OperationCode, x.RouteOperation.OperationName,
            x.StockId, x.StockCodeSnapshot, x.StockNameSnapshot, x.YapCodeId, x.YapCode == null ? null : x.YapCode.ConfigurationCode,
            x.WarehouseId, x.Warehouse.WarehouseCode, x.Warehouse.WarehouseName, x.UnitCode, x.QuantityPerUnit, x.WasteRate,
            x.NeedOffsetMinutes, x.IsMandatory, Convert.ToBase64String(x.RowVersion)));

    private async Task<IReadOnlyList<GeneratorWarehouseOption>> GetWarehouseOptionsAsync(CancellationToken ct) =>
        await uow.Repository<WarehouseEntity>().Query().OrderBy(x => x.WarehouseCode)
            .Select(x => new GeneratorWarehouseOption(x.Id, x.WarehouseCode, x.WarehouseName)).ToListAsync(ct);

    private async Task SoftDeleteDefinitionAsync<TEntity>(long id, long userId, string name, CancellationToken ct)
        where TEntity : verii_wms_api_v2.Shared.Domain.BaseEntity
    {
        var entity = await uow.Repository<TEntity>().FindByIdAsync(id, true, ct) ?? throw AppException.NotFound($"{name} bulunamadı.");
        entity.IsDeleted = true; entity.DeletedBy = userId; entity.DeletedDate = DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry("Delete", typeof(TEntity).Name, id.ToString(), "Success", "GeneratorProduction"), ct);
    }

    private static void CheckRowVersion(byte[] current, string? supplied, bool required, string name)
    {
        if (!required) return;
        if (string.IsNullOrWhiteSpace(supplied) || !current.SequenceEqual(DecodeRowVersion(supplied)))
            throw AppException.Conflict($"{name} başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");
    }

    private async Task ValidateManualStationAvailabilityAsync(
        GeneratorProductionOperation operation,
        GeneratorProductionStation station,
        DateTime start,
        DateTime end,
        GeneratorProductionPolicy policy,
        CancellationToken ct)
    {
        var stationShift = await uow.Repository<GeneratorProductionStationShift>().Query()
            .Include(x => x.Shift)
            .Where(x => x.StationId == station.Id && x.IsActive && x.Shift.IsActive)
            .OrderBy(x => x.Shift.PlanningOrder)
            .FirstOrDefaultAsync(ct)
            ?? throw AppException.Conflict("Seçilen istasyonun aktif vardiyası yok.");
        if (station.RequiresCrane && !stationShift.CraneAvailable)
            throw AppException.Conflict("Seçilen vardiyada istasyon için vinç kullanılamıyor.");
        if (station.RequiresTransport && !stationShift.TransportAvailable)
            throw AppException.Conflict("Seçilen vardiyada istasyon için taşıma kaynağı kullanılamıyor.");

        var exceptions = await uow.Repository<GeneratorProductionCalendarException>().Query()
            .Where(x => (x.StationId == null || x.StationId == station.Id)
                && (x.ShiftId == null || x.ShiftId == stationShift.ShiftId))
            .ToListAsync(ct);
        var overrides = new Dictionary<DateOnly, GeneratorWorkingDayOverride>();
        foreach (var exception in exceptions.Where(x => x.StationId == null))
            overrides[exception.ExceptionDate] = new(exception.IsWorking, exception.CapacityMinutes);
        foreach (var exception in exceptions.Where(x => x.StationId == station.Id))
            overrides[exception.ExceptionDate] = new(exception.IsWorking, exception.CapacityMinutes);
        var calendar = new GeneratorStationCalendar(stationShift.Shift.StartTime, stationShift.Shift.EndTime,
            stationShift.WeekdayMask, stationShift.CapacityMinutes, overrides);
        DateTime calendarStart;
        DateTime expectedEnd;
        try
        {
            calendarStart = GeneratorProductionPlanningPolicy.NextWorkingInstant(start, calendar, policy.WorkingCalendarSearchLimitDays);
            var efficiency = 100;
            var setupMinutes = 0;
            if (operation.Project.ProductId.HasValue)
            {
                var capability = await uow.Repository<GeneratorProductionStationCapability>().Query()
                    .Where(x => x.ProductId == operation.Project.ProductId.Value && x.RouteOperationId == operation.RouteOperationId
                        && x.StationId == station.Id && x.IsActive)
                    .Select(x => new { x.EfficiencyPercent, x.SetupMinutes }).SingleAsync(ct);
                efficiency = capability.EfficiencyPercent;
                setupMinutes = capability.SetupMinutes;
            }
            var duration = Math.Clamp((int)Math.Ceiling(operation.RouteOperation.DurationMinutes * 100m / efficiency),
                operation.RouteOperation.MinimumDurationMinutes, operation.RouteOperation.MaximumDurationMinutes) + setupMinutes;
            expectedEnd = GeneratorProductionPlanningPolicy.AddWorkingMinutes(start, duration, calendar, policy.WorkingCalendarSearchLimitDays);
        }
        catch (InvalidOperationException)
        {
            throw AppException.Conflict("Seçilen tarihten sonra istasyon için çalışılabilir vardiya bulunamadı.");
        }
        if (calendarStart != start)
            throw AppException.Conflict($"Başlangıç istasyon vardiyası içinde olmalıdır. İlk uygun zaman: {calendarStart:yyyy-MM-dd HH:mm} UTC.");
        if (expectedEnd != end)
            throw AppException.Conflict($"Bitiş, operasyon süresi, yetenek verimi ve vardiya takvimine göre {expectedEnd:yyyy-MM-dd HH:mm} UTC olmalıdır.");

        var overlappingStations = await uow.Repository<GeneratorProductionOperation>().Query()
            .Where(x => x.Id != operation.Id && x.Status != GeneratorOperationStatus.Cancelled
                && x.PlannedStartAtUtc < end && x.PlannedEndAtUtc > start)
            .Select(x => x.StationId).ToListAsync(ct);
        var stationOverlapCount = overlappingStations.Count(x => x == station.Id);
        var parallelLimit = Math.Max(1, Math.Min(station.MaxParallelJobs,
            Math.Min(Math.Max(1, stationShift.MachineCapacity), Math.Max(1, stationShift.PersonnelCapacity))));
        if (stationOverlapCount >= parallelLimit)
            throw AppException.Conflict("Seçilen zaman aralığında istasyonun makine/personel paralel iş kapasitesi dolu.");

        var targetResources = await uow.Repository<GeneratorProductionStationResource>().Query()
            .Include(x => x.Resource)
            .Where(x => x.StationId == station.Id && x.Resource.IsActive)
            .ToListAsync(ct);
        if (targetResources.Count == 0) return;
        var resourceIds = targetResources.Select(x => x.ResourceId).ToArray();
        var assignments = await uow.Repository<GeneratorProductionStationResource>().Query()
            .Where(x => resourceIds.Contains(x.ResourceId))
            .Select(x => new { x.ResourceId, x.StationId, x.RequiredQuantity }).ToListAsync(ct);
        foreach (var target in targetResources)
        {
            var quantityByStation = assignments.Where(x => x.ResourceId == target.ResourceId)
                .ToDictionary(x => x.StationId, x => x.RequiredQuantity);
            var reserved = overlappingStations.Sum(stationId => quantityByStation.GetValueOrDefault(stationId));
            if (reserved + target.RequiredQuantity > target.Resource.Capacity)
                throw AppException.Conflict($"Seçilen zaman aralığında {target.Resource.Code} kaynağının kapasitesi dolu.");
        }
    }

    private async Task ValidateManualDependenciesAsync(long operationId, DateTime start, DateTime end, CancellationToken ct)
    {
        var predecessors = await uow.Repository<GeneratorProductionOperationDependency>().Query()
            .Where(x => x.SuccessorOperationId == operationId)
            .Select(x => new { x.DependencyType, x.LagMinutes, x.PredecessorOperation.PlannedStartAtUtc, x.PredecessorOperation.PlannedEndAtUtc }).ToListAsync(ct);
        foreach (var dependency in predecessors)
        {
            var valid = dependency.DependencyType switch
            {
                GeneratorDependencyType.StartToStart => start >= dependency.PlannedStartAtUtc.AddMinutes(dependency.LagMinutes),
                GeneratorDependencyType.FinishToFinish => end >= dependency.PlannedEndAtUtc.AddMinutes(dependency.LagMinutes),
                _ => start >= dependency.PlannedEndAtUtc.AddMinutes(dependency.LagMinutes)
            };
            if (!valid) throw AppException.Conflict("Manuel tarih öncül operasyon bağımlılığını ihlal ediyor.");
        }
        var successors = await uow.Repository<GeneratorProductionOperationDependency>().Query()
            .Where(x => x.PredecessorOperationId == operationId)
            .Select(x => new { x.DependencyType, x.LagMinutes, x.SuccessorOperation.PlannedStartAtUtc, x.SuccessorOperation.PlannedEndAtUtc }).ToListAsync(ct);
        foreach (var dependency in successors)
        {
            var valid = dependency.DependencyType switch
            {
                GeneratorDependencyType.StartToStart => dependency.PlannedStartAtUtc >= start.AddMinutes(dependency.LagMinutes),
                GeneratorDependencyType.FinishToFinish => dependency.PlannedEndAtUtc >= end.AddMinutes(dependency.LagMinutes),
                _ => dependency.PlannedStartAtUtc >= end.AddMinutes(dependency.LagMinutes)
            };
            if (!valid) throw AppException.Conflict("Manuel tarih ardıl operasyon bağımlılığını ihlal ediyor.");
        }
    }
}
