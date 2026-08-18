using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.StockBalance.Application;

public sealed partial class StockBalanceService(
    IUnitOfWork unitOfWork,
    IStockTrackingPolicyResolver trackingPolicies) : IStockBalanceService
{
    private static readonly IReadOnlyDictionary<string,string> SerialHistorySearchColumns=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"]=nameof(SerialMovementHistoryRow.Id),["operationCode"]=nameof(SerialMovementHistoryRow.OperationCode),
        ["referenceNo"]=nameof(SerialMovementHistoryRow.ReferenceSearchText),["warehouseCode"]=nameof(SerialMovementHistoryRow.WarehouseCode),
        ["warehouseName"]=nameof(SerialMovementHistoryRow.WarehouseName),["locationCode"]=nameof(SerialMovementHistoryRow.LocationCode),
        ["locationName"]=nameof(SerialMovementHistoryRow.LocationName),["quantityDelta"]=nameof(SerialMovementHistoryRow.QuantitySearchText)
    };
    private static readonly string[] DefaultSerialHistorySearchColumns=["operationCode","referenceNo","warehouseCode","warehouseName","locationCode","locationName"];
    private IGenericRepository<LocationStockBalance> Locations => unitOfWork.Repository<LocationStockBalance>();
    private IGenericRepository<WarehouseStockBalance> Warehouses => unitOfWork.Repository<WarehouseStockBalance>();
    private IGenericRepository<StockBalanceProjectionState> States => unitOfWork.Repository<StockBalanceProjectionState>();
    private IGenericRepository<StockMovementEntry> Entries => unitOfWork.Repository<StockMovementEntry>();
    private IGenericRepository<StockMovementOperation> Operations => unitOfWork.Repository<StockMovementOperation>();
    private IGenericRepository<StockReservationOperation> ReservationOperations => unitOfWork.Repository<StockReservationOperation>();
    private IGenericRepository<StockReservationEntry> ReservationEntries => unitOfWork.Repository<StockReservationEntry>();
    private IGenericRepository<StockEntity> Stocks => unitOfWork.Repository<StockEntity>();
    private IGenericRepository<WarehouseEntity> WarehouseDefinitions => unitOfWork.Repository<WarehouseEntity>();
    private IGenericRepository<WarehouseLocation> LocationDefinitions => unitOfWork.Repository<WarehouseLocation>();
    private IGenericRepository<YapCodeEntity> YapCodes => unitOfWork.Repository<YapCodeEntity>();

    public async Task ApplyEntriesAsync(IReadOnlyCollection<StockMovementEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0) return;
        var groups = entries.GroupBy(EntryKey).ToList();
        var warehouseIds = groups.Select(x => x.Key.WarehouseId).Distinct().ToList();
        var locationIds = groups.Select(x => x.Key.LocationId).Distinct().ToList();
        var stockIds = groups.Select(x => x.Key.StockId).Distinct().ToList();
        var existing = await Locations.Query(true).Where(x => warehouseIds.Contains(x.WarehouseId) && locationIds.Contains(x.LocationId) && stockIds.Contains(x.StockId)).ToListAsync(cancellationToken);
        var map = existing.ToDictionary(BalanceKey);

        foreach (var group in groups)
        {
            if (!map.TryGetValue(group.Key, out var balance))
            {
                balance = new LocationStockBalance
                {
                    DimensionKey = HashLocationKey(group.Key),
                    BranchCode = group.First().BranchCode, WarehouseId = group.Key.WarehouseId, LocationId = group.Key.LocationId,
                    StockId = group.Key.StockId, YapCodeId = group.Key.YapCodeId, UnitCode = group.Key.UnitCode,
                    LotNo = group.Key.LotNo, SerialNo = group.Key.SerialNo, StockStatus = group.Key.StockStatus
                };
                await Locations.AddAsync(balance, cancellationToken); map[group.Key] = balance;
            }
            balance.Quantity += group.Sum(x => x.QuantityDelta);
            balance.AvailableQuantity = balance.Quantity - balance.ReservedQuantity;
            balance.LastMovementEntryId = Math.Max(balance.LastMovementEntryId, group.Max(x => x.Id));
            balance.LastTransactionDate = group.Max(x => x.OccurredAt);
            balance.UpdatedDate = DateTime.UtcNow;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await RecalculateWarehouseRowsAsync(groups.Select(x => WarehouseKey(x.Key)).Distinct().ToList(), cancellationToken);
        await UpdateStateAsync(entries.Max(x => x.Id), null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task<StockReservationPostResult> PostReservationAsync(
        PostStockReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeReservation(request);
        ValidateReservation(normalized);
        var hash = Hash(JsonSerializer.Serialize(normalized));
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var replay = await ReservationOperations.Query()
                .FirstOrDefaultAsync(x => x.IdempotencyKey == normalized.IdempotencyKey, ct);
            if (replay is not null)
            {
                if (!string.Equals(replay.RequestHash, hash, StringComparison.Ordinal))
                    throw AppException.Conflict("Aynı rezervasyon idempotency anahtarı farklı bir istekle kullanılamaz.");
                var replayTotal = await ReservationEntries.Query().Where(x => x.OperationId == replay.Id)
                    .SumAsync(x => (decimal?)x.QuantityDelta, ct) ?? 0;
                return new StockReservationPostResult(replay.Id, true, replayTotal);
            }

            await ValidateReservationTrackingPoliciesAsync(normalized, ct);

            var groups = normalized.Lines.GroupBy(ReservationKey).ToList();
            var warehouseIds = groups.Select(x => x.Key.WarehouseId).Distinct().ToArray();
            var locationIds = groups.Select(x => x.Key.LocationId).Distinct().ToArray();
            var stockIds = groups.Select(x => x.Key.StockId).Distinct().ToArray();
            var candidates = await Locations.Query(true)
                .Where(x => warehouseIds.Contains(x.WarehouseId)
                    && locationIds.Contains(x.LocationId)
                    && stockIds.Contains(x.StockId))
                .ToListAsync(ct);
            var balances = candidates.ToDictionary(BalanceKey);

            foreach (var group in groups)
            {
                if (!balances.TryGetValue(group.Key, out var balance))
                {
                    var nearMatches = candidates
                        .Where(x => x.WarehouseId == group.Key.WarehouseId && x.LocationId == group.Key.LocationId && x.StockId == group.Key.StockId)
                        .Select(x => $"[YapKod:{(x.YapCodeId.HasValue ? x.YapCodeId.Value.ToString() : "-")} Birim:{x.UnitCode} Lot:'{x.LotNo}' Seri:'{x.SerialNo}' Durum:{x.StockStatus} Kullanılabilir:{x.AvailableQuantity}]")
                        .ToList();
                    var detail = nearMatches.Count > 0
                        ? $"Bu depo/rafta aynı stok için bulunan bakiyeler: {string.Join(" | ", nearMatches)}."
                        : "Bu depo/rafta bu stoğa ait hiç bakiye kaydı yok.";
                    throw AppException.Conflict(
                        "Rezervasyon için stok/raf/lot/seri bakiyesi bulunamadı. " +
                        $"Aranan: Depo:{group.Key.WarehouseId} Raf:{group.Key.LocationId} Stok:{group.Key.StockId} " +
                        $"YapKod:{(group.Key.YapCodeId.HasValue ? group.Key.YapCodeId.Value.ToString() : "-")} Birim:{group.Key.UnitCode} " +
                        $"Lot:'{group.Key.LotNo}' Seri:'{group.Key.SerialNo}' Durum:{group.Key.StockStatus}. " + detail);
                }
                var delta = group.Sum(x => x.QuantityDelta);
                if (delta > 0 && balance.AvailableQuantity < delta)
                    throw AppException.Conflict($"Yetersiz kullanılabilir raf bakiyesi. Kullanılabilir: {balance.AvailableQuantity}, istenen: {delta}.");
                if (delta < 0 && balance.ReservedQuantity < -delta)
                    throw AppException.Conflict($"Rezervasyon çözme miktarı mevcut rezervasyonu aşıyor. Rezerve: {balance.ReservedQuantity}, istenen: {-delta}.");
                balance.ReservedQuantity += delta;
                balance.AvailableQuantity = balance.Quantity - balance.ReservedQuantity;
                balance.UpdatedDate = DateTime.UtcNow;
            }

            var operation = new StockReservationOperation
            {
                BranchCode = candidates[0].BranchCode,
                IdempotencyKey = normalized.IdempotencyKey,
                RequestHash = hash,
                ReferenceType = normalized.ReferenceType,
                ReferenceId = normalized.ReferenceId,
                ReferenceNo = normalized.ReferenceNo,
                OperationType = normalized.OperationType,
                Reason = normalized.Reason,
                OccurredAtUtc = DateTime.UtcNow
            };
            await ReservationOperations.AddAsync(operation, ct);
            await unitOfWork.SaveChangesAsync(ct);
            var lineNo = 0;
            foreach (var line in normalized.Lines)
            {
                await ReservationEntries.AddAsync(new StockReservationEntry
                {
                    BranchCode = operation.BranchCode,
                    OperationId = operation.Id,
                    LineNo = ++lineNo,
                    ReferenceLineId = line.ReferenceLineId,
                    WarehouseId = line.WarehouseId,
                    LocationId = line.LocationId,
                    StockId = line.StockId,
                    YapCodeId = line.YapCodeId,
                    UnitCode = line.UnitCode,
                    LotNo = line.LotNo ?? string.Empty,
                    SerialNo = line.SerialNo ?? string.Empty,
                    StockStatus = line.StockStatus,
                    QuantityDelta = line.QuantityDelta,
                    OccurredAtUtc = operation.OccurredAtUtc
                }, ct);
            }
            await unitOfWork.SaveChangesAsync(ct);
            await RecalculateWarehouseRowsAsync(groups.Select(x => WarehouseKey(x.Key)).Distinct().ToList(), ct);
            await unitOfWork.SaveChangesAsync(ct);
            return new StockReservationPostResult(operation.Id, false, normalized.Lines.Sum(x => x.QuantityDelta));
        }, cancellationToken, IsolationLevel.Serializable);
    }

    public async Task<IReadOnlyList<SerialLocationMatchDto>> ResolveSerialLocationsAsync(
        ResolveSerialLocationsRequest request, CancellationToken cancellationToken = default)
    {
        var requestedSerials = request.SerialNumbers.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (requestedSerials.Length == 0) return [];

        var candidates = await Locations.Query(true)
            .Where(x => x.BranchCode == request.BranchCode && x.WarehouseId == request.WarehouseId
                && x.StockId == request.StockId && x.YapCodeId == request.YapCodeId
                && x.AvailableQuantity > 0 && !string.IsNullOrEmpty(x.SerialNo))
            .ToListAsync(cancellationToken);
        var balanceBySerial = candidates
            .GroupBy(x => NormalizeKeyPart(x.SerialNo))
            .ToDictionary(g => g.Key, g => g.First());

        var locationIds = balanceBySerial.Values.Select(x => x.LocationId).Distinct().ToArray();
        var locationLookup = locationIds.Length == 0
            ? new Dictionary<long, WarehouseLocation>()
            : await LocationDefinitions.Query().Where(x => locationIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);

        return requestedSerials.Select(serial =>
        {
            if (!balanceBySerial.TryGetValue(NormalizeKeyPart(serial), out var balance))
                return new SerialLocationMatchDto(serial, null, null, null, 0);
            locationLookup.TryGetValue(balance.LocationId, out var location);
            return new SerialLocationMatchDto(serial, balance.LocationId, location?.Code, location?.Name, balance.AvailableQuantity);
        }).ToList();
    }

    public async Task<IReadOnlyList<StockLocationBalanceDto>> ResolveStockLocationsAsync(
        string branchCode, long warehouseId, long stockId, long? yapCodeId,
        IReadOnlyCollection<long>? excludeLocationIds = null, bool includeOnHand = false,
        CancellationToken cancellationToken = default)
    {
        var balances = Locations.Query(true)
            .Where(x => x.BranchCode == branchCode && x.WarehouseId == warehouseId
                && x.StockId == stockId && x.YapCodeId == yapCodeId);
        var filtered = includeOnHand
            ? balances.Where(x => x.StockStatus == "Available" && x.Quantity > 0)
            : balances.Where(x => x.AvailableQuantity > 0);
        var candidates = await filtered
            .GroupBy(x => x.LocationId)
            .Select(g => new
            {
                LocationId = g.Key,
                AvailableQuantity = g.Sum(x => x.AvailableQuantity),
                Quantity = g.Sum(x => x.Quantity),
                ReservedQuantity = g.Sum(x => x.ReservedQuantity),
            })
            .ToListAsync(cancellationToken);
        if (excludeLocationIds is { Count: > 0 })
            candidates = candidates.Where(x => !excludeLocationIds.Contains(x.LocationId)).ToList();
        if (candidates.Count == 0) return [];

        var locationIds = candidates.Select(x => x.LocationId).ToArray();
        var locations = await LocationDefinitions.Query().Where(x => locationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        return candidates.Select(x => locations.TryGetValue(x.LocationId, out var location)
            ? new StockLocationBalanceDto(x.LocationId, location.Code, location.Name, x.AvailableQuantity, x.Quantity, x.ReservedQuantity)
            : new StockLocationBalanceDto(x.LocationId, "?", "?", x.AvailableQuantity, x.Quantity, x.ReservedQuantity)).ToList();
    }

    public async Task<PagedResponse<LocationBalanceRow>> GetLocationBalancesAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        var search = request.LegacySearch?.Trim() ?? string.Empty;
        var balances = Locations.Query();
        var joined = from balance in balances
                     join warehouse in WarehouseDefinitions.Query() on balance.WarehouseId equals warehouse.Id
                     join location in LocationDefinitions.Query() on balance.LocationId equals location.Id
                     join stock in Stocks.Query() on balance.StockId equals stock.Id
                     join yap in YapCodes.Query() on balance.YapCodeId equals yap.Id into yapJoin
                     from yap in yapJoin.DefaultIfEmpty()
                     where string.IsNullOrWhiteSpace(search) || warehouse.WarehouseName.Contains(search) || location.Code.Contains(search)
                         || location.Name.Contains(search) || stock.ErpStockCode.Contains(search) || stock.StockName.Contains(search)
                         || balance.LotNo.Contains(search) || balance.SerialNo.Contains(search) || (yap != null && yap.ConfigurationCode.Contains(search))
                     select new { Balance = balance, Warehouse = warehouse, Location = location, Stock = stock, Yap = yap };
        var query = joined.Select(x => new LocationBalanceRow(x.Balance.Id, x.Balance.BranchCode, x.Warehouse.Id, x.Warehouse.WarehouseCode, x.Warehouse.WarehouseName,
            x.Location.Id, x.Location.Code, x.Location.Name, x.Stock.Id, x.Stock.ErpStockCode, x.Stock.StockName,
            x.Balance.YapCodeId, x.Yap != null ? x.Yap.ConfigurationCode : null, x.Balance.UnitCode,
            x.Balance.LotNo == "" ? null : x.Balance.LotNo, x.Balance.SerialNo == "" ? null : x.Balance.SerialNo, x.Balance.StockStatus,
            x.Balance.Quantity, x.Balance.ReservedQuantity, x.Balance.AvailableQuantity, x.Balance.LastMovementEntryId, x.Balance.LastTransactionDate,
            x.Balance.CreatedBy, x.Balance.CreatedDate, x.Balance.UpdatedBy, x.Balance.UpdatedDate));
        return await query.ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(LocationBalanceRow.LastTransactionDate))
            .ToPagedResponseAsync(request, cancellationToken);
    }

    public async Task<PagedResponse<WarehouseBalanceRow>> GetWarehouseBalancesAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        var search = request.LegacySearch?.Trim();
        var balances = Warehouses.Query();
        var joined = from balance in balances
                     join warehouse in WarehouseDefinitions.Query() on balance.WarehouseId equals warehouse.Id
                     join stock in Stocks.Query() on balance.StockId equals stock.Id
                     join yap in YapCodes.Query() on balance.YapCodeId equals yap.Id into yapJoin
                     from yap in yapJoin.DefaultIfEmpty()
                     where string.IsNullOrWhiteSpace(search) || warehouse.WarehouseName.Contains(search) || stock.ErpStockCode.Contains(search)
                         || stock.StockName.Contains(search) || (yap != null && yap.ConfigurationCode.Contains(search))
                     select new { Balance = balance, Warehouse = warehouse, Stock = stock, Yap = yap };
        var query = joined.Select(x => new WarehouseBalanceRow(x.Balance.Id, x.Balance.BranchCode, x.Warehouse.Id, x.Warehouse.WarehouseCode, x.Warehouse.WarehouseName,
            x.Stock.Id, x.Stock.ErpStockCode, x.Stock.StockName, x.Balance.YapCodeId, x.Yap != null ? x.Yap.ConfigurationCode : null,
            x.Balance.UnitCode, x.Balance.StockStatus, x.Balance.Quantity, x.Balance.ReservedQuantity, x.Balance.AvailableQuantity,
            x.Balance.DistinctLocationCount, x.Balance.DistinctLotCount, x.Balance.DistinctSerialCount,
            x.Balance.LastMovementEntryId, x.Balance.LastTransactionDate, x.Balance.CreatedBy, x.Balance.CreatedDate, x.Balance.UpdatedBy, x.Balance.UpdatedDate));
        return await query.ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(WarehouseBalanceRow.LastTransactionDate))
            .ToPagedResponseAsync(request, cancellationToken);
    }

    public async Task<PagedResponse<SerialBalanceRow>> GetSerialBalancesAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        var search = request.LegacySearch?.Trim();
        var balances = Locations.Query().Where(x => x.SerialNo != "");
        var joined = from balance in balances
                     join warehouse in WarehouseDefinitions.Query() on balance.WarehouseId equals warehouse.Id
                     join location in LocationDefinitions.Query() on balance.LocationId equals location.Id
                     join stock in Stocks.Query() on balance.StockId equals stock.Id
                     join yap in YapCodes.Query() on balance.YapCodeId equals yap.Id into yapJoin
                     from yap in yapJoin.DefaultIfEmpty()
                     where string.IsNullOrWhiteSpace(search) || balance.SerialNo.Contains(search) || warehouse.WarehouseName.Contains(search)
                         || location.Code.Contains(search) || location.Name.Contains(search) || stock.ErpStockCode.Contains(search)
                         || stock.StockName.Contains(search) || balance.LotNo.Contains(search) || (yap != null && yap.ConfigurationCode.Contains(search))
                     select new { Balance = balance, Warehouse = warehouse, Location = location, Stock = stock, Yap = yap };
        var query = joined.Select(x => new SerialBalanceRow(x.Balance.Id, x.Balance.BranchCode, x.Warehouse.Id, x.Warehouse.WarehouseCode, x.Warehouse.WarehouseName,
            x.Location.Id, x.Location.Code, x.Location.Name, x.Stock.Id, x.Stock.ErpStockCode, x.Stock.StockName,
            x.Balance.YapCodeId, x.Yap != null ? x.Yap.ConfigurationCode : null, x.Balance.UnitCode,
            x.Balance.LotNo == "" ? null : x.Balance.LotNo, x.Balance.SerialNo, x.Balance.StockStatus,
            x.Balance.Quantity, x.Balance.ReservedQuantity, x.Balance.AvailableQuantity, x.Balance.LastMovementEntryId, x.Balance.LastTransactionDate,
            x.Balance.CreatedBy, x.Balance.CreatedDate, x.Balance.UpdatedBy, x.Balance.UpdatedDate));
        return await query.ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(SerialBalanceRow.LastTransactionDate))
            .ToPagedResponseAsync(request, cancellationToken);
    }

    public async Task<PagedResponse<SerialMovementHistoryRow>> GetSerialMovementHistoryAsync(long serialBalanceId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        var balance = await Locations.FindByIdAsync(serialBalanceId, cancellationToken: cancellationToken)
            ?? throw AppException.NotFound("Stok seri bakiyesi bulunamadı.");
        if (string.IsNullOrWhiteSpace(balance.SerialNo)) throw AppException.BadRequest("Seçilen bakiye seri takipli değildir.");

        var search = request.LegacySearch?.Trim() ?? string.Empty;
        var entries = Entries.Query().Where(x => x.StockId == balance.StockId && x.YapCodeId == balance.YapCodeId
            && x.UnitCode == balance.UnitCode && (x.LotNo ?? "") == balance.LotNo && x.SerialNo == balance.SerialNo
            && x.StockStatus == balance.StockStatus);
        var joined = from entry in entries
                     join operation in Operations.Query() on entry.OperationId equals operation.Id
                     join warehouse in WarehouseDefinitions.Query(ignoreQueryFilters: true) on entry.WarehouseId equals warehouse.Id
                     join location in LocationDefinitions.Query(ignoreQueryFilters: true) on entry.LocationId equals location.Id
                     join stock in Stocks.Query(ignoreQueryFilters: true) on entry.StockId equals stock.Id
                     join yap in YapCodes.Query(ignoreQueryFilters: true) on entry.YapCodeId equals yap.Id into yapJoin
                     from yap in yapJoin.DefaultIfEmpty()
                     where search == "" || operation.OperationCode.ToString().Contains(search)
                         || operation.OperationType.Contains(search) || (operation.ReferenceNo ?? "").Contains(search)
                         || warehouse.WarehouseName.Contains(search) || location.Code.Contains(search) || location.Name.Contains(search)
                     select new { Entry = entry, Operation = operation, Warehouse = warehouse, Location = location, Stock = stock, Yap = yap };
        var query = joined.Select(x => new SerialMovementHistoryRow(x.Entry.Id, x.Operation.Id, x.Operation.OperationCode, x.Operation.OperationType,
            Operations.Query().Any(reversal => reversal.ReversalOfOperationId == x.Operation.Id) ? StockMovementStatuses.Reversed : x.Operation.Status,
            x.Operation.ReferenceType, x.Operation.ReferenceNo, x.Warehouse.Id, x.Warehouse.WarehouseCode, x.Warehouse.WarehouseName,
            x.Location.Id, x.Location.Code, x.Location.Name, x.Stock.Id, x.Stock.ErpStockCode, x.Stock.StockName,
            x.Entry.YapCodeId, x.Yap != null ? x.Yap.ConfigurationCode : null, x.Entry.UnitCode,
            x.Entry.LotNo, x.Entry.SerialNo!, x.Entry.StockStatus, x.Entry.QuantityDelta, x.Entry.OccurredAt,
            x.Entry.CreatedBy, x.Entry.CreatedDate, x.Entry.UpdatedBy, x.Entry.UpdatedDate,
            (x.Operation.ReferenceType??"")+" / "+(x.Operation.ReferenceNo??""),
            x.Entry.QuantityDelta+" "+x.Entry.UnitCode));
        return await query.ApplySearch(request,SerialHistorySearchColumns,DefaultSerialHistorySearchColumns).ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(SerialMovementHistoryRow.OccurredAt))
            .ToPagedResponseAsync(request, cancellationToken);
    }

    public async Task<StockBalanceDrillDown> GetDrillDownAsync(long warehouseBalanceId, CancellationToken cancellationToken = default)
    {
        var balance = await Warehouses.FindByIdAsync(warehouseBalanceId, cancellationToken: cancellationToken)
            ?? throw AppException.NotFound("Depo stok bakiyesi bulunamadı.");
        var summaryPage = await GetWarehouseBalancesAsync(new PagedRequest { PageNumber = 1, PageSize = 1, Filters =
            [new AdvancedFilterRequest("id", "equals", warehouseBalanceId.ToString())] }, cancellationToken);
        var summary = summaryPage.Items.FirstOrDefault() ?? throw AppException.NotFound("Depo stok bakiyesi bulunamadı.");
        var balanceDetails = Locations.Query().Where(x => x.WarehouseId == balance.WarehouseId && x.StockId == balance.StockId
            && x.YapCodeId == balance.YapCodeId && x.UnitCode == balance.UnitCode && x.StockStatus == balance.StockStatus)
            .OrderByDescending(x => x.AvailableQuantity).ThenBy(x => x.LocationId);
        var detailQuery = BuildLocationRows(balanceDetails);
        return new StockBalanceDrillDown(summary, await detailQuery.ToListAsync(cancellationToken));
    }

    public Task<ProjectionRebuildResult> RebuildAsync(CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await Warehouses.Query(ignoreQueryFilters: true).ExecuteDeleteAsync(ct);
            await Locations.Query(ignoreQueryFilters: true).ExecuteDeleteAsync(ct);
            await States.Query(ignoreQueryFilters: true).ExecuteDeleteAsync(ct);
            var aggregates = await Entries.Query().GroupBy(x => new { x.BranchCode, x.WarehouseId, x.LocationId, x.StockId, x.YapCodeId, x.UnitCode, x.LotNo, x.SerialNo, x.StockStatus })
                .Select(x => new { x.Key, Quantity = x.Sum(v => v.QuantityDelta), LastId = x.Max(v => v.Id), LastDate = x.Max(v => v.OccurredAt) }).ToListAsync(ct);
            var reservationAggregates = await ReservationEntries.Query()
                .GroupBy(x => new { x.WarehouseId, x.LocationId, x.StockId, x.YapCodeId, x.UnitCode, x.LotNo, x.SerialNo, x.StockStatus })
                .Select(x => new { x.Key, Quantity = x.Sum(v => v.QuantityDelta) })
                .ToListAsync(ct);
            var reservationMap = reservationAggregates.ToDictionary(
                x => new LocationDimensionKey(x.Key.WarehouseId, x.Key.LocationId, x.Key.StockId, x.Key.YapCodeId,
                    x.Key.UnitCode, NormalizeKeyPart(x.Key.LotNo), NormalizeKeyPart(x.Key.SerialNo), x.Key.StockStatus),
                x => x.Quantity);
            var now = DateTime.UtcNow;
            var locationRows = aggregates.Select(x =>
            {
                var key = new LocationDimensionKey(x.Key.WarehouseId, x.Key.LocationId, x.Key.StockId, x.Key.YapCodeId, x.Key.UnitCode, NormalizeKeyPart(x.Key.LotNo), NormalizeKeyPart(x.Key.SerialNo), x.Key.StockStatus);
                var reserved = reservationMap.GetValueOrDefault(key);
                if (reserved < 0 || reserved > x.Quantity)
                    throw AppException.Conflict("Rezervasyon defteri ile stok hareket defteri arasında tutarsızlık bulundu.");
                return new LocationStockBalance
            {
                DimensionKey = HashLocationKey(key),
                BranchCode = x.Key.BranchCode, WarehouseId = x.Key.WarehouseId, LocationId = x.Key.LocationId, StockId = x.Key.StockId,
                YapCodeId = x.Key.YapCodeId, UnitCode = x.Key.UnitCode, LotNo = x.Key.LotNo ?? "", SerialNo = x.Key.SerialNo ?? "",
                StockStatus = x.Key.StockStatus, Quantity = x.Quantity, ReservedQuantity = reserved, AvailableQuantity = x.Quantity - reserved,
                LastMovementEntryId = x.LastId, LastTransactionDate = x.LastDate, LastReconciledAt = now
            };}).ToList();
            await Locations.AddRangeAsync(locationRows, ct); await unitOfWork.SaveChangesAsync(ct);
            await BuildAllWarehouseRowsAsync(locationRows, now, ct);
            var lastId = aggregates.Count == 0 ? 0 : aggregates.Max(x => x.LastId);
            await UpdateStateAsync(lastId, now, ct); await unitOfWork.SaveChangesAsync(ct);
            return new ProjectionRebuildResult(locationRows.Count, await Warehouses.CountAsync(cancellationToken: ct), lastId, now);
        }, cancellationToken, IsolationLevel.Serializable);

    public async Task<ReconciliationSummary> GetReconciliationSummaryAsync(CancellationToken cancellationToken = default)
    {
        var (ledger, projection) = await LoadReconciliationMapsAsync(cancellationToken);
        var issues = BuildIssues(ledger, projection);
        var state = await States.Query().FirstOrDefaultAsync(x => x.ProjectionName == StockBalanceProjectionNames.Current, cancellationToken);
        return new ReconciliationSummary(ledger.Count, projection.Count, issues.Count,
            issues.Count(x => x.IssueType == "MissingProjection"), issues.Count(x => x.IssueType == "ExtraProjection"),
            ledger.Count == 0 ? 0 : ledger.Values.Max(x => x.LastId), state?.LastMovementEntryId ?? 0, DateTime.UtcNow);
    }

    public async Task<PagedResponse<ReconciliationIssue>> GetReconciliationIssuesAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        var (ledger, projection) = await LoadReconciliationMapsAsync(cancellationToken);
        IQueryable<ReconciliationIssue> query = BuildIssues(ledger, projection).AsQueryable();
        query = query.ApplySearch(request);
        query = string.Equals(request.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
            ? query.OrderBy(x => x.Difference) : query.OrderByDescending(x => Math.Abs(x.Difference));
        var pageNumber = PagedQueryExtensions.NormalizePageNumber(request.EffectivePageNumber);
        var pageSize = PagedQueryExtensions.NormalizePageSize(request.PageSize);
        var rows = query.ToList();
        return new PagedResponse<ReconciliationIssue> { Items = rows.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), TotalCount = rows.Count, PageNumber = pageNumber, PageSize = pageSize };
    }

    private IQueryable<LocationBalanceRow> BuildLocationRows(IQueryable<LocationStockBalance> balances) =>
        from balance in balances
        join warehouse in WarehouseDefinitions.Query() on balance.WarehouseId equals warehouse.Id
        join location in LocationDefinitions.Query(ignoreQueryFilters: true) on balance.LocationId equals location.Id
        join stock in Stocks.Query(ignoreQueryFilters: true) on balance.StockId equals stock.Id
        join yap in YapCodes.Query(ignoreQueryFilters: true) on balance.YapCodeId equals yap.Id into yapJoin
        from yap in yapJoin.DefaultIfEmpty()
        select new LocationBalanceRow(balance.Id, balance.BranchCode, warehouse.Id, warehouse.WarehouseCode, warehouse.WarehouseName,
            location.Id, location.Code, location.Name, stock.Id, stock.ErpStockCode, stock.StockName, balance.YapCodeId,
            yap != null ? yap.ConfigurationCode : null, balance.UnitCode, balance.LotNo == "" ? null : balance.LotNo,
            balance.SerialNo == "" ? null : balance.SerialNo, balance.StockStatus, balance.Quantity, balance.ReservedQuantity,
            balance.AvailableQuantity, balance.LastMovementEntryId, balance.LastTransactionDate, balance.CreatedBy, balance.CreatedDate, balance.UpdatedBy, balance.UpdatedDate);

    private async Task RecalculateWarehouseRowsAsync(IReadOnlyCollection<WarehouseDimensionKey> keys, CancellationToken ct)
    {
        foreach (var key in keys)
        {
            var rows = await Locations.Query().Where(x => x.WarehouseId == key.WarehouseId && x.StockId == key.StockId && x.YapCodeId == key.YapCodeId
                && x.UnitCode == key.UnitCode && x.StockStatus == key.StockStatus).ToListAsync(ct);
            var summary = await Warehouses.FirstOrDefaultAsync(x => x.WarehouseId == key.WarehouseId && x.StockId == key.StockId && x.YapCodeId == key.YapCodeId
                && x.UnitCode == key.UnitCode && x.StockStatus == key.StockStatus, true, ct);
            if (summary is null)
            {
                summary = new WarehouseStockBalance { BranchCode = rows.First().BranchCode, WarehouseId = key.WarehouseId, StockId = key.StockId,
                    YapCodeId = key.YapCodeId, UnitCode = key.UnitCode, StockStatus = key.StockStatus, DimensionKey = HashWarehouseKey(key) };
                await Warehouses.AddAsync(summary, ct);
            }
            ApplySummary(summary, rows, DateTime.UtcNow);
        }
    }

    private async Task BuildAllWarehouseRowsAsync(IReadOnlyCollection<LocationStockBalance> rows, DateTime reconciledAt, CancellationToken ct)
    {
        var summaries = rows.GroupBy(x => new WarehouseDimensionKey(x.WarehouseId, x.StockId, x.YapCodeId, x.UnitCode, x.StockStatus)).Select(group =>
        {
            var summary = new WarehouseStockBalance { BranchCode = group.First().BranchCode, WarehouseId = group.Key.WarehouseId,
                StockId = group.Key.StockId, YapCodeId = group.Key.YapCodeId, UnitCode = group.Key.UnitCode, StockStatus = group.Key.StockStatus,
                DimensionKey = HashWarehouseKey(group.Key) };
            ApplySummary(summary, group, reconciledAt); return summary;
        }).ToList();
        await Warehouses.AddRangeAsync(summaries, ct);
    }

    private static void ApplySummary(WarehouseStockBalance summary, IEnumerable<LocationStockBalance> source, DateTime reconciledAt)
    {
        var rows = source.ToList(); summary.Quantity = rows.Sum(x => x.Quantity); summary.ReservedQuantity = rows.Sum(x => x.ReservedQuantity);
        summary.AvailableQuantity = rows.Sum(x => x.AvailableQuantity); summary.DistinctLocationCount = rows.Where(x => x.Quantity != 0).Select(x => x.LocationId).Distinct().Count();
        summary.DistinctLotCount = rows.Where(x => x.Quantity != 0 && x.LotNo != "").Select(x => x.LotNo).Distinct().Count();
        summary.DistinctSerialCount = rows.Where(x => x.Quantity != 0 && x.SerialNo != "").Select(x => x.SerialNo).Distinct().Count();
        summary.LastMovementEntryId = rows.Count == 0 ? 0 : rows.Max(x => x.LastMovementEntryId);
        summary.LastTransactionDate = rows.Count == 0 ? DateTime.UtcNow : rows.Max(x => x.LastTransactionDate);
        summary.LastReconciledAt = reconciledAt; summary.UpdatedDate = DateTime.UtcNow;
    }

    private async Task UpdateStateAsync(long lastEntryId, DateTime? reconciledAt, CancellationToken ct)
    {
        var state = await States.FirstOrDefaultAsync(x => x.ProjectionName == StockBalanceProjectionNames.Current, true, ct);
        if (state is null) { state = new StockBalanceProjectionState(); await States.AddAsync(state, ct); }
        state.LastMovementEntryId = Math.Max(state.LastMovementEntryId, lastEntryId); state.LastProjectedAt = DateTime.UtcNow;
        if (reconciledAt.HasValue) state.LastReconciledAt = reconciledAt;
    }

    private async Task<(Dictionary<LocationDimensionKey, LedgerAggregate> ledger, Dictionary<LocationDimensionKey, ProjectionAggregate> projection)> LoadReconciliationMapsAsync(CancellationToken ct)
    {
        var ledgerRows = await Entries.Query().GroupBy(x => new { x.WarehouseId, x.LocationId, x.StockId, x.YapCodeId, x.UnitCode, x.LotNo, x.SerialNo, x.StockStatus })
            .Select(x => new { x.Key, Quantity = x.Sum(v => v.QuantityDelta), LastId = x.Max(v => v.Id) }).ToListAsync(ct);
        var projectionRows = await Locations.Query().Select(x => new { x.WarehouseId, x.LocationId, x.StockId, x.YapCodeId, x.UnitCode, x.LotNo, x.SerialNo, x.StockStatus, x.Quantity, x.LastMovementEntryId }).ToListAsync(ct);
        var ledger = ledgerRows.ToDictionary(x => new LocationDimensionKey(x.Key.WarehouseId, x.Key.LocationId, x.Key.StockId, x.Key.YapCodeId, x.Key.UnitCode, NormalizeKeyPart(x.Key.LotNo), NormalizeKeyPart(x.Key.SerialNo), x.Key.StockStatus), x => new LedgerAggregate(x.Quantity, x.LastId));
        var projection = projectionRows.ToDictionary(x => new LocationDimensionKey(x.WarehouseId, x.LocationId, x.StockId, x.YapCodeId, x.UnitCode, NormalizeKeyPart(x.LotNo), NormalizeKeyPart(x.SerialNo), x.StockStatus), x => new ProjectionAggregate(x.Quantity, x.LastMovementEntryId));
        return (ledger, projection);
    }

    private static List<ReconciliationIssue> BuildIssues(Dictionary<LocationDimensionKey, LedgerAggregate> ledger, Dictionary<LocationDimensionKey, ProjectionAggregate> projection)
    {
        var issues = new List<ReconciliationIssue>();
        foreach (var key in ledger.Keys.Union(projection.Keys))
        {
            ledger.TryGetValue(key, out var left); projection.TryGetValue(key, out var right);
            if (left.Quantity == right.Quantity && ledger.ContainsKey(key) && projection.ContainsKey(key)) continue;
            var type = !projection.ContainsKey(key) ? "MissingProjection" : !ledger.ContainsKey(key) ? "ExtraProjection" : "QuantityMismatch";
            issues.Add(new(type, key.WarehouseId, key.LocationId, key.StockId, key.YapCodeId, key.UnitCode,
                key.LotNo == "" ? null : key.LotNo, key.SerialNo == "" ? null : key.SerialNo, key.StockStatus,
                left.Quantity, right.Quantity, left.Quantity - right.Quantity, left.LastId, right.LastId));
        }
        return issues;
    }

    // Lot/Seri karşılaştırması boşluk ve büyük/küçük harf farkına duyarlı olmamalı — aksi halde aynı fiziksel
    // birim, farklı ekranlarda (draft, sayım artışı, transfer) girilen değerler arasında eşleşme bulamıyor.
    private static string NormalizeKeyPart(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static LocationDimensionKey EntryKey(StockMovementEntry x) => new(x.WarehouseId, x.LocationId, x.StockId, x.YapCodeId, x.UnitCode, NormalizeKeyPart(x.LotNo), NormalizeKeyPart(x.SerialNo), x.StockStatus);
    private static LocationDimensionKey ReservationKey(StockReservationLineRequest x) => new(x.WarehouseId, x.LocationId, x.StockId, x.YapCodeId, x.UnitCode, NormalizeKeyPart(x.LotNo), NormalizeKeyPart(x.SerialNo), x.StockStatus);
    private static LocationDimensionKey BalanceKey(LocationStockBalance x) => new(x.WarehouseId, x.LocationId, x.StockId, x.YapCodeId, x.UnitCode, NormalizeKeyPart(x.LotNo), NormalizeKeyPart(x.SerialNo), x.StockStatus);
    private static WarehouseDimensionKey WarehouseKey(LocationDimensionKey x) => new(x.WarehouseId, x.StockId, x.YapCodeId, x.UnitCode, x.StockStatus);
    private static string HashLocationKey(LocationDimensionKey key) => Hash($"{key.WarehouseId}|{key.LocationId}|{key.StockId}|{key.YapCodeId?.ToString() ?? "0"}|{key.UnitCode}|{key.LotNo}|{key.SerialNo}|{key.StockStatus}");
    private static string HashWarehouseKey(WarehouseDimensionKey key) => Hash($"{key.WarehouseId}|{key.StockId}|{key.YapCodeId?.ToString() ?? "0"}|{key.UnitCode}|{key.StockStatus}");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static PostStockReservationRequest NormalizeReservation(PostStockReservationRequest request) =>
        request with
        {
            IdempotencyKey = request.IdempotencyKey?.Trim() ?? string.Empty,
            ReferenceType = request.ReferenceType?.Trim() ?? string.Empty,
            ReferenceNo = string.IsNullOrWhiteSpace(request.ReferenceNo) ? null : request.ReferenceNo.Trim(),
            OperationType = request.OperationType?.Trim() ?? string.Empty,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            Lines = (request.Lines ?? []).Select(x => x with
            {
                UnitCode = x.UnitCode.Trim().ToUpperInvariant(),
                LotNo = string.IsNullOrWhiteSpace(x.LotNo) ? null : x.LotNo.Trim(),
                SerialNo = string.IsNullOrWhiteSpace(x.SerialNo) ? null : x.SerialNo.Trim(),
                StockStatus = string.IsNullOrWhiteSpace(x.StockStatus) ? "Available" : x.StockStatus.Trim()
            }).ToList()
        };
    private static void ValidateReservation(PostStockReservationRequest request)
    {
        if (request.IdempotencyKey.Length is < 8 or > 100) throw AppException.BadRequest("Rezervasyon idempotency anahtarı 8-100 karakter olmalıdır.");
        if (request.ReferenceId <= 0 || request.ReferenceType.Length is < 2 or > 50) throw AppException.BadRequest("Rezervasyon referansı geçersiz.");
        if (!StockReservationOperationTypes.All.Contains(request.OperationType)) throw AppException.BadRequest("Rezervasyon işlem tipi geçersiz.");
        if (request.Lines.Count is < 1 or > 500 || request.Lines.Any(x => x.ReferenceLineId <= 0 || x.QuantityDelta == 0
            || x.WarehouseId <= 0 || x.LocationId <= 0 || x.StockId <= 0))
            throw AppException.BadRequest("Rezervasyon satırları geçersiz.");
    }

    private async Task ValidateReservationTrackingPoliciesAsync(
        PostStockReservationRequest request,
        CancellationToken cancellationToken)
    {
        var serializedLines = request.Lines.Where(x => !string.IsNullOrWhiteSpace(x.SerialNo)).ToArray();
        if (serializedLines.Length == 0) return;

        var stockIds = serializedLines.Select(x => x.StockId).Distinct().ToArray();
        var branches = await Stocks.Query()
            .Where(x => stockIds.Contains(x.Id))
            .Select(x => new { x.Id, x.BranchCode })
            .ToDictionaryAsync(x => x.Id, x => x.BranchCode, cancellationToken);
        if (branches.Count != stockIds.Length)
            throw AppException.BadRequest("Seri rezervasyonu için stok kartlarından biri bulunamadı.");

        var policies = new Dictionary<long, EffectiveStockTrackingPolicy>();
        foreach (var stockId in stockIds)
            policies[stockId] = await trackingPolicies.ResolveAsync(branches[stockId], stockId, cancellationToken);

        try
        {
            foreach (var line in serializedLines)
                StockTrackingPolicyGuard.ValidateSerialQuantity(
                    policies[line.StockId], Math.Abs(line.QuantityDelta), line.SerialNo);
        }
        catch (StockTrackingPolicyViolationException exception)
        {
            throw AppException.BadRequest(exception.Message);
        }
    }
    private sealed record LocationDimensionKey(long WarehouseId, long LocationId, long StockId, long? YapCodeId, string UnitCode, string LotNo, string SerialNo, string StockStatus);
    private sealed record WarehouseDimensionKey(long WarehouseId, long StockId, long? YapCodeId, string UnitCode, string StockStatus);
    private readonly record struct LedgerAggregate(decimal Quantity, long LastId);
    private readonly record struct ProjectionAggregate(decimal Quantity, long LastId);
}
