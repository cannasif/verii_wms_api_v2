using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Application;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.SerialNumberPolicy.Infrastructure;

/// <summary>
/// Reserves a contiguous sequence range with a single SQL statement. This prevents
/// two warehouse terminals from receiving the same sequence under concurrency.
/// </summary>
public sealed class SqlServerSerialSequenceAllocator(WmsDbContext context) : ISerialSequenceAllocator
{
    public async Task<long> AllocateAsync(long ruleId, int count, CancellationToken ct = default)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            UPDATE dbo.RII_SERIAL_NUMBER_RULES WITH (UPDLOCK, ROWLOCK)
            SET NextSequence = NextSequence + @Count
            OUTPUT DELETED.NextSequence
            WHERE Id = @RuleId AND IsDeleted = 0 AND IsActive = 1;
            """;
        var ruleParameter = command.CreateParameter();
        ruleParameter.ParameterName = "@RuleId";
        ruleParameter.Value = ruleId;
        command.Parameters.Add(ruleParameter);
        var countParameter = command.CreateParameter();
        countParameter.ParameterName = "@Count";
        countParameter.Value = count;
        command.Parameters.Add(countParameter);

        var result = await command.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull)
            throw AppException.Conflict("Aktif seri kuralı bulunamadı veya eşzamanlı olarak değiştirildi.");
        return Convert.ToInt64(result);
    }
}
