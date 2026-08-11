using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.NetsisRead.Application;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.NetsisRead.Api;

[Authorize, ApiController, Route("api/netsis-read")]
public sealed class NetsisReadController(INetsisReadService service, ILogger<NetsisReadController> logger, IPermissionAuthorizationService permissions) : ControllerBase
{
    [AllowAnonymous, HttpGet("branches"), HttpGet("getBranches")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<BranchDto>>>> Branches([FromQuery] int? branchNo, CancellationToken ct) => Execute(() => service.GetBranchesAsync(branchNo, ct));

    [HttpGet("warehouses"), HttpGet("getWarehouses")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WarehouseDto>>>> Warehouses([FromQuery] short? warehouseCode, [FromQuery] int? branchCode, CancellationToken ct) { await Require(ct); return await Execute(() => service.GetWarehousesAsync(warehouseCode, branchCode, ct)); }

    [HttpGet("stocks"), HttpGet("getStocks")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StockDto>>>> Stocks([FromQuery] string? stockCode, [FromQuery] int? branchCode, CancellationToken ct) { await Require(ct); return await Execute(() => service.GetStocksAsync(stockCode, branchCode, ct)); }

    /// <summary>
    /// Reads Netsis warehouse/stock balances. Omitting both filters returns the complete function result.
    /// </summary>
    [HttpGet("stock-balances")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NetsisStockBalanceDto>>>> StockBalances(
        [FromQuery] short? warehouseCode,
        [FromQuery] string? stockCode,
        CancellationToken ct)
    {
        await Require(ct);
        return await Execute(() => service.GetStockBalancesAsync(warehouseCode, stockCode, ct));
    }

    [HttpGet("customers"), HttpGet("getCustomers")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CustomerDto>>>> Customers([FromQuery] string? customerCode, [FromQuery] int? branchCode, CancellationToken ct) { await Require(ct); return await Execute(() => service.GetCustomersAsync(customerCode, branchCode, ct)); }

    [HttpGet("configuration-codes")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ConfigurationCodeDto>>>> ConfigurationCodes([FromQuery] string? search, [FromQuery] int? branchCode, CancellationToken ct)
    {
        await Require(ct);
        return await Execute(() => service.GetConfigurationCodesAsync(search, branchCode, ct));
    }

    [Obsolete("Use GET /api/netsis-read/configuration-codes.")]
    [HttpGet("yap-codes")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LegacyYapCodeDto>>>> LegacyYapCodes([FromQuery] string? search, [FromQuery] int? branchCode, CancellationToken ct)
    {
        await Require(ct);
        return await Execute(async () =>
        {
            var rows = await service.GetConfigurationCodesAsync(search, branchCode, ct);
            return (IReadOnlyList<LegacyYapCodeDto>)rows
                .Select(x => new LegacyYapCodeDto(x.ConfigurationCode, x.Description, x.BranchCode, x.ConfigurableStockCode, x.StockId))
                .ToList();
        });
    }

    [HttpGet("goods-receipt/open-orders/headers")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<GoodsReceiptOpenOrderHeaderDto>>>> GoodsReceiptOpenOrderHeaders([FromQuery] string customerCode, [FromQuery] string? branchCode, CancellationToken ct) { await Require(ct); return await Execute(() => service.GetGoodsReceiptOpenOrderHeadersAsync(customerCode, branchCode, ct)); }

    [HttpGet("goods-receipt/open-orders/lines")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<GoodsReceiptOpenOrderLineDto>>>> GoodsReceiptOpenOrderLines(
        [FromQuery] string? orderNumbersCsv,
        [FromQuery] string? customerCode,
        [FromQuery] string? branchCode,
        [FromQuery] bool includeUnavailable,
        CancellationToken ct)
    {
        await Require(ct);
        return await Execute(() => service.GetGoodsReceiptOpenOrderLinesAsync(
            orderNumbersCsv, customerCode, branchCode, includeUnavailable, ct));
    }

    [HttpGet("warehouse-transfer/open-orders/headers")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WarehouseTransferOpenOrderHeaderDto>>>> WarehouseTransferOpenOrderHeaders([FromQuery]string customerCode,[FromQuery]string? branchCode,CancellationToken ct){await Require(ct);return await Execute(()=>service.GetWarehouseTransferOpenOrderHeadersAsync(customerCode,branchCode,ct));}

    [HttpGet("warehouse-transfer/open-orders/lines")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WarehouseTransferOpenOrderLineDto>>>> WarehouseTransferOpenOrderLines([FromQuery]string orderNumbersCsv,[FromQuery]string? branchCode,CancellationToken ct){await Require(ct);return await Execute(()=>service.GetWarehouseTransferOpenOrderLinesAsync(orderNumbersCsv,branchCode,ct));}

    [HttpGet("shipping/open-orders/headers")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ShipmentOpenOrderHeaderDto>>>> ShipmentOpenOrderHeaders([FromQuery]string customerCode,[FromQuery]string? branchCode,CancellationToken ct){await Require(ct);return await Execute(()=>service.GetShipmentOpenOrderHeadersAsync(customerCode,branchCode,ct));}

    [HttpGet("shipping/open-orders/lines")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ShipmentOpenOrderLineDto>>>> ShipmentOpenOrderLines([FromQuery]string orderNumbersCsv,[FromQuery]string? branchCode,CancellationToken ct){await Require(ct);return await Execute(()=>service.GetShipmentOpenOrderLinesAsync(orderNumbersCsv,branchCode,ct));}

    [HttpGet("production/work-orders")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProductionWorkOrderDto>>>> ProductionWorkOrders(
        [FromQuery] string? workOrderNumber,
        [FromQuery] bool includeClosed = false,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        await Require(ct);
        return await Execute(() => service.GetProductionWorkOrdersAsync(workOrderNumber, CurrentBranchCode(), includeClosed, take, ct));
    }

    [HttpGet("production/stocks/{stockCode}/recipe")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StockRecipeComponentDto>>>> StockRecipe(
        string stockCode,
        [FromQuery] string? configurationCode,
        CancellationToken ct)
    {
        await Require(ct);
        return await Execute(() => service.GetStockRecipeAsync(stockCode, CurrentBranchCode(), configurationCode, ct));
    }

    [HttpGet("production/work-orders/{workOrderNumber}/recipe")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProductionWorkOrderRecipeComponentDto>>>> ProductionWorkOrderRecipe(
        string workOrderNumber,
        CancellationToken ct)
    {
        await Require(ct);
        return await Execute(() => service.GetProductionWorkOrderRecipeAsync(workOrderNumber, CurrentBranchCode(), ct));
    }

    private async Task Require(CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, "ERP.NETSIS_READ.VIEW", ct)) throw AppException.Forbidden();
    }

    private int CurrentBranchCode()
    {
        var value = User.FindFirstValue(JwtTokenIssuer.BranchCodeClaim)?.Trim();
        return int.TryParse(value, out var branchCode)
            ? branchCode
            : throw AppException.Unauthorized("Oturum şube bilgisi bulunamadı.");
    }

    private async Task<ActionResult<ApiResponse<T>>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(ApiResponse<T>.Ok(await action())); }
        catch (ArgumentException ex) { return BadRequest(ApiResponse<T>.Error(ex.Message)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "NetsisRead endpoint failed");
            return StatusCode(500, ApiResponse<T>.Error("ERP verisi okunurken bir hata oluştu."));
        }
    }
}
