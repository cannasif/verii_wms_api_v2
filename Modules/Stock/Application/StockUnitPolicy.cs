using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.Stock.Application;

public static class StockUnitPolicy
{
    public static string Resolve(StockEntity stock, string? suppliedUnitCode = null)
    {
        var authoritativeUnit = Normalize(stock.BaseUnitCode);
        if (authoritativeUnit is null)
            throw AppException.Conflict(
                $"{stock.ErpStockCode} stok kartının ölçü birimi tanımlı değil. ERP stok eşitlemesini çalıştırın.");

        var suppliedUnit = Normalize(suppliedUnitCode);
        if (suppliedUnit is not null
            && !string.Equals(suppliedUnit, authoritativeUnit, StringComparison.Ordinal))
        {
            throw AppException.Conflict(
                $"{stock.ErpStockCode} için birim '{authoritativeUnit}' olmalıdır. İstemciden '{suppliedUnit}' gönderilemez.");
        }

        return authoritativeUnit;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
