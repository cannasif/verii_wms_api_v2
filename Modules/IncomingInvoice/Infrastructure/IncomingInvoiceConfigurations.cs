using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.IncomingInvoice.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.IncomingInvoice.Infrastructure;

public sealed class ELogoConnectionConfiguration : BaseEntityConfiguration<ELogoConnection>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ELogoConnection> b)
    {
        b.ToTable("RII_ELOGO_CONNECTION");
        b.Property(x => x.Key).HasMaxLength(80).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Vkn).HasMaxLength(20).IsRequired();
        b.Property(x => x.Username).HasMaxLength(100).IsRequired();
        b.Property(x => x.PasswordCipherText).HasColumnType("nvarchar(max)");
        b.Property(x => x.Source).HasMaxLength(100).IsRequired();
        b.Property(x => x.EndpointUrl).HasMaxLength(500);
        b.Property(x => x.ApplicationName).HasMaxLength(100);
        b.Property(x => x.Version).HasMaxLength(20);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchCode, x.Key }).IsUnique()
            .HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_ELOGO_CONNECTION_BRANCH_KEY");
        b.HasIndex(x => new { x.BranchCode, x.IsActive, x.DisplayName })
            .HasDatabaseName("IX_RII_ELOGO_CONNECTION_BRANCH_ACTIVE_NAME");
    }
}

public sealed class IncomingInvoiceHeaderConfiguration : BaseEntityConfiguration<IncomingInvoiceHeader>
{
    protected override void ConfigureEntity(EntityTypeBuilder<IncomingInvoiceHeader> b)
    {
        b.ToTable("RII_INCOMING_INVOICE_HEADER");
        b.Property(x => x.OwnerVkn).HasMaxLength(20).IsRequired();
        b.Property(x => x.ProfileId).HasMaxLength(50);
        b.Property(x => x.InvoiceNo).HasMaxLength(50).IsRequired();
        b.Property(x => x.InvoiceTypeCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        b.Property(x => x.OrderReferenceNo).HasMaxLength(100);
        b.Property(x => x.DespatchReferenceNo).HasMaxLength(100);
        b.Property(x => x.SupplierVknOrTckn).HasMaxLength(20).IsRequired();
        b.Property(x => x.SupplierName).HasMaxLength(300).IsRequired();
        b.Property(x => x.SupplierTaxOffice).HasMaxLength(100);
        b.Property(x => x.CustomerVknOrTckn).HasMaxLength(20).IsRequired();
        b.Property(x => x.CustomerName).HasMaxLength(300).IsRequired();
        b.Property(x => x.LineExtensionAmount).HasPrecision(28, 8);
        b.Property(x => x.TaxExclusiveAmount).HasPrecision(28, 8);
        b.Property(x => x.TaxAmount).HasPrecision(28, 8);
        b.Property(x => x.TaxInclusiveAmount).HasPrecision(28, 8);
        b.Property(x => x.AllowanceTotalAmount).HasPrecision(28, 8);
        b.Property(x => x.PayableAmount).HasPrecision(28, 8);
        b.Property(x => x.ValidationMessage).HasMaxLength(1000);
        b.Property(x => x.RecognitionConfidence).HasPrecision(9, 6);
        b.Property(x => x.CaptureSource)
            .HasDefaultValue(IncomingInvoiceCaptureSource.ELogo);
        b.Property(x => x.SourceHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.ELogoConnection).WithMany().HasForeignKey(x => x.ELogoConnectionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BranchCode, x.OwnerVkn, x.Uuid }).IsUnique()
            .HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_INCOMING_INVOICE_OWNER_UUID");
        b.HasIndex(x => new { x.BranchCode, x.IssueDate, x.InvoiceNo })
            .HasDatabaseName("IX_RII_INCOMING_INVOICE_BRANCH_DATE_NO");
        b.HasIndex(x => new { x.BranchCode, x.ArchiveStatus, x.ImportedAtUtc })
            .HasDatabaseName("IX_RII_INCOMING_INVOICE_STATUS_IMPORTED");
    }
}

public sealed class IncomingInvoiceLineConfiguration : BaseEntityConfiguration<IncomingInvoiceLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<IncomingInvoiceLine> b)
    {
        b.ToTable("RII_INCOMING_INVOICE_LINE");
        b.Property(x => x.ExternalLineId).HasMaxLength(50).IsRequired();
        b.Property(x => x.StockCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.BuyerStockCode).HasMaxLength(100);
        b.Property(x => x.StockName).HasMaxLength(500).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Quantity).HasPrecision(28, 8);
        b.Property(x => x.UnitCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.UnitPrice).HasPrecision(28, 8);
        b.Property(x => x.LineExtensionAmount).HasPrecision(28, 8);
        b.Property(x => x.TaxRate).HasPrecision(18, 6);
        b.Property(x => x.TaxAmount).HasPrecision(28, 8);
        b.Property(x => x.ConversionFactor).HasPrecision(28, 8).HasDefaultValue(1m);
        b.Property(x => x.RecognitionConfidence).HasPrecision(9, 6);
        b.Property(x => x.YapCode).HasMaxLength(100);
        b.Property(x => x.MatchMessage).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Header).WithMany(x => x.Lines).HasForeignKey(x => x.IncomingInvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Modules.Stock.Domain.Stock>().WithMany()
            .HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SupplierStockMapping).WithMany()
            .HasForeignKey(x => x.SupplierStockMappingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Modules.YapCode.Domain.YapCode>().WithMany()
            .HasForeignKey(x => x.YapCodeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.IncomingInvoiceId, x.LineNo }).IsUnique()
            .HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_INCOMING_INVOICE_LINE_NO");
        b.HasIndex(x => new { x.BranchCode, x.StockCode }).HasDatabaseName("IX_RII_INCOMING_INVOICE_LINE_STOCK");
    }
}

public sealed class IncomingInvoiceDocumentConfiguration : BaseEntityConfiguration<IncomingInvoiceDocument>
{
    protected override void ConfigureEntity(EntityTypeBuilder<IncomingInvoiceDocument> b)
    {
        b.ToTable("RII_INCOMING_INVOICE_DOCUMENT");
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.StoragePath).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.HasOne(x => x.Header).WithMany(x => x.Documents).HasForeignKey(x => x.IncomingInvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.IncomingInvoiceId, x.Format }).IsUnique()
            .HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_INCOMING_INVOICE_DOCUMENT_FORMAT");
    }
}

public sealed class IncomingInvoiceGoodsReceiptLinkConfiguration : BaseEntityConfiguration<IncomingInvoiceGoodsReceiptLink>
{
    protected override void ConfigureEntity(EntityTypeBuilder<IncomingInvoiceGoodsReceiptLink> b)
    {
        b.ToTable("RII_INCOMING_INVOICE_GR_LINK");
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.LinkedQuantity).HasPrecision(28, 8);
        b.HasOne(x => x.IncomingInvoice).WithMany(x => x.GoodsReceiptLinks)
            .HasForeignKey(x => x.IncomingInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.GoodsReceipt).WithMany()
            .HasForeignKey(x => x.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.IncomingInvoiceId, x.GoodsReceiptId }).IsUnique()
            .HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_INCOMING_INVOICE_GR_LINK");
        b.HasIndex(x => new { x.IncomingInvoiceId, x.IdempotencyKey }).IsUnique()
            .HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_INCOMING_INVOICE_GR_IDEMPOTENCY");
    }
}

public sealed class IncomingInvoiceGoodsReceiptLineLinkConfiguration
    : BaseEntityConfiguration<IncomingInvoiceGoodsReceiptLineLink>
{
    protected override void ConfigureEntity(EntityTypeBuilder<IncomingInvoiceGoodsReceiptLineLink> b)
    {
        b.ToTable("RII_INCOMING_INVOICE_GR_LINE_LINK");
        b.Property(x => x.LinkedQuantity).HasPrecision(28, 8);
        b.HasOne(x => x.Link).WithMany(x => x.Lines)
            .HasForeignKey(x => x.IncomingInvoiceGoodsReceiptLinkId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.IncomingInvoiceLine).WithMany()
            .HasForeignKey(x => x.IncomingInvoiceLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.GoodsReceiptLine).WithMany()
            .HasForeignKey(x => x.GoodsReceiptLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.IncomingInvoiceLineId, x.GoodsReceiptLineId }).IsUnique()
            .HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_INCOMING_INVOICE_GR_LINE");
        b.HasIndex(x => new { x.IncomingInvoiceLineId, x.IsDeleted })
            .HasDatabaseName("IX_RII_INCOMING_INVOICE_LINE_LINK_REMAINING");
    }
}
