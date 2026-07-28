using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.DocumentSeries.Localization;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.DocumentSeries.Infrastructure;

public sealed class SqlServerDocumentNumberAllocator(
    WmsDbContext dbContext,
    IStringLocalizer<DocumentSeriesResource> localizer) : IDocumentNumberAllocator
{
    public async Task<AllocatedDocumentNumber> AllocateAsync(long documentSeriesId, WmsDocumentType expectedDocumentType, DateTime? issuedAt = null, CancellationToken cancellationToken = default)
    {
        var timestamp = issuedAt?.ToUniversalTime() ?? DateTime.UtcNow;
        var connection = dbContext.Database.GetDbConnection();
        var mustClose = connection.State != ConnectionState.Open;
        if (mustClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                UPDATE [RII_DOCUMENT_SERIES] WITH (UPDLOCK, ROWLOCK)
                SET [NextNumber] = [NextNumber] + [IncrementBy],
                    [HasIssuedNumbers] = CAST(1 AS bit),
                    [LastIssuedAt] = @issuedAt,
                    [UpdatedDate] = @issuedAt
                OUTPUT DELETED.[NextNumber], INSERTED.[DocumentType], INSERTED.[Prefix],
                       INSERTED.[YearFormat], INSERTED.[NumberLength]
                WHERE [Id] = @id AND [IsDeleted] = CAST(0 AS bit) AND [IsActive] = CAST(1 AS bit)
                  AND [DocumentType] = @documentType
                  AND LEN(CONVERT(varchar(20), [NextNumber])) <= [NumberLength];
                """;
            AddParameter(command, "@id", documentSeriesId, DbType.Int64);
            AddParameter(command, "@issuedAt", timestamp, DbType.DateTime2);
            AddParameter(command, "@documentType", expectedDocumentType.ToString(), DbType.String);

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.DisposeAsync();
                var exists = await dbContext.DocumentSeries.AsNoTracking().IgnoreQueryFilters()
                    .Where(x => x.Id == documentSeriesId)
                    .Select(x => new { x.IsDeleted, x.IsActive, x.DocumentType, x.NextNumber, x.NumberLength })
                    .FirstOrDefaultAsync(cancellationToken);
                if (exists is null || exists.IsDeleted) throw AppException.NotFound(localizer[DocumentSeriesMessageKeys.NotFound].Value);
                if (!exists.IsActive) throw AppException.Conflict(localizer[DocumentSeriesMessageKeys.InactiveSeries].Value);
                if (exists.NextNumber.ToString(System.Globalization.CultureInfo.InvariantCulture).Length > exists.NumberLength)
                    throw AppException.Conflict(localizer[DocumentSeriesMessageKeys.SequenceExhausted].Value);
                throw AppException.Conflict(localizer[DocumentSeriesMessageKeys.DocumentTypeMismatch].Value);
            }

            var sequenceNumber = reader.GetInt64(0);
            var documentType = Enum.Parse<WmsDocumentType>(reader.GetString(1));
            var prefix = reader.GetString(2);
            var yearFormat = Enum.Parse<DocumentYearFormat>(reader.GetString(3));
            var numberLength = reader.GetInt32(4);
            return new AllocatedDocumentNumber(documentSeriesId, documentType, sequenceNumber,
                DocumentSeriesService.FormatNumber(prefix, yearFormat, numberLength, sequenceNumber, timestamp), timestamp);
        }
        catch (AppException) { throw; }
        catch (Exception exception) { throw new InvalidOperationException(localizer[DocumentSeriesMessageKeys.NumberAllocationFailed].Value, exception); }
        finally { if (mustClose && connection.State == ConnectionState.Open) await connection.CloseAsync(); }
    }

    private static void AddParameter(DbCommand command, string name, object value, DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
