using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.GeneratorProduction.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.GeneratorProduction.Application;

public sealed class GeneratorProductionService(IUnitOfWork uow, IAuditLogWriter audit) : IGeneratorProductionService
{
    private IGenericRepository<GeneratorProductionProject> Projects => uow.Repository<GeneratorProductionProject>();

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
        var query = Projects.Query();
        if (!string.IsNullOrWhiteSpace(request.EffectiveSearch))
        {
            var term = request.EffectiveSearch.Trim();
            query = query.Where(x => x.ProjectCode.Contains(term) || x.ProjectName.Contains(term)
                || (x.GeneratorType != null && x.GeneratorType.Contains(term))
                || (x.SerialNumber != null && x.SerialNumber.Contains(term))
                || (x.CustomerNameSnapshot != null && x.CustomerNameSnapshot.Contains(term)));
        }

        return await query.OrderBy(x => x.Status).ThenByDescending(x => x.Priority).ThenBy(x => x.PlannedDeliveryAtUtc)
            .Select(x => new GeneratorProjectRow(
                x.Id, x.ProjectCode, x.ProjectName, x.GeneratorType, x.SerialNumber, x.CustomerNameSnapshot,
                x.Status, x.Priority, x.Quantity, x.PlannedStartAtUtc, x.PlannedDeliveryAtUtc,
                x.Operations.Count, x.Operations.Count(o => o.Status == GeneratorOperationStatus.Completed),
                Convert.ToBase64String(x.RowVersion)))
            .ToPagedResponseAsync(request, ct, 200);
    }

    public async Task<GeneratorProjectDetail> GetProjectAsync(long id, CancellationToken ct = default)
    {
        var entity = await Projects.FindByIdAsync(id, false, ct) ?? throw AppException.NotFound("Jeneratör üretim projesi bulunamadı.");
        return MapProject(entity);
    }

    public async Task<GeneratorProjectDetail> CreateProjectAsync(CreateGeneratorProjectRequest request, long userId, CancellationToken ct = default)
    {
        ValidateProject(request.ProjectCode, request.ProjectName, request.PlannedStartAtUtc, request.PlannedDeliveryAtUtc, request.Priority, request.Quantity,
            request.HasStator, request.HasRotor, request.HasStiffener, request.IncludeFinalAssembly);
        var code = request.ProjectCode.Trim();
        if (await Projects.AnyAsync(x => x.ProjectCode == code, ct)) throw AppException.Conflict("Bu jeneratör proje kodu zaten kullanılıyor.");
        var entity = new GeneratorProductionProject
        {
            ProductionHeaderId = request.ProductionHeaderId, ProjectCode = code, ProjectName = request.ProjectName.Trim(),
            GeneratorType = Clean(request.GeneratorType), SerialNumber = Clean(request.SerialNumber), CustomerCodeSnapshot = Clean(request.CustomerCode),
            CustomerNameSnapshot = Clean(request.CustomerName), ExternalWorkOrderNo = Clean(request.ExternalWorkOrderNo), SourceSystemCode = Clean(request.SourceSystemCode),
            PlannedStartAtUtc = AsUtc(request.PlannedStartAtUtc), PlannedDeliveryAtUtc = AsUtc(request.PlannedDeliveryAtUtc), Priority = request.Priority,
            Quantity = request.Quantity, HasStator = request.HasStator, HasRotor = request.HasRotor, HasStiffener = request.HasStiffener,
            IncludeFinalAssembly = request.IncludeFinalAssembly, Description = Clean(request.Description), Status = GeneratorProjectStatus.Draft, CreatedBy = userId
        };
        await Projects.AddAsync(entity, ct); await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry("Create", nameof(GeneratorProductionProject), entity.Id.ToString(), "Success", "GeneratorProduction", NewValues: MapProject(entity)), ct);
        return MapProject(entity);
    }

    public async Task<GeneratorProjectDetail> UpdateProjectAsync(long id, UpdateGeneratorProjectRequest request, long userId, CancellationToken ct = default)
    {
        var entity = await Projects.FindByIdAsync(id, true, ct) ?? throw AppException.NotFound("Jeneratör üretim projesi bulunamadı.");
        if (entity.Status is not (GeneratorProjectStatus.Draft or GeneratorProjectStatus.ReadyToPlan or GeneratorProjectStatus.Planned))
            throw AppException.Conflict("Serbest bırakılmış veya başlamış proje planlama bilgileri değiştirilemez.");
        var suppliedVersion = DecodeRowVersion(request.RowVersion);
        if (!entity.RowVersion.SequenceEqual(suppliedVersion)) throw AppException.Conflict("Proje başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");
        ValidateProject(entity.ProjectCode, request.ProjectName, request.PlannedStartAtUtc, request.PlannedDeliveryAtUtc, request.Priority, request.Quantity,
            request.HasStator, request.HasRotor, request.HasStiffener, request.IncludeFinalAssembly);
        var old = MapProject(entity);
        entity.ProjectName = request.ProjectName.Trim(); entity.GeneratorType = Clean(request.GeneratorType); entity.SerialNumber = Clean(request.SerialNumber);
        entity.CustomerCodeSnapshot = Clean(request.CustomerCode); entity.CustomerNameSnapshot = Clean(request.CustomerName);
        entity.PlannedStartAtUtc = AsUtc(request.PlannedStartAtUtc); entity.PlannedDeliveryAtUtc = AsUtc(request.PlannedDeliveryAtUtc);
        entity.Priority = request.Priority; entity.Quantity = request.Quantity; entity.HasStator = request.HasStator; entity.HasRotor = request.HasRotor;
        entity.HasStiffener = request.HasStiffener; entity.IncludeFinalAssembly = request.IncludeFinalAssembly; entity.Description = Clean(request.Description);
        entity.Status = GeneratorProjectStatus.ReadyToPlan; entity.UpdatedBy = userId;
        await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry("Update", nameof(GeneratorProductionProject), entity.Id.ToString(), "Success", "GeneratorProduction", OldValues: old, NewValues: MapProject(entity)), ct);
        return MapProject(entity);
    }

    public async Task DeleteProjectAsync(long id, long userId, CancellationToken ct = default)
    {
        var entity = await Projects.FindByIdAsync(id, true, ct) ?? throw AppException.NotFound("Jeneratör üretim projesi bulunamadı.");
        if (entity.Status is not (GeneratorProjectStatus.Draft or GeneratorProjectStatus.ReadyToPlan)) throw AppException.Conflict("Yalnızca planlanmamış taslak projeler silinebilir.");
        entity.IsDeleted = true; entity.DeletedDate = DateTime.UtcNow; entity.DeletedBy = userId; await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry("Delete", nameof(GeneratorProductionProject), id.ToString(), "Success", "GeneratorProduction", OldValues: MapProject(entity)), ct);
    }

    public async Task<GeneratorDefinitionsResult> GetDefinitionsAsync(CancellationToken ct = default)
    {
        var stations = await uow.Repository<GeneratorProductionStation>().Query().OrderBy(x => x.PlanningOrder)
            .Select(x => new GeneratorStationRow(x.Id, x.Code, x.Name, x.Area, x.PlanningOrder, x.MaxParallelJobs, x.IsActive, x.IsCritical, x.IsBottleneck, x.RequiresCrane, x.RequiresTransport)).ToListAsync(ct);
        var shifts = await uow.Repository<GeneratorProductionShift>().Query().OrderBy(x => x.PlanningOrder)
            .Select(x => new GeneratorShiftRow(x.Id, x.Code, x.Name, x.StartTime, x.EndTime, x.PlanningOrder, x.IsActive)).ToListAsync(ct);
        var routeEntities = await uow.Repository<GeneratorProductionRoute>().Query().Include(x => x.Operations).ThenInclude(x => x.Station)
            .OrderBy(x => x.PartType).ThenBy(x => x.Code).ToListAsync(ct);
        var routes = routeEntities.Select(x => new GeneratorRouteRow(x.Id, x.Code, x.Name, x.PartType, x.VersionNumber, x.IsActive,
            x.Operations.OrderBy(o => o.Sequence).Select(o => new GeneratorRouteOperationRow(o.Id, o.OperationCode, o.OperationName, o.Sequence,
                o.DurationMinutes, o.MinimumDurationMinutes, o.MaximumDurationMinutes, o.IsCritical, o.StationId, o.Station.Code, o.Station.Name)).ToArray())).ToArray();
        var rules = await uow.Repository<GeneratorProductionRule>().Query().OrderByDescending(x => x.Severity).ThenBy(x => x.Code)
            .Select(x => new GeneratorRuleRow(x.Id, x.Code, x.Name, x.Description, x.Severity, x.IsEnabled)).ToListAsync(ct);
        return new GeneratorDefinitionsResult(stations, shifts, routes, rules, stations.Count > 0 && routes.Length == 4);
    }

    public async Task<GeneratorBootstrapResult> BootstrapDefinitionsAsync(long userId, CancellationToken ct = default)
    {
        if (await uow.Repository<GeneratorProductionStation>().AnyAsync(x => true, ct)) throw AppException.Conflict("Jeneratör üretim tanımları bu şube için daha önce oluşturulmuş.");
        await uow.BeginTransactionAsync(cancellationToken: ct);
        try
        {
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
                CraneAvailable = !s.RequiresCrane || s.RequiresCrane, TransportAvailable = !s.RequiresTransport || s.RequiresTransport, CreatedBy = userId
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
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5) throw AppException.BadRequest("Plan uygulama nedeni en az 5 karakter olmalıdır.");
        var preview = await BuildPlanAsync(request.ProjectIds, request.EarliestStartAtUtc, ct);
        if (!preview.CanApply) throw AppException.Conflict("Planlama önizlemesinde engelleyici hatalar var.");
        await uow.BeginTransactionAsync(cancellationToken: ct);
        try
        {
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
                IsCritical = item.IsCritical, CreatedBy = userId
            }).ToArray();
            await uow.Repository<GeneratorProductionOperation>().AddRangeAsync(created, ct); await uow.SaveChangesAsync(ct);
            var byKey = preview.Items.Zip(created).ToDictionary(x => x.First.Key, x => x.Second);
            var dependencies = preview.Items.SelectMany(item => item.PredecessorKeys.Select(key => new GeneratorProductionOperationDependency
            {
                PredecessorOperationId = byKey[key].Id, SuccessorOperationId = byKey[item.Key].Id,
                DependencyType = GeneratorDependencyType.FinishToStart, CreatedBy = userId
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
        fromUtc = AsUtc(fromUtc); toUtc = AsUtc(toUtc); if (toUtc <= fromUtc) throw AppException.BadRequest("Takvim bitişi başlangıçtan sonra olmalıdır.");
        return await uow.Repository<GeneratorProductionOperation>().Query()
            .Where(x => x.PlannedStartAtUtc < toUtc && x.PlannedEndAtUtc > fromUtc)
            .OrderBy(x => x.Station.PlanningOrder).ThenBy(x => x.PlannedStartAtUtc)
            .Select(x => new GeneratorScheduleRow(x.Id, x.ProjectId, x.Project.ProjectCode, x.Project.ProjectName, x.UnitIndex, x.RouteOperation.Route.PartType,
                x.StationId, x.Station.Code, x.Station.Name, x.RouteOperation.OperationCode, x.RouteOperation.OperationName, x.Status,
                x.PlannedStartAtUtc, x.PlannedEndAtUtc, x.ActualStartAtUtc, x.ActualEndAtUtc, x.IsCritical, x.HasMaterialShortage, x.HasProblem,
                Convert.ToBase64String(x.RowVersion))).ToListAsync(ct);
    }

    private async Task<GeneratorPlanPreviewResult> BuildPlanAsync(IReadOnlyCollection<long> requestedIds, DateTime? earliestStart, CancellationToken ct)
    {
        var ids = requestedIds.Distinct().ToArray(); if (ids.Length == 0) throw AppException.BadRequest("Planlanacak en az bir proje seçin.");
        var projects = await Projects.Query().Where(x => ids.Contains(x.Id)).OrderByDescending(x => x.Priority).ThenBy(x => x.PlannedDeliveryAtUtc).ToListAsync(ct);
        if (projects.Count != ids.Length) throw AppException.NotFound("Seçilen projelerden biri bulunamadı.");
        if (projects.Any(x => x.Status is GeneratorProjectStatus.Released or GeneratorProjectStatus.InProgress or GeneratorProjectStatus.Completed or GeneratorProjectStatus.Cancelled))
            throw AppException.Conflict("Serbest bırakılmış, başlamış, tamamlanmış veya iptal edilmiş proje yeniden planlanamaz.");
        var routes = await uow.Repository<GeneratorProductionRoute>().Query().Where(x => x.IsActive)
            .Include(x => x.Operations.Where(o => o.IsActive)).ThenInclude(x => x.Station).ToListAsync(ct);
        var issues = new List<GeneratorPlanningIssue>();
        foreach (var part in new[] { GeneratorPartType.Stator, GeneratorPartType.Rotor, GeneratorPartType.Stiffener, GeneratorPartType.FinalAssembly })
            if (routes.Count(x => x.PartType == part) != 1) issues.Add(new GeneratorPlanningIssue("ROUTE_DEFINITION", GeneratorRuleSeverity.Error, null, $"{part} için tam bir aktif rota bulunmalıdır."));
        if (issues.Any(x => x.Severity == GeneratorRuleSeverity.Error)) return new([], issues, DateTime.UtcNow, false);

        var stations = routes.SelectMany(x => x.Operations).Select(x => x.Station).DistinctBy(x => x.Id).ToDictionary(x => x.Id);
        var scheduleStart = AsUtc(earliestStart ?? projects.Min(x => x.PlannedStartAtUtc));
        var existingEnds = await uow.Repository<GeneratorProductionOperation>().Query()
            .Where(x => x.PlannedEndAtUtc >= scheduleStart && !ids.Contains(x.ProjectId) && x.Status != GeneratorOperationStatus.Cancelled)
            .GroupBy(x => x.StationId).Select(x => new { StationId = x.Key, End = x.Max(o => o.PlannedEndAtUtc) }).ToDictionaryAsync(x => x.StationId, x => x.End, ct);
        var lanes = stations.ToDictionary(x => x.Key, x => Enumerable.Repeat(existingEnds.GetValueOrDefault(x.Key, scheduleStart), Math.Max(1, x.Value.MaxParallelJobs)).ToArray());
        var items = new List<GeneratorPlanItem>();

        foreach (var project in projects)
        for (var unit = 1; unit <= project.Quantity; unit++)
        {
            var partTypes = new List<GeneratorPartType>(); if (project.HasStator) partTypes.Add(GeneratorPartType.Stator); if (project.HasRotor) partTypes.Add(GeneratorPartType.Rotor); if (project.HasStiffener) partTypes.Add(GeneratorPartType.Stiffener);
            var componentLastKeys = new List<string>(); var componentEnd = AsUtc(project.PlannedStartAtUtc) > scheduleStart ? AsUtc(project.PlannedStartAtUtc) : scheduleStart;
            foreach (var part in partTypes)
            {
                var last = ScheduleRoute(project, unit, routes.Single(x => x.PartType == part), componentEnd, [], lanes, items);
                componentLastKeys.Add(last.Key); componentEnd = componentEnd > last.End ? componentEnd : last.End;
            }
            if (project.IncludeFinalAssembly)
                ScheduleRoute(project, unit, routes.Single(x => x.PartType == GeneratorPartType.FinalAssembly), componentEnd, componentLastKeys, lanes, items);
            var projectEnd = items.Where(x => x.ProjectId == project.Id && x.UnitIndex == unit).Max(x => x.PlannedEndAtUtc);
            if (projectEnd > project.PlannedDeliveryAtUtc)
                issues.Add(new GeneratorPlanningIssue("DELIVERY_DATE_RISK", GeneratorRuleSeverity.Warning, project.Id, $"{project.ProjectCode} / ünite {unit}, teslim tarihini {Math.Ceiling((projectEnd - project.PlannedDeliveryAtUtc).TotalHours)} saat aşıyor."));
        }
        return new GeneratorPlanPreviewResult(items, issues, DateTime.UtcNow, !issues.Any(x => x.Severity == GeneratorRuleSeverity.Error));
    }

    private static (string Key, DateTime End) ScheduleRoute(GeneratorProductionProject project, int unit, GeneratorProductionRoute route, DateTime earliest,
        IReadOnlyList<string> initialPredecessors, Dictionary<long, DateTime[]> lanes, List<GeneratorPlanItem> items)
    {
        string? previousKey = null; var previousEnd = earliest;
        foreach (var operation in route.Operations.OrderBy(x => x.Sequence))
        {
            var stationLanes = lanes[operation.StationId]; var laneIndex = Array.IndexOf(stationLanes, stationLanes.Min());
            var start = NextWorkingInstant(stationLanes[laneIndex] > previousEnd ? stationLanes[laneIndex] : previousEnd);
            var end = AddWorkingMinutes(start, operation.DurationMinutes); stationLanes[laneIndex] = end;
            var key = $"{project.Id}:{unit}:{operation.Id}";
            var predecessors = previousKey == null ? initialPredecessors : new[] { previousKey };
            items.Add(new GeneratorPlanItem(key, project.Id, project.ProjectCode, unit, route.PartType, operation.Id, operation.StationId,
                operation.Station.Code, operation.Station.Name, operation.OperationCode, operation.OperationName, start, end, operation.IsCritical || operation.Station.IsCritical, predecessors));
            previousKey = key; previousEnd = end;
        }
        if (previousKey == null) throw AppException.Conflict($"{route.Name} rotasında aktif operasyon yok.");
        return (previousKey, previousEnd);
    }

    private static DateTime NextWorkingInstant(DateTime value)
    {
        var utc = AsUtc(value);
        while (utc.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) utc = utc.Date.AddDays(1).AddHours(8);
        if (utc.TimeOfDay < TimeSpan.FromHours(8)) utc = utc.Date.AddHours(8);
        if (utc.TimeOfDay >= TimeSpan.FromHours(17)) { utc = utc.Date.AddDays(1).AddHours(8); return NextWorkingInstant(utc); }
        return utc;
    }

    private static DateTime AddWorkingMinutes(DateTime start, int minutes)
    {
        var current = NextWorkingInstant(start); var remaining = minutes;
        while (remaining > 0)
        {
            var available = (int)(current.Date.AddHours(17) - current).TotalMinutes;
            if (remaining <= available) return current.AddMinutes(remaining);
            remaining -= available; current = NextWorkingInstant(current.Date.AddDays(1).AddHours(8));
        }
        return current;
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
        var definitions = new (string Code, string Name, string Description, GeneratorRuleSeverity Severity)[]
        {
            ("DELIVERY_DATE_RISK", "Teslim tarihi riski", "Planlanan bitiş teslim tarihini aşamaz.", GeneratorRuleSeverity.Warning),
            ("CAPACITY_OVERLOAD", "İstasyon kapasitesi", "Aynı istasyon kapasitesinden fazla eşzamanlı iş planlanamaz.", GeneratorRuleSeverity.Error),
            ("OPERATION_CONFLICT", "Operasyon çakışması", "Aynı kapasite dilimindeki operasyonlar çakışamaz.", GeneratorRuleSeverity.Error),
            ("DEPENDENCY_VIOLATION", "Bağımlılık ihlali", "Ardıl operasyon öncül tamamlanmadan başlayamaz.", GeneratorRuleSeverity.Error),
            ("CRITICAL_PATH_DELAY", "Kritik yol gecikmesi", "Kritik istasyon gecikmeleri görünür olmalıdır.", GeneratorRuleSeverity.Warning),
            ("MATERIAL_SHORTAGE", "Malzeme eksikliği", "Eksik malzemeli operasyon serbest bırakılamaz.", GeneratorRuleSeverity.Error),
            ("LINE_UNAVAILABLE", "Hat kullanılabilirliği", "Pasif istasyonlarda plan oluşturulamaz.", GeneratorRuleSeverity.Error),
            ("SHIFT_CAPACITY_EXCEEDED", "Vardiya kapasitesi", "Operasyon vardiya kullanılabilir süresini aşamaz.", GeneratorRuleSeverity.Error),
            ("HOLIDAY_CONFLICT", "Tatil çakışması", "Çalışılmayan günlerde operasyon planlanamaz.", GeneratorRuleSeverity.Error),
            ("PROJECT_PRIORITY_CONFLICT", "Proje önceliği", "Yüksek öncelikli projeler teslim tarihiyle birlikte öne alınır.", GeneratorRuleSeverity.Warning),
            ("PARALLEL_JOB_LIMIT", "Paralel iş sınırı", "İstasyonun paralel iş sınırı korunur.", GeneratorRuleSeverity.Error),
            ("MIN_MAX_OPERATION_DURATION", "Operasyon süre sınırı", "Operasyon süresi tanımlı alt ve üst sınır içinde kalır.", GeneratorRuleSeverity.Error),
            ("INACTIVE_LINE_USAGE", "Pasif hat kullanımı", "Pasif rota veya istasyon kullanılamaz.", GeneratorRuleSeverity.Error)
        };
        return definitions.Select(x => new GeneratorProductionRule { Code = x.Code, Name = x.Name, Description = x.Description, Severity = x.Severity, CreatedBy = userId }).ToList();
    }

    private static GeneratorProjectDetail MapProject(GeneratorProductionProject x) => new(x.Id, x.ProductionHeaderId, x.ProjectCode, x.ProjectName, x.GeneratorType, x.SerialNumber,
        x.CustomerCodeSnapshot, x.CustomerNameSnapshot, x.ExternalWorkOrderNo, x.SourceSystemCode, x.PlannedStartAtUtc, x.PlannedDeliveryAtUtc,
        x.Status, x.Priority, x.Quantity, x.HasStator, x.HasRotor, x.HasStiffener, x.IncludeFinalAssembly, x.Description, Convert.ToBase64String(x.RowVersion));

    private static void ValidateProject(string code, string name, DateTime start, DateTime delivery, int priority, int quantity, bool stator, bool rotor, bool stiffener, bool finalAssembly)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 100) throw AppException.BadRequest("Proje kodu zorunludur ve en fazla 100 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 300) throw AppException.BadRequest("Proje adı zorunludur ve en fazla 300 karakter olabilir.");
        if (delivery <= start) throw AppException.BadRequest("Teslim tarihi plan başlangıcından sonra olmalıdır.");
        if (priority is < 0 or > 100) throw AppException.BadRequest("Öncelik 0 ile 100 arasında olmalıdır.");
        if (quantity is < 1 or > 100) throw AppException.BadRequest("Jeneratör adedi 1 ile 100 arasında olmalıdır.");
        if (!stator && !rotor && !stiffener && !finalAssembly) throw AppException.BadRequest("En az bir jeneratör bileşeni veya final montajı seçilmelidir.");
        if (finalAssembly && !stator && !rotor && !stiffener) throw AppException.BadRequest("Final montajı için en az bir bileşen rotası seçilmelidir.");
    }

    private static byte[] DecodeRowVersion(string value) { try { return Convert.FromBase64String(value); } catch { throw AppException.BadRequest("Satır sürümü geçersiz."); } }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
