using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.BarcodeDesigner.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.WarehouseInbound.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.BarcodeDesigner.Application;

public sealed class WarehouseBarcodeResolutionService(
    IUnitOfWork uow,
    IStockTrackingPolicyResolver trackingPolicies) : IWarehouseBarcodeResolver
{
    public async Task<ResolvedWarehouseBarcode> ResolveAsync(
        ResolveWarehouseBarcodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var raw = Clean(request.Barcode, 250)
            ?? throw AppException.BadRequest("Barkod zorunludur.");
        if (raw.Length < 2) throw AppException.BadRequest("Barkod çok kısa.");
        var branch = string.IsNullOrWhiteSpace(request.BranchCode) ? "0" : request.BranchCode.Trim();

        var generated = await uow.Repository<GeneratedBarcode>().Query()
            .FirstOrDefaultAsync(x => x.BranchCode == branch && x.BarcodeValue == raw, cancellationToken);
        var goodsReceiptLabel = await uow.Repository<GoodsReceiptLabel>().Query()
            .FirstOrDefaultAsync(x => x.BranchCode == branch && x.BarcodeValue == raw
                && x.Status != GoodsReceiptLabelStatus.Void, cancellationToken);
        var warehouseInboundLabel = goodsReceiptLabel is null
            ? await uow.Repository<WarehouseInboundLabel>().Query()
                .FirstOrDefaultAsync(x => x.BranchCode == branch && x.BarcodeValue == raw
                    && x.Status != WarehouseInboundLabelStatus.Void, cancellationToken)
            : null;

        var parsed = generated is null && goodsReceiptLabel is null && warehouseInboundLabel is null
            ? WarehouseBarcodeParser.TryParse(raw)
            : null;

        var stockId = goodsReceiptLabel?.StockId
            ?? warehouseInboundLabel?.StockId
            ?? request.ExpectedStockId;
        var stockCode = goodsReceiptLabel?.StockCodeSnapshot
            ?? warehouseInboundLabel?.StockCodeSnapshot
            ?? generated?.StockCode
            ?? parsed?.ProductCode;
        var yapCode = goodsReceiptLabel?.YapCodeSnapshot
            ?? warehouseInboundLabel?.YapCodeSnapshot
            ?? generated?.YapCode;
        var lot = Clean(goodsReceiptLabel?.LotNo
            ?? warehouseInboundLabel?.LotNo
            ?? generated?.LotNo
            ?? parsed?.LotNo, 100);
        var serial = Clean(goodsReceiptLabel?.SerialNo
            ?? warehouseInboundLabel?.SerialNo
            ?? generated?.SerialNo
            ?? parsed?.SerialNo, 100);
        var quantity = goodsReceiptLabel?.LabelQuantity
            ?? warehouseInboundLabel?.LabelQuantity
            ?? parsed?.Quantity;
        var manufacturingDate = goodsReceiptLabel?.ManufacturingDate
            ?? warehouseInboundLabel?.ManufacturingDate
            ?? parsed?.ManufacturingDate;
        var expirationDate = goodsReceiptLabel?.ExpirationDate
            ?? warehouseInboundLabel?.ExpirationDate
            ?? parsed?.ExpirationDate;

        LocationStockBalance? serialBalance = null;
        // Outbound callers normally provide ExpectedStockId so the scanned value can only
        // satisfy the selected transfer line. That stock context must narrow serial lookup;
        // it must not disable it. Otherwise a plain serial barcode is incorrectly treated as
        // a stock alias and serial-required lines fail with "Seri" missing.
        if (serial is null
            && request.Purpose == WarehouseBarcodePurpose.Outbound
            && generated is null
            && goodsReceiptLabel is null
            && warehouseInboundLabel is null)
        {
            var serialRows = await uow.Repository<LocationStockBalance>().Query()
                .Where(x => x.BranchCode == branch
                    && x.AvailableQuantity > 0
                    && x.SerialNo == raw
                    && (!stockId.HasValue || x.StockId == stockId.Value)
                    && (!request.WarehouseId.HasValue || x.WarehouseId == request.WarehouseId.Value)
                    && (!request.ExpectedLocationId.HasValue || x.LocationId == request.ExpectedLocationId.Value)
                    && (!request.ExpectedYapCodeId.HasValue || x.YapCodeId == request.ExpectedYapCodeId.Value)
                    && (string.IsNullOrWhiteSpace(request.ExpectedUnitCode)
                        || x.UnitCode == request.ExpectedUnitCode))
                .OrderByDescending(x => x.AvailableQuantity)
                .ThenBy(x => x.Id)
                .Take(25)
                .ToListAsync(cancellationToken);
            serialBalance = SelectOutboundSerialBalance(serialRows, stockId, request.ExpectedLocationId);
            if (serialBalance is not null)
            {
                stockId = serialBalance.StockId;
                serial = raw;
                lot = EmptyToNull(serialBalance.LotNo);
                // For an item-level serial this is 1. For a pallet/plate serial it is the
                // movable quantity represented by that serial; the effective tracking policy
                // and remaining transfer quantity cap the accepted amount later.
                quantity = serialBalance.AvailableQuantity;
            }
        }
        var unitCode = Clean(goodsReceiptLabel?.UnitCode
            ?? warehouseInboundLabel?.UnitCode
            ?? serialBalance?.UnitCode, 20);

        var stock = await ResolveStock(branch, stockId, stockCode, raw, request.Purpose, cancellationToken);
        unitCode ??= Clean(stock.BaseUnitCode, 20) ?? "ADET";
        if (request.Purpose == WarehouseBarcodePurpose.Outbound
            && request.ExpectedStockId.HasValue
            && generated is null
            && goodsReceiptLabel is null
            && warehouseInboundLabel is null
            && serialBalance is null)
        {
            var scannedStockAlias = Clean(parsed?.ProductCode, 100) ?? raw;
            if (!MatchesStockAlias(stock, scannedStockAlias))
                throw AppException.Conflict(
                    $"Okutulan barkod beklenen stokla uyuşmuyor. Beklenen: {stock.ErpStockCode}.");
        }
        if (request.ExpectedStockId.HasValue && stock.Id != request.ExpectedStockId.Value)
            throw AppException.Conflict($"Okutulan barkod beklenen stokla uyuşmuyor. Barkod: {stock.ErpStockCode}.");

        if (request.Purpose == WarehouseBarcodePurpose.Outbound
            && (manufacturingDate is null || expirationDate is null)
            && (serial is not null || lot is not null))
        {
            var evidence = await FindInboundTrackingEvidence(
                branch, stock.Id, lot, serial, cancellationToken);
            manufacturingDate ??= evidence?.ManufacturingDate;
            expirationDate ??= evidence?.ExpirationDate;
        }

        if (serial is null
            && request.Purpose == WarehouseBarcodePurpose.Inbound
            && request.ExpectedStockId == stock.Id
            && generated is null
            && goodsReceiptLabel is null
            && warehouseInboundLabel is null
            && parsed is null)
            serial = raw;

        var yap = await ResolveYapCode(
            branch,
            stock.Id,
            yapCode,
            goodsReceiptLabel?.YapCodeId ?? warehouseInboundLabel?.YapCodeId ?? serialBalance?.YapCodeId,
            cancellationToken);
        var policy = await trackingPolicies.ResolveAsync(branch, stock.Id, cancellationToken);
        if (serial is not null) quantity ??= 1;

        var balances = request.Purpose == WarehouseBarcodePurpose.Outbound
            ? await FindBalances(branch, request.WarehouseId, request.ExpectedLocationId, stock.Id, yap?.Id, lot, serial, cancellationToken)
            : [];

        var missing = new List<string>();
        if (policy.RequireSerial && serial is null) missing.Add("Seri");
        if (policy.RequireLot && lot is null) missing.Add("Lot");
        if (policy.RequireManufacturingDate && !manufacturingDate.HasValue) missing.Add("Üretim tarihi");
        if (policy.RequireExpirationDate && !expirationDate.HasValue) missing.Add("Son kullanma tarihi");
        if (request.Purpose == WarehouseBarcodePurpose.Outbound && balances.Count == 0)
            missing.Add("Kullanılabilir raf bakiyesi");

        var source = goodsReceiptLabel is not null ? "GoodsReceiptLabel"
            : warehouseInboundLabel is not null ? "WarehouseInboundLabel"
            : generated is not null ? "GeneratedBarcode"
            : parsed is not null ? "GS1"
            : serialBalance is not null ? "SerialBalance"
            : "StockAlias";

        return new ResolvedWarehouseBarcode(
            raw,
            source,
            stock.Id,
            stock.ErpStockCode,
            stock.StockName,
            yap?.Id,
            yap?.ConfigurationCode,
            quantity,
            unitCode,
            lot,
            serial,
            manufacturingDate,
            expirationDate,
            policy.RequireSerial,
            policy.RequireLot,
            policy.RequireManufacturingDate,
            policy.RequireExpirationDate,
            missing,
            balances,
            balances.Count == 1 ? balances[0].LocationId : null,
            missing.Count == 0);
    }

    private async Task<StockEntity> ResolveStock(
        string branch,
        long? stockId,
        string? productCode,
        string raw,
        WarehouseBarcodePurpose purpose,
        CancellationToken ct)
    {
        if (stockId.HasValue)
            return await uow.Repository<StockEntity>().FirstOrDefaultAsync(
                x => x.Id == stockId.Value && x.BranchCode == branch, false, ct)
                ?? throw AppException.NotFound("Barkoda bağlı stok bulunamadı.");

        var code = Clean(productCode, 100) ?? raw;
        var matches = await uow.Repository<StockEntity>().Query()
            .Where(x => x.BranchCode == branch
                && (x.ErpStockCode == code
                    || x.ManufacturerCode == code
                    || x.Code1 == code
                    || x.Code2 == code
                    || x.Code3 == code
                    || x.Code4 == code
                    || x.Code5 == code))
            .Take(3)
            .ToListAsync(ct);
        if (matches.Count == 0)
            throw AppException.NotFound(purpose == WarehouseBarcodePurpose.Outbound
                ? "Barkod mevcut seri/lot bakiyesi veya stok kartıyla eşleşmedi."
                : "Barkod stok kartıyla eşleşmedi; emir satırı bağlamı veya stok barkod aliası gereklidir.");
        if (matches.Count > 1)
            throw AppException.Conflict("Barkod birden fazla stok kartıyla eşleşiyor; stok barkod alanları tekil olmalıdır.");
        return matches[0];
    }

    private async Task<YapCodeEntity?> ResolveYapCode(
        string branch,
        long stockId,
        string? code,
        long? id,
        CancellationToken ct)
    {
        if (id.HasValue)
            return await uow.Repository<YapCodeEntity>().FirstOrDefaultAsync(
                x => x.Id == id.Value && x.BranchCode == branch, false, ct);
        if (string.IsNullOrWhiteSpace(code)) return null;
        var rows = await uow.Repository<YapCodeEntity>().Query()
            .Where(x => x.BranchCode == branch
                && x.ConfigurationCode == code
                && (!x.StockId.HasValue || x.StockId == stockId))
            .Take(2)
            .ToListAsync(ct);
        if (rows.Count > 1) throw AppException.Conflict("Barkod yapı kodu birden fazla kayıtla eşleşiyor.");
        return rows.SingleOrDefault()
            ?? throw AppException.NotFound("Barkod üzerindeki yapı kodu stokla eşleşmedi.");
    }

    private async Task<IReadOnlyList<WarehouseBarcodeBalanceCandidate>> FindBalances(
        string branch,
        long? warehouseId,
        long? expectedLocationId,
        long stockId,
        long? yapCodeId,
        string? lot,
        string? serial,
        CancellationToken ct)
    {
        var balances = uow.Repository<LocationStockBalance>().Query();
        var locations = uow.Repository<WarehouseLocation>().Query();
        var query =
            from balance in balances
            join location in locations on balance.LocationId equals location.Id
            where balance.BranchCode == branch
                && balance.StockId == stockId
                && balance.AvailableQuantity > 0
                && balance.StockStatus == "Available"
                && (!warehouseId.HasValue || balance.WarehouseId == warehouseId.Value)
                && (!expectedLocationId.HasValue || balance.LocationId == expectedLocationId.Value)
                && (!yapCodeId.HasValue || balance.YapCodeId == yapCodeId)
                && (lot == null || balance.LotNo == lot)
                && (serial == null || balance.SerialNo == serial)
            orderby balance.AvailableQuantity descending, location.Code
            select new WarehouseBarcodeBalanceCandidate(
                balance.Id,
                balance.WarehouseId,
                balance.LocationId,
                location.Code,
                location.Name,
                balance.StockId,
                balance.YapCodeId,
                balance.UnitCode,
                EmptyToNull(balance.LotNo),
                EmptyToNull(balance.SerialNo),
                balance.StockStatus,
                balance.AvailableQuantity);
        return await query.Take(25).ToListAsync(ct);
    }

    private async Task<TrackingDateEvidence?> FindInboundTrackingEvidence(
        string branch,
        long stockId,
        string? lot,
        string? serial,
        CancellationToken ct)
    {
        var goodsReceipt = await uow.Repository<GoodsReceiptExecutionLine>().Query()
            .Where(x => x.BranchCode == branch
                && x.StockId == stockId
                && (serial == null || x.SerialNo == serial)
                && (lot == null || x.LotNo == lot))
            .OrderByDescending(x => x.Id)
            .Select(x => new TrackingDateEvidence(
                x.ManufacturingDate, x.ExpirationDate, x.CreatedDate, x.Id))
            .FirstOrDefaultAsync(ct);
        var warehouseInbound = await uow.Repository<WarehouseInboundExecutionLine>().Query()
            .Where(x => x.BranchCode == branch
                && x.StockId == stockId
                && (serial == null || x.SerialNo == serial)
                && (lot == null || x.LotNo == lot))
            .OrderByDescending(x => x.Id)
            .Select(x => new TrackingDateEvidence(
                x.ManufacturingDate, x.ExpirationDate, x.CreatedDate, x.Id))
            .FirstOrDefaultAsync(ct);

        if (goodsReceipt is null) return warehouseInbound;
        if (warehouseInbound is null) return goodsReceipt;
        return Nullable.Compare(goodsReceipt.CreatedDate, warehouseInbound.CreatedDate) > 0
            || goodsReceipt.CreatedDate == warehouseInbound.CreatedDate && goodsReceipt.Id >= warehouseInbound.Id
                ? goodsReceipt
                : warehouseInbound;
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool MatchesStockAlias(StockEntity stock, string value) =>
        new[]
        {
            stock.ErpStockCode,
            stock.ManufacturerCode,
            stock.Code1,
            stock.Code2,
            stock.Code3,
            stock.Code4,
            stock.Code5
        }.Any(alias => string.Equals(alias?.Trim(), value, StringComparison.OrdinalIgnoreCase));

    private static string? Clean(string? value, int max)
    {
        var result = EmptyToNull(value);
        if (result?.Length > max) throw AppException.BadRequest($"Barkod alanı en fazla {max} karakter olabilir.");
        return result;
    }

    private static LocationStockBalance? SelectOutboundSerialBalance(
        IReadOnlyList<LocationStockBalance> serialRows,
        long? expectedStockId,
        long? expectedLocationId)
    {
        if (serialRows.Count == 0) return null;
        if (serialRows.Count == 1) return serialRows[0];

        var stockIds = serialRows.Select(x => x.StockId).Distinct().ToArray();
        if (!expectedStockId.HasValue && stockIds.Length > 1)
            throw AppException.Conflict("Okutulan seri birden fazla stok boyutuyla eşleşiyor; raf veya stok bağlamı zorunludur.");

        var candidates = serialRows.AsEnumerable();
        if (expectedLocationId.HasValue)
        {
            var atLocation = candidates.Where(x => x.LocationId == expectedLocationId.Value).ToArray();
            if (atLocation.Length > 0)
                candidates = atLocation;
        }

        var materialized = candidates.ToArray();
        if (materialized.Length == 0) return null;
        return materialized
            .OrderByDescending(x => x.AvailableQuantity)
            .ThenBy(x => x.Id)
            .First();
    }

    private sealed record TrackingDateEvidence(
        DateOnly? ManufacturingDate,
        DateOnly? ExpirationDate,
        DateTime? CreatedDate,
        long Id);
}
