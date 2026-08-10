using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal static class ProductionTransferLocationPolicy
{
    internal static long ResolveHandoverTargetLocationId(
        WarehouseTransferHeader header,
        WarehouseTransferLine line)
    {
        if (header.SourceWarehouseId != header.TargetWarehouseId)
        {
            return header.TargetPutawayLocationId
                ?? throw AppException.Conflict($"{line.LineNo}. kalem için üretim hedef rafı bulunamadı.");
        }

        return line.DefaultTargetLocationId ?? header.TargetPutawayLocationId
            ?? throw AppException.Conflict($"{line.LineNo}. kalem için üretim hedef rafı bulunamadı.");
    }
}
