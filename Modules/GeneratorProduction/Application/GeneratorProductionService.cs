using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.GeneratorProduction.Domain;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.GeneratorProduction.Application;

public sealed partial class GeneratorProductionService(IUnitOfWork uow, IAuditLogWriter audit) : IGeneratorProductionService
{
    private IGenericRepository<GeneratorProductionProject> Projects => uow.Repository<GeneratorProductionProject>();
    private IGenericRepository<GeneratorProductionPolicy> Policies => uow.Repository<GeneratorProductionPolicy>();

    public async Task<GeneratorOverviewResult> GetOverviewAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var projects = Projects.Query();
        var operations = uow.Repository<GeneratorProductionOperation>().Query();
        return new GeneratorOverviewResult(
            await projects.CountAsync(ct),
            await projects.CountAsync(x => x.Status == GeneratorProjectStatus.Planned, ct),
            await projects.CountAsync(x => x.Status == GeneratorProjectStatus.Released || x.Status == GeneratorProjectStatus.InProgress, ct),
            await operations.CountAsync(ct),
            await operations.CountAsync(x => x.PlannedEndAtUtc < now && x.Status != GeneratorOperationStatus.Completed && x.Status != GeneratorOperationStatus.Cancelled, ct),
            await uow.Repository<GeneratorProductionStation>().Query().CountAsync(x => x.IsActive && x.IsBottleneck, ct));
    }

    public async Task<PagedResponse<GeneratorProjectRow>> GetProjectsAsync(PagedRequest request, CancellationToken ct = default)
    {
        var query = Projects.Query()
            .Select(x => new GeneratorProjectRow(
                x.Id, x.ProjectCode, x.ProjectName, x.ProductId, x.Product == null ? null : x.Product.Code, x.GeneratorType, x.SerialNumber, x.CustomerNameSnapshot,
                x.Status, x.Priority, x.Quantity, x.PlannedStartAtUtc, x.PlannedDeliveryAtUtc,
                x.PlanningOrder, x.Operations.Count, x.Operations.Count(o => o.Status == GeneratorOperationStatus.Completed),
                Convert.ToBase64String(x.RowVersion)))
            .ApplySearch(request, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = nameof(GeneratorProjectRow.Id),
                ["projectCode"] = nameof(GeneratorProjectRow.ProjectCode),
                ["projectName"] = nameof(GeneratorProjectRow.ProjectName),
                ["generatorType"] = nameof(GeneratorProjectRow.GeneratorType),
                ["serialNumber"] = nameof(GeneratorProjectRow.SerialNumber),
                ["customerName"] = nameof(GeneratorProjectRow.CustomerName)
            }, ["projectCode", "projectName"]);

        return await query.OrderBy(x => x.Status).ThenByDescending(x => x.Priority).ThenBy(x => x.PlannedDeliveryAtUtc)
            .ToPagedResponseAsync(request, ct, 200);
    }

    public async Task<GeneratorProjectDetail> GetProjectAsync(long id, CancellationToken ct = default)
    {
        var entity = await Projects.FindByIdAsync(id, false, ct) ?? throw AppException.NotFound("Jeneratör üretim projesi bulunamadı.");
        return MapProject(entity);
    }

    public async Task<GeneratorProjectDetail> CreateProjectAsync(CreateGeneratorProjectRequest request, long userId, CancellationToken ct = default)
    {
        var policy = await GetRequiredPolicyEntityAsync(false, ct);
        var priority = request.Priority ?? policy.DefaultProjectPriority;
        var quantity = request.Quantity ?? policy.DefaultProjectQuantity;
        ValidateProject(request.ProjectCode, request.ProjectName, request.PlannedStartAtUtc, request.PlannedDeliveryAtUtc, priority, quantity,
            request.HasStator, request.HasRotor, request.HasStiffener, request.IncludeFinalAssembly, policy);
        var code = request.ProjectCode.Trim();
        if (await Projects.AnyAsync(x => x.ProjectCode == code, ct)) throw AppException.Conflict("Bu jeneratör proje kodu zaten kullanılıyor.");
        await ValidateProductSelectionAsync(request.ProductId, ct);
        var entity = new GeneratorProductionProject
        {
            ProductionHeaderId = request.ProductionHeaderId, ProductId = request.ProductId, ProjectCode = code, ProjectName = request.ProjectName.Trim(),
            GeneratorType = Clean(request.GeneratorType), SerialNumber = Clean(request.SerialNumber), CustomerCodeSnapshot = Clean(request.CustomerCode),
            CustomerNameSnapshot = Clean(request.CustomerName), ExternalWorkOrderNo = Clean(request.ExternalWorkOrderNo), SourceSystemCode = Clean(request.SourceSystemCode),
            PlannedStartAtUtc = AsUtc(request.PlannedStartAtUtc), PlannedDeliveryAtUtc = AsUtc(request.PlannedDeliveryAtUtc), Priority = priority,
            Quantity = quantity, HasStator = request.HasStator, HasRotor = request.HasRotor, HasStiffener = request.HasStiffener,
            IncludeFinalAssembly = request.IncludeFinalAssembly, PlanningOrder = request.PlanningOrder,
            Description = Clean(request.Description), Status = GeneratorProjectStatus.Draft, CreatedBy = userId
        };
        await Projects.AddAsync(entity, ct); await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry("Create", nameof(GeneratorProductionProject), entity.Id.ToString(), "Success", "GeneratorProduction", NewValues: MapProject(entity)), ct);
        return MapProject(entity);
    }

    public async Task<GeneratorProjectDetail> UpdateProjectAsync(long id, UpdateGeneratorProjectRequest request, long userId, CancellationToken ct = default)
    {
        var policy = await GetRequiredPolicyEntityAsync(false, ct);
        await ValidateProductSelectionAsync(request.ProductId, ct);
        await uow.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var entity = await Projects.Query(true).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw AppException.NotFound("Jeneratör üretim projesi bulunamadı.");
            if (entity.Status is not (GeneratorProjectStatus.Draft or GeneratorProjectStatus.ReadyToPlan or GeneratorProjectStatus.Planned))
                throw AppException.Conflict("Serbest bırakılmış veya başlamış proje planlama bilgileri değiştirilemez.");
            if (!entity.RowVersion.SequenceEqual(DecodeRowVersion(request.RowVersion)))
                throw AppException.Conflict("Proje başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");
            ValidateProject(entity.ProjectCode, request.ProjectName, request.PlannedStartAtUtc, request.PlannedDeliveryAtUtc, request.Priority, request.Quantity,
                request.HasStator, request.HasRotor, request.HasStiffener, request.IncludeFinalAssembly, policy);
            var invalidatesPlan = entity.Status == GeneratorProjectStatus.Planned;
            if (invalidatesPlan) RequireReason(request.Reason, "Planı geçersiz kılan değişiklik nedeni", policy.MinimumPlanReasonLength);

            var old = MapProject(entity);
            var now = DateTime.UtcNow;
            entity.ProjectName = request.ProjectName.Trim(); entity.ProductId = request.ProductId; entity.GeneratorType = Clean(request.GeneratorType); entity.SerialNumber = Clean(request.SerialNumber);
            entity.CustomerCodeSnapshot = Clean(request.CustomerCode); entity.CustomerNameSnapshot = Clean(request.CustomerName);
            entity.PlannedStartAtUtc = AsUtc(request.PlannedStartAtUtc); entity.PlannedDeliveryAtUtc = AsUtc(request.PlannedDeliveryAtUtc);
            entity.Priority = request.Priority; entity.Quantity = request.Quantity; entity.HasStator = request.HasStator; entity.HasRotor = request.HasRotor;
            entity.HasStiffener = request.HasStiffener; entity.IncludeFinalAssembly = request.IncludeFinalAssembly; entity.PlanningOrder = request.PlanningOrder; entity.Description = Clean(request.Description);
            entity.Status = GeneratorProjectStatus.ReadyToPlan; entity.UpdatedBy = userId; entity.UpdatedDate = now;

            if (invalidatesPlan)
            {
                var operations = await uow.Repository<GeneratorProductionOperation>().Query(true).Where(x => x.ProjectId == id).ToListAsync(ct);
                var operationIds = operations.Select(x => x.Id).ToArray();
                var dependencies = operationIds.Length == 0 ? [] : await uow.Repository<GeneratorProductionOperationDependency>().Query(true)
                    .Where(x => operationIds.Contains(x.PredecessorOperationId) || operationIds.Contains(x.SuccessorOperationId)).ToListAsync(ct);
                foreach (var dependency in dependencies) { dependency.IsDeleted = true; dependency.DeletedDate = now; dependency.DeletedBy = userId; }
                foreach (var operation in operations) { operation.IsDeleted = true; operation.DeletedDate = now; operation.DeletedBy = userId; }
                await uow.Repository<GeneratorProductionPlanRevision>().AddAsync(new GeneratorProductionPlanRevision
                {
                    ProjectId = id, ActionType = "PlanInvalidated", Reason = request.Reason!.Trim(),
                    PreviousPlanJson = JsonSerializer.Serialize(operations.Select(x => new { x.Id, x.StationId, x.PlannedStartAtUtc, x.PlannedEndAtUtc })),
                    NewPlanJson = "[]", OccurredAtUtc = now, ActorUserId = userId, CreatedBy = userId
                }, ct);
            }
            await uow.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditLogWriteEntry("Update", nameof(GeneratorProductionProject), entity.Id.ToString(), "Success", "GeneratorProduction",
                request.Reason, OldValues: old, NewValues: MapProject(entity), ChangedFields: invalidatesPlan ? ["Project", "PlanInvalidated"] : ["Project"]), ct);
            await uow.CommitTransactionAsync(ct);
            return MapProject(entity);
        }
        catch
        {
            await uow.RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task<GeneratorProjectDetail> ReleaseProjectAsync(long id, ReleaseGeneratorProjectRequest request, long userId, CancellationToken ct = default)
    {
        var policy = await GetRequiredPolicyEntityAsync(false, ct);
        RequireReason(request.Reason, "Üretime serbest bırakma nedeni", policy.MinimumPlanReasonLength);
        await uow.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var entity = await Projects.Query(true).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw AppException.NotFound("Jeneratör üretim projesi bulunamadı.");
            if (!entity.RowVersion.SequenceEqual(DecodeRowVersion(request.RowVersion)))
                throw AppException.Conflict("Proje başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");
            if (entity.Status != GeneratorProjectStatus.Planned)
                throw AppException.Conflict("Yalnızca planı uygulanmış proje üretime serbest bırakılabilir.");

            var operations = await uow.Repository<GeneratorProductionOperation>().Query(true)
                .Where(x => x.ProjectId == id).ToListAsync(ct);
            if (operations.Count == 0)
                throw AppException.Conflict("Operasyon planı bulunmayan proje üretime serbest bırakılamaz.");
            if (operations.Any(x => x.Status is not (GeneratorOperationStatus.Planned or GeneratorOperationStatus.Ready)))
                throw AppException.Conflict("Başlamış, tamamlanmış veya bloke operasyonu bulunan plan bu işlemle serbest bırakılamaz.");

            var operationIds = operations.Select(x => x.Id).ToArray();
            var successorIds = await uow.Repository<GeneratorProductionOperationDependency>().Query()
                .Where(x => operationIds.Contains(x.SuccessorOperationId))
                .Select(x => x.SuccessorOperationId).Distinct().ToListAsync(ct);
            var successorSet = successorIds.ToHashSet();
            var now = DateTime.UtcNow;
            foreach (var operation in operations.Where(x => !successorSet.Contains(x.Id)))
            {
                operation.Status = GeneratorOperationStatus.Ready;
                operation.UpdatedBy = userId;
                operation.UpdatedDate = now;
            }
            entity.Status = GeneratorProjectStatus.Released;
            entity.UpdatedBy = userId;
            entity.UpdatedDate = now;
            await uow.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditLogWriteEntry("Release", nameof(GeneratorProductionProject), entity.Id.ToString(), "Success", "GeneratorProduction",
                request.Reason.Trim(), OldValues: new { Status = GeneratorProjectStatus.Planned },
                NewValues: new { entity.Status, ReadyOperationCount = operations.Count(x => x.Status == GeneratorOperationStatus.Ready) }), ct);
            await uow.CommitTransactionAsync(ct);
            return MapProject(entity);
        }
        catch
        {
            await uow.RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task DeleteProjectAsync(long id, long userId, CancellationToken ct = default)
    {
        var entity = await Projects.FindByIdAsync(id, true, ct) ?? throw AppException.NotFound("Jeneratör üretim projesi bulunamadı.");
        if (entity.Status is not (GeneratorProjectStatus.Draft or GeneratorProjectStatus.ReadyToPlan)) throw AppException.Conflict("Yalnızca planlanmamış taslak projeler silinebilir.");
        entity.IsDeleted = true; entity.DeletedDate = DateTime.UtcNow; entity.DeletedBy = userId; await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry("Delete", nameof(GeneratorProductionProject), id.ToString(), "Success", "GeneratorProduction", OldValues: MapProject(entity)), ct);
    }

    public async Task<GeneratorPolicyRow> GetPolicyAsync(CancellationToken ct = default)
    {
        var entity = await Policies.FirstOrDefaultAsync(x => x.PolicyKey == "DEFAULT", false, ct);
        return MapPolicy(entity ?? new GeneratorProductionPolicy());
    }

    public async Task<GeneratorPolicyRow> UpdatePolicyAsync(UpdateGeneratorPolicyRequest request, long userId, CancellationToken ct = default)
    {
        ValidatePolicy(request);
        var entity = await Policies.FirstOrDefaultAsync(x => x.PolicyKey == "DEFAULT", true, ct);
        var before = entity is null ? null : MapPolicy(entity);
        if (entity is null)
        {
            entity = new GeneratorProductionPolicy { CreatedBy = userId };
            await Policies.AddAsync(entity, ct);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.RowVersion) || !entity.RowVersion.SequenceEqual(DecodeRowVersion(request.RowVersion)))
                throw AppException.Conflict("Jeneratör üretim parametreleri başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");
        }

        ApplyPolicy(entity, request);
        entity.UpdatedBy = userId;
        entity.UpdatedDate = DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
        var result = MapPolicy(entity);
        await audit.WriteAsync(new AuditLogWriteEntry("UpdatePolicy", nameof(GeneratorProductionPolicy), entity.Id.ToString(), "Success", "GeneratorProduction",
            OldValues: before, NewValues: result, ChangedFields: ["Policy"]), ct);
        return result;
    }

    public async Task<GeneratorRuleRow> UpdateRuleAsync(long id, UpdateGeneratorRuleRequest request, long userId, CancellationToken ct = default)
    {
        var entity = await uow.Repository<GeneratorProductionRule>().FindByIdAsync(id, true, ct)
            ?? throw AppException.NotFound("Planlama kuralı bulunamadı.");
        if (!entity.RowVersion.SequenceEqual(DecodeRowVersion(request.RowVersion)))
            throw AppException.Conflict("Planlama kuralı başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            throw AppException.BadRequest("Kural adı zorunludur ve en fazla 200 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 1000)
            throw AppException.BadRequest("Kural açıklaması zorunludur ve en fazla 1000 karakter olabilir.");
        ValidateJson(request.ParametersJson);
        ValidateRuleParameters(entity.Code, request.IsEnabled, request.ParametersJson);
        if (entity.IsSystemRequired && (!request.IsEnabled || request.Severity != GeneratorRuleSeverity.Error))
            throw AppException.BadRequest("Sistem bütünlüğü kuralı kapatılamaz ve hata seviyesinden düşürülemez.");

        var before = MapRule(entity);
        entity.Name = request.Name.Trim();
        entity.Description = request.Description.Trim();
        entity.Severity = request.Severity;
        entity.IsEnabled = request.IsEnabled;
        entity.ParametersJson = Clean(request.ParametersJson);
        entity.UpdatedBy = userId;
        entity.UpdatedDate = DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
        var result = MapRule(entity);
        await audit.WriteAsync(new AuditLogWriteEntry("UpdateRule", nameof(GeneratorProductionRule), entity.Id.ToString(), "Success", "GeneratorProduction",
            OldValues: before, NewValues: result, ChangedFields: ["Rule"]), ct);
        return result;
    }

    public async Task<GeneratorDefinitionsResult> GetDefinitionsAsync(CancellationToken ct = default)
    {
        var policy = await GetPolicyAsync(ct);
        var stations = await uow.Repository<GeneratorProductionStation>().Query().OrderBy(x => x.PlanningOrder)
            .Select(x => new GeneratorStationRow(x.Id, x.Code, x.Name, x.Area, x.PlanningOrder, x.MaxParallelJobs,
                x.DefaultPersonnelCapacity, x.IsActive, x.IsCritical, x.IsBottleneck, x.RequiresCrane, x.RequiresTransport, x.Description,
                Convert.ToBase64String(x.RowVersion))).ToListAsync(ct);
        var shifts = await uow.Repository<GeneratorProductionShift>().Query().OrderBy(x => x.PlanningOrder)
            .Select(x => new GeneratorShiftRow(x.Id, x.Code, x.Name, x.StartTime, x.EndTime, x.PlanningOrder, x.IsActive, Convert.ToBase64String(x.RowVersion))).ToListAsync(ct);
        var stationShifts = await uow.Repository<GeneratorProductionStationShift>().Query()
            .OrderBy(x => x.Station.PlanningOrder).ThenBy(x => x.Shift.PlanningOrder)
            .Select(x => new GeneratorStationShiftRow(
                x.Id, x.StationId, x.Station.Code, x.Station.Name, x.ShiftId, x.Shift.Code, x.Shift.Name,
                x.WeekdayMask, x.CapacityMinutes, x.PersonnelCapacity, x.MachineCapacity,
                x.CraneAvailable, x.TransportAvailable, x.IsActive, Convert.ToBase64String(x.RowVersion))).ToListAsync(ct);
        var calendarExceptions = await uow.Repository<GeneratorProductionCalendarException>().Query()
            .OrderBy(x => x.ExceptionDate).ThenBy(x => x.StationId)
            .Select(x => new GeneratorCalendarExceptionRow(
                x.Id, x.StationId, x.Station == null ? null : x.Station.Code, x.ShiftId, x.Shift == null ? null : x.Shift.Code,
                x.ExceptionDate, x.IsWorking, x.CapacityMinutes, x.Reason)).ToListAsync(ct);
        var resourceEntities = await uow.Repository<GeneratorProductionResource>().Query().OrderBy(x => x.ResourceType).ThenBy(x => x.Code).ToListAsync(ct);
        var resourceAssignments = await uow.Repository<GeneratorProductionStationResource>().Query()
            .OrderBy(x => x.Station.PlanningOrder)
            .Select(x => new { x.ResourceId, Row = new GeneratorResourceStationRow(x.StationId, x.Station.Code, x.Station.Name, x.RequiredQuantity) })
            .ToListAsync(ct);
        var resources = resourceEntities.Select(x => new GeneratorResourceRow(
            x.Id, x.Code, x.Name, x.ResourceType, x.Capacity, x.IsExclusive, x.IsActive,
            resourceAssignments.Where(a => a.ResourceId == x.Id).Select(a => a.Row).ToArray(), Convert.ToBase64String(x.RowVersion))).ToArray();
        var routeEntities = await uow.Repository<GeneratorProductionRoute>().Query()
            .Include(x => x.Operations).ThenInclude(x => x.Station)
            .Include(x => x.Dependencies)
            .OrderBy(x => x.PartType).ThenBy(x => x.Code).ToListAsync(ct);
        var routes = routeEntities.Select(x => new GeneratorRouteRow(x.Id, x.Code, x.Name, x.PartType, x.VersionNumber, x.IsActive,
            x.Operations.OrderBy(o => o.Sequence).Select(o => new GeneratorRouteOperationRow(o.Id, o.OperationCode, o.OperationName, o.Sequence,
                o.DurationMinutes, o.MinimumDurationMinutes, o.MaximumDurationMinutes, o.IsCritical, o.StationId, o.Station.Code, o.Station.Name,
                Convert.ToBase64String(o.RowVersion))).ToArray(),
            x.Dependencies.Select(d => new GeneratorRouteDependencyRow(
                d.Id, d.PredecessorOperationId, d.SuccessorOperationId, d.DependencyType, d.LagMinutes)).ToArray())).ToArray();
        var rules = await uow.Repository<GeneratorProductionRule>().Query().OrderByDescending(x => x.Severity).ThenBy(x => x.Code)
            .Select(x => new GeneratorRuleRow(x.Id, x.Code, x.Name, x.Description, x.Severity, x.IsEnabled, x.IsSystemRequired, x.ParametersJson,
                Convert.ToBase64String(x.RowVersion))).ToListAsync(ct);
        var products = await GetProductRowsAsync(ct);
        var capabilities = await GetStationCapabilityRowsAsync(ct);
        var materials = await GetOperationMaterialRowsAsync(ct);
        var warehouses = await GetWarehouseOptionsAsync(ct);
        return new GeneratorDefinitionsResult(policy, stations, shifts, stationShifts, calendarExceptions, resources, routes,
            products, capabilities, materials, warehouses, rules, stations.Count > 0 && routes.Length > 0);
    }

    public async Task<GeneratorBootstrapResult> BootstrapDefinitionsAsync(long userId, CancellationToken ct = default)
    {
        if (await uow.Repository<GeneratorProductionStation>().AnyAsync(x => true, ct)) throw AppException.Conflict("Jeneratör üretim tanımları bu şube için daha önce oluşturulmuş.");
        await uow.BeginTransactionAsync(cancellationToken: ct);
        try
        {
            if (!await Policies.AnyAsync(x => x.PolicyKey == "DEFAULT", ct))
            {
                await Policies.AddAsync(new GeneratorProductionPolicy { CreatedBy = userId }, ct);
                await uow.SaveChangesAsync(ct);
            }
            var stations = DefaultStations(userId); await uow.Repository<GeneratorProductionStation>().AddRangeAsync(stations, ct); await uow.SaveChangesAsync(ct);
            var shifts = new[]
            {
                new GeneratorProductionShift { Code = "GUNDUZ", Name = "Gündüz Vardiyası", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0), PlanningOrder = 10, CreatedBy = userId },
                new GeneratorProductionShift { Code = "AKSAM", Name = "Akşam Vardiyası", StartTime = new TimeOnly(17, 0), EndTime = new TimeOnly(1, 0), PlanningOrder = 20, IsActive = false, CreatedBy = userId }
            };
            await uow.Repository<GeneratorProductionShift>().AddRangeAsync(shifts, ct); await uow.SaveChangesAsync(ct);
            await uow.Repository<GeneratorProductionStationShift>().AddRangeAsync(stations.Select(s => new GeneratorProductionStationShift
            {
                StationId = s.Id, ShiftId = shifts[0].Id, WeekdayMask = 31, CapacityMinutes = 480,
                PersonnelCapacity = s.DefaultPersonnelCapacity, MachineCapacity = s.MaxParallelJobs,
                CraneAvailable = true, TransportAvailable = true, CreatedBy = userId
            }), ct);

            var resources = DefaultResources(userId); await uow.Repository<GeneratorProductionResource>().AddRangeAsync(resources, ct); await uow.SaveChangesAsync(ct);
            var resourceByCode = resources.ToDictionary(x => x.Code); var stationByCode = stations.ToDictionary(x => x.Code);
            var assignments = DefaultStationResources(stationByCode, resourceByCode, userId);
            await uow.Repository<GeneratorProductionStationResource>().AddRangeAsync(assignments, ct);

            var routes = new[]
            {
                new GeneratorProductionRoute { Code = "GEN-STATOR", Name = "Jeneratör Stator Rotası", PartType = GeneratorPartType.Stator, CreatedBy = userId },
                new GeneratorProductionRoute { Code = "GEN-ROTOR", Name = "Jeneratör Rotor Rotası", PartType = GeneratorPartType.Rotor, CreatedBy = userId },
                new GeneratorProductionRoute { Code = "GEN-STIFFENER", Name = "Jeneratör Taşıyıcı Kol Rotası", PartType = GeneratorPartType.Stiffener, CreatedBy = userId },
                new GeneratorProductionRoute { Code = "GEN-FINAL", Name = "Jeneratör Final Montaj Rotası", PartType = GeneratorPartType.FinalAssembly, CreatedBy = userId }
            };
            await uow.Repository<GeneratorProductionRoute>().AddRangeAsync(routes, ct); await uow.SaveChangesAsync(ct);
            var routeByPart = routes.ToDictionary(x => x.PartType);
            var routeOperations = DefaultRouteOperations(routeByPart, stationByCode, userId);
            await uow.Repository<GeneratorProductionRouteOperation>().AddRangeAsync(routeOperations, ct); await uow.SaveChangesAsync(ct);
            var dependencies = new List<GeneratorProductionRouteDependency>();
            foreach (var route in routes)
            {
                var ordered = routeOperations.Where(x => x.RouteId == route.Id).OrderBy(x => x.Sequence).ToArray();
                for (var i = 1; i < ordered.Length; i++) dependencies.Add(new GeneratorProductionRouteDependency
                {
                    RouteId = route.Id, PredecessorOperationId = ordered[i - 1].Id, SuccessorOperationId = ordered[i].Id, CreatedBy = userId
                });
            }
            await uow.Repository<GeneratorProductionRouteDependency>().AddRangeAsync(dependencies, ct);
            var defaultProduct = new GeneratorProductionProduct
            {
                Code = "GEN-STD", Name = "Standart Jeneratör", GeneratorType = "Standart",
                Description = "Başlangıç SA/RA/FA rotalarıyla oluşturulan varsayılan jeneratör ürün tanımı.", CreatedBy = userId
            };
            await uow.Repository<GeneratorProductionProduct>().AddAsync(defaultProduct, ct); await uow.SaveChangesAsync(ct);
            await uow.Repository<GeneratorProductionProductRoute>().AddRangeAsync(routes.Select(route => new GeneratorProductionProductRoute
            {
                ProductId = defaultProduct.Id, PartType = route.PartType, RouteId = route.Id, CreatedBy = userId
            }), ct);
            await uow.Repository<GeneratorProductionStationCapability>().AddRangeAsync(routeOperations.Select(operation => new GeneratorProductionStationCapability
            {
                ProductId = defaultProduct.Id, RouteOperationId = operation.Id, StationId = operation.StationId,
                IsPrimary = true, EfficiencyPercent = 100, CreatedBy = userId
            }), ct);
            var rules = DefaultRules(userId); await uow.Repository<GeneratorProductionRule>().AddRangeAsync(rules, ct); await uow.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditLogWriteEntry("Bootstrap", "GeneratorProductionDefinitions", "branch", "Success", "GeneratorProduction",
                NewValues: new { Stations = stations.Count, Routes = routes.Length, Operations = routeOperations.Count, Rules = rules.Count }), ct);
            await uow.CommitTransactionAsync(ct);
            return new GeneratorBootstrapResult(stations.Count, routes.Length, routeOperations.Count, rules.Count);
        }
        catch { await uow.RollbackTransactionAsync(ct); throw; }
    }

    public Task<GeneratorPlanPreviewResult> PreviewPlanAsync(GeneratorPlanPreviewRequest request, CancellationToken ct = default) => BuildPlanAsync(request.ProjectIds, request.EarliestStartAtUtc, ct);

    public async Task<GeneratorPlanApplyResult> ApplyPlanAsync(GeneratorPlanApplyRequest request, long userId, CancellationToken ct = default)
    {
        var policy = await GetRequiredPolicyEntityAsync(false, ct);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < policy.MinimumPlanReasonLength)
            throw AppException.BadRequest($"Plan uygulama nedeni en az {policy.MinimumPlanReasonLength} karakter olmalıdır.");
        await uow.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            // Önizleme ile kayıt arasındaki stok/kapasite değişimini aynı seri işlem içinde yeniden değerlendir.
            var preview = await BuildPlanAsync(request.ProjectIds, request.EarliestStartAtUtc, ct);
            if (!preview.CanApply) throw AppException.Conflict("Planlama önizlemesinde engelleyici hatalar var.");
            var projectIds = preview.Items.Select(x => x.ProjectId).Distinct().ToArray();
            var projects = await Projects.Query(true).Where(x => projectIds.Contains(x.Id)).ToListAsync(ct);
            var oldOperations = await uow.Repository<GeneratorProductionOperation>().Query(true).Where(x => projectIds.Contains(x.ProjectId)).ToListAsync(ct);
            if (oldOperations.Any(x => x.Status is GeneratorOperationStatus.InProgress or GeneratorOperationStatus.Completed))
                throw AppException.Conflict("Başlamış veya tamamlanmış operasyonu bulunan proje yeniden planlanamaz.");
            var oldIds = oldOperations.Select(x => x.Id).ToArray();
            var oldDependencies = oldIds.Length == 0 ? [] : await uow.Repository<GeneratorProductionOperationDependency>().Query(true)
                .Where(x => oldIds.Contains(x.PredecessorOperationId) || oldIds.Contains(x.SuccessorOperationId)).ToListAsync(ct);
            foreach (var dependency in oldDependencies) { dependency.IsDeleted = true; dependency.DeletedDate = DateTime.UtcNow; dependency.DeletedBy = userId; }
            foreach (var operation in oldOperations) { operation.IsDeleted = true; operation.DeletedDate = DateTime.UtcNow; operation.DeletedBy = userId; }
            await uow.SaveChangesAsync(ct);

            var created = preview.Items.Select(item => new GeneratorProductionOperation
            {
                ProjectId = item.ProjectId, RouteOperationId = item.RouteOperationId, StationId = item.StationId, UnitIndex = item.UnitIndex,
                Status = GeneratorOperationStatus.Planned, PlannedStartAtUtc = item.PlannedStartAtUtc, PlannedEndAtUtc = item.PlannedEndAtUtc,
                IsCritical = item.IsCritical, HasMaterialShortage = item.HasMaterialShortage,
                IsScheduleLocked = item.IsScheduleLocked, ManualScheduleReason = item.ManualScheduleReason,
                ManualScheduledBy = item.IsScheduleLocked ? userId : null, ManualScheduledAtUtc = item.IsScheduleLocked ? DateTime.UtcNow : null,
                CreatedBy = userId
            }).ToArray();
            await uow.Repository<GeneratorProductionOperation>().AddRangeAsync(created, ct); await uow.SaveChangesAsync(ct);
            var byKey = preview.Items.Zip(created).ToDictionary(x => x.First.Key, x => x.Second);
            var dependencies = preview.Items.SelectMany(item => item.Predecessors.Select(predecessor => new GeneratorProductionOperationDependency
            {
                PredecessorOperationId = byKey[predecessor.Key].Id, SuccessorOperationId = byKey[item.Key].Id,
                DependencyType = predecessor.DependencyType, LagMinutes = predecessor.LagMinutes, CreatedBy = userId
            })).ToArray();
            await uow.Repository<GeneratorProductionOperationDependency>().AddRangeAsync(dependencies, ct);
            foreach (var project in projects) { project.Status = GeneratorProjectStatus.Planned; project.UpdatedBy = userId; project.UpdatedDate = DateTime.UtcNow; }
            var revision = new GeneratorProductionPlanRevision
            {
                ProjectId = projectIds.Length == 1 ? projectIds[0] : null, ActionType = oldOperations.Count == 0 ? "PlanCreated" : "PlanReplaced",
                Reason = request.Reason.Trim(), PreviousPlanJson = oldOperations.Count == 0 ? null : JsonSerializer.Serialize(oldOperations.Select(x => new { x.Id, x.ProjectId, x.StationId, x.PlannedStartAtUtc, x.PlannedEndAtUtc })),
                NewPlanJson = JsonSerializer.Serialize(preview.Items), OccurredAtUtc = DateTime.UtcNow, ActorUserId = userId, CreatedBy = userId
            };
            await uow.Repository<GeneratorProductionPlanRevision>().AddAsync(revision, ct); await uow.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditLogWriteEntry("ApplyPlan", nameof(GeneratorProductionPlanRevision), revision.Id.ToString(), "Success", "GeneratorProduction", request.Reason,
                NewValues: new { ProjectIds = projectIds, OperationCount = created.Length, DependencyCount = dependencies.Length }), ct);
            await uow.CommitTransactionAsync(ct);
            return new GeneratorPlanApplyResult(projects.Count, created.Length, dependencies.Length, revision.Id, preview.Issues);
        }
        catch { await uow.RollbackTransactionAsync(ct); throw; }
    }

    public async Task<IReadOnlyList<GeneratorScheduleRow>> GetScheduleAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var policy = await GetRequiredPolicyEntityAsync(false, ct);
        fromUtc = AsUtc(fromUtc); toUtc = AsUtc(toUtc); if (toUtc <= fromUtc) throw AppException.BadRequest("Takvim bitişi başlangıçtan sonra olmalıdır.");
        if ((toUtc - fromUtc).TotalDays > policy.MaximumScheduleRangeDays)
            throw AppException.BadRequest($"Takvim aralığı en fazla {policy.MaximumScheduleRangeDays} gün olabilir.");
        return await uow.Repository<GeneratorProductionOperation>().Query()
            .Where(x => x.PlannedStartAtUtc < toUtc && x.PlannedEndAtUtc > fromUtc)
            .OrderBy(x => x.Station.PlanningOrder).ThenBy(x => x.PlannedStartAtUtc)
            .Select(x => new GeneratorScheduleRow(x.Id, x.ProjectId, x.Project.ProjectCode, x.Project.ProjectName, x.UnitIndex, x.RouteOperation.Route.PartType,
                x.StationId, x.Station.Code, x.Station.Name, x.RouteOperation.OperationCode, x.RouteOperation.OperationName, x.Status,
                x.PlannedStartAtUtc, x.PlannedEndAtUtc, x.ActualStartAtUtc, x.ActualEndAtUtc, x.IsCritical, x.HasMaterialShortage, x.HasProblem,
                x.IsScheduleLocked, x.ManualScheduleReason, Convert.ToBase64String(x.RowVersion),
                x.QualityGate == null ? null : (GeneratorQualityGateStatus?)x.QualityGate.Status,
                x.QualityGate == null ? null : Convert.ToBase64String(x.QualityGate.RowVersion), x.RouteOperationId, x.Project.ProductId)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GeneratorScheduleRow>> GetProjectOperationsAsync(long projectId, CancellationToken ct = default)
    {
        if (!await Projects.AnyAsync(x => x.Id == projectId, ct)) throw AppException.NotFound("Jeneratör üretim projesi bulunamadı.");
        return await uow.Repository<GeneratorProductionOperation>().Query()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.UnitIndex).ThenBy(x => x.PlannedStartAtUtc).ThenBy(x => x.Station.PlanningOrder)
            .Select(x => new GeneratorScheduleRow(x.Id, x.ProjectId, x.Project.ProjectCode, x.Project.ProjectName, x.UnitIndex, x.RouteOperation.Route.PartType,
                x.StationId, x.Station.Code, x.Station.Name, x.RouteOperation.OperationCode, x.RouteOperation.OperationName, x.Status,
                x.PlannedStartAtUtc, x.PlannedEndAtUtc, x.ActualStartAtUtc, x.ActualEndAtUtc, x.IsCritical, x.HasMaterialShortage, x.HasProblem,
                x.IsScheduleLocked, x.ManualScheduleReason, Convert.ToBase64String(x.RowVersion),
                x.QualityGate == null ? null : (GeneratorQualityGateStatus?)x.QualityGate.Status,
                x.QualityGate == null ? null : Convert.ToBase64String(x.QualityGate.RowVersion), x.RouteOperationId, x.Project.ProductId)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GeneratorPlanRevisionRow>> GetPlanRevisionsAsync(long? projectId, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        var query = uow.Repository<GeneratorProductionPlanRevision>().Query();
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        var rows = await query.OrderByDescending(x => x.OccurredAtUtc).Take(take)
            .Select(x => new { x.Id, x.ProjectId, ProjectCode = x.Project == null ? null : x.Project.ProjectCode, x.ActionType, x.Reason,
                x.OccurredAtUtc, x.ActorUserId, HasPreviousPlan = x.PreviousPlanJson != null, x.NewPlanJson })
            .ToListAsync(ct);
        return rows.Select(x => new GeneratorPlanRevisionRow(
            x.Id, x.ProjectId, x.ProjectCode, x.ActionType, x.Reason, x.OccurredAtUtc, x.ActorUserId,
            x.HasPreviousPlan, CountRevisionOperations(x.NewPlanJson))).ToArray();
    }

    public async Task<GeneratorScheduleRow> TransitionOperationAsync(long operationId, GeneratorOperationTransitionRequest request, long userId, CancellationToken ct = default)
    {
        var policy = await GetRequiredPolicyEntityAsync(false, ct);
        var operation = await uow.Repository<GeneratorProductionOperation>().Query(true)
            .Include(x => x.Project).Include(x => x.Station).Include(x => x.RouteOperation).ThenInclude(x => x.Route).Include(x => x.QualityGate)
            .FirstOrDefaultAsync(x => x.Id == operationId, ct)
            ?? throw AppException.NotFound("Jeneratör üretim operasyonu bulunamadı.");
        if (!operation.RowVersion.SequenceEqual(DecodeRowVersion(request.RowVersion)))
            throw AppException.Conflict("Operasyon başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");
        if (request.GoodQuantity < 0 || request.DefectQuantity < 0 || request.ScrapQuantity < 0)
            throw AppException.BadRequest("Üretim miktarları negatif olamaz.");

        var oldValues = new { operation.Status, operation.ActualStartAtUtc, operation.ActualEndAtUtc, operation.HasProblem, operation.ProblemDescription };
        var now = DateTime.UtcNow;
        switch (request.Action)
        {
            case GeneratorOperationAction.Start:
                if (operation.Project.Status is not (GeneratorProjectStatus.Released or GeneratorProjectStatus.InProgress))
                    throw AppException.Conflict("Proje üretime serbest bırakılmadan operasyon başlatılamaz.");
                if (operation.Status != GeneratorOperationStatus.Ready)
                    throw AppException.Conflict("Yalnızca bağımlılıkları karşılanmış hazır operasyon başlatılabilir.");
                var predecessorStates = await uow.Repository<GeneratorProductionOperationDependency>().Query()
                    .Where(x => x.SuccessorOperationId == operationId)
                    .Select(x => new { x.PredecessorOperationId, x.DependencyType, x.PredecessorOperation.Status, x.PredecessorOperation.ActualStartAtUtc })
                    .ToListAsync(ct);
                if (predecessorStates.Any(x => x.DependencyType == GeneratorDependencyType.FinishToStart
                        ? x.Status != GeneratorOperationStatus.Completed
                        : x.ActualStartAtUtc == null))
                    throw AppException.Conflict("Operasyon bağımlılıkları karşılanmadan bu operasyon başlatılamaz.");
                if (await HasBlockingQualityInspectionAsync(predecessorStates.Select(x => x.PredecessorOperationId), ct))
                    throw AppException.Conflict("Öncül operasyonun kalite kontrolü kabul edilmeden sonraki operasyon başlatılamaz.");
                if (policy.RequireMaterialAvailabilityToStart)
                {
                    var materialCheck = await CheckOperationMaterialAvailabilityAsync(operation, now, policy.InboundQualityBufferDays, ct);
                    operation.HasMaterialShortage = materialCheck.HasShortage;
                    if (materialCheck.HasShortage)
                        throw AppException.Conflict(materialCheck.Message ?? "Zorunlu malzeme güncel stok ve termin hesabında kullanılabilir değil.");
                }
                operation.Status = GeneratorOperationStatus.InProgress; operation.ActualStartAtUtc ??= now;
                operation.Project.Status = GeneratorProjectStatus.InProgress;
                break;
            case GeneratorOperationAction.Pause:
                if (operation.Status != GeneratorOperationStatus.InProgress) throw AppException.Conflict("Yalnızca devam eden operasyon duraklatılabilir.");
                RequireReason(request.Reason, "Duraklatma nedeni", policy.MinimumOperationReasonLength); operation.Status = GeneratorOperationStatus.Paused; operation.ProblemDescription = Clean(request.Reason);
                break;
            case GeneratorOperationAction.Resume:
                if (operation.Status != GeneratorOperationStatus.Paused) throw AppException.Conflict("Yalnızca duraklatılmış operasyon devam ettirilebilir.");
                operation.Status = GeneratorOperationStatus.InProgress; operation.ProblemDescription = null;
                break;
            case GeneratorOperationAction.Complete:
                if (operation.Status != GeneratorOperationStatus.InProgress) throw AppException.Conflict("Yalnızca devam eden operasyon tamamlanabilir.");
                var unfinishedFinishDependencies = await uow.Repository<GeneratorProductionOperationDependency>().Query()
                    .AnyAsync(x => x.SuccessorOperationId == operationId
                        && x.DependencyType == GeneratorDependencyType.FinishToFinish
                        && x.PredecessorOperation.Status != GeneratorOperationStatus.Completed, ct);
                if (unfinishedFinishDependencies) throw AppException.Conflict("Finish-to-finish öncülleri tamamlanmadan operasyon tamamlanamaz.");
                if (policy.RequireProblemClosureToComplete && operation.HasProblem) throw AppException.Conflict("Açık problem çözülmeden operasyon tamamlanamaz.");
                if (policy.RequireMaterialAvailabilityToStart)
                {
                    var materialCheck = await CheckOperationMaterialAvailabilityAsync(operation, operation.ActualStartAtUtc ?? now, policy.InboundQualityBufferDays, ct);
                    operation.HasMaterialShortage = materialCheck.HasShortage;
                    if (materialCheck.HasShortage)
                        throw AppException.Conflict(materialCheck.Message ?? "Malzeme eksiği çözülmeden operasyon tamamlanamaz.");
                }
                if (policy.RequirePositiveCompletionQuantity && request.GoodQuantity + request.DefectQuantity + request.ScrapQuantity <= 0) throw AppException.BadRequest("Tamamlama için en az bir üretim miktarı girilmelidir.");
                operation.Status = GeneratorOperationStatus.Completed; operation.ActualEndAtUtc = now;
                operation.GoodQuantity = request.GoodQuantity; operation.DefectQuantity = request.DefectQuantity; operation.ScrapQuantity = request.ScrapQuantity;
                if (operation.IsCritical && operation.QualityGate is null)
                {
                    operation.QualityGate = new GeneratorProductionQualityGate
                    {
                        OperationId = operation.Id,
                        Status = GeneratorQualityGateStatus.Pending,
                        RequestedAtUtc = now,
                        CreatedBy = userId
                    };
                    await uow.Repository<GeneratorProductionQualityGate>().AddAsync(operation.QualityGate, ct);
                }
                break;
            case GeneratorOperationAction.ReportProblem:
                RequireReason(request.Reason, "Problem açıklaması", policy.MinimumOperationReasonLength); operation.HasProblem = true; operation.ProblemDescription = Clean(request.Reason);
                break;
            case GeneratorOperationAction.ResolveProblem:
                if (!operation.HasProblem) throw AppException.Conflict("Operasyonda açık problem bulunmuyor.");
                RequireReason(request.Reason, "Çözüm açıklaması", policy.MinimumOperationReasonLength); operation.HasProblem = false; operation.ProblemDescription = Clean(request.Reason);
                break;
            default: throw AppException.BadRequest("Desteklenmeyen operasyon işlemi.");
        }

        operation.UpdatedBy = userId; operation.UpdatedDate = now; operation.Project.UpdatedBy = userId; operation.Project.UpdatedDate = now;
        await uow.SaveChangesAsync(ct);
        if (request.Action is GeneratorOperationAction.Start or GeneratorOperationAction.Complete)
        {
            await RefreshSuccessorReadinessAsync(operationId, userId, now, ct);
        }
        if (request.Action == GeneratorOperationAction.Complete)
        {
            var hasOpenOperation = await uow.Repository<GeneratorProductionOperation>().Query()
                .AnyAsync(x => x.ProjectId == operation.ProjectId && x.Id != operationId && x.Status != GeneratorOperationStatus.Completed && x.Status != GeneratorOperationStatus.Cancelled, ct);
            var hasOpenQualityGate = await uow.Repository<GeneratorProductionOperation>().Query()
                .AnyAsync(x => x.ProjectId == operation.ProjectId && x.IsCritical
                    && (x.QualityGate == null || x.QualityGate.Status != GeneratorQualityGateStatus.Passed), ct);
            if (!hasOpenOperation && !hasOpenQualityGate) operation.Project.Status = GeneratorProjectStatus.Completed;
            await uow.SaveChangesAsync(ct);
        }
        await audit.WriteAsync(new AuditLogWriteEntry(request.Action.ToString(), nameof(GeneratorProductionOperation), operation.Id.ToString(), "Success", "GeneratorProduction",
            request.Reason, OldValues: oldValues, NewValues: new { operation.Status, operation.ActualStartAtUtc, operation.ActualEndAtUtc, operation.GoodQuantity, operation.DefectQuantity, operation.ScrapQuantity, operation.HasProblem, operation.ProblemDescription }), ct);
        return ToScheduleRow(operation);
    }

    public async Task<GeneratorScheduleRow> DecideOperationQualityAsync(long operationId, GeneratorQualityDecisionRequest request, long userId, CancellationToken ct = default)
    {
        if (request.Status is not (GeneratorQualityGateStatus.Passed or GeneratorQualityGateStatus.Rejected))
            throw AppException.BadRequest("Kalite kararı kabul veya ret olmalıdır.");
        var policy = await GetRequiredPolicyEntityAsync(false, ct);
        RequireReason(request.Reason, "Kalite karar açıklaması", policy.MinimumOperationReasonLength);
        var operation = await uow.Repository<GeneratorProductionOperation>().Query(true)
            .Include(x => x.Project).Include(x => x.Station).Include(x => x.RouteOperation).ThenInclude(x => x.Route).Include(x => x.QualityGate)
            .FirstOrDefaultAsync(x => x.Id == operationId, ct)
            ?? throw AppException.NotFound("Jeneratör üretim operasyonu bulunamadı.");
        if (!operation.IsCritical)
            throw AppException.Conflict("Bu operasyon kalite kabul kapısı olarak tanımlı değil.");
        if (operation.Status != GeneratorOperationStatus.Completed)
            throw AppException.Conflict("Operasyon tamamlanmadan kalite kararı verilemez.");
        var gate = operation.QualityGate ?? throw AppException.Conflict("Operasyon için bekleyen kalite kontrolü bulunamadı.");
        if (!gate.RowVersion.SequenceEqual(DecodeRowVersion(request.RowVersion)))
            throw AppException.Conflict("Kalite kontrolü başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");

        var oldStatus = gate.Status;
        var now = DateTime.UtcNow;
        gate.Status = request.Status;
        gate.DecisionBy = userId;
        gate.DecisionAtUtc = now;
        gate.DecisionNote = request.Reason.Trim();
        gate.UpdatedBy = userId;
        gate.UpdatedDate = now;
        await uow.SaveChangesAsync(ct);
        if (request.Status == GeneratorQualityGateStatus.Passed)
        {
            await RefreshSuccessorReadinessAsync(operationId, userId, now, ct);
            var hasOpenOperation = await uow.Repository<GeneratorProductionOperation>().Query()
                .AnyAsync(x => x.ProjectId == operation.ProjectId && x.Status != GeneratorOperationStatus.Completed && x.Status != GeneratorOperationStatus.Cancelled, ct);
            var hasOpenQualityGate = await uow.Repository<GeneratorProductionOperation>().Query()
                .AnyAsync(x => x.ProjectId == operation.ProjectId && x.IsCritical
                    && (x.QualityGate == null || x.QualityGate.Status != GeneratorQualityGateStatus.Passed), ct);
            if (!hasOpenOperation && !hasOpenQualityGate)
            {
                operation.Project.Status = GeneratorProjectStatus.Completed;
                operation.Project.UpdatedBy = userId;
                operation.Project.UpdatedDate = now;
                await uow.SaveChangesAsync(ct);
            }
        }
        await audit.WriteAsync(new AuditLogWriteEntry("QualityDecision", nameof(GeneratorProductionQualityGate), gate.Id.ToString(), "Success", "GeneratorProduction",
            request.Reason.Trim(), OldValues: new { Status = oldStatus }, NewValues: new { gate.Status, gate.DecisionBy, gate.DecisionAtUtc }), ct);
        return ToScheduleRow(operation);
    }

    private async Task RefreshSuccessorReadinessAsync(long predecessorOperationId, long userId, DateTime now, CancellationToken ct)
    {
        var changed = false;
        var successorIds = await uow.Repository<GeneratorProductionOperationDependency>().Query()
            .Where(x => x.PredecessorOperationId == predecessorOperationId)
            .Select(x => x.SuccessorOperationId)
            .Distinct()
            .ToListAsync(ct);
        foreach (var successorId in successorIds)
        {
            var states = await uow.Repository<GeneratorProductionOperationDependency>().Query()
                .Where(x => x.SuccessorOperationId == successorId)
                .Select(x => new { x.PredecessorOperationId, x.DependencyType, x.PredecessorOperation.Status, x.PredecessorOperation.ActualStartAtUtc })
                .ToListAsync(ct);
            var isReady = states.All(x => x.DependencyType == GeneratorDependencyType.FinishToStart
                ? x.Status == GeneratorOperationStatus.Completed
                : x.ActualStartAtUtc != null);
            if (!isReady) continue;
            if (await HasBlockingQualityInspectionAsync(states.Select(x => x.PredecessorOperationId), ct)) continue;
            var successor = await uow.Repository<GeneratorProductionOperation>().FindByIdAsync(successorId, true, ct);
            if (successor?.Status == GeneratorOperationStatus.Planned)
            {
                successor.Status = GeneratorOperationStatus.Ready;
                successor.UpdatedBy = userId;
                successor.UpdatedDate = now;
                changed = true;
            }
        }
        if (changed) await uow.SaveChangesAsync(ct);
    }

    private async Task<bool> HasBlockingQualityInspectionAsync(IEnumerable<long> operationIds, CancellationToken ct)
    {
        var ids = operationIds.Distinct().ToArray();
        if (ids.Length == 0) return false;
        var generatorGates = await uow.Repository<GeneratorProductionOperation>().Query()
            .Where(x => ids.Contains(x.Id) && x.IsCritical)
            .Select(x => new { x.Id, Status = x.QualityGate == null ? null : (GeneratorQualityGateStatus?)x.QualityGate.Status })
            .ToListAsync(ct);
        if (generatorGates.Any(x => x.Status != GeneratorQualityGateStatus.Passed)) return true;
        var inspections = await uow.Repository<QualityInspection>().Query()
            .Where(x => x.SourceDocumentType == "GeneratorProductionOperation" && ids.Contains(x.SourceDocumentId))
            .Select(x => new { x.SourceDocumentId, x.Status, x.CreatedAtUtc, x.Id }).ToListAsync(ct);
        return inspections.GroupBy(x => x.SourceDocumentId)
            .Select(x => x.OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id).First())
            .Any(x => x.Status is not (QualityInspectionStatus.Passed or QualityInspectionStatus.Released));
    }

    private async Task<GeneratorPlanPreviewResult> BuildPlanAsync(IReadOnlyCollection<long> requestedIds, DateTime? earliestStart, CancellationToken ct)
    {
        var ids = requestedIds.Distinct().ToArray(); if (ids.Length == 0) throw AppException.BadRequest("Planlanacak en az bir proje seçin.");
        var policy = await GetRequiredPolicyEntityAsync(false, ct);
        var projects = await Projects.Query().Include(x => x.Product).Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        projects = OrderProjects(projects, policy.PlanningOrderStrategy).ToList();
        if (projects.Count != ids.Length) throw AppException.NotFound("Seçilen projelerden biri bulunamadı.");
        if (projects.Any(x => x.Status is GeneratorProjectStatus.Released or GeneratorProjectStatus.InProgress or GeneratorProjectStatus.Completed or GeneratorProjectStatus.Cancelled))
            throw AppException.Conflict("Serbest bırakılmış, başlamış, tamamlanmış veya iptal edilmiş proje yeniden planlanamaz.");
        var routes = await uow.Repository<GeneratorProductionRoute>().Query().Where(x => x.IsActive)
            .Include(x => x.Operations.Where(o => o.IsActive)).ThenInclude(x => x.Station)
            .Include(x => x.Dependencies).ToListAsync(ct);
        var ruleSet = await uow.Repository<GeneratorProductionRule>().Query().ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);
        var issues = new List<GeneratorPlanningIssue>();
        foreach (var code in PlanningRuleCodes)
            if (!ruleSet.ContainsKey(code))
                issues.Add(new GeneratorPlanningIssue("RULE_DEFINITION", GeneratorRuleSeverity.Error, null, $"{code} planlama kuralı tanımlı değil."));

        void AddIssue(string code, long? projectId, string message)
        {
            if (!ruleSet.TryGetValue(code, out var rule))
            {
                issues.Add(new GeneratorPlanningIssue("RULE_DEFINITION", GeneratorRuleSeverity.Error, projectId, $"{code} planlama kuralı tanımlı değil."));
                return;
            }
            if (!rule.IsEnabled && !rule.IsSystemRequired) return;
            issues.Add(new GeneratorPlanningIssue(code, rule.IsSystemRequired ? GeneratorRuleSeverity.Error : rule.Severity, projectId, message));
        }

        var productIds = projects.Where(x => x.ProductId.HasValue).Select(x => x.ProductId!.Value).Distinct().ToArray();
        var productRouteLinks = productIds.Length == 0 ? [] : await uow.Repository<GeneratorProductionProductRoute>().Query()
            .Where(x => productIds.Contains(x.ProductId) && x.IsActive).ToListAsync(ct);
        var routesByProject = new Dictionary<long, IReadOnlyList<GeneratorProductionRoute>>();
        foreach (var project in projects)
        {
            var requiredParts = GeneratorProductionPlanningPolicy.SelectRoutes(project).ToArray();
            var selectedRoutes = new List<GeneratorProductionRoute>();
            foreach (var part in requiredParts)
            {
                GeneratorProductionRoute[] matches;
                if (project.ProductId.HasValue)
                {
                    var mappedIds = productRouteLinks.Where(x => x.ProductId == project.ProductId && x.PartType == part).Select(x => x.RouteId).ToArray();
                    matches = routes.Where(x => mappedIds.Contains(x.Id)).ToArray();
                }
                else matches = routes.Where(x => x.PartType == part).ToArray();
                if (matches.Length != 1)
                    AddIssue("ROUTE_DEFINITION", project.Id, $"{project.ProjectCode} / {part} için tam bir aktif ürün rotası bulunmalıdır.");
                else selectedRoutes.Add(matches[0]);
            }
            routesByProject[project.Id] = selectedRoutes;
        }
        var selectedRoutesAll = routesByProject.Values.SelectMany(x => x).DistinctBy(x => x.Id).ToArray();
        foreach (var route in selectedRoutesAll)
            if (!TryTopologicalOrder(route, out _, out var graphError))
                AddIssue("DEPENDENCY_VIOLATION", null, $"{route.Code}: {graphError}");
        var selectedOperationIds = selectedRoutesAll.SelectMany(x => x.Operations).Select(x => x.Id).Distinct().ToArray();
        var capabilityRows = productIds.Length == 0 ? [] : await uow.Repository<GeneratorProductionStationCapability>().Query()
            .Include(x => x.Station).Where(x => productIds.Contains(x.ProductId) && selectedOperationIds.Contains(x.RouteOperationId) && x.IsActive).ToListAsync(ct);
        foreach (var project in projects.Where(x => x.ProductId.HasValue))
        foreach (var operation in routesByProject[project.Id].SelectMany(x => x.Operations).Where(x => x.IsActive))
            if (!capabilityRows.Any(x => x.ProductId == project.ProductId && x.RouteOperationId == operation.Id))
                AddIssue("LINE_UNAVAILABLE", project.Id, $"{project.ProjectCode} / {operation.OperationCode} için ürünü işleyebilen aktif istasyon tanımlı değil.");
        var legacyStations = projects.Where(x => !x.ProductId.HasValue).SelectMany(x => routesByProject[x.Id]).SelectMany(x => x.Operations).Select(x => x.Station);
        var stations = legacyStations.Concat(capabilityRows.Select(x => x.Station)).DistinctBy(x => x.Id).ToDictionary(x => x.Id);
        foreach (var operation in selectedRoutesAll.SelectMany(x => x.Operations))
        {
            if (projects.Any(x => !x.ProductId.HasValue && routesByProject[x.Id].Any(r => r.Id == operation.RouteId)) && !operation.Station.IsActive)
                AddIssue("INACTIVE_LINE_USAGE", null, $"{operation.Station.Code} istasyonu pasif olduğu için planlamada kullanılamaz.");
            if (operation.DurationMinutes < operation.MinimumDurationMinutes || operation.DurationMinutes > operation.MaximumDurationMinutes)
                AddIssue("MIN_MAX_OPERATION_DURATION", null, $"{operation.OperationCode} süresi tanımlı alt ve üst sınırın dışında.");
        }
        var stationShifts = await uow.Repository<GeneratorProductionStationShift>().Query()
            .Include(x => x.Shift).Where(x => x.IsActive && x.Shift.IsActive).OrderBy(x => x.Shift.PlanningOrder).ToListAsync(ct);
        foreach (var station in stations.Values)
        {
            var shift = stationShifts.FirstOrDefault(x => x.StationId == station.Id);
            if (shift is null)
                AddIssue("SHIFT_CAPACITY_EXCEEDED", null, $"{station.Code} istasyonu için aktif vardiya kapasitesi tanımlı değil.");
            else if ((station.RequiresCrane && !shift.CraneAvailable) || (station.RequiresTransport && !shift.TransportAvailable))
                AddIssue("LINE_UNAVAILABLE", null, $"{station.Code} istasyonunun vardiyasında gerekli vinç veya taşıma kaynağı kullanılamıyor.");
        }
        var stationResources = await uow.Repository<GeneratorProductionStationResource>().Query()
            .Include(x => x.Resource)
            .Where(x => stations.Keys.Contains(x.StationId))
            .ToListAsync(ct);
        foreach (var assignment in stationResources)
        {
            if (!assignment.Resource.IsActive)
                AddIssue("LINE_UNAVAILABLE", null, $"{assignment.Resource.Code} kaynağı pasif olduğu için {stations[assignment.StationId].Code} istasyonu planlanamaz.");
            else if (assignment.RequiredQuantity > assignment.Resource.Capacity)
                AddIssue("CAPACITY_OVERLOAD", null, $"{stations[assignment.StationId].Code} istasyonunun {assignment.Resource.Code} ihtiyacı tanımlı kaynak kapasitesini aşıyor.");
        }
        var deliveryToleranceMinutes = 0;
        if (ruleSet.TryGetValue("DELIVERY_DATE_RISK", out var deliveryRule) && deliveryRule.IsEnabled
            && !TryReadRuleIntParameter(deliveryRule, "toleranceMinutes", 0, 525_600, out deliveryToleranceMinutes))
            AddIssue("RULE_DEFINITION", null, "DELIVERY_DATE_RISK kuralında 0-525600 aralığında toleranceMinutes parametresi tanımlanmalıdır.");
        if (issues.Any(x => x.Severity == GeneratorRuleSeverity.Error)) return new([], issues, [], [], DateTime.UtcNow, false);

        var exceptions = await uow.Repository<GeneratorProductionCalendarException>().Query().ToListAsync(ct);
        var calendars = stations.Keys.ToDictionary(stationId => stationId, stationId =>
        {
            var stationShift = stationShifts.First(x => x.StationId == stationId);
            var overrides = new Dictionary<DateOnly, GeneratorWorkingDayOverride>();
            foreach (var exception in exceptions.Where(x => x.StationId == null && (x.ShiftId == null || x.ShiftId == stationShift.ShiftId)))
                overrides[exception.ExceptionDate] = new(exception.IsWorking, exception.CapacityMinutes);
            foreach (var exception in exceptions.Where(x => x.StationId == stationId && (x.ShiftId == null || x.ShiftId == stationShift.ShiftId)))
                overrides[exception.ExceptionDate] = new(exception.IsWorking, exception.CapacityMinutes);
            return new GeneratorStationCalendar(stationShift.Shift.StartTime, stationShift.Shift.EndTime, stationShift.WeekdayMask,
                stationShift.CapacityMinutes, overrides);
        });
        var scheduleStart = AsUtc(earliestStart ?? projects.Min(x => x.PlannedStartAtUtc));
        var materialAnalysis = await CreateMaterialPlanningAnalysisAsync(projects, routesByProject, policy, scheduleStart, ids, ct);
        foreach (var suggestion in materialAnalysis.Suggestions.Where(x => x.Severity == GeneratorRuleSeverity.Error))
            AddIssue("MATERIAL_SHORTAGE", suggestion.ProjectId, suggestion.Explanation);
        var existingEnds = await uow.Repository<GeneratorProductionOperation>().Query()
            .Where(x => x.PlannedEndAtUtc >= scheduleStart && !ids.Contains(x.ProjectId) && x.Status != GeneratorOperationStatus.Cancelled)
            .GroupBy(x => x.StationId).Select(x => new { StationId = x.Key, End = x.Max(o => o.PlannedEndAtUtc) }).ToDictionaryAsync(x => x.StationId, x => x.End, ct);
        var lanes = stations.ToDictionary(x => x.Key, x =>
        {
            var shift = stationShifts.First(s => s.StationId == x.Key);
            var laneCount = Math.Max(1, Math.Min(x.Value.MaxParallelJobs, Math.Max(1, shift.MachineCapacity)));
            return Enumerable.Repeat(existingEnds.GetValueOrDefault(x.Key, scheduleStart), laneCount).ToArray();
        });
        var resourceLanes = stationResources.Select(x => x.Resource).DistinctBy(x => x.Id).ToDictionary(
            x => x.Id,
            x =>
            {
                var assignedStationIds = stationResources.Where(a => a.ResourceId == x.Id).Select(a => a.StationId).ToHashSet();
                var availableAt = existingEnds.Where(e => assignedStationIds.Contains(e.Key)).Select(e => e.Value).DefaultIfEmpty(scheduleStart).Max();
                return Enumerable.Repeat(availableAt, x.Capacity).ToArray();
            });
        var resourcesByStation = stationResources.GroupBy(x => x.StationId).ToDictionary(x => x.Key, x => x.ToArray());
        var capabilitiesByOperation = capabilityRows.GroupBy(x => (x.ProductId, x.RouteOperationId)).ToDictionary(x => x.Key, x => x.ToArray());
        var items = new List<GeneratorPlanItem>();
        var projectOrder = projects.Select((project, index) => new { project.Id, Index = index }).ToDictionary(x => x.Id, x => x.Index);
        var remaining = projects.SelectMany(project => Enumerable.Range(1, project.Quantity).Select(unit => (Project: project, Unit: unit))).ToList();
        while (remaining.Count > 0)
        {
            var selected = remaining.Select(x => new
                {
                    Candidate = x,
                    ReleaseAt = materialAnalysis.Context.EstimateProjectRelease(x.Project, routesByProject[x.Project.Id],
                        AsUtc(x.Project.PlannedStartAtUtc) > scheduleStart ? AsUtc(x.Project.PlannedStartAtUtc) : scheduleStart)
                })
                .OrderBy(x => x.ReleaseAt.HasValue ? 0 : 1).ThenBy(x => x.ReleaseAt ?? DateTime.MaxValue)
                .ThenBy(x => projectOrder[x.Candidate.Project.Id]).ThenBy(x => x.Candidate.Unit).First().Candidate;
            remaining.Remove(selected);
            var project = selected.Project; var unit = selected.Unit;
            var projectRoutes = routesByProject[project.Id];
            var componentRoutes = projectRoutes.Where(x => x.PartType != GeneratorPartType.FinalAssembly).ToArray();
            var componentLastKeys = new List<string>();
            var componentStart = AsUtc(project.PlannedStartAtUtc) > scheduleStart ? AsUtc(project.PlannedStartAtUtc) : scheduleStart;
            var componentEnd = componentStart;
            foreach (var route in componentRoutes)
            {
                var routeResult = ScheduleRoute(project, unit, route, componentStart, [], lanes,
                    resourceLanes, resourcesByStation, calendars, stations, capabilitiesByOperation, materialAnalysis.Context,
                    items, policy.WorkingCalendarSearchLimitDays, (projectId, message) => AddIssue("MATERIAL_SHORTAGE", projectId, message));
                componentLastKeys.AddRange(routeResult.TerminalKeys); componentEnd = componentEnd > routeResult.End ? componentEnd : routeResult.End;
            }
            if (project.IncludeFinalAssembly)
                ScheduleRoute(project, unit, projectRoutes.Single(x => x.PartType == GeneratorPartType.FinalAssembly), componentEnd,
                    componentLastKeys.Select(x => new GeneratorPlanPredecessor(x, GeneratorDependencyType.FinishToStart, 0)).ToArray(),
                    lanes, resourceLanes, resourcesByStation, calendars, stations, capabilitiesByOperation, materialAnalysis.Context,
                    items, policy.WorkingCalendarSearchLimitDays, (projectId, message) => AddIssue("MATERIAL_SHORTAGE", projectId, message));
            var projectEnd = items.Where(x => x.ProjectId == project.Id && x.UnitIndex == unit).Max(x => x.PlannedEndAtUtc);
            if (projectEnd > project.PlannedDeliveryAtUtc.AddMinutes(deliveryToleranceMinutes))
                AddIssue("DELIVERY_DATE_RISK", project.Id,
                    $"{project.ProjectCode} / ünite {unit}, teslim tarihini {Math.Ceiling((projectEnd - project.PlannedDeliveryAtUtc).TotalHours)} saat aşıyor.");
        }
        var lockedOperations = await uow.Repository<GeneratorProductionOperation>().Query()
            .Where(x => ids.Contains(x.ProjectId) && x.IsScheduleLocked && x.Status != GeneratorOperationStatus.Cancelled)
            .Select(x => new { x.ProjectId, x.UnitIndex, x.RouteOperationId, x.StationId, x.Station.Code, x.Station.Name,
                x.PlannedStartAtUtc, x.PlannedEndAtUtc, x.ManualScheduleReason, x.HasMaterialShortage }).ToListAsync(ct);
        foreach (var locked in lockedOperations)
        {
            var index = items.FindIndex(x => x.ProjectId == locked.ProjectId && x.UnitIndex == locked.UnitIndex && x.RouteOperationId == locked.RouteOperationId);
            if (index < 0) { AddIssue("ROUTE_DEFINITION", locked.ProjectId, "Kilitli operasyon yeni ürün rotasında bulunamadı."); continue; }
            var current = items[index];
            items[index] = current with
            {
                StationId = locked.StationId, StationCode = locked.Code, StationName = locked.Name,
                PlannedStartAtUtc = locked.PlannedStartAtUtc, PlannedEndAtUtc = locked.PlannedEndAtUtc,
                UsesAlternativeStation = locked.StationId != current.StationId || current.UsesAlternativeStation,
                HasMaterialShortage = locked.HasMaterialShortage, IsScheduleLocked = true,
                ManualScheduleReason = locked.ManualScheduleReason
            };
        }
        ValidateLockedPlan(items, stations, AddIssue);
        return new GeneratorPlanPreviewResult(items, issues, materialAnalysis.Coverage, materialAnalysis.Suggestions,
            DateTime.UtcNow, !issues.Any(x => x.Severity == GeneratorRuleSeverity.Error));
    }

    private sealed record RouteScheduleResult(IReadOnlyList<string> TerminalKeys, DateTime End);

    private static RouteScheduleResult ScheduleRoute(
        GeneratorProductionProject project,
        int unit,
        GeneratorProductionRoute route,
        DateTime earliest,
        IReadOnlyList<GeneratorPlanPredecessor> initialPredecessors,
        Dictionary<long, DateTime[]> lanes,
        Dictionary<long, DateTime[]> resourceLanes,
        IReadOnlyDictionary<long, GeneratorProductionStationResource[]> resourcesByStation,
        IReadOnlyDictionary<long, GeneratorStationCalendar> calendars,
        IReadOnlyDictionary<long, GeneratorProductionStation> stations,
        IReadOnlyDictionary<(long ProductId, long RouteOperationId), GeneratorProductionStationCapability[]> capabilitiesByOperation,
        MaterialPlanningContext materialContext,
        List<GeneratorPlanItem> items,
        int calendarSearchLimitDays,
        Action<long, string> materialShortage)
    {
        if (!TryTopologicalOrder(route, out var ordered, out var error))
            throw AppException.Conflict($"{route.Name} rota bağımlılıkları geçersiz: {error}");

        var operationIds = ordered.Select(x => x.Id).ToHashSet();
        var dependencies = route.Dependencies
            .Where(x => operationIds.Contains(x.PredecessorOperationId) && operationIds.Contains(x.SuccessorOperationId))
            .ToArray();
        var scheduled = new Dictionary<long, GeneratorPlanItem>();
        var allItemsByKey = items.ToDictionary(x => x.Key, StringComparer.Ordinal);

        foreach (var operation in ordered)
        {
            var operationDependencies = dependencies.Where(x => x.SuccessorOperationId == operation.Id).ToArray();
            var predecessors = operationDependencies.Length == 0
                ? initialPredecessors.ToArray()
                : operationDependencies.Select(x => new GeneratorPlanPredecessor(
                    scheduled[x.PredecessorOperationId].Key, x.DependencyType, x.LagMinutes)).ToArray();
            var dependencyStart = earliest;

            foreach (var predecessor in predecessors)
            {
                var predecessorItem = scheduled.Values.FirstOrDefault(x => x.Key == predecessor.Key)
                    ?? allItemsByKey.GetValueOrDefault(predecessor.Key)
                    ?? throw AppException.Conflict($"{route.Name} rotasında {predecessor.Key} öncül operasyonu bulunamadı.");
                var constrainedStart = predecessor.DependencyType switch
                {
                    GeneratorDependencyType.StartToStart => predecessorItem.PlannedStartAtUtc.AddMinutes(predecessor.LagMinutes),
                    GeneratorDependencyType.FinishToFinish => predecessorItem.PlannedEndAtUtc.AddMinutes(predecessor.LagMinutes - operation.DurationMinutes),
                    _ => predecessorItem.PlannedEndAtUtc.AddMinutes(predecessor.LagMinutes)
                };
                if (constrainedStart > dependencyStart) dependencyStart = constrainedStart;
            }
            var choices = project.ProductId.HasValue
                ? capabilitiesByOperation.GetValueOrDefault((project.ProductId.Value, operation.Id), [])
                    .Select(x => new StationChoice(x.StationId, x.IsPrimary, x.EfficiencyPercent, x.SetupMinutes)).ToArray()
                : [new StationChoice(operation.StationId, true, 100, 0)];
            StationCandidate? selected = null;
            foreach (var choice in choices)
            {
                if (!stations.ContainsKey(choice.StationId) || !calendars.TryGetValue(choice.StationId, out var calendar)) continue;
                var stationLanes = lanes[choice.StationId];
                var laneIndex = Array.IndexOf(stationLanes, stationLanes.Min());
                var candidate = stationLanes[laneIndex] > dependencyStart ? stationLanes[laneIndex] : dependencyStart;
                var resourceReservations = new List<(DateTime[] Lanes, int[] Indices)>();
                foreach (var assignment in resourcesByStation.GetValueOrDefault(choice.StationId, []))
                {
                    var assignedLanes = resourceLanes[assignment.ResourceId];
                    var indices = GeneratorProductionPlanningPolicy.SelectEarliestCapacityLanes(assignedLanes, assignment.RequiredQuantity);
                    var resourceAvailableAt = indices.Max(x => assignedLanes[x]);
                    if (resourceAvailableAt > candidate) candidate = resourceAvailableAt;
                    resourceReservations.Add((assignedLanes, indices));
                }
                var capacityCandidate = candidate;
                var material = materialContext.Find(project.Id, operation.Id, candidate);
                if (material.AvailableAtUtc > candidate) candidate = material.AvailableAtUtc.Value;
                var start = GeneratorProductionPlanningPolicy.NextWorkingInstant(candidate, calendar, calendarSearchLimitDays);
                var adjustedDuration = Math.Clamp((int)Math.Ceiling(operation.DurationMinutes * 100m / choice.EfficiencyPercent),
                    operation.MinimumDurationMinutes, operation.MaximumDurationMinutes) + choice.SetupMinutes;
                var end = GeneratorProductionPlanningPolicy.AddWorkingMinutes(start, adjustedDuration, calendar, calendarSearchLimitDays);
                var current = new StationCandidate(choice, stationLanes, laneIndex, resourceReservations, start, end, material,
                    material.AvailableAtUtc.HasValue && material.AvailableAtUtc.Value > capacityCandidate);
                if (selected is null || current.End < selected.End || current.End == selected.End && current.Choice.IsPrimary) selected = current;
            }
            if (selected is null) throw AppException.Conflict($"{project.ProjectCode} / {operation.OperationCode} için planlanabilir istasyon bulunamadı.");
            selected.StationLanes[selected.LaneIndex] = selected.End;
            foreach (var reservation in selected.ResourceReservations)
                foreach (var index in reservation.Indices) reservation.Lanes[index] = selected.End;
            materialContext.Reserve(project.Id, operation.Id, selected.Start, $"Preview:{project.Id}:{unit}:{operation.Id}");
            if (selected.Material.HasShortage)
                materialShortage(project.Id, $"{project.ProjectCode} / ünite {unit} / {operation.OperationCode}: {selected.Material.Message}");
            var station = stations[selected.Choice.StationId];
            var key = $"{project.Id}:{unit}:{operation.Id}";
            var item = new GeneratorPlanItem(key, project.Id, project.ProjectCode, unit, route.PartType, operation.Id, station.Id,
                station.Code, station.Name, operation.OperationCode, operation.OperationName, selected.Start, selected.End,
                operation.IsCritical || station.IsCritical, station.Id != operation.StationId, selected.Material.HasShortage,
                selected.MaterialDelayed ? selected.Material.AvailableAtUtc : null, false, null, predecessors);
            items.Add(item);
            scheduled[operation.Id] = item;
            allItemsByKey[key] = item;
        }

        if (scheduled.Count == 0) throw AppException.Conflict($"{route.Name} rotasında aktif operasyon yok.");
        var predecessorIds = dependencies.Select(x => x.PredecessorOperationId).ToHashSet();
        var terminalKeys = scheduled.Where(x => !predecessorIds.Contains(x.Key)).Select(x => x.Value.Key).ToArray();
        return new RouteScheduleResult(terminalKeys, scheduled.Values.Max(x => x.PlannedEndAtUtc));
    }

    private sealed record StationChoice(long StationId, bool IsPrimary, int EfficiencyPercent, int SetupMinutes);
    private sealed record StationCandidate(
        StationChoice Choice, DateTime[] StationLanes, int LaneIndex,
        List<(DateTime[] Lanes, int[] Indices)> ResourceReservations,
        DateTime Start, DateTime End, MaterialAvailability Material, bool MaterialDelayed);

    private static void ValidateLockedPlan(
        IReadOnlyList<GeneratorPlanItem> items,
        IReadOnlyDictionary<long, GeneratorProductionStation> stations,
        Action<string, long?, string> addIssue)
    {
        var byKey = items.ToDictionary(x => x.Key, StringComparer.Ordinal);
        foreach (var item in items)
        foreach (var predecessor in item.Predecessors)
        {
            if (!byKey.TryGetValue(predecessor.Key, out var previous)) continue;
            var valid = predecessor.DependencyType switch
            {
                GeneratorDependencyType.StartToStart => item.PlannedStartAtUtc >= previous.PlannedStartAtUtc.AddMinutes(predecessor.LagMinutes),
                GeneratorDependencyType.FinishToFinish => item.PlannedEndAtUtc >= previous.PlannedEndAtUtc.AddMinutes(predecessor.LagMinutes),
                _ => item.PlannedStartAtUtc >= previous.PlannedEndAtUtc.AddMinutes(predecessor.LagMinutes)
            };
            if (!valid) addIssue("DEPENDENCY_VIOLATION", item.ProjectId,
                $"Kilitli {item.OperationCode} zamanı {previous.OperationCode} bağımlılığıyla çakışıyor.");
        }
        foreach (var stationGroup in items.GroupBy(x => x.StationId))
        {
            var capacity = stations.GetValueOrDefault(stationGroup.Key)?.MaxParallelJobs ?? 1;
            foreach (var instant in stationGroup.SelectMany(x => new[] { x.PlannedStartAtUtc, x.PlannedEndAtUtc }).Distinct())
            {
                var concurrent = stationGroup.Count(x => x.PlannedStartAtUtc <= instant && x.PlannedEndAtUtc > instant);
                if (concurrent > capacity)
                {
                    addIssue("CAPACITY_OVERLOAD", null, $"Kilitli plan {stationGroup.First().StationCode} istasyonunda {concurrent}/{capacity} kapasite aşımı oluşturuyor.");
                    break;
                }
            }
        }
    }

    private static DateTime ApplyWorkingLag(DateTime value, int lagMinutes, GeneratorStationCalendar calendar, int searchLimitDays) =>
        lagMinutes == 0
            ? value
            : lagMinutes > 0
            ? GeneratorProductionPlanningPolicy.AddWorkingMinutes(value, lagMinutes, calendar, searchLimitDays)
            : GeneratorProductionPlanningPolicy.SubtractWorkingMinutes(value, Math.Abs(lagMinutes), calendar, searchLimitDays);

    private static bool TryTopologicalOrder(
        GeneratorProductionRoute route,
        out IReadOnlyList<GeneratorProductionRouteOperation> ordered,
        out string error)
    {
        var operations = route.Operations.Where(x => x.IsActive).OrderBy(x => x.Sequence).ToArray();
        if (operations.Length == 0)
        {
            ordered = [];
            error = "Aktif operasyon bulunmuyor.";
            return false;
        }

        var operationIds = operations.Select(x => x.Id).ToHashSet();
        var dependencies = route.Dependencies
            .Where(x => operationIds.Contains(x.PredecessorOperationId) && operationIds.Contains(x.SuccessorOperationId))
            .ToArray();
        if (dependencies.Any(x => x.PredecessorOperationId == x.SuccessorOperationId))
        {
            ordered = [];
            error = "Bir operasyon kendisine bağımlı olamaz.";
            return false;
        }

        var indegree = operations.ToDictionary(x => x.Id, _ => 0);
        var successors = operations.ToDictionary(x => x.Id, _ => new List<long>());
        foreach (var dependency in dependencies)
        {
            indegree[dependency.SuccessorOperationId]++;
            successors[dependency.PredecessorOperationId].Add(dependency.SuccessorOperationId);
        }

        var byId = operations.ToDictionary(x => x.Id);
        var queue = new PriorityQueue<long, int>();
        foreach (var operation in operations.Where(x => indegree[x.Id] == 0)) queue.Enqueue(operation.Id, operation.Sequence);
        var result = new List<GeneratorProductionRouteOperation>(operations.Length);
        while (queue.TryDequeue(out var operationId, out _))
        {
            result.Add(byId[operationId]);
            foreach (var successorId in successors[operationId])
            {
                indegree[successorId]--;
                if (indegree[successorId] == 0) queue.Enqueue(successorId, byId[successorId].Sequence);
            }
        }

        ordered = result;
        error = result.Count == operations.Length ? string.Empty : "Döngüsel operasyon bağımlılığı bulundu.";
        return result.Count == operations.Length;
    }

    private static List<GeneratorProductionStation> DefaultStations(long userId) =>
    [
        Station("SA-RA-1.1", "Ortak Giriş ve Hazırlık", GeneratorStationArea.CommonEntry, 10, userId),
        Station("SA-1.3", "Stator GDK ve Lazer", GeneratorStationArea.Stator, 20, userId),
        Station("SA-2.0", "Stator Paketleme ve Kaynak", GeneratorStationArea.Stator, 30, userId, critical: true, bottleneck: true),
        Station("SA-3.0", "Stator Astar Boya", GeneratorStationArea.Stator, 40, userId),
        Station("SA-4.0", "Formcoil ve Bağlantı", GeneratorStationArea.Stator, 50, userId, critical: true),
        Station("SA-6.0", "Stator Kürleme Fırını", GeneratorStationArea.Stator, 60, userId, critical: true),
        Station("SA-7.0", "Stator Son Kat Boya", GeneratorStationArea.Stator, 70, userId),
        Station("SA-8.0", "Stator Final Kalite", GeneratorStationArea.Stator, 80, userId, critical: true),
        Station("RA-1.2", "Rotor GDK ve Lazer", GeneratorStationArea.Rotor, 90, userId),
        Station("RA-2.0", "Rotor Paketleme ve Robot Kaynak", GeneratorStationArea.Rotor, 100, userId, critical: true, bottleneck: true),
        Station("RA-4.0", "Mıknatıs ve Segman", GeneratorStationArea.Rotor, 110, userId, critical: true),
        Station("RA-3.0", "Rotor Dış Boya", GeneratorStationArea.Rotor, 120, userId),
        Station("HOL-2-BUFFER", "Taşıyıcı Kol Ara Stok", GeneratorStationArea.Stiffener, 130, userId, parallel: 4),
        Station("FA-3.0", "Taşıyıcı Kol Ön Montaj", GeneratorStationArea.Stiffener, 140, userId, crane: true),
        Station("FA-1.1", "Stator Montaj Hazırlık", GeneratorStationArea.FinalAssembly, 150, userId, crane: true),
        Station("FA-2.0", "Rotor Ön Montaj", GeneratorStationArea.FinalAssembly, 160, userId, crane: true),
        Station("FA-4.0", "Birleştirme, Döndürme ve Balans", GeneratorStationArea.FinalAssembly, 170, userId, critical: true, bottleneck: true, crane: true),
        Station("FA-5.6", "Gövde ve Soğutma Montajı", GeneratorStationArea.FinalAssembly, 180, userId, crane: true),
        Station("FA-7.0", "Elektrik, HV, Son İşlem ve Paket", GeneratorStationArea.FinalAssembly, 190, userId, critical: true),
        Station("OUTBOUND", "Sevkiyat Tamponu", GeneratorStationArea.Outbound, 200, userId, parallel: 4, transport: true)
    ];

    private static GeneratorProductionStation Station(string code, string name, GeneratorStationArea area, int order, long userId, bool critical = false, bool bottleneck = false, int parallel = 1, bool crane = false, bool transport = false) =>
        new() { Code = code, Name = name, Area = area, PlanningOrder = order, IsCritical = critical, IsBottleneck = bottleneck, MaxParallelJobs = parallel, DefaultPersonnelCapacity = critical ? 2 : 1, RequiresCrane = crane, RequiresTransport = transport, CreatedBy = userId };

    private static List<GeneratorProductionResource> DefaultResources(long userId) =>
    [
        Resource("PERSONNEL", "Üretim Personeli", GeneratorResourceType.Personnel, 40, false, userId), Resource("WELD", "Kaynak Ekibi", GeneratorResourceType.Welding, 4, true, userId),
        Resource("ROBOT-WELD", "Robot Kaynak Hücresi", GeneratorResourceType.RobotWelding, 1, true, userId), Resource("CURING-OVEN", "Kürleme Fırını", GeneratorResourceType.CuringOven, 1, true, userId),
        Resource("LASER", "Lazer Ölçüm", GeneratorResourceType.Laser, 2, true, userId), Resource("CRANE", "Tavan Vinci", GeneratorResourceType.Crane, 2, true, userId),
        Resource("TRANSPORT", "İç Taşıma", GeneratorResourceType.Transport, 2, true, userId)
    ];

    private static GeneratorProductionResource Resource(string code, string name, GeneratorResourceType type, int capacity, bool exclusive, long userId) =>
        new() { Code = code, Name = name, ResourceType = type, Capacity = capacity, IsExclusive = exclusive, CreatedBy = userId };

    private static List<GeneratorProductionStationResource> DefaultStationResources(Dictionary<string, GeneratorProductionStation> s, Dictionary<string, GeneratorProductionResource> r, long userId)
    {
        var result = s.Values.Select(x => new GeneratorProductionStationResource { StationId = x.Id, ResourceId = r["PERSONNEL"].Id, RequiredQuantity = x.DefaultPersonnelCapacity, CreatedBy = userId }).ToList();
        void Add(string station, string resource) => result.Add(new() { StationId = s[station].Id, ResourceId = r[resource].Id, CreatedBy = userId });
        Add("SA-1.3", "LASER"); Add("RA-1.2", "LASER"); Add("SA-2.0", "WELD"); Add("RA-2.0", "ROBOT-WELD"); Add("SA-6.0", "CURING-OVEN");
        foreach (var station in s.Values.Where(x => x.RequiresCrane)) Add(station.Code, "CRANE"); Add("OUTBOUND", "TRANSPORT"); return result;
    }

    private static List<GeneratorProductionRouteOperation> DefaultRouteOperations(Dictionary<GeneratorPartType, GeneratorProductionRoute> routes, Dictionary<string, GeneratorProductionStation> stations, long userId)
    {
        var result = new List<GeneratorProductionRouteOperation>();
        void Add(GeneratorPartType part, string station, int sequence, int minutes, bool critical = false)
        {
            var route = routes[part]; result.Add(new GeneratorProductionRouteOperation { RouteId = route.Id, StationId = stations[station].Id,
                OperationCode = $"{route.Code}-{sequence:00}", OperationName = stations[station].Name, Sequence = sequence, DurationMinutes = minutes,
                MinimumDurationMinutes = Math.Max(15, minutes / 2), MaximumDurationMinutes = minutes * 2, IsCritical = critical, CreatedBy = userId });
        }
        Add(GeneratorPartType.Stator, "SA-RA-1.1", 10, 120); Add(GeneratorPartType.Stator, "SA-1.3", 20, 180); Add(GeneratorPartType.Stator, "SA-2.0", 30, 480, true);
        Add(GeneratorPartType.Stator, "SA-3.0", 40, 180); Add(GeneratorPartType.Stator, "SA-4.0", 50, 420, true); Add(GeneratorPartType.Stator, "SA-6.0", 60, 480, true);
        Add(GeneratorPartType.Stator, "SA-7.0", 70, 180); Add(GeneratorPartType.Stator, "SA-8.0", 80, 240, true); Add(GeneratorPartType.Stator, "FA-1.1", 90, 240);
        Add(GeneratorPartType.Rotor, "SA-RA-1.1", 10, 120); Add(GeneratorPartType.Rotor, "RA-1.2", 20, 180); Add(GeneratorPartType.Rotor, "RA-2.0", 30, 420, true);
        Add(GeneratorPartType.Rotor, "RA-4.0", 40, 300, true); Add(GeneratorPartType.Rotor, "RA-3.0", 50, 180); Add(GeneratorPartType.Rotor, "FA-2.0", 60, 240);
        Add(GeneratorPartType.Stiffener, "SA-RA-1.1", 10, 90); Add(GeneratorPartType.Stiffener, "HOL-2-BUFFER", 20, 60); Add(GeneratorPartType.Stiffener, "FA-3.0", 30, 240);
        Add(GeneratorPartType.FinalAssembly, "FA-4.0", 10, 480, true); Add(GeneratorPartType.FinalAssembly, "FA-5.6", 20, 360); Add(GeneratorPartType.FinalAssembly, "FA-7.0", 30, 480, true); Add(GeneratorPartType.FinalAssembly, "OUTBOUND", 40, 120);
        return result;
    }

    private static List<GeneratorProductionRule> DefaultRules(long userId)
    {
        var definitions = new (string Code, string Name, string Description, GeneratorRuleSeverity Severity, bool Required, string? ParametersJson)[]
        {
            ("RULE_DEFINITION", "Kural tanım bütünlüğü", "Plan motorunun kullandığı zorunlu kurallar eksiksiz tanımlı olmalıdır.", GeneratorRuleSeverity.Error, true, null),
            ("ROUTE_DEFINITION", "Rota tanım bütünlüğü", "Seçilen her bileşen için tek bir aktif ve geçerli rota bulunmalıdır.", GeneratorRuleSeverity.Error, true, null),
            ("DELIVERY_DATE_RISK", "Teslim tarihi riski", "Planlanan bitiş, tanımlı toleransın üzerinde teslim tarihini aşmamalıdır.", GeneratorRuleSeverity.Warning, false, "{\"toleranceMinutes\":0}"),
            ("CAPACITY_OVERLOAD", "İstasyon kapasitesi", "Aynı istasyon kapasitesinden fazla eşzamanlı iş planlanamaz.", GeneratorRuleSeverity.Error, true, null),
            ("OPERATION_CONFLICT", "Operasyon çakışması", "Aynı kapasite dilimindeki operasyonlar çakışamaz.", GeneratorRuleSeverity.Error, true, null),
            ("DEPENDENCY_VIOLATION", "Bağımlılık ihlali", "Rota bağımlılık grafiği döngüsüz olmalı ve yürütmede korunmalıdır.", GeneratorRuleSeverity.Error, true, null),
            ("CRITICAL_PATH_DELAY", "Kritik yol gecikmesi", "Kritik istasyon gecikmeleri görünür olmalıdır.", GeneratorRuleSeverity.Warning, false, null),
            ("MATERIAL_SHORTAGE", "Malzeme eksikliği", "Eksik malzemeli operasyon politika izin vermiyorsa başlatılamaz.", GeneratorRuleSeverity.Error, true, null),
            ("LINE_UNAVAILABLE", "Hat kullanılabilirliği", "Pasif veya kaynağı yetersiz istasyonlarda plan oluşturulamaz.", GeneratorRuleSeverity.Error, true, null),
            ("SHIFT_CAPACITY_EXCEEDED", "Vardiya kapasitesi", "Operasyon vardiya kullanılabilir süresini aşamaz.", GeneratorRuleSeverity.Error, true, null),
            ("HOLIDAY_CONFLICT", "Tatil çakışması", "Çalışılmayan günlerde operasyon planlanamaz.", GeneratorRuleSeverity.Error, true, null),
            ("PROJECT_PRIORITY_CONFLICT", "Proje önceliği", "Proje sıralaması seçilen politika stratejisine göre yapılır.", GeneratorRuleSeverity.Warning, false, null),
            ("PARALLEL_JOB_LIMIT", "Paralel iş sınırı", "İstasyonun paralel iş sınırı korunur.", GeneratorRuleSeverity.Error, true, null),
            ("MIN_MAX_OPERATION_DURATION", "Operasyon süre sınırı", "Operasyon süresi tanımlı alt ve üst sınır içinde kalır.", GeneratorRuleSeverity.Error, true, null),
            ("INACTIVE_LINE_USAGE", "Pasif hat kullanımı", "Pasif rota veya istasyon kullanılamaz.", GeneratorRuleSeverity.Error, true, null)
        };
        return definitions.Select(x => new GeneratorProductionRule
        {
            Code = x.Code, Name = x.Name, Description = x.Description, Severity = x.Severity,
            IsSystemRequired = x.Required, ParametersJson = x.ParametersJson, CreatedBy = userId
        }).ToList();
    }

    private static readonly string[] PlanningRuleCodes =
    [
        "RULE_DEFINITION", "ROUTE_DEFINITION", "DELIVERY_DATE_RISK", "CAPACITY_OVERLOAD", "DEPENDENCY_VIOLATION",
        "INACTIVE_LINE_USAGE", "MIN_MAX_OPERATION_DURATION", "SHIFT_CAPACITY_EXCEEDED", "LINE_UNAVAILABLE"
    ];

    private async Task<GeneratorProductionPolicy> GetRequiredPolicyEntityAsync(bool tracking, CancellationToken ct) =>
        await Policies.FirstOrDefaultAsync(x => x.PolicyKey == "DEFAULT", tracking, ct)
        ?? throw AppException.Conflict("Jeneratör üretim parametreleri tanımlı değil. Tanımlar > Parametreler ekranından kaydedin.");

    private static GeneratorPolicyRow MapPolicy(GeneratorProductionPolicy x) => new(
        x.Id, x.BranchCode, x.MinimumProjectPriority, x.MaximumProjectPriority, x.DefaultProjectPriority,
        x.DefaultProjectQuantity, x.MaximumProjectQuantity, x.DefaultLeadTimeDays,
        x.MinimumPlanReasonLength, x.MinimumOperationReasonLength, x.MaximumScheduleRangeDays,
        x.SchedulePastDays, x.ScheduleFutureDays, x.GanttDefaultWindowDays, x.AndonRefreshSeconds, x.InboundQualityBufferDays,
        x.WorkingCalendarSearchLimitDays, x.RequireComponentForFinalAssembly,
        x.RequireMaterialAvailabilityToStart, x.RequireProblemClosureToComplete,
        x.RequirePositiveCompletionQuantity, x.PlanningOrderStrategy, Convert.ToBase64String(x.RowVersion));

    private static void ApplyPolicy(GeneratorProductionPolicy entity, UpdateGeneratorPolicyRequest request)
    {
        entity.MinimumProjectPriority = request.MinimumProjectPriority;
        entity.MaximumProjectPriority = request.MaximumProjectPriority;
        entity.DefaultProjectPriority = request.DefaultProjectPriority;
        entity.DefaultProjectQuantity = request.DefaultProjectQuantity;
        entity.MaximumProjectQuantity = request.MaximumProjectQuantity;
        entity.DefaultLeadTimeDays = request.DefaultLeadTimeDays;
        entity.MinimumPlanReasonLength = request.MinimumPlanReasonLength;
        entity.MinimumOperationReasonLength = request.MinimumOperationReasonLength;
        entity.MaximumScheduleRangeDays = request.MaximumScheduleRangeDays;
        entity.SchedulePastDays = request.SchedulePastDays;
        entity.ScheduleFutureDays = request.ScheduleFutureDays;
        entity.GanttDefaultWindowDays = request.GanttDefaultWindowDays;
        entity.AndonRefreshSeconds = request.AndonRefreshSeconds;
        entity.InboundQualityBufferDays = request.InboundQualityBufferDays;
        entity.WorkingCalendarSearchLimitDays = request.WorkingCalendarSearchLimitDays;
        entity.RequireComponentForFinalAssembly = request.RequireComponentForFinalAssembly;
        entity.RequireMaterialAvailabilityToStart = request.RequireMaterialAvailabilityToStart;
        entity.RequireProblemClosureToComplete = request.RequireProblemClosureToComplete;
        entity.RequirePositiveCompletionQuantity = request.RequirePositiveCompletionQuantity;
        entity.PlanningOrderStrategy = request.PlanningOrderStrategy;
    }

    internal static void ValidatePolicy(UpdateGeneratorPolicyRequest request)
    {
        if (request.MinimumProjectPriority < 0 || request.MaximumProjectPriority > 100
            || request.MinimumProjectPriority > request.DefaultProjectPriority
            || request.DefaultProjectPriority > request.MaximumProjectPriority)
            throw AppException.BadRequest("Proje öncelik alt, varsayılan ve üst sınırları 0-100 aralığında ve sıralı olmalıdır.");
        if (request.DefaultProjectQuantity < 1 || request.MaximumProjectQuantity < request.DefaultProjectQuantity || request.MaximumProjectQuantity > 10_000)
            throw AppException.BadRequest("Varsayılan ve azami proje adetleri geçerli değil.");
        if (request.DefaultLeadTimeDays < 1 || request.DefaultLeadTimeDays > 3_650)
            throw AppException.BadRequest("Varsayılan teslim süresi 1-3650 gün arasında olmalıdır.");
        if (request.MinimumPlanReasonLength is < 3 or > 1000 || request.MinimumOperationReasonLength is < 3 or > 1000)
            throw AppException.BadRequest("Gerekçe uzunlukları 3-1000 karakter arasında olmalıdır.");
        if (request.MaximumScheduleRangeDays < 1
            || request.SchedulePastDays < 0
            || request.ScheduleFutureDays < 1
            || request.SchedulePastDays + request.ScheduleFutureDays > request.MaximumScheduleRangeDays)
            throw AppException.BadRequest("Takvim geçmiş/gelecek penceresi azami takvim aralığını aşamaz.");
        if (request.GanttDefaultWindowDays < 1 || request.SchedulePastDays + request.GanttDefaultWindowDays > request.MaximumScheduleRangeDays)
            throw AppException.BadRequest("Geçmiş ve Gantt pencerelerinin toplamı azami takvim aralığını aşamaz.");
        if (request.AndonRefreshSeconds is < 5 or > 3600)
            throw AppException.BadRequest("Andon yenileme süresi 5-3600 saniye arasında olmalıdır.");
        if (request.InboundQualityBufferDays is < 0 or > 365)
            throw AppException.BadRequest("Mal kabul ve kalite bekleme süresi 0-365 gün arasında olmalıdır.");
        if (request.WorkingCalendarSearchLimitDays is < 1 or > 36_600)
            throw AppException.BadRequest("Çalışma takvimi arama sınırı 1-36600 gün arasında olmalıdır.");
    }

    private static IEnumerable<GeneratorProductionProject> OrderProjects(
        IEnumerable<GeneratorProductionProject> projects,
        GeneratorPlanningOrderStrategy strategy) => strategy switch
        {
            GeneratorPlanningOrderStrategy.DeliveryThenPriority => projects
                .OrderBy(x => x.PlannedDeliveryAtUtc).ThenByDescending(x => x.Priority),
            GeneratorPlanningOrderStrategy.ManualOrderThenDelivery => projects
                .OrderBy(x => x.PlanningOrder).ThenBy(x => x.PlannedDeliveryAtUtc),
            _ => projects.OrderByDescending(x => x.Priority).ThenBy(x => x.PlannedDeliveryAtUtc)
        };

    private static GeneratorRuleRow MapRule(GeneratorProductionRule x) => new(
        x.Id, x.Code, x.Name, x.Description, x.Severity, x.IsEnabled, x.IsSystemRequired,
        x.ParametersJson, Convert.ToBase64String(x.RowVersion));

    private static bool TryReadRuleIntParameter(
        GeneratorProductionRule rule,
        string parameterName,
        int minimum,
        int maximum,
        out int result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(rule.ParametersJson)) return false;
        try
        {
            using var document = JsonDocument.Parse(rule.ParametersJson);
            if (!document.RootElement.TryGetProperty(parameterName, out var value) || !value.TryGetInt32(out var parsed)
                || parsed < minimum || parsed > maximum) return false;
            result = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ValidateJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try { using var _ = JsonDocument.Parse(value); }
        catch (JsonException) { throw AppException.BadRequest("Kural parametreleri geçerli JSON olmalıdır."); }
    }

    private static void ValidateRuleParameters(string code, bool isEnabled, string? parametersJson)
    {
        if (!isEnabled || !code.Equals("DELIVERY_DATE_RISK", StringComparison.OrdinalIgnoreCase)) return;
        var rule = new GeneratorProductionRule { ParametersJson = parametersJson };
        if (!TryReadRuleIntParameter(rule, "toleranceMinutes", 0, 525_600, out _))
            throw AppException.BadRequest("Teslim tarihi riski için 0-525600 aralığında toleranceMinutes parametresi zorunludur.");
    }

    private static GeneratorProjectDetail MapProject(GeneratorProductionProject x) => new(x.Id, x.ProductionHeaderId, x.ProductId, x.Product?.Code, x.ProjectCode, x.ProjectName, x.GeneratorType, x.SerialNumber,
        x.CustomerCodeSnapshot, x.CustomerNameSnapshot, x.ExternalWorkOrderNo, x.SourceSystemCode, x.PlannedStartAtUtc, x.PlannedDeliveryAtUtc,
        x.Status, x.Priority, x.Quantity, x.HasStator, x.HasRotor, x.HasStiffener, x.IncludeFinalAssembly, x.PlanningOrder, x.Description, Convert.ToBase64String(x.RowVersion));

    private static GeneratorScheduleRow ToScheduleRow(GeneratorProductionOperation x) => new(x.Id, x.ProjectId, x.Project.ProjectCode, x.Project.ProjectName, x.UnitIndex,
        x.RouteOperation.Route.PartType, x.StationId, x.Station.Code, x.Station.Name, x.RouteOperation.OperationCode, x.RouteOperation.OperationName,
        x.Status, x.PlannedStartAtUtc, x.PlannedEndAtUtc, x.ActualStartAtUtc, x.ActualEndAtUtc, x.IsCritical, x.HasMaterialShortage, x.HasProblem,
        x.IsScheduleLocked, x.ManualScheduleReason, Convert.ToBase64String(x.RowVersion), x.QualityGate?.Status,
        x.QualityGate is null ? null : Convert.ToBase64String(x.QualityGate.RowVersion), x.RouteOperationId, x.Project.ProductId);

    private static int CountRevisionOperations(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetArrayLength() : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static void ValidateProject(
        string code, string name, DateTime start, DateTime delivery, int priority, int quantity,
        bool stator, bool rotor, bool stiffener, bool finalAssembly, GeneratorProductionPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 100) throw AppException.BadRequest("Proje kodu zorunludur ve en fazla 100 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 300) throw AppException.BadRequest("Proje adı zorunludur ve en fazla 300 karakter olabilir.");
        if (delivery <= start) throw AppException.BadRequest("Teslim tarihi plan başlangıcından sonra olmalıdır.");
        if (priority < policy.MinimumProjectPriority || priority > policy.MaximumProjectPriority)
            throw AppException.BadRequest($"Öncelik {policy.MinimumProjectPriority} ile {policy.MaximumProjectPriority} arasında olmalıdır.");
        if (quantity < 1 || quantity > policy.MaximumProjectQuantity)
            throw AppException.BadRequest($"Jeneratör adedi 1 ile {policy.MaximumProjectQuantity} arasında olmalıdır.");
        if (!stator && !rotor && !stiffener && !finalAssembly) throw AppException.BadRequest("En az bir jeneratör bileşeni veya final montajı seçilmelidir.");
        if (policy.RequireComponentForFinalAssembly && finalAssembly && !stator && !rotor && !stiffener)
            throw AppException.BadRequest("Final montajı için en az bir bileşen rotası seçilmelidir.");
    }

    private static byte[] DecodeRowVersion(string value) { try { return Convert.FromBase64String(value); } catch { throw AppException.BadRequest("Satır sürümü geçersiz."); } }
    private static void RequireReason(string? value, string field, int minimumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < minimumLength)
            throw AppException.BadRequest($"{field} en az {minimumLength} karakter olmalıdır.");
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
