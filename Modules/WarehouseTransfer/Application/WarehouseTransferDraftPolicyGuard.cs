using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.WarehouseTransfer.Application;

internal static class WarehouseTransferDraftPolicyGuard
{
    public static void Validate(CreateWarehouseTransferDraftRequest request, WarehouseTransferDraftPolicyContext policy)
    {
        if(policy.ValidateInitiationMode)
        {
            var allowed=request.InitiationMode switch
            {
                WarehouseTransferInitiationMode.OrderBasedTask=>policy.AllowOrderBasedTask,
                WarehouseTransferInitiationMode.StockBasedTask=>policy.AllowStockBasedTask,
                WarehouseTransferInitiationMode.DirectTransfer=>policy.AllowStockBasedDirect,
                WarehouseTransferInitiationMode.OrderBasedDirectTransfer=>policy.AllowOrderBasedDirect,
                _=>false
            };
            if(!allowed)
                throw AppException.BadRequest("Seçilen sipariş/emir kombinasyonu transfer politikasında kapalıdır.");
        }

        if(policy.RequireSourceLocation&&!request.AutoAssignSources&&request.Lines.Any(x=>!x.DefaultSourceLocationId.HasValue))
            throw AppException.BadRequest("Transfer politikası kaynak rafı kalem bazında zorunlu tutuyor.");
        if(policy.RequireTargetLocation&&request.Lines.Any(x=>!x.DefaultTargetLocationId.HasValue))
            throw AppException.BadRequest("Transfer politikası hedef rafı kalem bazında zorunlu tutuyor.");

        foreach(var line in request.Lines.Where(x=>x.Source is not null))
        {
            var source=line.Source!;
            if(string.IsNullOrWhiteSpace(source.OrderNumber)||string.IsNullOrWhiteSpace(source.ExternalLineId)||string.IsNullOrWhiteSpace(source.ExternalStockCode))
                throw AppException.BadRequest("Sipariş kaynak belge, satır ve stok bilgisi zorunludur.");
            if(line.Quantity>source.AvailableQuantity)
                throw AppException.BadRequest($"{source.OrderNumber}/{source.ExternalLineId} için miktar açık miktarı aşamaz.");
        }
    }
}
