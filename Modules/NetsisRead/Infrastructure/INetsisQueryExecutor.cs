using Microsoft.Data.SqlClient;

namespace verii_wms_api_v2.Modules.NetsisRead.Infrastructure;

public interface INetsisQueryExecutor
{
    Task<List<T>> QueryAsync<T>(string operation, string sql, Func<SqlDataReader, T> map, CancellationToken cancellationToken, params SqlParameter[] parameters);
}
