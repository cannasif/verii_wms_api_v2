using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace verii_wms_api_v2.Modules.NetsisRead.Infrastructure;

public sealed class NetsisQueryExecutor(IConfiguration configuration, ILogger<NetsisQueryExecutor> logger) : INetsisQueryExecutor
{
    public async Task<List<T>> QueryAsync<T>(string operation, string sql, Func<SqlDataReader, T> map, CancellationToken cancellationToken, params SqlParameter[] parameters)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("DefaultConnection is not configured.");
        var timer = Stopwatch.StartNew();
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 60;
            command.Parameters.AddRange(parameters);
            var result = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(map(reader));
            logger.LogInformation("Netsis read completed. Operation={Operation} RowCount={RowCount} DurationMs={DurationMs}", operation, result.Count, timer.ElapsedMilliseconds);
            return result;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Netsis read failed. Operation={Operation} DurationMs={DurationMs}", operation, timer.ElapsedMilliseconds);
            throw;
        }
    }
}
