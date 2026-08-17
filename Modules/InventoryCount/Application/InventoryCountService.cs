using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.InventoryCount.Domain;
using verii_wms_api_v2.Modules.InventoryCount.Localization;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;
using DocumentSeriesEntity = verii_wms_api_v2.Modules.DocumentSeries.Domain.DocumentSeries;

namespace verii_wms_api_v2.Modules.InventoryCount.Application;

public sealed class InventoryCountService(
    IUnitOfWork unitOfWork,
    IDocumentNumberAllocator documentNumberAllocator,
    IAuditLogWriter audit,
    IStringLocalizer<InventoryCountResource> localizer) : IInventoryCountService
{
    private IGenericRepository<InventoryCountHeader> Headers => unitOfWork.Repository<InventoryCountHeader>();
    private IGenericRepository<InventoryCountScope> Scopes => unitOfWork.Repository<InventoryCountScope>();
    private IGenericRepository<InventoryCountTask> Tasks => unitOfWork.Repository<InventoryCountTask>();
    private IGenericRepository<InventoryCountLine> Lines => unitOfWork.Repository<InventoryCountLine>();
    private IGenericRepository<InventoryCountPolicy> Policies => unitOfWork.Repository<InventoryCountPolicy>();
    private IGenericRepository<WarehouseEntity> Warehouses => unitOfWork.Repository<WarehouseEntity>();
    private IGenericRepository<WarehouseLocation> Locations => unitOfWork.Repository<WarehouseLocation>();
    private IGenericRepository<StockEntity> Stocks => unitOfWork.Repository<StockEntity>();
    private IGenericRepository<YapCodeEntity> YapCodes => unitOfWork.Repository<YapCodeEntity>();
    private IGenericRepository<LocationStockBalance> Balances => unitOfWork.Repository<LocationStockBalance>();
    private IGenericRepository<StockMovementEntry> MovementEntries => unitOfWork.Repository<StockMovementEntry>();
    private IGenericRepository<DocumentSeriesEntity> DocumentSeries => unitOfWork.Repository<DocumentSeriesEntity>();

    public async Task<PagedResponse<InventoryCountGridRow>> GetPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var search = request.Search?.Trim();
        var query = BuildGridQuery().Where(x => string.IsNullOrWhiteSpace(search)
            || x.DocumentNo.Contains(search)
            || x.WarehouseName.Contains(search)
            || x.WarehouseCode.ToString().Contains(search)
            || (x.Description != null && x.Description.Contains(search)));
        query = query.ApplySearch(request, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = nameof(InventoryCountGridRow.Id),
            ["documentNo"] = nameof(InventoryCountGridRow.DocumentNo),
            ["warehouseCode"] = nameof(InventoryCountGridRow.WarehouseCode),
            ["warehouseName"] = nameof(InventoryCountGridRow.WarehouseName),
            ["description"] = nameof(InventoryCountGridRow.Description),
            ["branchCode"] = nameof(InventoryCountGridRow.BranchCode),
            ["priority"] = nameof(InventoryCountGridRow.Priority),
            ["taskCount"] = nameof(InventoryCountGridRow.TaskProgressSearchText),
            ["lineCount"] = nameof(InventoryCountGridRow.LineProgressSearchText),
            ["varianceLineCount"] = nameof(InventoryCountGridRow.VarianceLineCount),
            ["createdBy"] = nameof(InventoryCountGridRow.CreatedBySearchText),
            ["updatedBy"] = nameof(InventoryCountGridRow.UpdatedBySearchText)
        }, ["documentNo", "warehouseCode", "warehouseName"]);
        query = query.ApplyAdvancedFilters(request).ApplySort(request, nameof(InventoryCountGridRow.CreatedDate));
        return await query.ToPagedResponseAsync(request, ct);
    }

    public async Task<InventoryCountDetail> GetDetailAsync(long id, bool revealBookQuantity, CancellationToken ct = default)
    {
        var header = await BuildGridQuery().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw AppException.NotFound(Message(InventoryCountMessageKeys.NotFound));
        var entity = await Headers.FindByIdAsync(id, cancellationToken: ct) ?? throw AppException.NotFound(Message(InventoryCountMessageKeys.NotFound));

        var scopes = await (from scope in Scopes.Query()
                            where scope.HeaderId == id
                            join location in Locations.Query() on scope.LocationId equals location.Id into locationJoin
                            from location in locationJoin.DefaultIfEmpty()
                            join stock in Stocks.Query() on scope.StockId equals stock.Id into stockJoin
                            from stock in stockJoin.DefaultIfEmpty()
                            join yap in YapCodes.Query() on scope.YapCodeId equals yap.Id into yapJoin
                            from yap in yapJoin.DefaultIfEmpty()
                            orderby scope.SequenceNo
                            select new InventoryCountScopeRow(
                                scope.Id, scope.SequenceNo, scope.LocationId, location == null ? null : location.Code,
                                location == null ? null : location.Name, scope.StockId, stock == null ? null : stock.ErpStockCode,
                                stock == null ? null : stock.StockName, scope.YapCodeId, yap == null ? null : yap.ConfigurationCode,
                                scope.StockGroupCode, scope.IncludeDescendantLocations, scope.IncludeEmptyLocations))
            .ToListAsync(ct);

        var tasks = await (from task in Tasks.Query()
                           where task.HeaderId == id
                           join location in Locations.Query() on task.LocationId equals location.Id
                           orderby task.RouteSequence, task.CountRound
                           select new InventoryCountTaskRow(
                               task.Id, task.TaskCode, task.TaskNo, task.LocationId, location.Code, location.Name,
                               task.RouteSequence, task.CountRound, task.Status, task.AssignedUserId, task.LineCount,
                               task.CountedLineCount, task.VarianceLineCount, task.LocationBarcodeConfirmed,
                               task.StartedAtUtc, task.CompletedAtUtc, Convert.ToBase64String(task.RowVersion)))
            .ToListAsync(ct);

        var lines = await (from line in Lines.Query()
                           where line.HeaderId == id
                           join location in Locations.Query() on line.LocationId equals location.Id
                           join stock in Stocks.Query() on line.StockId equals stock.Id
                           join yap in YapCodes.Query() on line.YapCodeId equals yap.Id into yapJoin
                           from yap in yapJoin.DefaultIfEmpty()
                           orderby line.TaskId, line.SequenceNo
                           select new InventoryCountLineRow(
                               line.Id, line.TaskId, line.SequenceNo, line.LocationId, location.Code, line.StockId,
                               stock.ErpStockCode, stock.StockName, line.YapCodeId, yap == null ? null : yap.ConfigurationCode,
                               line.UnitCode, line.LotNo == "" ? null : line.LotNo, line.SerialNo == "" ? null : line.SerialNo,
                               line.StockStatus, revealBookQuantity ? line.SnapshotQuantity : null, line.CountedQuantity,
                               revealBookQuantity ? line.VarianceQuantity : null, revealBookQuantity ? line.VariancePercentage : null,
                               line.IsUnexpectedStock, line.IsWithinTolerance, line.Status, Convert.ToBase64String(line.RowVersion)))
            .ToListAsync(ct);

        return new InventoryCountDetail(
            header, entity.QuantityTolerance, entity.PercentageTolerance, entity.MaxCountAttempts,
            entity.RequireIndependentRecount, entity.AllowUnexpectedStock, entity.AutoApproveWithinTolerance,
            entity.IncludeEmptyLocations, entity.SnapshotMovementEntryId, Convert.ToBase64String(entity.RowVersion),
            scopes, tasks, lines);
    }

    public async Task<long> CreateDraftAsync(CreateInventoryCountDraftRequest request, long actor, CancellationToken ct = default)
    {
        ValidateDraft(request.WarehouseId, request.Priority, request.PlannedStartUtc, request.PlannedEndUtc,
            request.QuantityTolerance ?? 0, request.PercentageTolerance ?? 0, request.MaxCountAttempts ?? 2, request.Scopes);

        var warehouse = await Warehouses.FindByIdAsync(request.WarehouseId, cancellationToken: ct)
            ?? throw AppException.NotFound(Message(InventoryCountMessageKeys.WarehouseNotFound));
        var branchCode = NormalizeBranch(request.BranchCode, warehouse.BranchCode);
        await ValidateScopesAsync(request.WarehouseId, request.Scopes, ct);
        var policy = await ResolvePolicyEntityAsync(branchCode, request.WarehouseId, ct);
        var document = await AllocateDocumentNoAsync(branchCode, request.DocumentSeriesId, ct);

        var entity = new InventoryCountHeader
        {
            BranchCode = branchCode,
            DocumentSeriesId = document.SeriesId,
            DocumentNo = document.DocumentNo,
            WarehouseId = request.WarehouseId,
            CountType = request.CountType,
            CountMode = request.CountMode ?? policy.DefaultCountMode,
            MovementPolicy = request.MovementPolicy ?? policy.DefaultMovementPolicy,
            Priority = request.Priority,
            PlannedStartUtc = AsUtc(request.PlannedStartUtc),
            PlannedEndUtc = AsUtc(request.PlannedEndUtc),
            QuantityTolerance = request.QuantityTolerance ?? policy.QuantityTolerance,
            PercentageTolerance = request.PercentageTolerance ?? policy.PercentageTolerance,
            MaxCountAttempts = request.MaxCountAttempts ?? policy.MaxCountAttempts,
            RequireIndependentRecount = request.RequireIndependentRecount ?? policy.RequireIndependentRecount,
            AllowUnexpectedStock = request.AllowUnexpectedStock ?? policy.AllowUnexpectedStock,
            AutoApproveWithinTolerance = request.AutoApproveWithinTolerance ?? policy.AutoApproveWithinTolerance,
            IncludeEmptyLocations = request.IncludeEmptyLocations,
            Description = Clean(request.Description, 1000),
            Status = request.PlannedStartUtc.HasValue ? InventoryCountStatus.Planned : InventoryCountStatus.Draft,
            CreatedBy = actor,
            CreatedDate = DateTime.UtcNow
        };
        await Headers.AddAsync(entity, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await ReplaceScopesAsync(entity, request.Scopes, actor, ct);
        await audit.WriteAsync(new AuditLogWriteEntry("inventory-count.create", nameof(InventoryCountHeader), entity.Id.ToString(), "Succeeded", "inventory-count", NewValues: Snapshot(entity), ChangedFields: HeaderFields), ct);
        return entity.Id;
    }

    public async Task UpdateDraftAsync(long id, UpdateInventoryCountDraftRequest request, long actor, CancellationToken ct = default)
    {
        var entity = await Headers.FindByIdAsync(id, tracking: true, ct)
            ?? throw AppException.NotFound(Message(InventoryCountMessageKeys.NotFound));
        EnsureDraft(entity);
        EnsureConcurrency(entity.RowVersion, request.ConcurrencyToken);
        ValidateDraft(entity.WarehouseId, request.Priority, request.PlannedStartUtc, request.PlannedEndUtc,
            request.QuantityTolerance, request.PercentageTolerance, request.MaxCountAttempts, request.Scopes);
        await ValidateScopesAsync(entity.WarehouseId, request.Scopes, ct);
        var old = Snapshot(entity);

        entity.CountType = request.CountType;
        entity.CountMode = request.CountMode;
        entity.MovementPolicy = request.MovementPolicy;
        entity.Priority = request.Priority;
        entity.PlannedStartUtc = AsUtc(request.PlannedStartUtc);
        entity.PlannedEndUtc = AsUtc(request.PlannedEndUtc);
        entity.QuantityTolerance = request.QuantityTolerance;
        entity.PercentageTolerance = request.PercentageTolerance;
        entity.MaxCountAttempts = request.MaxCountAttempts;
        entity.RequireIndependentRecount = request.RequireIndependentRecount;
        entity.AllowUnexpectedStock = request.AllowUnexpectedStock;
        entity.AutoApproveWithinTolerance = request.AutoApproveWithinTolerance;
        entity.IncludeEmptyLocations = request.IncludeEmptyLocations;
        entity.Description = Clean(request.Description, 1000);
        entity.Status = request.PlannedStartUtc.HasValue ? InventoryCountStatus.Planned : InventoryCountStatus.Draft;
        entity.UpdatedBy = actor;
        entity.UpdatedDate = DateTime.UtcNow;
        await ReplaceScopesAsync(entity, request.Scopes, actor, ct);
        await audit.WriteAsync(new AuditLogWriteEntry("inventory-count.update", nameof(InventoryCountHeader), id.ToString(), "Succeeded", "inventory-count", OldValues: old, NewValues: Snapshot(entity), ChangedFields: HeaderFields), ct);
    }

    public async Task DeleteDraftAsync(long id, long actor, CancellationToken ct = default)
    {
        var entity = await Headers.FindByIdAsync(id, tracking: true, ct)
            ?? throw AppException.NotFound(Message(InventoryCountMessageKeys.NotFound));
        EnsureDraft(entity);
        var old = Snapshot(entity);
        entity.IsDeleted = true;
        entity.DeletedBy = actor;
        entity.DeletedDate = DateTime.UtcNow;
        var scopes = await Scopes.Query(tracking: true).Where(x => x.HeaderId == id).ToListAsync(ct);
        foreach (var scope in scopes)
        {
            scope.IsDeleted = true;
            scope.DeletedBy = actor;
            scope.DeletedDate = DateTime.UtcNow;
        }
        await unitOfWork.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry("inventory-count.delete", nameof(InventoryCountHeader), id.ToString(), "Succeeded", "inventory-count", OldValues: old, ChangedFields: ["IsDeleted"]), ct);
    }

    public async Task<InventoryCountPreviewResult> PreviewAsync(long id, CancellationToken ct = default)
    {
        var header = await Headers.FindByIdAsync(id, cancellationToken: ct)
            ?? throw AppException.NotFound(Message(InventoryCountMessageKeys.NotFound));
        EnsureDraft(header);
        var candidate = await BuildCandidatesAsync(header, ct);
        return BuildPreview(candidate);
    }

    public async Task<ReleaseInventoryCountResult> ReleaseAsync(long id, ReleaseInventoryCountRequest request, long actor, CancellationToken ct = default)
    {
        var key = Clean(request.IdempotencyKey, 100) ?? string.Empty;
        if (key.Length is < 8 or > 100) throw AppException.BadRequest(Message(InventoryCountMessageKeys.InvalidRequest));

        ReleaseInventoryCountResult? result = null;
        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var header = await Headers.FindByIdAsync(id, tracking: true, token)
                ?? throw AppException.NotFound(Message(InventoryCountMessageKeys.NotFound));
            if (header.ReleaseIdempotencyKey == key && header.Status is not InventoryCountStatus.Draft and not InventoryCountStatus.Planned)
            {
                result = new ReleaseInventoryCountResult(header.Id, header.DocumentNo, header.TaskCount, header.LineCount,
                    header.SnapshotMovementEntryId ?? 0, header.SnapshotAtUtc ?? DateTime.UtcNow, true);
                return true;
            }
            EnsureDraft(header);
            EnsureConcurrency(header.RowVersion, request.ConcurrencyToken);

            var candidate = await BuildCandidatesAsync(header, token);
            if (candidate.Locations.Count == 0)
                throw AppException.BadRequest(Message(InventoryCountMessageKeys.EmptyScope));

            var locationIds = candidate.Locations.Select(x => x.Id).ToArray();
            var activeStatuses = new[]
            {
                InventoryCountTaskStatus.Ready, InventoryCountTaskStatus.Assigned, InventoryCountTaskStatus.InProgress,
                InventoryCountTaskStatus.AwaitingReview, InventoryCountTaskStatus.RecountRequired
            };
            var conflict = await Tasks.Query().AnyAsync(x => locationIds.Contains(x.LocationId) && activeStatuses.Contains(x.Status), token);
            if (conflict) throw AppException.Conflict(Message(InventoryCountMessageKeys.ActiveCountConflict));

            var now = DateTime.UtcNow;
            var watermark = await MovementEntries.Query().Select(x => (long?)x.Id).MaxAsync(token) ?? 0;
            var tasks = new List<InventoryCountTask>(candidate.Locations.Count);
            var route = 1;
            foreach (var location in candidate.Locations.OrderBy(x => x.ZoneCode).ThenBy(x => x.AisleNo).ThenBy(x => x.RackNo).ThenBy(x => x.LevelNo).ThenBy(x => x.BinNo).ThenBy(x => x.Code))
            {
                var task = new InventoryCountTask
                {
                    BranchCode = header.BranchCode,
                    HeaderId = header.Id,
                    TaskNo = $"{header.DocumentNo}-{route:0000}",
                    WarehouseId = header.WarehouseId,
                    LocationId = location.Id,
                    RouteSequence = route++,
                    Status = InventoryCountTaskStatus.Ready,
                    CreatedBy = actor,
                    CreatedDate = now
                };
                var sequence = 1;
                foreach (var balance in candidate.Balances.Where(x => x.LocationId == location.Id)
                             .OrderBy(x => x.StockId).ThenBy(x => x.YapCodeId).ThenBy(x => x.LotNo).ThenBy(x => x.SerialNo))
                {
                    task.Lines.Add(new InventoryCountLine
                    {
                        BranchCode = header.BranchCode,
                        HeaderId = header.Id,
                        SequenceNo = sequence++,
                        WarehouseId = header.WarehouseId,
                        LocationId = location.Id,
                        StockId = balance.StockId,
                        YapCodeId = balance.YapCodeId,
                        UnitCode = balance.UnitCode,
                        LotNo = balance.LotNo,
                        SerialNo = balance.SerialNo,
                        StockStatus = balance.StockStatus,
                        SnapshotQuantity = balance.Quantity,
                        Status = InventoryCountLineStatus.Pending,
                        CreatedBy = actor,
                        CreatedDate = now
                    });
                }
                task.LineCount = task.Lines.Count;
                tasks.Add(task);
            }

            await Tasks.AddRangeAsync(tasks, token);
            header.Status = InventoryCountStatus.Released;
            header.ReleaseIdempotencyKey = key;
            header.SnapshotMovementEntryId = watermark;
            header.SnapshotAtUtc = now;
            header.ReleasedAtUtc = now;
            header.ReleasedByUserId = actor;
            header.TaskCount = tasks.Count;
            header.LineCount = tasks.Sum(x => x.LineCount);
            header.UpdatedBy = actor;
            header.UpdatedDate = now;
            await unitOfWork.SaveChangesAsync(token);

            result = new ReleaseInventoryCountResult(header.Id, header.DocumentNo, header.TaskCount, header.LineCount, watermark, now, false);
            return true;
        }, ct, IsolationLevel.Serializable);

        await audit.WriteAsync(new AuditLogWriteEntry("inventory-count.release", nameof(InventoryCountHeader), id.ToString(), "Succeeded", "inventory-count", Reason: key, NewValues: result, ChangedFields: ["Status", "SnapshotMovementEntryId", "TaskCount", "LineCount"]), ct);
        return result!;
    }

    public async Task<InventoryCountPolicyResponse> GetPolicyAsync(string branchCode, long? warehouseId, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branchCode, branchCode);
        var entity = await ResolvePolicyEntityAsync(normalizedBranch, warehouseId, ct);
        return PolicyResponse(entity, normalizedBranch, warehouseId);
    }

    public async Task<InventoryCountPolicyResponse> UpsertPolicyAsync(UpsertInventoryCountPolicyRequest request, long actor, CancellationToken ct = default)
    {
        if (request.QuantityTolerance < 0 || request.PercentageTolerance < 0 || request.MaxCountAttempts is < 1 or > 10)
            throw AppException.BadRequest(Message(InventoryCountMessageKeys.InvalidRequest));
        var branch = NormalizeBranch(request.BranchCode, request.BranchCode);
        if (request.WarehouseId.HasValue)
        {
            var warehouse = await Warehouses.FindByIdAsync(request.WarehouseId.Value, cancellationToken: ct)
                ?? throw AppException.NotFound(Message(InventoryCountMessageKeys.WarehouseNotFound));
            branch = NormalizeBranch(branch, warehouse.BranchCode);
        }
        var entity = await Policies.Query(tracking: true).FirstOrDefaultAsync(x => x.BranchCode == branch && x.WarehouseId == request.WarehouseId, ct);
        if (entity is null)
        {
            entity = new InventoryCountPolicy { BranchCode = branch, WarehouseId = request.WarehouseId, CreatedBy = actor, CreatedDate = DateTime.UtcNow };
            await Policies.AddAsync(entity, ct);
        }
        else
        {
            EnsureConcurrency(entity.RowVersion, request.ConcurrencyToken);
            entity.UpdatedBy = actor;
            entity.UpdatedDate = DateTime.UtcNow;
        }
        entity.DefaultCountMode = request.DefaultCountMode;
        entity.DefaultMovementPolicy = request.DefaultMovementPolicy;
        entity.QuantityTolerance = request.QuantityTolerance;
        entity.PercentageTolerance = request.PercentageTolerance;
        entity.MaxCountAttempts = request.MaxCountAttempts;
        entity.RequireIndependentRecount = request.RequireIndependentRecount;
        entity.AllowUnexpectedStock = request.AllowUnexpectedStock;
        entity.AutoApproveWithinTolerance = request.AutoApproveWithinTolerance;
        entity.RequireDifferenceReason = request.RequireDifferenceReason;
        entity.IsActive = request.IsActive;
        await unitOfWork.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry("inventory-count.policy.upsert", nameof(InventoryCountPolicy), entity.Id.ToString(), "Succeeded", "inventory-count", NewValues: PolicyResponse(entity, branch, request.WarehouseId), ChangedFields: PolicyFields), ct);
        return PolicyResponse(entity, branch, request.WarehouseId);
    }

    private IQueryable<InventoryCountGridRow> BuildGridQuery() =>
        from header in Headers.Query()
        join warehouse in Warehouses.Query() on header.WarehouseId equals warehouse.Id
        join createdUser in unitOfWork.Repository<User>().Query() on header.CreatedBy equals (long?)createdUser.Id into createdUsers
        from createdUser in createdUsers.DefaultIfEmpty()
        join createdDetail in unitOfWork.Repository<UserDetail>().Query() on header.CreatedBy equals (long?)createdDetail.UserId into createdDetails
        from createdDetail in createdDetails.DefaultIfEmpty()
        join updatedUser in unitOfWork.Repository<User>().Query() on header.UpdatedBy equals (long?)updatedUser.Id into updatedUsers
        from updatedUser in updatedUsers.DefaultIfEmpty()
        join updatedDetail in unitOfWork.Repository<UserDetail>().Query() on header.UpdatedBy equals (long?)updatedDetail.UserId into updatedDetails
        from updatedDetail in updatedDetails.DefaultIfEmpty()
        select new InventoryCountGridRow
        {
            Id = header.Id,
            CountCode = header.CountCode,
            DocumentNo = header.DocumentNo,
            BranchCode = header.BranchCode,
            WarehouseId = header.WarehouseId,
            WarehouseCode = warehouse.WarehouseCode,
            WarehouseName = warehouse.WarehouseName,
            CountType = header.CountType,
            CountMode = header.CountMode,
            MovementPolicy = header.MovementPolicy,
            Status = header.Status,
            Priority = header.Priority,
            PlannedStartUtc = header.PlannedStartUtc,
            PlannedEndUtc = header.PlannedEndUtc,
            SnapshotAtUtc = header.SnapshotAtUtc,
            TaskCount = header.TaskCount,
            CompletedTaskCount = header.CompletedTaskCount,
            LineCount = header.LineCount,
            CountedLineCount = header.CountedLineCount,
            VarianceLineCount = header.VarianceLineCount,
            Description = header.Description,
            CreatedBy = header.CreatedBy,
            CreatedDate = header.CreatedDate,
            UpdatedBy = header.UpdatedBy,
            UpdatedDate = header.UpdatedDate,
            ConcurrencyToken = Convert.ToBase64String(header.RowVersion),
            TaskProgressSearchText = header.CompletedTaskCount + " " + header.TaskCount,
            LineProgressSearchText = header.CountedLineCount + " " + header.LineCount,
            CreatedBySearchText = (header.CreatedBy == null ? "Sistem System" : header.CreatedBy.GetValueOrDefault().ToString()) + " "
                + (createdUser == null ? "" : createdUser.Username + " " + createdUser.Email) + " "
                + (createdDetail == null ? "" : createdDetail.FirstName + " " + createdDetail.LastName),
            UpdatedBySearchText = (header.UpdatedBy == null ? "Sistem System" : header.UpdatedBy.GetValueOrDefault().ToString()) + " "
                + (updatedUser == null ? "" : updatedUser.Username + " " + updatedUser.Email) + " "
                + (updatedDetail == null ? "" : updatedDetail.FirstName + " " + updatedDetail.LastName)
        };

    private async Task ReplaceScopesAsync(InventoryCountHeader header, IReadOnlyList<InventoryCountScopeRequest> requests, long actor, CancellationToken ct)
    {
        var existing = await Scopes.Query(tracking: true).Where(x => x.HeaderId == header.Id).ToListAsync(ct);
        foreach (var scope in existing)
        {
            scope.IsDeleted = true;
            scope.DeletedBy = actor;
            scope.DeletedDate = DateTime.UtcNow;
        }
        var sequence = 1;
        await Scopes.AddRangeAsync(requests.Select(x => new InventoryCountScope
        {
            BranchCode = header.BranchCode,
            HeaderId = header.Id,
            SequenceNo = sequence++,
            LocationId = x.LocationId,
            StockId = x.StockId,
            YapCodeId = x.YapCodeId,
            StockGroupCode = Clean(x.StockGroupCode, 50),
            IncludeDescendantLocations = x.IncludeDescendantLocations,
            IncludeEmptyLocations = x.IncludeEmptyLocations,
            CreatedBy = actor,
            CreatedDate = DateTime.UtcNow
        }), ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task ValidateScopesAsync(long warehouseId, IReadOnlyList<InventoryCountScopeRequest> scopes, CancellationToken ct)
    {
        var locationIds = scopes.Where(x => x.LocationId.HasValue).Select(x => x.LocationId!.Value).Distinct().ToArray();
        if (locationIds.Length > 0)
        {
            var validCount = await Locations.Query().CountAsync(x => locationIds.Contains(x.Id) && x.WarehouseId == warehouseId && x.IsActive && x.AllowCycleCount, ct);
            if (validCount != locationIds.Length) throw AppException.BadRequest(Message(InventoryCountMessageKeys.LocationNotCountable));
        }
        var stockIds = scopes.Where(x => x.StockId.HasValue).Select(x => x.StockId!.Value).Distinct().ToArray();
        if (stockIds.Length > 0 && await Stocks.Query().CountAsync(x => stockIds.Contains(x.Id), ct) != stockIds.Length)
            throw AppException.BadRequest(Message(InventoryCountMessageKeys.InvalidRequest));
        var yapIds = scopes.Where(x => x.YapCodeId.HasValue).Select(x => x.YapCodeId!.Value).Distinct().ToArray();
        if (yapIds.Length > 0 && await YapCodes.Query().CountAsync(x => yapIds.Contains(x.Id), ct) != yapIds.Length)
            throw AppException.BadRequest(Message(InventoryCountMessageKeys.InvalidRequest));
    }

    private async Task<CandidateSet> BuildCandidatesAsync(InventoryCountHeader header, CancellationToken ct)
    {
        var scopes = await Scopes.Query().Where(x => x.HeaderId == header.Id).OrderBy(x => x.SequenceNo).ToListAsync(ct);
        var locations = await Locations.Query().Where(x => x.WarehouseId == header.WarehouseId && x.IsActive && x.AllowCycleCount).ToListAsync(ct);
        if (locations.Count == 0) return new CandidateSet([], []);

        var scopeLocationIds = ResolveScopeLocationMap(scopes, locations);
        var selectedLocationIds = scopes.Count == 0
            ? locations.Select(x => x.Id).ToHashSet()
            : scopeLocationIds.Values.SelectMany(x => x).ToHashSet();
        var selectedLocations = locations.Where(x => selectedLocationIds.Contains(x.Id)).ToList();
        if (selectedLocations.Count == 0) return new CandidateSet([], []);

        var ids = selectedLocations.Select(x => x.Id).ToArray();
        var balances = await Balances.Query().Where(x => ids.Contains(x.LocationId) && x.Quantity != 0).ToListAsync(ct);
        if (scopes.Count > 0 && balances.Count > 0)
        {
            var stockIds = balances.Select(x => x.StockId).Distinct().ToArray();
            var groups = await Stocks.Query().Where(x => stockIds.Contains(x.Id)).Select(x => new { x.Id, x.GroupCode }).ToDictionaryAsync(x => x.Id, x => x.GroupCode, ct);
            balances = balances.Where(balance => scopes.Any(scope => ScopeMatches(scope, balance, scopeLocationIds, groups))).ToList();
        }

        var locationsWithBalances = balances.Select(x => x.LocationId).ToHashSet();
        selectedLocations = selectedLocations.Where(location => locationsWithBalances.Contains(location.Id)
            || header.IncludeEmptyLocations
            || scopes.Any(scope => scope.IncludeEmptyLocations && ScopeLocationMatches(scope, location.Id, scopeLocationIds))).ToList();
        return new CandidateSet(selectedLocations, balances.Where(x => selectedLocations.Any(l => l.Id == x.LocationId)).ToList());
    }

    private static IReadOnlyDictionary<long, HashSet<long>> ResolveScopeLocationMap(IReadOnlyList<InventoryCountScope> scopes, IReadOnlyList<WarehouseLocation> locations)
    {
        var children = locations.Where(x => x.ParentLocationId.HasValue).GroupBy(x => x.ParentLocationId!.Value)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Id).ToArray());
        var all = locations.Select(x => x.Id).ToHashSet();
        var result = new Dictionary<long, HashSet<long>>();
        foreach (var scope in scopes)
        {
            if (!scope.LocationId.HasValue)
            {
                result[scope.Id] = new HashSet<long>(all);
                continue;
            }
            var selected = new HashSet<long> { scope.LocationId.Value };
            if (!scope.IncludeDescendantLocations)
            {
                result[scope.Id] = selected;
                continue;
            }
            var queue = new Queue<long>();
            queue.Enqueue(scope.LocationId.Value);
            while (queue.Count > 0)
            {
                var parent = queue.Dequeue();
                if (!children.TryGetValue(parent, out var directChildren)) continue;
                foreach (var child in directChildren)
                    if (selected.Add(child)) queue.Enqueue(child);
            }
            result[scope.Id] = selected;
        }
        return result;
    }

    private static bool ScopeMatches(InventoryCountScope scope, LocationStockBalance balance, IReadOnlyDictionary<long, HashSet<long>> scopeLocationIds, IReadOnlyDictionary<long, string?> groups) =>
        ScopeLocationMatches(scope, balance.LocationId, scopeLocationIds)
        && (!scope.StockId.HasValue || scope.StockId == balance.StockId)
        && (!scope.YapCodeId.HasValue || scope.YapCodeId == balance.YapCodeId)
        && (string.IsNullOrWhiteSpace(scope.StockGroupCode)
            || groups.TryGetValue(balance.StockId, out var group) && string.Equals(group, scope.StockGroupCode, StringComparison.OrdinalIgnoreCase));

    private static bool ScopeLocationMatches(InventoryCountScope scope, long locationId, IReadOnlyDictionary<long, HashSet<long>> scopeLocationIds) =>
        scopeLocationIds.TryGetValue(scope.Id, out var allowed) && allowed.Contains(locationId);

    private InventoryCountPreviewResult BuildPreview(CandidateSet candidate)
    {
        var locationsWithBalance = candidate.Balances.Select(x => x.LocationId).ToHashSet();
        return new InventoryCountPreviewResult(
            candidate.Locations.Count,
            candidate.Locations.Count(x => !locationsWithBalance.Contains(x.Id)),
            candidate.Balances.Count,
            candidate.Balances.Select(x => x.StockId).Distinct().Count(),
            candidate.Balances.Where(x => x.LotNo != "").Select(x => new { x.StockId, x.LotNo }).Distinct().Count(),
            candidate.Balances.Where(x => x.SerialNo != "").Select(x => new { x.StockId, x.SerialNo }).Distinct().Count(),
            candidate.Balances.Sum(x => x.Quantity),
            candidate.Locations.Count == 0 ? [Message(InventoryCountMessageKeys.EmptyScope)] : []);
    }

    private async Task<InventoryCountPolicy> ResolvePolicyEntityAsync(string branch, long? warehouseId, CancellationToken ct)
    {
        var policy = await Policies.Query().Where(x => x.BranchCode == branch && x.IsActive && (x.WarehouseId == warehouseId || x.WarehouseId == null))
            .OrderByDescending(x => x.WarehouseId.HasValue).FirstOrDefaultAsync(ct);
        return policy ?? new InventoryCountPolicy { BranchCode = branch, WarehouseId = warehouseId };
    }

    private async Task<(long? SeriesId, string DocumentNo)> AllocateDocumentNoAsync(string branch, long? requestedSeriesId, CancellationToken ct)
    {
        var seriesId = requestedSeriesId;
        if (!seriesId.HasValue)
            seriesId = await DocumentSeries.Query().Where(x => x.BranchCode == branch && x.DocumentType == WmsDocumentType.InventoryCount && x.IsActive)
                .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Id).Select(x => (long?)x.Id).FirstOrDefaultAsync(ct);
        if (seriesId.HasValue)
        {
            var allocated = await documentNumberAllocator.AllocateAsync(seriesId.Value, WmsDocumentType.InventoryCount, DateTime.UtcNow, ct);
            return (seriesId, allocated.DocumentNumber);
        }
        return (null, $"SAY-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..31].ToUpperInvariant());
    }

    private void ValidateDraft(long warehouseId, int priority, DateTime? plannedStart, DateTime? plannedEnd,
        decimal quantityTolerance, decimal percentageTolerance, int maxAttempts, IReadOnlyList<InventoryCountScopeRequest> scopes)
    {
        if (warehouseId <= 0 || priority is < 1 or > 5 || quantityTolerance < 0 || percentageTolerance < 0 || maxAttempts is < 1 or > 10)
            throw AppException.BadRequest(Message(InventoryCountMessageKeys.InvalidRequest));
        if (plannedStart.HasValue && plannedEnd.HasValue && plannedEnd.Value <= plannedStart.Value)
            throw AppException.BadRequest(Message(InventoryCountMessageKeys.InvalidRequest));
        if (scopes.Any(x => x.LocationId is <= 0 || x.StockId is <= 0 || x.YapCodeId is <= 0))
            throw AppException.BadRequest(Message(InventoryCountMessageKeys.InvalidRequest));
    }

    private void EnsureDraft(InventoryCountHeader header)
    {
        if (header.Status is not InventoryCountStatus.Draft and not InventoryCountStatus.Planned)
            throw AppException.Conflict(Message(InventoryCountMessageKeys.DraftOnly));
    }

    private void EnsureConcurrency(byte[] rowVersion, string? token)
    {
        byte[] supplied;
        try { supplied = Convert.FromBase64String(token ?? string.Empty); }
        catch (FormatException) { throw AppException.Conflict(Message(InventoryCountMessageKeys.ConcurrencyConflict)); }
        if (!rowVersion.SequenceEqual(supplied)) throw AppException.Conflict(Message(InventoryCountMessageKeys.ConcurrencyConflict));
    }

    private string Message(string key) => localizer[key].Value;
    private string NormalizeBranch(string requested, string warehouseBranch)
    {
        var warehouse = string.IsNullOrWhiteSpace(warehouseBranch) ? "0" : warehouseBranch.Trim();
        var branch = string.IsNullOrWhiteSpace(requested) ? warehouse : requested.Trim();
        if (!string.Equals(branch, warehouse, StringComparison.OrdinalIgnoreCase)) throw AppException.BadRequest(Message(InventoryCountMessageKeys.InvalidRequest));
        return branch;
    }
    private static string? Clean(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? value.Value.Kind == DateTimeKind.Utc ? value : value.Value.ToUniversalTime() : null;
    private static object Snapshot(InventoryCountHeader x) => new { x.Id, x.DocumentNo, x.CountType, x.CountMode, x.MovementPolicy, x.Status, x.WarehouseId, x.Priority, x.PlannedStartUtc, x.PlannedEndUtc, x.QuantityTolerance, x.PercentageTolerance, x.MaxCountAttempts, x.RequireIndependentRecount, x.AllowUnexpectedStock, x.AutoApproveWithinTolerance, x.IncludeEmptyLocations, x.SnapshotMovementEntryId, x.TaskCount, x.LineCount };
    private static InventoryCountPolicyResponse PolicyResponse(InventoryCountPolicy x, string branch, long? warehouseId) => new(x.Id == 0 ? null : x.Id, branch, x.Id == 0 ? warehouseId : x.WarehouseId, x.DefaultCountMode, x.DefaultMovementPolicy, x.QuantityTolerance, x.PercentageTolerance, x.MaxCountAttempts, x.RequireIndependentRecount, x.AllowUnexpectedStock, x.AutoApproveWithinTolerance, x.RequireDifferenceReason, x.IsActive, x.Id == 0 ? null : Convert.ToBase64String(x.RowVersion));
    private static readonly string[] HeaderFields = ["CountType", "CountMode", "MovementPolicy", "Status", "WarehouseId", "Priority", "PlannedStartUtc", "PlannedEndUtc", "QuantityTolerance", "PercentageTolerance", "MaxCountAttempts", "RequireIndependentRecount", "AllowUnexpectedStock", "AutoApproveWithinTolerance", "IncludeEmptyLocations"];
    private static readonly string[] PolicyFields = ["DefaultCountMode", "DefaultMovementPolicy", "QuantityTolerance", "PercentageTolerance", "MaxCountAttempts", "RequireIndependentRecount", "AllowUnexpectedStock", "AutoApproveWithinTolerance", "RequireDifferenceReason", "IsActive"];
    private sealed record CandidateSet(List<WarehouseLocation> Locations, List<LocationStockBalance> Balances);
}
