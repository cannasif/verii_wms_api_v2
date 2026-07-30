using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Api;

[Authorize, ApiController, Route("api/goods-receipts/supplier-stock-mappings")]
public sealed class SupplierStockMappingsController(
    ISupplierStockMappingService service,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpPost("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] string branchCode, PagedRequest request, CancellationToken ct)
    {
        await Require("WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<SupplierStockMappingRow>>.Ok(
            await service.GetPagedAsync(branchCode, request, ct)));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(
        long id, [FromQuery] string branchCode, CancellationToken ct)
    {
        await Require("WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.VIEW", ct);
        return Ok(ApiResponse<SupplierStockMappingRow>.Ok(
            await service.GetAsync(id, branchCode, ct)));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        SaveSupplierStockMappingRequest request, CancellationToken ct)
    {
        await Require("WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.MANAGE", ct);
        return Ok(ApiResponse<SupplierStockMappingRow>.Ok(
            await service.CreateAsync(request, ct),
            "Tedarikçi stok eşlemesi oluşturuldu."));
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(
        long id, SaveSupplierStockMappingRequest request, CancellationToken ct)
    {
        await Require("WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.MANAGE", ct);
        return Ok(ApiResponse<SupplierStockMappingRow>.Ok(
            await service.UpdateAsync(id, request, ct),
            "Tedarikçi stok eşlemesi güncellendi."));
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(
        long id, [FromQuery] string branchCode, CancellationToken ct)
    {
        await Require("WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.MANAGE", ct);
        await service.DeleteAsync(id, branchCode, ct);
        return Ok(ApiResponse<bool>.Ok(true, "Tedarikçi stok eşlemesi silindi."));
    }

    private async Task Require(string permission, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, permission, ct))
            throw AppException.Forbidden();
    }
}
