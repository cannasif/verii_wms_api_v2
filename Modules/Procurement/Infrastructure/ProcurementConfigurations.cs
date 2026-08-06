using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Procurement.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.Procurement.Infrastructure;

public sealed class ProcurementRequestConfiguration : BaseEntityConfiguration<ProcurementRequest>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProcurementRequest> b)
    {
        b.ToTable("RII_PC_REQUEST"); b.Property(x=>x.RequestNo).HasMaxLength(50).IsRequired(); b.Property(x=>x.Subject).HasMaxLength(250).IsRequired(); b.Property(x=>x.DepartmentCode).HasMaxLength(80); b.Property(x=>x.ProjectCode).HasMaxLength(100); b.Property(x=>x.Description).HasMaxLength(2000); b.Property(x=>x.DecisionNote).HasMaxLength(1000); b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(30); b.Property(x=>x.RowVersion).IsRowVersion(); b.HasIndex(x=>new{x.BranchCode,x.RequestNo}).IsUnique().HasFilter("[IsDeleted] = 0"); b.HasIndex(x=>new{x.BranchCode,x.Status,x.RequestDate});
    }
}
public sealed class ProcurementRequestLineConfiguration : BaseEntityConfiguration<ProcurementRequestLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProcurementRequestLine> b) { b.ToTable("RII_PC_REQUEST_LINE",t=>t.HasCheckConstraint("CK_RII_PC_REQUEST_LINE_QTY","[RequestedQuantity] > 0 AND [ConvertedQuantity] >= 0 AND [ConvertedQuantity] <= [RequestedQuantity]")); b.Property(x=>x.StockCodeSnapshot).HasMaxLength(100); b.Property(x=>x.StockNameSnapshot).HasMaxLength(300).IsRequired(); b.Property(x=>x.UnitCode).HasMaxLength(20).IsRequired(); b.Property(x=>x.ProjectCode).HasMaxLength(100); b.Property(x=>x.Description).HasMaxLength(1000); b.Property(x=>x.RequestedQuantity).HasPrecision(20,6); b.Property(x=>x.ConvertedQuantity).HasPrecision(20,6); b.Property(x=>x.RowVersion).IsRowVersion(); b.HasOne(x=>x.Request).WithMany(x=>x.Lines).HasForeignKey(x=>x.ProcurementRequestId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x=>new{x.ProcurementRequestId,x.LineNo}).IsUnique(); }
}
public sealed class ProcurementRfqConfiguration : BaseEntityConfiguration<ProcurementRfq>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProcurementRfq> b) { b.ToTable("RII_PC_RFQ"); b.Property(x=>x.RfqNo).HasMaxLength(50).IsRequired(); b.Property(x=>x.Subject).HasMaxLength(250).IsRequired(); b.Property(x=>x.BuyerMessage).HasMaxLength(2000); b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(30); b.Property(x=>x.RowVersion).IsRowVersion(); b.HasOne(x=>x.Request).WithMany().HasForeignKey(x=>x.ProcurementRequestId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x=>new{x.BranchCode,x.RfqNo}).IsUnique().HasFilter("[IsDeleted] = 0"); }
}
public sealed class ProcurementRfqLineConfiguration : BaseEntityConfiguration<ProcurementRfqLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProcurementRfqLine> b) { b.ToTable("RII_PC_RFQ_LINE",t=>t.HasCheckConstraint("CK_RII_PC_RFQ_LINE_QTY","[RequestedQuantity] > 0")); b.Property(x=>x.StockCodeSnapshot).HasMaxLength(100); b.Property(x=>x.StockNameSnapshot).HasMaxLength(300).IsRequired(); b.Property(x=>x.UnitCode).HasMaxLength(20).IsRequired(); b.Property(x=>x.ProjectCode).HasMaxLength(100); b.Property(x=>x.RequestedQuantity).HasPrecision(20,6); b.HasOne(x=>x.Rfq).WithMany(x=>x.Lines).HasForeignKey(x=>x.ProcurementRfqId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x=>new{x.ProcurementRfqId,x.LineNo}).IsUnique(); }
}
public sealed class ProcurementRfqSupplierConfiguration : BaseEntityConfiguration<ProcurementRfqSupplier>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProcurementRfqSupplier> b) { b.ToTable("RII_PC_RFQ_SUPPLIER"); b.Property(x=>x.SupplierCodeSnapshot).HasMaxLength(100).IsRequired(); b.Property(x=>x.SupplierNameSnapshot).HasMaxLength(300).IsRequired(); b.HasOne(x=>x.Rfq).WithMany(x=>x.Suppliers).HasForeignKey(x=>x.ProcurementRfqId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x=>new{x.ProcurementRfqId,x.SupplierId}).IsUnique().HasFilter("[IsDeleted] = 0"); }
}
public sealed class ProcurementSupplierQuoteConfiguration : BaseEntityConfiguration<ProcurementSupplierQuote>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProcurementSupplierQuote> b) { b.ToTable("RII_PC_QUOTE"); b.Property(x=>x.SupplierCodeSnapshot).HasMaxLength(100).IsRequired(); b.Property(x=>x.SupplierNameSnapshot).HasMaxLength(300).IsRequired(); b.Property(x=>x.QuoteNo).HasMaxLength(100).IsRequired(); b.Property(x=>x.CurrencyCode).HasMaxLength(10).IsRequired(); b.Property(x=>x.ExchangeRate).HasPrecision(20,8); b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(30); b.Property(x=>x.Note).HasMaxLength(2000); b.Property(x=>x.RowVersion).IsRowVersion(); b.HasOne(x=>x.Rfq).WithMany(x=>x.Quotes).HasForeignKey(x=>x.ProcurementRfqId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x=>new{x.ProcurementRfqId,x.SupplierId,x.QuoteNo}).IsUnique().HasFilter("[IsDeleted] = 0"); }
}
public sealed class ProcurementSupplierQuoteLineConfiguration : BaseEntityConfiguration<ProcurementSupplierQuoteLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProcurementSupplierQuoteLine> b) { b.ToTable("RII_PC_QUOTE_LINE",t=>t.HasCheckConstraint("CK_RII_PC_QUOTE_LINE_AMOUNTS","[QuotedQuantity] > 0 AND [ConvertedQuantity] >= 0 AND [ConvertedQuantity] <= [QuotedQuantity] AND [UnitPrice] >= 0 AND [DiscountRate] >= 0 AND [DiscountRate] <= 100 AND [VatRate] >= 0")); b.Property(x=>x.QuotedQuantity).HasPrecision(20,6); b.Property(x=>x.ConvertedQuantity).HasPrecision(20,6); b.Property(x=>x.UnitPrice).HasPrecision(20,6); b.Property(x=>x.DiscountRate).HasPrecision(9,4); b.Property(x=>x.VatRate).HasPrecision(9,4); b.Property(x=>x.RowVersion).IsRowVersion(); b.HasOne(x=>x.Quote).WithMany(x=>x.Lines).HasForeignKey(x=>x.ProcurementSupplierQuoteId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x=>new{x.ProcurementSupplierQuoteId,x.LineNo}).IsUnique(); }
}
public sealed class ProcurementQuoteInvitationConfiguration : BaseEntityConfiguration<ProcurementQuoteInvitation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProcurementQuoteInvitation> b)
    {
        b.ToTable("RII_PC_QUOTE_INVITATION");
        b.Property(x=>x.RecipientEmail).HasMaxLength(320).IsRequired();
        b.Property(x=>x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasOne(x=>x.Rfq).WithMany().HasForeignKey(x=>x.ProcurementRfqId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.RfqSupplier).WithMany().HasForeignKey(x=>x.ProcurementRfqSupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.CurrentQuote).WithMany().HasForeignKey(x=>x.CurrentQuoteId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>x.TokenHash).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.ProcurementRfqId,x.SupplierId}).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.Status,x.ExpiresAtUtc});
    }
}
public sealed class ProcurementPolicyConfiguration : BaseEntityConfiguration<ProcurementPolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProcurementPolicy> b)
    {
        b.ToTable("RII_PC_POLICY", table => table.HasCheckConstraint(
            "CK_RII_PC_POLICY_SUPPLIER_PORTAL",
            "[SupplierQuoteChannelMode] IN (1, 2, 3) AND [InvitationValidityDays] BETWEEN 1 AND 30 AND [MaximumSupplierRevisionCount] BETWEEN 0 AND 20"));
        b.Property(x => x.PolicyKey).HasMaxLength(30).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchCode, x.PolicyKey }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
public sealed class ProcurementPurchaseOrderConfiguration : BaseEntityConfiguration<ProcurementPurchaseOrder>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProcurementPurchaseOrder> b) { b.ToTable("RII_PC_ORDER"); b.Property(x=>x.OrderNo).HasMaxLength(50).IsRequired(); b.Property(x=>x.SupplierCodeSnapshot).HasMaxLength(100).IsRequired(); b.Property(x=>x.SupplierNameSnapshot).HasMaxLength(300).IsRequired(); b.Property(x=>x.CurrencyCode).HasMaxLength(10).IsRequired(); b.Property(x=>x.ExchangeRate).HasPrecision(20,8); b.Property(x=>x.ProjectCode).HasMaxLength(100); b.Property(x=>x.Description).HasMaxLength(2000); b.Property(x=>x.ErpOrderNo).HasMaxLength(100); b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(30); b.Property(x=>x.RowVersion).IsRowVersion(); b.HasIndex(x=>new{x.BranchCode,x.OrderNo}).IsUnique().HasFilter("[IsDeleted] = 0"); b.HasIndex(x=>new{x.BranchCode,x.Status,x.SupplierId}); }
}
public sealed class ProcurementPurchaseOrderLineConfiguration : BaseEntityConfiguration<ProcurementPurchaseOrderLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProcurementPurchaseOrderLine> b) { b.ToTable("RII_PC_ORDER_LINE",t=>t.HasCheckConstraint("CK_RII_PC_ORDER_LINE_AMOUNTS","[OrderedQuantity] > 0 AND [ReceivedQuantity] >= 0 AND [CancelledQuantity] >= 0 AND [ReceivedQuantity] + [CancelledQuantity] <= [OrderedQuantity] AND [UnitPrice] >= 0")); b.Property(x=>x.StockCodeSnapshot).HasMaxLength(100); b.Property(x=>x.StockNameSnapshot).HasMaxLength(300).IsRequired(); b.Property(x=>x.UnitCode).HasMaxLength(20).IsRequired(); b.Property(x=>x.ProjectCode).HasMaxLength(100); b.Property(x=>x.OrderedQuantity).HasPrecision(20,6); b.Property(x=>x.ReceivedQuantity).HasPrecision(20,6); b.Property(x=>x.CancelledQuantity).HasPrecision(20,6); b.Property(x=>x.UnitPrice).HasPrecision(20,6); b.Property(x=>x.DiscountRate).HasPrecision(9,4); b.Property(x=>x.VatRate).HasPrecision(9,4); b.HasOne(x=>x.Order).WithMany(x=>x.Lines).HasForeignKey(x=>x.ProcurementPurchaseOrderId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x=>new{x.ProcurementPurchaseOrderId,x.LineNo}).IsUnique(); b.HasIndex(x=>new{x.StockId,x.DeliveryDate}); }
}
public sealed class ProcurementStatusHistoryConfiguration : BaseEntityConfiguration<ProcurementStatusHistory>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProcurementStatusHistory> b) { b.ToTable("RII_PC_STATUS_HISTORY"); b.Property(x=>x.DocumentType).HasMaxLength(30).IsRequired(); b.Property(x=>x.FromStatus).HasMaxLength(30).IsRequired(); b.Property(x=>x.ToStatus).HasMaxLength(30).IsRequired(); b.Property(x=>x.Note).HasMaxLength(1000); b.HasIndex(x=>new{x.DocumentType,x.DocumentId,x.ChangedAtUtc}); }
}
public sealed class ProcurementAttachmentConfiguration : BaseEntityConfiguration<ProcurementAttachment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProcurementAttachment> b)
    {
        b.ToTable("RII_PC_ATTACHMENT");
        b.Property(x => x.OwnerType).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
        b.Property(x => x.Caption).HasMaxLength(500);
        b.HasIndex(x => new { x.OwnerType, x.OwnerId, x.CreatedDate });
    }
}
