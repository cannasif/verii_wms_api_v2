using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Modules.VehicleCheckIn.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using static verii_wms_api_v2.Modules.WarehouseAssistant.Localization.WarehouseAssistantMessageKeys;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

public sealed partial class WarehouseAssistantService
{
    private static readonly WarehouseTransferBusinessContext[] ProductionTransferContexts =
    [
        WarehouseTransferBusinessContext.ProductionMaterialSupply,
        WarehouseTransferBusinessContext.ProductionWipMove,
        WarehouseTransferBusinessContext.ProductionOutputMove
    ];

    private async Task<ExecutionResult> ExecuteSteelVehicleAnalysisAsync(
        WarehouseAssistantIntentResolution resolution,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewSteelVehicles)
            return Denied(resolution.Intent, M(SteelVehicleAnalysisDenied));

        var normalizedPlate = NormalizePlateQuery(resolution.VehiclePlateQuery);
        var isPlateHistoryLookup = !string.IsNullOrWhiteSpace(normalizedPlate)
            && !resolution.HasExplicitDateFilter;
        DateTimeOffset start = default;
        DateTimeOffset end = default;
        var periodLabel = string.Empty;
        if (!isPlateHistoryLookup)
        {
            var range = await ResolveDateRangeAsync(
                resolution.DatePreset, ct, resolution.DateFrom, resolution.DateTo);
            start = new DateTimeOffset(range.StartUtc, TimeSpan.Zero);
            end = new DateTimeOffset(range.EndUtc, TimeSpan.Zero);
            periodLabel = range.Label;
        }
        var vehicles = unitOfWork.Repository<VehicleCheckInHeader>().Query()
            .Where(x => x.BranchCode == branchCode);
        if (!isPlateHistoryLookup)
            vehicles = vehicles.Where(x => x.CheckedInAtUtc >= start && x.CheckedInAtUtc < end);

        if (!string.IsNullOrWhiteSpace(normalizedPlate))
            vehicles = vehicles.Where(x => x.PlateNoNormalized.Contains(normalizedPlate));

        var vehicleCount = await vehicles.CountAsync(ct);
        var declaredSheetCount = vehicleCount == 0
            ? 0
            : await vehicles.SumAsync(x => x.SteelSheetCount, ct);
        var vehicleIds = vehicles.Select(x => x.Id);
        var acceptedPlates = unitOfWork.Repository<SteelVehicleAcceptedPlate>().Query()
            .Where(x => vehicleIds.Contains(x.VehicleCheckInId));
        var acceptedPlateCount = await acceptedPlates.CountAsync(ct);
        var unresolvedPlateCount = await acceptedPlates.CountAsync(
            x => x.IdentityStatus == SteelPlateIdentityStatus.Unknown, ct);

        var rows = await vehicles
            .OrderByDescending(x => x.CheckedInAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new WarehouseAssistantSteelVehicleRow(
                x.Id,
                x.PlateNo,
                x.TrailerPlateNo,
                ((x.DriverFirstName ?? "") + " " + (x.DriverLastName ?? "")).Trim(),
                x.CarrierName,
                x.SteelSheetCount,
                unitOfWork.Repository<SteelVehicleAcceptedPlate>().Query()
                    .Count(p => p.VehicleCheckInId == x.Id),
                unitOfWork.Repository<SteelVehicleAcceptedPlate>().Query()
                    .Count(p => p.VehicleCheckInId == x.Id && p.IdentityStatus == SteelPlateIdentityStatus.Unknown),
                x.Status.ToString(),
                x.CheckedInAtUtc,
                x.BusinessDate,
                x.CustomerCodeSnapshot,
                x.CustomerNameSnapshot))
            .Take(MaximumResultCount)
            .ToListAsync(ct);

        var plateLabel = string.IsNullOrWhiteSpace(resolution.VehiclePlateQuery)
            ? M(SteelVehicleAllPlates)
            : resolution.VehiclePlateQuery!.Trim();
        var answer = isPlateHistoryLookup
            ? vehicleCount == 0
                ? M(SteelVehicleHistoryNone, plateLabel)
                : M(SteelVehicleHistoryFound, plateLabel, vehicleCount, declaredSheetCount, acceptedPlateCount, unresolvedPlateCount)
            : vehicleCount == 0
                ? M(SteelVehicleAnalysisNone, periodLabel, plateLabel)
                : M(SteelVehicleAnalysisFound, periodLabel, plateLabel, vehicleCount, declaredSheetCount, acceptedPlateCount, unresolvedPlateCount);
        if (vehicleCount > rows.Count)
            answer += " " + M(SteelVehicleResultLimited, MaximumResultCount, vehicleCount);

        return new ExecutionResult(
            resolution.Intent,
            "authorized",
            "query-steel-vehicle-analysis",
            answer,
            [], [], [], [], null, [], [],
            new WarehouseAssistantContext(
                null, null, null,
                DateFrom: resolution.DateFrom,
                DateTo: resolution.DateTo,
                VehiclePlate: resolution.VehiclePlateQuery),
            [M(CapabilityExampleSteelVehicles), M(SuggestionSteelVehicleByPlate)],
            SteelVehicles: rows);
    }

    private async Task<ExecutionResult> ExecuteWarehouseTransferAnalysisAsync(
        WarehouseAssistantIntentResolution resolution,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (resolution.TransferScope == WarehouseAssistantTransferScope.Production && !access.CanViewProductionTransfers)
            return Denied(resolution.Intent, M(ProductionTransferAnalysisDenied));
        if (resolution.TransferScope == WarehouseAssistantTransferScope.InterWarehouse && !access.CanViewWarehouseTransfers)
            return Denied(resolution.Intent, M(WarehouseTransferAnalysisDenied));
        if (resolution.TransferScope == WarehouseAssistantTransferScope.All
            && !access.CanViewWarehouseTransfers
            && !access.CanViewProductionTransfers)
            return Denied(resolution.Intent, M(TransferAnalysisDenied));

        var isDocumentLookup = !string.IsNullOrWhiteSpace(resolution.TransferDocumentQuery)
            && !resolution.HasExplicitDateFilter;
        DateOnly fromDate = default;
        DateOnly toDate = default;
        string periodLabel = string.Empty;
        if (!isDocumentLookup)
            (fromDate, toDate, periodLabel) = await ResolveDocumentDateRangeAsync(resolution, ct);
        var warehouseAccess = await UserWarehouseAccessService.ResolveAsync(unitOfWork, actorUserId, branchCode, ct);
        var headers = unitOfWork.Repository<WarehouseTransferHeader>().Query()
            .Where(x => x.BranchCode == branchCode);
        if (!isDocumentLookup)
            headers = headers.Where(x => x.DocumentDate >= fromDate && x.DocumentDate <= toDate);

        headers = resolution.TransferScope switch
        {
            WarehouseAssistantTransferScope.Production => headers.Where(x => ProductionTransferContexts.Contains(x.BusinessContext)),
            WarehouseAssistantTransferScope.InterWarehouse => headers.Where(x => x.BusinessContext == WarehouseTransferBusinessContext.InterWarehouse),
            _ => headers.Where(x =>
                (ProductionTransferContexts.Contains(x.BusinessContext) && access.CanViewProductionTransfers)
                || (!ProductionTransferContexts.Contains(x.BusinessContext) && access.CanViewWarehouseTransfers))
        };

        if (warehouseAccess.IsRestricted)
            headers = headers.Where(x => warehouseAccess.WarehouseIds.Contains(x.SourceWarehouseId)
                || warehouseAccess.WarehouseIds.Contains(x.TargetWarehouseId));

        if (!string.IsNullOrWhiteSpace(resolution.TransferDocumentQuery))
        {
            var documentQuery = resolution.TransferDocumentQuery.Trim().ToUpper();
            headers = headers.Where(x => x.DocumentNo.ToUpper().Contains(documentQuery)
                || (x.ExternalReferenceNo != null && x.ExternalReferenceNo.ToUpper().Contains(documentQuery)));
        }

        var totalDocuments = await headers.CountAsync(ct);
        var selectedHeaders = await (from header in headers
                                     join source in unitOfWork.Repository<WarehouseEntity>().Query() on header.SourceWarehouseId equals source.Id
                                     join target in unitOfWork.Repository<WarehouseEntity>().Query() on header.TargetWarehouseId equals target.Id
                                     orderby header.DocumentDate descending, header.Id descending
                                     select new
                                     {
                                         Header = header,
                                         SourceCode = source.WarehouseCode,
                                         SourceName = source.WarehouseName,
                                         TargetCode = target.WarehouseCode,
                                         TargetName = target.WarehouseName
                                     })
            .Take(MaximumResultCount)
            .ToListAsync(ct);

        var selectedIds = selectedHeaders.Select(x => x.Header.Id).ToArray();
        var selectedLineSummaries = await unitOfWork.Repository<WarehouseTransferLine>().Query()
            .Where(x => selectedIds.Contains(x.WtHeaderId))
            .GroupBy(x => new { x.WtHeaderId, x.UnitCode })
            .Select(x => new
            {
                x.Key.WtHeaderId,
                x.Key.UnitCode,
                LineCount = x.Count(),
                Requested = x.Sum(line => line.RequestedQuantity),
                Picked = x.Sum(line => line.PickedQuantity),
                Shipped = x.Sum(line => line.ShippedQuantity),
                Received = x.Sum(line => line.ReceivedQuantity),
                Putaway = x.Sum(line => line.PutawayQuantity),
                ShortClosed = x.Sum(line => line.ShortClosedQuantity)
            })
            .ToListAsync(ct);

        var rows = selectedHeaders.SelectMany(x =>
        {
            var summaries = selectedLineSummaries.Where(line => line.WtHeaderId == x.Header.Id).ToArray();
            if (summaries.Length == 0)
                return [CreateTransferRow(x.Header, x.SourceCode, x.SourceName, x.TargetCode, x.TargetName, "", 0, 0, 0, 0, 0, 0, 0)];
            return summaries.Select(line => CreateTransferRow(
                x.Header, x.SourceCode, x.SourceName, x.TargetCode, x.TargetName,
                line.UnitCode, line.LineCount, line.Requested, line.Picked, line.Shipped,
                line.Received, line.Putaway, line.ShortClosed));
        }).ToArray();

        var matchingIds = headers.Select(x => x.Id);
        var totalsByUnit = await unitOfWork.Repository<WarehouseTransferLine>().Query()
            .Where(x => matchingIds.Contains(x.WtHeaderId))
            .GroupBy(x => x.UnitCode)
            .Select(x => new
            {
                UnitCode = x.Key,
                Requested = x.Sum(line => line.RequestedQuantity),
                Picked = x.Sum(line => line.PickedQuantity),
                Received = x.Sum(line => line.ReceivedQuantity),
                ShortClosed = x.Sum(line => line.ShortClosedQuantity)
            })
            .ToListAsync(ct);
        var totalsLabel = totalsByUnit.Count == 0
            ? M(TransferNoQuantity)
            : string.Join("; ", totalsByUnit.Select(x => $"{x.UnitCode}: {x.Requested:0.###}/{x.Picked:0.###}/{x.Received:0.###}/{x.ShortClosed:0.###}"));
        var scopeLabel = resolution.TransferScope switch
        {
            WarehouseAssistantTransferScope.Production => M(TransferScopeProduction),
            WarehouseAssistantTransferScope.InterWarehouse => M(TransferScopeInterWarehouse),
            _ => M(TransferScopeAll)
        };
        var answer = isDocumentLookup
            ? totalDocuments == 0
                ? M(TransferDocumentAnalysisNone, resolution.TransferDocumentQuery!.Trim(), scopeLabel)
                : M(TransferDocumentAnalysisFound, resolution.TransferDocumentQuery!.Trim(), scopeLabel, totalDocuments, totalsLabel)
            : totalDocuments == 0
                ? M(TransferAnalysisNone, periodLabel, scopeLabel)
                : M(TransferAnalysisFound, periodLabel, scopeLabel, totalDocuments, totalsLabel);
        if (totalDocuments > selectedHeaders.Count)
            answer += " " + M(TransferResultLimited, MaximumResultCount, totalDocuments);

        return new ExecutionResult(
            resolution.Intent,
            "authorized-warehouses",
            "query-warehouse-transfer-analysis",
            answer,
            [], [], [], [], null, [], [],
            new WarehouseAssistantContext(
                null, null, null,
                DateFrom: resolution.DateFrom,
                DateTo: resolution.DateTo,
                TransferDocumentNo: resolution.TransferDocumentQuery,
                TransferScope: resolution.TransferScope),
            [M(CapabilityExampleWarehouseTransfers), M(CapabilityExampleProductionTransfers)],
            Transfers: rows);
    }

    private static WarehouseAssistantTransferRow CreateTransferRow(
        WarehouseTransferHeader header,
        int sourceWarehouseCode,
        string sourceWarehouseName,
        int targetWarehouseCode,
        string targetWarehouseName,
        string unitCode,
        int lineCount,
        decimal requested,
        decimal picked,
        decimal shipped,
        decimal received,
        decimal putaway,
        decimal shortClosed) => new(
            header.Id,
            header.DocumentNo,
            header.DocumentDate,
            header.BusinessContext.ToString(),
            sourceWarehouseCode,
            sourceWarehouseName,
            targetWarehouseCode,
            targetWarehouseName,
            header.Status.ToString(),
            header.ApprovalStatus.ToString(),
            header.ErpIntegrationStatus.ToString(),
            lineCount,
            unitCode,
            requested,
            picked,
            shipped,
            received,
            putaway,
            shortClosed,
            header.ExternalReferenceNo,
            header.CompletedAtUtc);

    private static string NormalizePlateQuery(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
