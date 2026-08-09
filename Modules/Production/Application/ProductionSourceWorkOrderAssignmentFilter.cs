using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;

namespace verii_wms_api_v2.Modules.Production.Application;

internal static class ProductionSourceWorkOrderAssignmentFilter
{
    internal static readonly WarehouseTransferBusinessContext[] ProductionContexts =
    [
        WarehouseTransferBusinessContext.ProductionMaterialSupply,
        WarehouseTransferBusinessContext.ProductionWipMove,
        WarehouseTransferBusinessContext.ProductionOutputMove
    ];

    internal static IReadOnlyList<ProductionSourceWorkOrderRow> ExcludeAssigned(
        IReadOnlyList<ProductionSourceWorkOrderRow> rows,
        IReadOnlySet<string> assignedWorkOrderNumbers) =>
        rows.Where(row => !IsAssigned(row.WorkOrderNumber, assignedWorkOrderNumbers)).ToArray();

    internal static bool IsAssigned(string? workOrderNumber, IReadOnlySet<string> assignedWorkOrderNumbers) =>
        !string.IsNullOrWhiteSpace(workOrderNumber)
        && assignedWorkOrderNumbers.Contains(workOrderNumber.Trim());
}
