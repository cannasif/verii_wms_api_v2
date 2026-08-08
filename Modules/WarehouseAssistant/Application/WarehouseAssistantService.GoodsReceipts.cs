using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.ProjectSettings.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using static verii_wms_api_v2.Modules.WarehouseAssistant.Localization.WarehouseAssistantMessageKeys;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

public sealed partial class WarehouseAssistantService
{
    private async Task<ExecutionResult> ExecuteGoodsReceiptAnalysisAsync(
        WarehouseAssistantIntentResolution resolution,
        string message,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewGoodsReceipts)
            return Denied(resolution.Intent, M(GoodsReceiptAnalysisDenied));

        var supplier = await ResolveSupplierAsync(resolution.SupplierQuery, branchCode, ct);
        if (!string.IsNullOrWhiteSpace(resolution.SupplierQuery) && supplier is null)
            return MissingEntity(resolution.Intent, M(GoodsReceiptSupplierRequired));

        var stockCandidates = ExtractStockCandidates(message);
        var stock = stockCandidates.Count > 0
            ? await ResolveStockAsync(message, branchCode, ct)
            : null;
        var (dateFrom, dateTo, periodLabel) = await ResolveDocumentDateRangeAsync(resolution, ct);
        var warehouseAccess = await UserWarehouseAccessService.ResolveAsync(unitOfWork, actorUserId, branchCode, ct);

        var query =
            from line in unitOfWork.Repository<GoodsReceiptLine>().Query()
            join header in unitOfWork.Repository<GoodsReceiptHeader>().Query() on line.GrHeaderId equals header.Id
            join warehouse in unitOfWork.Repository<WarehouseEntity>().Query() on line.TargetWarehouseId equals warehouse.Id
            where header.BranchCode == branchCode
                && header.DocumentDate >= dateFrom
                && header.DocumentDate <= dateTo
                && header.Status != WarehouseOperationStatus.Cancelled
                && line.Status != GoodsReceiptLineStatus.Cancelled
                && line.ReceivedQuantity > 0
            select new { Header = header, Line = line, Warehouse = warehouse };

        if (warehouseAccess.IsRestricted)
            query = query.Where(x => warehouseAccess.WarehouseIds.Contains(x.Line.TargetWarehouseId));
        if (supplier is not null)
        {
            var supplierCode = supplier.Code;
            query = query.Where(x => x.Header.SupplierId == supplier.Id
                || x.Header.SupplierCodeSnapshot == supplierCode);
        }
        if (stock is not null)
            query = query.Where(x => x.Line.StockId == stock.Id);

        var receiptCount = await query.Select(x => x.Header.Id).Distinct().CountAsync(ct);
        var lineCount = await query.CountAsync(ct);
        var quantityTotals = await query
            .GroupBy(x => x.Line.UnitCode)
            .Select(group => new { UnitCode = group.Key, Quantity = group.Sum(x => x.Line.ReceivedQuantity) })
            .OrderBy(x => x.UnitCode)
            .Take(12)
            .ToListAsync(ct);

        var rawRows = await query
            .OrderByDescending(x => x.Header.DocumentDate)
            .ThenByDescending(x => x.Header.Id)
            .ThenBy(x => x.Line.LineNo)
            .Take(MaximumResultCount)
            .Select(x => new
            {
                HeaderId = x.Header.Id,
                x.Header.DocumentNo,
                x.Header.DocumentDate,
                x.Header.ReceivedAtUtc,
                x.Header.SupplierId,
                SupplierCode = x.Header.SupplierCodeSnapshot ?? string.Empty,
                SupplierName = x.Header.SupplierNameSnapshot ?? string.Empty,
                x.Warehouse.WarehouseCode,
                x.Warehouse.WarehouseName,
                x.Line.StockId,
                StockCode = x.Line.StockCodeSnapshot,
                StockName = x.Line.StockNameSnapshot ?? string.Empty,
                YapCode = x.Line.YapCodeSnapshot,
                x.Line.UnitCode,
                x.Line.ReceivedQuantity,
                x.Line.AcceptedQuantity,
                x.Line.RejectedQuantity,
                x.Line.QuarantineQuantity,
                x.Line.PutawayQuantity,
                HeaderStatus = x.Header.Status.ToString(),
                QualityStatus = x.Header.QualityStatus.ToString(),
                ErpStatus = x.Header.ErpIntegrationStatus.ToString(),
                ActorUserId = x.Header.ReceivedBy ?? x.Header.CompletedBy ?? x.Header.CreatedBy
            })
            .ToListAsync(ct);

        var names = await ResolveUserNamesAsync(rawRows.Select(x => x.ActorUserId), ct);
        var rows = rawRows.Select(x => new WarehouseAssistantGoodsReceiptRow(
            x.HeaderId,
            x.DocumentNo,
            x.DocumentDate,
            x.ReceivedAtUtc,
            x.SupplierId,
            x.SupplierCode,
            x.SupplierName,
            x.WarehouseCode,
            x.WarehouseName,
            x.StockId,
            x.StockCode,
            x.StockName,
            x.YapCode,
            x.UnitCode,
            x.ReceivedQuantity,
            x.AcceptedQuantity,
            x.RejectedQuantity,
            x.QuarantineQuantity,
            x.PutawayQuantity,
            x.HeaderStatus,
            x.QualityStatus,
            x.ErpStatus,
            x.ActorUserId,
            DisplayUser(x.ActorUserId, null, names))).ToArray();

        var supplierLabel = supplier is null
            ? M(GoodsReceiptAllSuppliers)
            : $"{supplier.Code} - {supplier.Name}";
        var totalsLabel = quantityTotals.Count == 0
            ? M(GoodsReceiptNoQuantity)
            : string.Join(", ", quantityTotals.Select(x => $"{x.Quantity:0.###} {x.UnitCode}"));
        var answer = receiptCount == 0
            ? M(GoodsReceiptAnalysisNone, periodLabel, supplierLabel)
            : M(GoodsReceiptAnalysisFound, periodLabel, supplierLabel, receiptCount, lineCount, totalsLabel);
        if (lineCount > MaximumResultCount)
            answer += " " + M(GoodsReceiptResultLimited, MaximumResultCount, lineCount);

        return new ExecutionResult(
            resolution.Intent,
            "authorized-warehouses",
            "query-goods-receipt-analysis",
            answer,
            [], [], [], [], null, [], [],
            new WarehouseAssistantContext(
                null,
                stock?.Id,
                stock?.ErpStockCode,
                SupplierId: supplier?.Id,
                SupplierCode: supplier?.Code,
                SupplierName: supplier?.Name,
                DateFrom: dateFrom,
                DateTo: dateTo),
            [M(CapabilityExampleGoodsReceiptAnalysis)],
            GoodsReceipts: rows);
    }

    private async Task<SupplierMatch?> ResolveSupplierAsync(string? query, string branchCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var normalized = WarehouseAssistantIntentResolver.Normalize(query);
        var customers = await unitOfWork.Repository<CustomerEntity>().Query()
            .Where(x => x.BranchCode == branchCode)
            .Select(x => new SupplierMatch(x.Id, x.CustomerCode, x.CustomerName))
            .Take(5000)
            .ToListAsync(ct);
        var matched = customers
            .Where(x => IsSupplierMentioned(normalized, x.Code, x.Name))
            .OrderByDescending(x => Math.Max(x.Code.Length, x.Name.Length))
            .FirstOrDefault();
        if (matched is not null) return matched;

        var historical = await unitOfWork.Repository<GoodsReceiptHeader>().Query()
            .Where(x => x.BranchCode == branchCode
                && x.SupplierCodeSnapshot != null
                && x.SupplierNameSnapshot != null)
            .Select(x => new { x.SupplierId, Code = x.SupplierCodeSnapshot!, Name = x.SupplierNameSnapshot! })
            .Distinct()
            .Take(1000)
            .ToListAsync(ct);
        return historical
            .Select(x => new SupplierMatch(x.SupplierId, x.Code, x.Name))
            .Where(x => IsSupplierMentioned(normalized, x.Code, x.Name))
            .OrderByDescending(x => Math.Max(x.Code.Length, x.Name.Length))
            .FirstOrDefault();
    }

    private async Task<(DateOnly From, DateOnly To, string Label)> ResolveDocumentDateRangeAsync(
        WarehouseAssistantIntentResolution resolution,
        CancellationToken ct)
    {
        var (startUtc, endUtc, label) = await ResolveDateRangeAsync(
            resolution.DatePreset,
            ct,
            resolution.DateFrom,
            resolution.DateTo);
        var configuredZone = await unitOfWork.Repository<ProjectSetting>().Query()
            .Where(x => x.SettingKey == "GLOBAL")
            .Select(x => x.TimeZoneId)
            .FirstOrDefaultAsync(ct);
        var zone = ResolveTimeZone(configuredZone);
        var from = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc, zone));
        var to = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(endUtc.AddTicks(-1), zone));
        return (from, to, label);
    }

    private static bool IsSupplierMentioned(string normalizedMessage, string code, string name)
    {
        var normalizedCode = WarehouseAssistantIntentResolver.Normalize(code);
        var normalizedName = WarehouseAssistantIntentResolver.Normalize(name);
        return (!string.IsNullOrWhiteSpace(normalizedCode) && normalizedMessage.Contains(normalizedCode, StringComparison.Ordinal))
            || (normalizedName.Length >= 3 && normalizedMessage.Contains(normalizedName, StringComparison.Ordinal));
    }

    private sealed record SupplierMatch(long? Id, string Code, string Name);
}
