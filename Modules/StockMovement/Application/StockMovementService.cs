using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Domain;
using verii_wms_api_v2.Modules.Stock.Application;
using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.StockTracking.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.StockMovement.Application;

public sealed class StockMovementService(
    IUnitOfWork unitOfWork,
    IAuditLogWriter audit,
    IStockBalanceService balanceProjection,
    IStockTrackingPolicyResolver trackingPolicyResolver) : IStockMovementService
{
    private static readonly IReadOnlySet<string> EntrySummaryColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "entryCount", "inboundQuantity", "outboundQuantity"
    };
    private static readonly IReadOnlySet<string> ReversalSummaryColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "status"
    };
    private static readonly IReadOnlyDictionary<string, string> GridSearchColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = nameof(StockMovementGridProjection.Id),
        ["operationCode"] = nameof(StockMovementGridProjection.OperationCode),
        ["operationType"] = nameof(StockMovementGridProjection.OperationType),
        ["referenceNo"] = nameof(StockMovementGridProjection.ReferenceSearchText),
        ["entryCount"] = nameof(StockMovementGridProjection.EntryCount),
        ["inboundQuantity"] = nameof(StockMovementGridProjection.InboundQuantity),
        ["outboundQuantity"] = nameof(StockMovementGridProjection.OutboundQuantity),
        ["reason"] = nameof(StockMovementGridProjection.Reason)
    };
    private static readonly string[] DefaultGridSearchColumns = ["operationCode", "operationType", "referenceNo", "reason"];

    private IGenericRepository<StockMovementOperation> Operations => unitOfWork.Repository<StockMovementOperation>();
    private IGenericRepository<StockMovementEntry> Entries => unitOfWork.Repository<StockMovementEntry>();
    private IGenericRepository<StockEntity> Stocks => unitOfWork.Repository<StockEntity>();
    private IGenericRepository<WarehouseEntity> Warehouses => unitOfWork.Repository<WarehouseEntity>();
    private IGenericRepository<WarehouseLocation> Locations => unitOfWork.Repository<WarehouseLocation>();
    private IGenericRepository<Modules.YapCode.Domain.YapCode> YapCodes => unitOfWork.Repository<Modules.YapCode.Domain.YapCode>();
    private IGenericRepository<LocationStockBalance> LocationBalances => unitOfWork.Repository<LocationStockBalance>();
    private IGenericRepository<StockSerialRegistry> SerialRegistry => unitOfWork.Repository<StockSerialRegistry>();
    private readonly Dictionary<(string BranchCode, long StockId), EffectiveStockTrackingPolicy> _trackingPolicyCache = [];

    public async Task<PagedResponse<StockMovementGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        var entries = Entries.Query();
        var operations = Operations.Query();
        var query = BuildPagedQuery(request, operations, entries);
        var countQuery = BuildCountQuery(request, operations, entries);
        var page = await query.ToPagedResponseAsync(countQuery, request, cancellationToken);
        if (page.Items.Count == 0) return page;

        var includeEntrySummary = RequiresInMainQuery(request, EntrySummaryColumns);
        var includeReversalSummary = RequiresInMainQuery(request, ReversalSummaryColumns);
        if (includeEntrySummary && includeReversalSummary) return page;
        return new PagedResponse<StockMovementGridRow>
        {
            Items = await EnrichGridRowsAsync(page.Items, entries, operations,
                !includeEntrySummary, !includeReversalSummary, cancellationToken),
            TotalCount = page.TotalCount,
            PageNumber = page.PageNumber,
            PageSize = page.PageSize
        };
    }

    internal static IQueryable<StockMovementGridRow> BuildPagedQuery(
        PagedRequest request,
        IQueryable<StockMovementOperation> operations,
        IQueryable<StockMovementEntry> entries)
    {
        var rows = BuildGridRows(operations, entries,
            RequiresInMainQuery(request, EntrySummaryColumns),
            RequiresInMainQuery(request, ReversalSummaryColumns));
        rows = rows.ApplySearch(request, GridSearchColumns, DefaultGridSearchColumns)
            .ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(StockMovementGridProjection.OccurredAt));
        return rows.Select(ToGridRow());
    }

    internal static IQueryable<long> BuildCountQuery(
        PagedRequest request,
        IQueryable<StockMovementOperation> operations,
        IQueryable<StockMovementEntry> entries)
    {
        var rows = BuildGridRows(operations, entries,
            RequiresForCount(request, EntrySummaryColumns),
            RequiresForCount(request, ReversalSummaryColumns));
        return rows.ApplySearch(request, GridSearchColumns, DefaultGridSearchColumns)
            .ApplyAdvancedFilters(request)
            .Select(x => x.Id);
    }

    private static IQueryable<StockMovementGridProjection> BuildGridRows(
        IQueryable<StockMovementOperation> operations,
        IQueryable<StockMovementEntry> entries,
        bool includeEntrySummary,
        bool includeReversalSummary)
    {
        IQueryable<StockMovementGridProjection> rows = operations.Select(x => new StockMovementGridProjection
        {
            Id = x.Id,
            OperationCode = x.OperationCode,
            OperationType = x.OperationType,
            Status = x.Status,
            ReferenceType = x.ReferenceType,
            ReferenceNo = x.ReferenceNo,
            OccurredAt = x.OccurredAt,
            Reason = x.Reason,
            ReversalOfOperationId = x.ReversalOfOperationId,
            CreatedBy = x.CreatedBy,
            CreatedDate = x.CreatedDate,
            UpdatedBy = x.UpdatedBy,
            UpdatedDate = x.UpdatedDate,
            ReferenceSearchText = (x.ReferenceType ?? "") + " / " + (x.ReferenceNo ?? "")
        });

        if (includeEntrySummary)
        {
            var totals = entries.GroupBy(x => x.OperationId).Select(groupRows => new
            {
                OperationId = groupRows.Key,
                EntryCount = groupRows.Count(),
                InboundQuantity = groupRows.Where(x => x.QuantityDelta > 0).Sum(x => (decimal?)x.QuantityDelta) ?? 0,
                OutboundQuantity = -(groupRows.Where(x => x.QuantityDelta < 0).Sum(x => (decimal?)x.QuantityDelta) ?? 0)
            });
            rows = from row in rows
                   join total in totals on row.Id equals total.OperationId into totalRows
                   from total in totalRows.DefaultIfEmpty()
                   select new StockMovementGridProjection
                   {
                       Id = row.Id, OperationCode = row.OperationCode, OperationType = row.OperationType, Status = row.Status,
                       ReferenceType = row.ReferenceType, ReferenceNo = row.ReferenceNo, OccurredAt = row.OccurredAt,
                       EntryCount = (int?)total.EntryCount ?? 0, InboundQuantity = (decimal?)total.InboundQuantity ?? 0,
                       OutboundQuantity = (decimal?)total.OutboundQuantity ?? 0, Reason = row.Reason,
                       ReversalOfOperationId = row.ReversalOfOperationId, CreatedBy = row.CreatedBy, CreatedDate = row.CreatedDate,
                       UpdatedBy = row.UpdatedBy, UpdatedDate = row.UpdatedDate, ReferenceSearchText = row.ReferenceSearchText
                   };
        }

        if (includeReversalSummary)
        {
            var reversals = operations.Where(x => x.ReversalOfOperationId.HasValue);
            rows = from row in rows
                   join reversal in reversals on (long?)row.Id equals reversal.ReversalOfOperationId into reversalRows
                   from reversal in reversalRows.DefaultIfEmpty()
                   select new StockMovementGridProjection
                   {
                       Id = row.Id, OperationCode = row.OperationCode, OperationType = row.OperationType,
                       Status = reversal != null ? StockMovementStatuses.Reversed : row.Status,
                       ReferenceType = row.ReferenceType, ReferenceNo = row.ReferenceNo, OccurredAt = row.OccurredAt,
                       EntryCount = row.EntryCount, InboundQuantity = row.InboundQuantity, OutboundQuantity = row.OutboundQuantity,
                       Reason = row.Reason, ReversalOfOperationId = row.ReversalOfOperationId, CreatedBy = row.CreatedBy,
                       CreatedDate = row.CreatedDate, UpdatedBy = row.UpdatedBy, UpdatedDate = row.UpdatedDate,
                       ReferenceSearchText = row.ReferenceSearchText
                   };
        }

        return rows;
    }

    private static bool RequiresForCount(PagedRequest request, IReadOnlySet<string> columns) =>
        (!string.IsNullOrWhiteSpace(request.EffectiveSearch) && request.SearchFields.Any(columns.Contains))
        || request.Filters.Any(filter => columns.Contains(filter.Column));

    private static bool RequiresInMainQuery(PagedRequest request, IReadOnlySet<string> columns) =>
        RequiresForCount(request, columns) || columns.Contains(request.SortBy ?? string.Empty);

    private static async Task<IReadOnlyList<StockMovementGridRow>> EnrichGridRowsAsync(
        IReadOnlyList<StockMovementGridRow> rows,
        IQueryable<StockMovementEntry> entries,
        IQueryable<StockMovementOperation> operations,
        bool enrichEntrySummary,
        bool enrichReversalSummary,
        CancellationToken cancellationToken)
    {
        var ids = rows.Select(x => x.Id).ToArray();
        var totals = enrichEntrySummary
            ? await entries.Where(x => ids.Contains(x.OperationId)).GroupBy(x => x.OperationId).Select(groupRows => new
            {
                OperationId = groupRows.Key,
                EntryCount = groupRows.Count(),
                InboundQuantity = groupRows.Where(x => x.QuantityDelta > 0).Sum(x => (decimal?)x.QuantityDelta) ?? 0,
                OutboundQuantity = -(groupRows.Where(x => x.QuantityDelta < 0).Sum(x => (decimal?)x.QuantityDelta) ?? 0)
            }).ToDictionaryAsync(x => x.OperationId, cancellationToken)
            : null;
        var reversedIds = enrichReversalSummary
            ? await operations.Where(x => x.ReversalOfOperationId.HasValue && ids.Contains(x.ReversalOfOperationId.Value))
                .Select(x => x.ReversalOfOperationId!.Value).ToHashSetAsync(cancellationToken)
            : null;

        return rows.Select(row =>
        {
            var entryCount = row.EntryCount;
            var inboundQuantity = row.InboundQuantity;
            var outboundQuantity = row.OutboundQuantity;
            if (totals is not null && totals.TryGetValue(row.Id, out var total))
            {
                entryCount = total.EntryCount;
                inboundQuantity = total.InboundQuantity;
                outboundQuantity = total.OutboundQuantity;
            }
            return row with
            {
                EntryCount = entryCount,
                InboundQuantity = inboundQuantity,
                OutboundQuantity = outboundQuantity,
                Status = reversedIds?.Contains(row.Id) == true ? StockMovementStatuses.Reversed : row.Status
            };
        }).ToArray();
    }

    private static System.Linq.Expressions.Expression<Func<StockMovementGridProjection, StockMovementGridRow>> ToGridRow() => row =>
        new StockMovementGridRow
        {
            Id = row.Id, OperationCode = row.OperationCode, OperationType = row.OperationType, Status = row.Status,
            ReferenceType = row.ReferenceType, ReferenceNo = row.ReferenceNo, OccurredAt = row.OccurredAt,
            EntryCount = row.EntryCount, InboundQuantity = row.InboundQuantity, OutboundQuantity = row.OutboundQuantity,
            Reason = row.Reason, ReversalOfOperationId = row.ReversalOfOperationId, CreatedBy = row.CreatedBy,
            CreatedDate = row.CreatedDate, UpdatedBy = row.UpdatedBy, UpdatedDate = row.UpdatedDate,
            ReferenceSearchText = row.ReferenceSearchText
        };

    private sealed class StockMovementGridProjection
    {
        public long Id { get; init; }
        public Guid OperationCode { get; init; }
        public string OperationType { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? ReferenceType { get; init; }
        public string? ReferenceNo { get; init; }
        public DateTime OccurredAt { get; init; }
        public int EntryCount { get; init; }
        public decimal InboundQuantity { get; init; }
        public decimal OutboundQuantity { get; init; }
        public string? Reason { get; init; }
        public long? ReversalOfOperationId { get; init; }
        public long? CreatedBy { get; init; }
        public DateTime? CreatedDate { get; init; }
        public long? UpdatedBy { get; init; }
        public DateTime? UpdatedDate { get; init; }
        public string ReferenceSearchText { get; init; } = string.Empty;
    }

    public async Task<StockMovementDetail> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var operation = await Operations.Query().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw AppException.NotFound("Stok hareket operasyonu bulunamadı.");
        var rows = await (from entry in Entries.Query()
                          join stock in Stocks.Query(ignoreQueryFilters: true) on entry.StockId equals stock.Id
                          join yap in YapCodes.Query(ignoreQueryFilters: true) on entry.YapCodeId equals yap.Id into yapJoin
                          from yap in yapJoin.DefaultIfEmpty()
                          join warehouse in Warehouses.Query(ignoreQueryFilters: true) on entry.WarehouseId equals warehouse.Id
                          join location in Locations.Query(ignoreQueryFilters: true) on entry.LocationId equals location.Id
                          where entry.OperationId == id
                          orderby entry.LineNo
                          select new StockMovementEntryRow(entry.Id, entry.LineNo, stock.Id, stock.ErpStockCode, stock.StockName,
                              entry.YapCodeId, yap != null ? yap.ConfigurationCode : null,
                              warehouse.Id, warehouse.WarehouseCode, warehouse.WarehouseName, location.Id, location.Code, location.Name,
                              entry.QuantityDelta, entry.UnitCode, entry.LotNo, entry.SerialNo, entry.StockStatus, entry.OccurredAt))
            .ToListAsync(cancellationToken);
        var displayStatus = await Operations.AnyAsync(x => x.ReversalOfOperationId == operation.Id, cancellationToken) ? StockMovementStatuses.Reversed : operation.Status;
        return new(operation.Id, operation.OperationCode, operation.IdempotencyKey, operation.OperationType, displayStatus,
            operation.ReferenceType, operation.ReferenceNo, operation.ReferenceId, operation.OccurredAt, operation.Reason,
            operation.Description, operation.ReversalOfOperationId, operation.CreatedBy, operation.CreatedDate, rows);
    }

    public async Task ValidateAsync(
        PostStockMovementRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(request);
        ValidateEnvelope(normalized);
        var effective = normalized with { OccurredAt = normalized.OccurredAt ?? DateTime.UtcNow };
        var drafts = await BuildEntriesAsync(effective, cancellationToken);
        await EnsureSufficientBalanceAsync(drafts, cancellationToken);
        await EnsureSerialUniquenessAsync(drafts, cancellationToken);
        await EnsureSerialRegistryAcceptsAsync(drafts, effective.OperationType, cancellationToken);
    }

    public async Task<StockMovementPostResult> PostAsync(PostStockMovementRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(request);
        ValidateEnvelope(normalized);
        var hash = Hash(normalized);
        // The server-generated timestamp is deliberately assigned only after the
        // request hash is calculated. Otherwise an exact retry with OccurredAt=null
        // would produce a different hash and break idempotent replay semantics.
        var effective = normalized with { OccurredAt = normalized.OccurredAt ?? DateTime.UtcNow };

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var existing = await Operations.Query().FirstOrDefaultAsync(x => x.IdempotencyKey == normalized.IdempotencyKey, ct);
            if (existing is not null) return await ReplayAsync(existing, hash, ct);
            var drafts = await BuildEntriesAsync(effective, ct);
            await EnsureSufficientBalanceAsync(drafts, ct);
            await EnsureSerialUniquenessAsync(drafts, ct);
            var operation = new StockMovementOperation
            {
                IdempotencyKey = normalized.IdempotencyKey, RequestHash = hash, OperationType = normalized.OperationType,
                Status = StockMovementStatuses.Posted, ReferenceType = normalized.ReferenceType, ReferenceNo = normalized.ReferenceNo,
                ReferenceId = effective.ReferenceId, OccurredAt = effective.OccurredAt!.Value, Reason = effective.Reason,
                Description = normalized.Description, BranchCode = drafts[0].BranchCode
            };
            await Operations.AddAsync(operation, ct);
            await unitOfWork.SaveChangesAsync(ct);
            await SynchronizeSerialRegistryAsync(drafts, operation, ct);
            var lineNo = 0;
            foreach (var draft in drafts) { draft.OperationId = operation.Id; draft.LineNo = ++lineNo; await Entries.AddAsync(draft, ct); }
            await unitOfWork.SaveChangesAsync(ct);
            await balanceProjection.ApplyEntriesAsync(drafts, ct);
            await audit.WriteAsync(new AuditLogWriteEntry("stock-movement.post", "StockMovementOperation", operation.Id.ToString(), "Succeeded", "stock-movement",
                NewValues: new { operation.OperationCode, operation.OperationType, operation.ReferenceType, operation.ReferenceNo, EntryCount = drafts.Count },
                ChangedFields: ["Operation", "Entries"]), ct);
            return new StockMovementPostResult(operation.Id, operation.OperationCode, false, drafts.Count);
        }, cancellationToken, IsolationLevel.Serializable);
    }

    public async Task<StockMovementPostResult> ReverseAsync(long operationId, ReverseStockMovementRequest request, CancellationToken cancellationToken = default)
    {
        var key = request.IdempotencyKey?.Trim() ?? string.Empty;
        if (key.Length is < 8 or > 100 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            throw AppException.BadRequest("İdempotency anahtarı ve ters kayıt nedeni zorunludur.");
        var requestedAt = request.OccurredAt.HasValue ? NormalizeDate(request.OccurredAt) : (DateTime?)null;
        var hash = Hash(new { OperationId = operationId, IdempotencyKey = key, Reason = request.Reason.Trim(), OccurredAt = requestedAt });
        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var replay = await Operations.Query().FirstOrDefaultAsync(x => x.IdempotencyKey == key, ct);
            if (replay is not null) return await ReplayAsync(replay, hash, ct);
            var original = await Operations.Query().FirstOrDefaultAsync(x => x.Id == operationId, ct)
                ?? throw AppException.NotFound("Ters çevrilecek stok hareketi bulunamadı.");
            if (original.OperationType == StockMovementTypes.Reversal) throw AppException.Conflict("Ters kayıt tekrar ters çevrilemez.");
            if (await Operations.AnyAsync(x => x.ReversalOfOperationId == operationId, ct)) throw AppException.Conflict("Bu operasyon daha önce ters çevrilmiş.");
            var originalEntries = await Entries.Query().Where(x => x.OperationId == operationId).OrderBy(x => x.LineNo).ToListAsync(ct);
            if (originalEntries.Count == 0) throw AppException.Conflict("Operasyon hareket satırı içermiyor.");
            var occurredAt = requestedAt ?? DateTime.UtcNow;
            var drafts = originalEntries.Select(x => new StockMovementEntry
            {
                BranchCode = x.BranchCode, StockId = x.StockId, YapCodeId = x.YapCodeId, WarehouseId = x.WarehouseId, LocationId = x.LocationId,
                QuantityDelta = -x.QuantityDelta, UnitCode = x.UnitCode, LotNo = x.LotNo, SerialNo = x.SerialNo,
                StockStatus = x.StockStatus, OccurredAt = occurredAt
            }).ToList();
            await EnsureSufficientBalanceAsync(drafts, ct);
            await EnsureSerialUniquenessAsync(drafts, ct);
            var operation = new StockMovementOperation
            {
                BranchCode = original.BranchCode, IdempotencyKey = key, RequestHash = hash, OperationType = StockMovementTypes.Reversal,
                Status = StockMovementStatuses.Posted, ReferenceType = original.ReferenceType, ReferenceNo = original.ReferenceNo,
                ReferenceId = original.ReferenceId, OccurredAt = occurredAt, Reason = request.Reason.Trim(),
                Description = $"{original.OperationCode} operasyonunun ters kaydı", ReversalOfOperationId = original.Id
            };
            await Operations.AddAsync(operation, ct); await unitOfWork.SaveChangesAsync(ct);
            await SynchronizeSerialRegistryAsync(drafts, operation, ct);
            var lineNo = 0;
            foreach (var draft in drafts) { draft.OperationId = operation.Id; draft.LineNo = ++lineNo; await Entries.AddAsync(draft, ct); }
            await unitOfWork.SaveChangesAsync(ct);
            await balanceProjection.ApplyEntriesAsync(drafts, ct);
            await audit.WriteAsync(new AuditLogWriteEntry("stock-movement.reverse", "StockMovementOperation", operation.Id.ToString(), "Succeeded", "stock-movement",
                NewValues: new { operation.OperationCode, ReversalOfOperationId = original.Id, EntryCount = drafts.Count }, ChangedFields: ["Reversal"]), ct);
            return new StockMovementPostResult(operation.Id, operation.OperationCode, false, drafts.Count);
        }, cancellationToken, IsolationLevel.Serializable);
    }

    private async Task<List<StockMovementEntry>> BuildEntriesAsync(PostStockMovementRequest request, CancellationToken ct)
    {
        var stockIds = request.Lines.Select(x => x.StockId).Distinct().ToList();
        var yapCodeIds = request.Lines.Where(x => x.YapCodeId.HasValue).Select(x => x.YapCodeId!.Value).Distinct().ToList();
        var warehouseIds = request.Lines.SelectMany(x => new[] { x.SourceWarehouseId, x.TargetWarehouseId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var locationIds = request.Lines.SelectMany(x => new[] { x.SourceLocationId, x.TargetLocationId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var stocks = await Stocks.Query().Where(x => stockIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var yapCodes = await YapCodes.Query().Where(x => yapCodeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var warehouses = await Warehouses.Query().Where(x => warehouseIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var locations = await Locations.Query().Where(x => locationIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
        if (stocks.Count != stockIds.Count) throw AppException.BadRequest("Geçersiz veya pasif stok seçildi.");
        if (yapCodes.Count != yapCodeIds.Count) throw AppException.BadRequest("Geçersiz veya pasif yapılandırma kodu seçildi.");
        if (warehouses.Count != warehouseIds.Count) throw AppException.BadRequest("Geçersiz veya pasif depo seçildi.");
        if (locations.Count != locationIds.Count) throw AppException.BadRequest("Geçersiz veya pasif raf seçildi.");
        var trackingPolicies = new Dictionary<long, EffectiveStockTrackingPolicy>();
        foreach (var stock in stocks.Values)
            trackingPolicies[stock.Id] = await ResolveTrackingPolicyAsync(stock.BranchCode, stock.Id, ct);

        var result = new List<StockMovementEntry>(request.Lines.Count * 2);
        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0 || line.Quantity > StockMovementLimits.MaxQuantity)
                throw AppException.BadRequest(
                    $"Hareket miktarı sıfırdan büyük ve en fazla {StockMovementLimits.MaxQuantity:N0} olmalıdır.");
            var stock = stocks[line.StockId];
            if (line.YapCodeId.HasValue && yapCodes[line.YapCodeId.Value].StockId.HasValue && yapCodes[line.YapCodeId.Value].StockId != stock.Id)
                throw AppException.BadRequest("YAP kodu seçilen stokla uyuşmuyor.");
            var unit = StockUnitPolicy.Resolve(stock, line.UnitCode);
            var lot = NormalizeText(line.LotNo, 100); var serial = NormalizeText(line.SerialNo, 100)?.ToUpperInvariant();
            var status = NormalizeText(line.StockStatus, 30) ?? "Available";
            var sourceStatus = NormalizeText(line.SourceStockStatus, 30) ?? status;
            var targetStatus = NormalizeText(line.TargetStockStatus, 30) ?? status;
            try
            {
                StockTrackingPolicyGuard.ValidateSerialQuantity(
                    trackingPolicies[stock.Id], line.Quantity, serial);
            }
            catch (StockTrackingPolicyViolationException exception)
            {
                throw AppException.BadRequest(exception.Message);
            }

            void Add(long? warehouseId, long? locationId, decimal delta, string entryStatus)
            {
                if (!warehouseId.HasValue || !locationId.HasValue) throw AppException.BadRequest("Hareket için depo ve raf zorunludur.");
                var warehouse = warehouses[warehouseId.Value]; var location = locations[locationId.Value];
                if (location.WarehouseId != warehouse.Id) throw AppException.BadRequest("Raf seçilen depoya ait değil.");
                if (!string.Equals(stock.BranchCode, warehouse.BranchCode, StringComparison.OrdinalIgnoreCase)) throw AppException.BadRequest("Stok ve depo şubesi uyuşmuyor.");
                result.Add(new StockMovementEntry { BranchCode = warehouse.BranchCode, StockId = stock.Id, YapCodeId = line.YapCodeId, WarehouseId = warehouse.Id,
                    LocationId = location.Id, QuantityDelta = delta, UnitCode = unit, LotNo = lot, SerialNo = serial,
                    StockStatus = entryStatus, OccurredAt = request.OccurredAt!.Value });
            }

            switch (request.OperationType)
            {
                case StockMovementTypes.Receipt or StockMovementTypes.AdjustmentIncrease or StockMovementTypes.CustomerReturn:
                    if (line.SourceWarehouseId.HasValue || line.SourceLocationId.HasValue) throw AppException.BadRequest("Giriş hareketinde kaynak depo/raf gönderilemez.");
                    Add(line.TargetWarehouseId, line.TargetLocationId, line.Quantity, targetStatus); break;
                case StockMovementTypes.Shipment or StockMovementTypes.AdjustmentDecrease or StockMovementTypes.SupplierReturn:
                    if (line.TargetWarehouseId.HasValue || line.TargetLocationId.HasValue) throw AppException.BadRequest("Çıkış hareketinde hedef depo/raf gönderilemez.");
                    Add(line.SourceWarehouseId, line.SourceLocationId, -line.Quantity, sourceStatus); break;
                case StockMovementTypes.Transfer:
                    if (line.SourceWarehouseId == line.TargetWarehouseId && line.SourceLocationId == line.TargetLocationId
                        && string.Equals(sourceStatus, targetStatus, StringComparison.OrdinalIgnoreCase))
                        throw AppException.BadRequest("Transfer kaynağı/hedefi veya stok statüsü değişmelidir.");
                    Add(line.SourceWarehouseId, line.SourceLocationId, -line.Quantity, sourceStatus);
                    Add(line.TargetWarehouseId, line.TargetLocationId, line.Quantity, targetStatus); break;
                case StockMovementTypes.BalanceReconciliation:
                    var hasSource = line.SourceWarehouseId.HasValue || line.SourceLocationId.HasValue;
                    var hasTarget = line.TargetWarehouseId.HasValue || line.TargetLocationId.HasValue;
                    if (hasSource == hasTarget)
                        throw AppException.BadRequest(
                            "Bakiye eşitleme satırı yalnız kaynak (azaltma) veya yalnız hedef (artırma) depo/raf içermelidir.");
                    if (hasSource)
                        Add(line.SourceWarehouseId, line.SourceLocationId, -line.Quantity, sourceStatus);
                    else
                        Add(line.TargetWarehouseId, line.TargetLocationId, line.Quantity, targetStatus);
                    break;
            }
        }
        if (result.Select(x => x.BranchCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1) throw AppException.BadRequest("Tek operasyonda farklı şubeler kullanılamaz.");
        return result;
    }

    private async Task EnsureSufficientBalanceAsync(IReadOnlyCollection<StockMovementEntry> drafts, CancellationToken ct)
    {
        var negatives = drafts.Where(x => x.QuantityDelta < 0).ToList();
        if (negatives.Count == 0) return;
        var stockIds = negatives.Select(x => x.StockId).Distinct().ToList();
        var warehouseIds = negatives.Select(x => x.WarehouseId).Distinct().ToList();
        var locationIds = negatives.Select(x => x.LocationId).Distinct().ToList();
        var currentRows = await LocationBalances.Query().Where(x => stockIds.Contains(x.StockId) && warehouseIds.Contains(x.WarehouseId) && locationIds.Contains(x.LocationId))
            .Select(x => new { x.StockId, x.YapCodeId, x.WarehouseId, x.LocationId, x.UnitCode, x.LotNo, x.SerialNo, x.StockStatus, x.AvailableQuantity }).ToListAsync(ct);
        var current = currentRows.ToDictionary(
            x => Key(x.StockId, x.YapCodeId, x.WarehouseId, x.LocationId, x.UnitCode, x.LotNo, x.SerialNo, x.StockStatus),
            x => x.AvailableQuantity);
        foreach (var group in negatives.GroupBy(x => Key(x.StockId, x.YapCodeId, x.WarehouseId, x.LocationId, x.UnitCode, x.LotNo, x.SerialNo, x.StockStatus)))
        {
            var available = current.GetValueOrDefault(group.Key); var requested = -group.Sum(x => x.QuantityDelta);
            if (available < requested) throw AppException.Conflict($"Yetersiz raf bakiyesi. Kullanılabilir: {available}, istenen: {requested}.");
        }
    }

    private async Task EnsureSerialUniquenessAsync(IReadOnlyCollection<StockMovementEntry> drafts, CancellationToken ct)
    {
        var serialDrafts = drafts.Where(x => !string.IsNullOrWhiteSpace(x.SerialNo)).ToList();
        if (serialDrafts.Count == 0) return;
        var stockIds = serialDrafts.Select(x => x.StockId).Distinct().ToList();
        var policies = new Dictionary<long, EffectiveStockTrackingPolicy>();
        foreach (var group in serialDrafts.GroupBy(x => new { x.BranchCode, x.StockId }))
            policies[group.Key.StockId] = await ResolveTrackingPolicyAsync(
                group.Key.BranchCode, group.Key.StockId, ct);
        var serials = serialDrafts.Select(x => x.SerialNo!).Distinct().ToList();
        var currentRows = await Entries.Query().Where(x => stockIds.Contains(x.StockId) && x.SerialNo != null && serials.Contains(x.SerialNo))
            .Select(x => new { x.StockId, x.YapCodeId, x.UnitCode, x.LotNo, x.SerialNo, x.StockStatus, x.QuantityDelta }).ToListAsync(ct);
        var current = currentRows.GroupBy(x => SerialKey(x.StockId, x.YapCodeId, x.UnitCode, x.LotNo, x.SerialNo!, x.StockStatus))
            .ToDictionary(x => x.Key, x => x.Sum(v => v.QuantityDelta));
        foreach (var group in serialDrafts.GroupBy(x => SerialKey(x.StockId, x.YapCodeId, x.UnitCode, x.LotNo, x.SerialNo!, x.StockStatus)))
        {
            var resultingQuantity = current.GetValueOrDefault(group.Key) + group.Sum(x => x.QuantityDelta);
            var rule = policies[group.First().StockId].SerialQuantityRule;
            if (resultingQuantity < 0
                || (rule != SerialQuantityRule.OneSerialPerLine && resultingQuantity > 1))
                throw AppException.Conflict(rule == SerialQuantityRule.OneSerialPerLine
                    ? $"Seri bakiyesi negatife düşemez. Seri: {group.First().SerialNo}."
                    : $"Seri numarası tekil bir stok örneğidir; toplam bakiye 0 veya 1 olabilir. Seri: {group.First().SerialNo}.");
        }

    }

    private async Task<EffectiveStockTrackingPolicy> ResolveTrackingPolicyAsync(
        string branchCode,
        long stockId,
        CancellationToken cancellationToken)
    {
        var key = (branchCode.Trim().ToUpperInvariant(), stockId);
        if (_trackingPolicyCache.TryGetValue(key, out var cached)) return cached;

        var resolved = await trackingPolicyResolver.ResolveAsync(branchCode, stockId, cancellationToken);
        _trackingPolicyCache[key] = resolved;
        return resolved;
    }

    private async Task SynchronizeSerialRegistryAsync(
        IReadOnlyCollection<StockMovementEntry> drafts,
        StockMovementOperation operation,
        CancellationToken ct)
    {
        var groups = drafts.Where(x => !string.IsNullOrWhiteSpace(x.SerialNo))
            .GroupBy(x => new { x.StockId, Serial = x.SerialNo!.Trim().ToUpperInvariant() })
            .ToList();
        var stockIds = groups.Select(x => x.Key.StockId).Distinct().ToList();
        var serials = groups.Select(x => x.Key.Serial).Distinct().ToList();
        var currentRows = await Entries.Query()
            .Where(x => stockIds.Contains(x.StockId)
                && x.SerialNo != null
                && serials.Contains(x.SerialNo))
            .Select(x => new { x.StockId, Serial = x.SerialNo!, x.QuantityDelta })
            .ToListAsync(ct);
        var currentQuantities = currentRows
            .GroupBy(x => new { x.StockId, Serial = x.Serial.Trim().ToUpperInvariant() })
            .ToDictionary(x => (x.Key.StockId, x.Key.Serial), x => x.Sum(v => v.QuantityDelta));
        var registryRows = await SerialRegistry.Query(true)
            .Where(x => stockIds.Contains(x.StockId) && serials.Contains(x.NormalizedSerialNo))
            .ToListAsync(ct);
        var registryByKey = registryRows.ToDictionary(
            x => (x.StockId, x.NormalizedSerialNo),
            x => x);
        var ordinal = 0;
        foreach (var group in groups)
        {
            ordinal++;
            registryByKey.TryGetValue((group.Key.StockId, group.Key.Serial), out var row);
            var netQuantity = group.Sum(x => x.QuantityDelta);
            var resultingQuantity = currentQuantities.GetValueOrDefault(
                (group.Key.StockId, group.Key.Serial)) + netQuantity;
            if (row is null)
            {
                if (resultingQuantity <= 0)
                    throw AppException.Conflict(
                        $"Çıkış veya transfer işleminde yalnız mevcut stok serisi kullanılabilir. Seri: {group.First().SerialNo}");
                row = new StockSerialRegistry
                {
                    BranchCode = group.First().BranchCode,
                    StockId = group.Key.StockId,
                    SerialNo = group.First().SerialNo!.Trim(),
                    NormalizedSerialNo = group.Key.Serial,
                    Status = StockSerialStatus.Available,
                    SerialNumberRuleId = null,
                    SequenceNumber = 0,
                    GenerationRequestKey = $"MOV:{operation.Id}",
                    GenerationOrdinal = ordinal,
                    SourceOperationType = operation.OperationType,
                    SourceOperationId = operation.Id,
                    ReservedAtUtc = DateTimeOffset.UtcNow,
                    ActivatedAtUtc = DateTimeOffset.UtcNow,
                    LastStockMovementOperationId = operation.Id,
                    CreatedDate = DateTime.UtcNow
                };
                await SerialRegistry.AddAsync(row, ct);
                registryByKey[(group.Key.StockId, group.Key.Serial)] = row;
                continue;
            }

            if (row.Status == StockSerialStatus.Voided)
                throw AppException.Conflict($"İptal edilmiş seri kullanılamaz. Seri: {row.SerialNo}");

            if (netQuantity > 0)
            {
                var isReturnOrReversal = operation.OperationType is StockMovementTypes.CustomerReturn
                    or StockMovementTypes.Reversal
                    or StockMovementTypes.BalanceReconciliation;
                if ((row.Status == StockSerialStatus.Available && !isReturnOrReversal)
                    || (row.Status == StockSerialStatus.Consumed && !isReturnOrReversal))
                    throw AppException.Conflict($"Seri bu stok için daha önce kullanılmış. Seri: {row.SerialNo}");
                row.Status = StockSerialStatus.Available;
                row.ActivatedAtUtc ??= DateTimeOffset.UtcNow;
                row.ConsumedAtUtc = null;
            }
            else if (netQuantity < 0)
            {
                if (row.Status != StockSerialStatus.Available)
                    throw AppException.Conflict($"Yalnız kullanılabilir durumdaki seri çıkışa konu olabilir. Seri: {row.SerialNo}");
                row.Status = resultingQuantity > 0
                    ? StockSerialStatus.Available
                    : StockSerialStatus.Consumed;
                row.ConsumedAtUtc = resultingQuantity > 0 ? null : DateTimeOffset.UtcNow;
            }
            else if (row.Status != StockSerialStatus.Available)
            {
                throw AppException.Conflict($"Transferde yalnız kullanılabilir stok serisi seçilebilir. Seri: {row.SerialNo}");
            }

            row.LastStockMovementOperationId = operation.Id;
            row.UpdatedDate = DateTime.UtcNow;
        }
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task EnsureSerialRegistryAcceptsAsync(
        IReadOnlyCollection<StockMovementEntry> drafts,
        string operationType,
        CancellationToken ct)
    {
        if (operationType is StockMovementTypes.CustomerReturn
            or StockMovementTypes.Reversal
            or StockMovementTypes.BalanceReconciliation)
            return;

        var inboundSerials = drafts
            .Where(x => x.QuantityDelta > 0 && !string.IsNullOrWhiteSpace(x.SerialNo))
            .Select(x => new
            {
                x.StockId,
                Serial = x.SerialNo!.Trim().ToUpperInvariant()
            })
            .Distinct()
            .ToList();
        if (inboundSerials.Count == 0) return;

        var stockIds = inboundSerials.Select(x => x.StockId).Distinct().ToList();
        var serials = inboundSerials.Select(x => x.Serial).Distinct().ToList();
        var existing = await SerialRegistry.Query()
            .Where(x => stockIds.Contains(x.StockId) && serials.Contains(x.NormalizedSerialNo))
            .Select(x => new { x.StockId, x.SerialNo, x.NormalizedSerialNo, x.Status })
            .ToListAsync(ct);
        var requested = inboundSerials
            .Select(x => (x.StockId, x.Serial))
            .ToHashSet();
        var conflict = existing.FirstOrDefault(x =>
            requested.Contains((x.StockId, x.NormalizedSerialNo))
            && x.Status is StockSerialStatus.Available or StockSerialStatus.Consumed or StockSerialStatus.Voided);
        if (conflict is not null)
            throw AppException.Conflict(
                $"Seri bu stok için daha önce kullanılmış veya iptal edilmiştir. Seri: {conflict.SerialNo}.");
    }

    private async Task<StockMovementPostResult> ReplayAsync(StockMovementOperation existing, string hash, CancellationToken ct)
    {
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(existing.RequestHash), Convert.FromHexString(hash)))
            throw AppException.Conflict("Aynı idempotency anahtarı farklı bir istek gövdesiyle kullanılamaz.");
        return new(existing.Id, existing.OperationCode, true, await Entries.CountAsync(x => x.OperationId == existing.Id, ct));
    }

    private static PostStockMovementRequest Normalize(PostStockMovementRequest request)
    {
        var type = StockMovementTypes.All.FirstOrDefault(x => string.Equals(x, request.OperationType?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? request.OperationType?.Trim() ?? string.Empty;
        return request with { IdempotencyKey = request.IdempotencyKey?.Trim() ?? string.Empty, OperationType = type,
            ReferenceType = NormalizeText(request.ReferenceType, 50), ReferenceNo = NormalizeText(request.ReferenceNo, 100),
            OccurredAt = request.OccurredAt.HasValue ? NormalizeDate(request.OccurredAt) : null, Reason = NormalizeText(request.Reason, 500),
            Description = NormalizeText(request.Description, 1000), Lines = request.Lines ?? [] };
    }

    private static void ValidateEnvelope(PostStockMovementRequest request)
    {
        if (request.IdempotencyKey.Length is < 8 or > 100) throw AppException.BadRequest("İdempotency anahtarı 8-100 karakter olmalıdır.");
        if (!StockMovementTypes.All.Contains(request.OperationType)) throw AppException.BadRequest("Geçersiz stok hareket tipi.");
        if (request.Lines.Count is < 1 or > 500) throw AppException.BadRequest("Operasyon 1-500 hareket satırı içermelidir.");
    }

    private static DateTime NormalizeDate(DateTime? value)
    {
        var date = value?.ToUniversalTime() ?? DateTime.UtcNow;
        if (date > DateTime.UtcNow.AddMinutes(5)) throw AppException.BadRequest("Hareket zamanı gelecekte olamaz.");
        return date;
    }
    private static string? NormalizeText(string? value, int max) { var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); if (text?.Length > max) throw AppException.BadRequest($"Alan uzunluğu en fazla {max} olabilir."); return text; }
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static string Key(long stockId, long? yapCodeId, long warehouseId, long locationId, string unit, string? lot, string? serial, string status) => $"{stockId}|{yapCodeId?.ToString() ?? "0"}|{warehouseId}|{locationId}|{unit}|{lot ?? ""}|{serial ?? ""}|{status}";
    private static string SerialKey(long stockId, long? yapCodeId, string unit, string? lot, string serial, string status) => $"{stockId}|{yapCodeId?.ToString() ?? "0"}|{unit}|{lot ?? ""}|{serial}|{status}";
}
