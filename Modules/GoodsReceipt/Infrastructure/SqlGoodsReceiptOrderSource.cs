using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Infrastructure;

public sealed class SqlGoodsReceiptOrderSource(WmsDbContext dbContext) : IGoodsReceiptOrderSource
{
    public async Task<IReadOnlyList<GoodsReceiptOrderSourceLine>> GetOpenLinesAsync(string orderNumbersCsv, string customerCode, string branchCode, CancellationToken cancellationToken = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        var mustClose = connection.State != ConnectionState.Open;
        if (mustClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandTimeout = 60;
            command.CommandText = "SELECT * FROM dbo.RII_FN_GR_OPENORDERS_LINE(@orders, @customer, @branch)";
            Add(command, "@orders", orderNumbersCsv); Add(command, "@customer", customerCode); Add(command, "@branch", branchCode);
            var result = new List<GoodsReceiptOrderSourceLine>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new GoodsReceiptOrderSourceLine(
                    Text(reader, "SiparisNo")!, Convert.ToInt32(Value(reader, "OrderID")), Text(reader, "StockCode"), Text(reader, "StockName"),
                    Text(reader, "UnitCode"), Text(reader, "YapKod"), Text(reader, "YapAcik"), Text(reader, "CustomerCode"), Text(reader, "CustomerName"),
                    Number<int>(reader, "BranchCode"), Number<int>(reader, "TargetWh"), Number<DateTime>(reader, "OrderDate"),
                    Number<decimal>(reader, "OrderedQty") ?? 0, Number<decimal>(reader, "DeliveredQty") ?? 0,
                    Number<decimal>(reader, "RemainingHamax") ?? 0, Number<decimal>(reader, "PlannedQtyAllocated") ?? 0,
                    Number<decimal>(reader, "RemainingForImport") ?? 0));
            }
            return result;
        }
        finally { if (mustClose && connection.State == ConnectionState.Open) await connection.CloseAsync(); }
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
    }
    private static object Value(System.Data.Common.DbDataReader reader, string name) => reader.GetValue(reader.GetOrdinal(name));
    private static string? Text(System.Data.Common.DbDataReader reader, string name) { var i = reader.GetOrdinal(name); return reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i)); }
    private static T? Number<T>(System.Data.Common.DbDataReader reader, string name) where T : struct { var i = reader.GetOrdinal(name); return reader.IsDBNull(i) ? null : (T)Convert.ChangeType(reader.GetValue(i), typeof(T)); }
}
