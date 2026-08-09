using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.ProjectSettings.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
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

        EntityLookupResult<SupplierMatch>? supplierLookup = null;
        SupplierMatch? supplier = null;
        if (!string.IsNullOrWhiteSpace(resolution.SupplierQuery)
            && HasExplicitEntityReference(message, EntityKind.Customer))
        {
            supplierLookup = await ResolveSupplierAsync(resolution.SupplierQuery, message, branchCode, ct);
            supplier = supplierLookup.Entity;
        }
        if (supplierLookup is not null && supplier is null)
            return string.IsNullOrWhiteSpace(supplierLookup.SearchTerm)
                ? MissingEntity(resolution.Intent, M(GoodsReceiptSupplierRequired))
                : EntityClarification(resolution.Intent, supplierLookup.SearchTerm, supplierLookup.Candidates);

        EntityLookupResult<verii_wms_api_v2.Modules.Stock.Domain.Stock>? stockLookup = null;
        verii_wms_api_v2.Modules.Stock.Domain.Stock? stock = null;
        if (!string.IsNullOrWhiteSpace(resolution.StockQuery)
            && HasExplicitEntityReference(message, EntityKind.Stock))
        {
            stockLookup = await ResolveStockAsync(resolution.StockQuery, message, branchCode, ct);
            stock = stockLookup.Entity;
        }
        if (stockLookup is not null && stock is null)
            return EntityClarification(resolution.Intent, stockLookup.SearchTerm, stockLookup.Candidates);

        if (supplierLookup is null && stockLookup is null)
        {
            var untypedReference = ExtractUntypedEntityReference(message);
            if (!string.IsNullOrWhiteSpace(untypedReference))
            {
                var possibleStock = await ResolveStockAsync(untypedReference, untypedReference, branchCode, ct);
                var possibleSupplier = await ResolveSupplierAsync(untypedReference, untypedReference, branchCode, ct);
                var candidates = new List<WarehouseAssistantEntityCandidateRow>();
                if (possibleStock.Entity is not null)
                    candidates.Add(ToExactCandidate(EntityKind.Stock, possibleStock.Entity.Id, possibleStock.Entity.ErpStockCode, possibleStock.Entity.StockName, message));
                else
                    candidates.AddRange(possibleStock.Candidates.Select(x => x with
                    {
                        SelectionMessage = BuildEntitySelectionMessage(EntityKind.Stock, message, x.Code)
                    }));
                if (possibleSupplier.Entity is not null)
                    candidates.Add(ToExactCandidate(EntityKind.Customer, possibleSupplier.Entity.Id, possibleSupplier.Entity.Code, possibleSupplier.Entity.Name, message));
                else
                    candidates.AddRange(possibleSupplier.Candidates.Select(x => x with
                    {
                        SelectionMessage = BuildEntitySelectionMessage(EntityKind.Customer, message, x.Code)
                    }));
                if (candidates.Count > 0)
                    return EntityClarification(
                        resolution.Intent,
                        untypedReference,
                        candidates
                            .OrderByDescending(x => x.MatchScore)
                            .ThenBy(x => x.EntityType)
                            .Take(MaximumEntitySuggestions)
                            .ToArray());
            }
        }
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

    private sealed record SupplierMatch(long? Id, string Code, string Name);
}
