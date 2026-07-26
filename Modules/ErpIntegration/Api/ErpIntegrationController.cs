using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ErpIntegration.Api;

[Authorize, ApiController]
public sealed class ErpIntegrationController(
    IErpPostingService postingService,
    INetsisTokenService tokenService,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpPost("api/goods-receipts/{id:long}/erp/post")]
    public async Task<IActionResult> PostGoodsReceipt(
        long id,
        ErpPostRequest request,
        CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.ERP_RETRY", cancellationToken);
        var result = await postingService.PostGoodsReceiptAsync(
            id, request.IdempotencyKey, CurrentUserId(), cancellationToken);
        return Ok(ApiResponse<ErpPostingResult>.Ok(result, ResolveMessage(result)));
    }

    [HttpPost("api/warehouse-transfers/{id:long}/erp/post")]
    public async Task<IActionResult> PostWarehouseTransfer(
        long id,
        ErpPostRequest request,
        CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.OPERATE", cancellationToken);
        var result = await postingService.PostWarehouseTransferAsync(
            id, request.IdempotencyKey, CurrentUserId(), cancellationToken);
        return Ok(ApiResponse<ErpPostingResult>.Ok(result, ResolveMessage(result)));
    }

    [HttpPost("api/shipments/{id:long}/erp/post")]
    public async Task<IActionResult> PostShipment(
        long id,
        ErpPostRequest request,
        CancellationToken cancellationToken)
    {
        await Require("WMS.SHIPPING.OPERATE", cancellationToken);
        var result = await postingService.PostShipmentAsync(
            id, request.IdempotencyKey, CurrentUserId(), cancellationToken);
        return Ok(ApiResponse<ErpPostingResult>.Ok(result, ResolveMessage(result)));
    }

    [HttpGet("api/erp-postings/{sourceType}/{sourceEntityId:long}")]
    public async Task<IActionResult> Get(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        CancellationToken cancellationToken)
    {
        await RequireViewPermission(sourceType, cancellationToken);
        return Ok(ApiResponse<ErpPostingResult>.Ok(
            await postingService.GetAsync(sourceType, sourceEntityId, cancellationToken)));
    }

    [HttpPost("api/erp-integration/test-login")]
    public async Task<IActionResult> TestLogin(CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.SETTINGS.MANAGE", cancellationToken);
        await tokenService.GetAccessTokenAsync(true, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, "Netsis REST oturumu başarıyla açıldı."));
    }

    private async Task RequireViewPermission(
        ErpPostingSourceType sourceType,
        CancellationToken cancellationToken)
    {
        var code = sourceType switch
        {
            ErpPostingSourceType.GoodsReceipt => "WMS.GOODS_RECEIPT.VIEW",
            ErpPostingSourceType.WarehouseTransfer => "WMS.WAREHOUSE_TRANSFER.VIEW",
            ErpPostingSourceType.Shipment => "WMS.SHIPPING.VIEW",
            _ => throw AppException.BadRequest("Desteklenmeyen ERP kaynak tipi.")
        };
        await Require(code, cancellationToken);
    }

    private async Task Require(string permissionCode, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(User, permissionCode, cancellationToken))
            throw AppException.Forbidden();
    }

    private long CurrentUserId() =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");

    private static string ResolveMessage(ErpPostingResult result) => result.Status switch
    {
        ErpPostingStatus.Succeeded => "ERP belgesi başarıyla oluşturuldu.",
        ErpPostingStatus.CommitUncertain => "ERP yanıtı kesinleşmedi; tekrar gönderim güvenlik amacıyla durduruldu.",
        _ => "ERP belge gönderimi tamamlanamadı."
    };
}
