using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Production.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal static class ProductionTransferOverIssueSupport
{
    internal static async Task<ProductionTransferPolicy> LoadPolicyAsync(
        IUnitOfWork uow,
        string branchCode,
        CancellationToken ct)
    {
        var branch = string.IsNullOrWhiteSpace(branchCode) ? "0" : branchCode.Trim();
        return await uow.Repository<ProductionTransferPolicy>().Query()
                   .FirstOrDefaultAsync(x => x.BranchCode == branch && x.PolicyKey == "DEFAULT", ct)
               ?? new ProductionTransferPolicy { BranchCode = branch, PolicyKey = "DEFAULT" };
    }

    internal static decimal GetMaxPickQuantity(WarehouseTransferLine line, ProductionTransferPolicy policy) =>
        policy.AllowOverIssue
            ? line.RequestedQuantity * (1 + policy.OverIssueTolerancePercent / 100m)
            : line.RequestedQuantity;

    internal static decimal GetRemainingPickCapacity(WarehouseTransferLine line, ProductionTransferPolicy policy)
    {
        var picked = ProductionWorkOrderMaterialAssignment.ResolveEffectivePickedQuantity(line);
        return Math.Max(0, GetMaxPickQuantity(line, policy) - picked);
    }

    internal static decimal GetOverIssueQuantity(WarehouseTransferLine line)
    {
        var picked = ProductionWorkOrderMaterialAssignment.ResolveEffectivePickedQuantity(line);
        return Math.Max(0, picked - line.RequestedQuantity);
    }

    internal static IReadOnlyList<ProductionTransferOverIssueLineDto> BuildOverIssueLines(
        IEnumerable<WarehouseTransferLine> lines) =>
        lines
            .Select(line => new
            {
                Line = line,
                Picked = ProductionWorkOrderMaterialAssignment.ResolveEffectivePickedQuantity(line),
            })
            .Select(x => new
            {
                x.Line,
                x.Picked,
                OverIssue = Math.Max(0, x.Picked - x.Line.RequestedQuantity),
            })
            .Where(x => x.OverIssue > 0.000001m)
            .OrderBy(x => x.Line.LineNo)
            .Select(x => new ProductionTransferOverIssueLineDto(
                x.Line.Id,
                x.Line.LineNo,
                x.Line.StockCodeSnapshot,
                x.Line.StockNameSnapshot,
                x.Line.UnitCode,
                x.Line.RequestedQuantity,
                x.Picked,
                x.OverIssue))
            .ToArray();
}
