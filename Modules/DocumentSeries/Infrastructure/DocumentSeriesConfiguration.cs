using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.DocumentSeries.Infrastructure;

using SeriesEntity = verii_wms_api_v2.Modules.DocumentSeries.Domain.DocumentSeries;
public sealed class DocumentSeriesConfiguration : BaseEntityConfiguration<SeriesEntity>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SeriesEntity> builder)
    {
        builder.ToTable("RII_DOCUMENT_SERIES", table =>
        {
            table.HasCheckConstraint("CK_RII_DOCUMENT_SERIES_NUMBER_LENGTH", "[NumberLength] BETWEEN 3 AND 15");
            table.HasCheckConstraint(
                "CK_RII_DOCUMENT_SERIES_DOCUMENT_NO_LENGTH",
                "LEN([Prefix]) + [NumberLength] + CASE [YearFormat] WHEN N'TwoDigit' THEN 2 WHEN N'FourDigit' THEN 4 ELSE 0 END <= 15");
            table.HasCheckConstraint(
                "CK_RII_DOCUMENT_SERIES_COUNTER_LENGTH",
                "LEN(CONVERT(varchar(20), [StartNumber])) <= [NumberLength] AND LEN(CONVERT(varchar(20), [NextNumber])) <= [NumberLength]");
            table.HasCheckConstraint("CK_RII_DOCUMENT_SERIES_START_NUMBER", "[StartNumber] > 0");
            table.HasCheckConstraint("CK_RII_DOCUMENT_SERIES_NEXT_NUMBER", "[NextNumber] >= [StartNumber]");
            table.HasCheckConstraint("CK_RII_DOCUMENT_SERIES_INCREMENT", "[IncrementBy] BETWEEN 1 AND 1000");
        });

        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.Prefix).HasMaxLength(10).IsRequired();
        builder.Property(x => x.YearFormat).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.NumberLength).HasDefaultValue(8);
        builder.Property(x => x.StartNumber).HasDefaultValue(1L);
        builder.Property(x => x.NextNumber).HasDefaultValue(1L);
        builder.Property(x => x.IncrementBy).HasDefaultValue(1);
        builder.Property(x => x.IsDefault).HasDefaultValue(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.HasIssuedNumbers).HasDefaultValue(false);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

        builder.HasIndex(x => new { x.BranchCode, x.DocumentType, x.Code }).IsUnique()
            .HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_DOCUMENT_SERIES_SCOPE_CODE");
        builder.HasIndex(x => new { x.BranchCode, x.DocumentType }).IsUnique()
            .HasFilter("[IsDefault] = 1 AND [IsActive] = 1 AND [IsDeleted] = 0")
            .HasDatabaseName("UX_RII_DOCUMENT_SERIES_DEFAULT_SCOPE");
        builder.HasIndex(x => new { x.BranchCode, x.DocumentType, x.IsActive })
            .HasDatabaseName("IX_RII_DOCUMENT_SERIES_RESOLUTION");
    }
}
