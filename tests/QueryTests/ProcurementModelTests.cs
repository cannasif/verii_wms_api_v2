using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Procurement.Application;
using verii_wms_api_v2.Modules.Procurement.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class ProcurementModelTests
{
    [Fact]
    public void Procurement_documents_are_independent_branch_scoped_aggregates()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal("RII_PC_REQUEST", Entity<ProcurementRequest>(model).GetTableName());
        Assert.Equal("RII_PC_RFQ", Entity<ProcurementRfq>(model).GetTableName());
        Assert.Equal("RII_PC_QUOTE", Entity<ProcurementSupplierQuote>(model).GetTableName());
        Assert.Equal("RII_PC_ORDER", Entity<ProcurementPurchaseOrder>(model).GetTableName());
        Assert.Equal("RII_PC_POLICY", Entity<ProcurementPolicy>(model).GetTableName());
        Assert.NotNull(Entity<ProcurementRequest>(model).FindProperty(nameof(ProcurementRequest.BranchCode)));
        Assert.NotNull(Entity<ProcurementPurchaseOrder>(model).FindProperty(nameof(ProcurementPurchaseOrder.BranchCode)));
    }

    [Fact]
    public void Purchase_order_line_tracks_open_receipt_quantity_without_goods_receipt_foreign_key()
    {
        using var context = CreateContext();
        var entity = Entity<ProcurementPurchaseOrderLine>(context.GetService<IDesignTimeModel>().Model);

        Assert.NotNull(entity.FindProperty(nameof(ProcurementPurchaseOrderLine.OrderedQuantity)));
        Assert.NotNull(entity.FindProperty(nameof(ProcurementPurchaseOrderLine.ReceivedQuantity)));
        Assert.NotNull(entity.FindProperty(nameof(ProcurementPurchaseOrderLine.CancelledQuantity)));
        Assert.Contains(entity.GetCheckConstraints(), x =>
            x.Name == "CK_RII_PC_ORDER_LINE_AMOUNTS"
            && x.Sql.Contains("[ReceivedQuantity] + [CancelledQuantity] <= [OrderedQuantity]", StringComparison.Ordinal));
        Assert.DoesNotContain(entity.GetForeignKeys(), x =>
            x.PrincipalEntityType.ClrType.Namespace?.Contains("GoodsReceipt", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Procurement_lifecycle_has_explicit_approval_and_receipt_states()
    {
        Assert.Contains(nameof(ProcurementRequestStatus.PartiallyConverted), Enum.GetNames<ProcurementRequestStatus>());
        Assert.Contains(nameof(ProcurementQuoteStatus.PartiallyConverted), Enum.GetNames<ProcurementQuoteStatus>());
        Assert.Contains(nameof(ProcurementOrderStatus.PartiallyReceived), Enum.GetNames<ProcurementOrderStatus>());
        Assert.Contains(nameof(ProcurementOrderStatus.Received), Enum.GetNames<ProcurementOrderStatus>());
    }

    [Fact]
    public void Split_awards_have_branch_policy_and_concurrency_tokens()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var policy = Entity<ProcurementPolicy>(model);
        var requestLine = Entity<ProcurementRequestLine>(model);
        var quoteLine = Entity<ProcurementSupplierQuoteLine>(model);

        Assert.NotNull(policy.FindProperty(nameof(ProcurementPolicy.AllowMultipleRfqsPerRequest)));
        Assert.NotNull(policy.FindProperty(nameof(ProcurementPolicy.AllowMultipleOrdersPerQuote)));
        Assert.True(requestLine.FindProperty(nameof(ProcurementRequestLine.RowVersion))!.IsConcurrencyToken);
        Assert.True(quoteLine.FindProperty(nameof(ProcurementSupplierQuoteLine.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(quoteLine.GetCheckConstraints(), x =>
            x.Name == "CK_RII_PC_QUOTE_LINE_AMOUNTS"
            && x.Sql.Contains("[ConvertedQuantity] <= [QuotedQuantity]", StringComparison.Ordinal));
    }

    [Fact]
    public void Supplier_portal_invitation_uses_hashed_unique_token_and_concurrency_control()
    {
        using var context=CreateContext();
        var invitation=Entity<ProcurementQuoteInvitation>(context.GetService<IDesignTimeModel>().Model);
        Assert.Equal("RII_PC_QUOTE_INVITATION",invitation.GetTableName());
        Assert.Equal(64,invitation.FindProperty(nameof(ProcurementQuoteInvitation.TokenHash))!.GetMaxLength());
        Assert.True(invitation.FindProperty(nameof(ProcurementQuoteInvitation.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(invitation.GetIndexes(),x=>x.IsUnique&&x.Properties.Select(p=>p.Name).SequenceEqual([nameof(ProcurementQuoteInvitation.TokenHash)]));
        Assert.Contains(invitation.GetIndexes(),x=>x.IsUnique&&x.Properties.Select(p=>p.Name).SequenceEqual([nameof(ProcurementQuoteInvitation.ProcurementRfqId),nameof(ProcurementQuoteInvitation.SupplierId)]));
    }

    [Fact]
    public void Supplier_portal_behavior_is_branch_policy_driven_and_database_constrained()
    {
        using var context = CreateContext();
        var policy = Entity<ProcurementPolicy>(context.GetService<IDesignTimeModel>().Model);

        Assert.NotNull(policy.FindProperty(nameof(ProcurementPolicy.SupplierQuoteChannelMode)));
        Assert.NotNull(policy.FindProperty(nameof(ProcurementPolicy.InvitationValidityDays)));
        Assert.NotNull(policy.FindProperty(nameof(ProcurementPolicy.AllowSupplierDraftSave)));
        Assert.NotNull(policy.FindProperty(nameof(ProcurementPolicy.AllowSupplierQuantityChange)));
        Assert.NotNull(policy.FindProperty(nameof(ProcurementPolicy.AllowSupplierRevisions)));
        Assert.NotNull(policy.FindProperty(nameof(ProcurementPolicy.MaximumSupplierRevisionCount)));
        Assert.NotNull(policy.FindProperty(nameof(ProcurementPolicy.RequireSupplierDeliveryDate)));
        Assert.NotNull(policy.FindProperty(nameof(ProcurementPolicy.AllowZeroUnitPrice)));
        Assert.Contains(policy.GetCheckConstraints(), x =>
            x.Name == "CK_RII_PC_POLICY_SUPPLIER_PORTAL"
            && x.Sql.Contains("[InvitationValidityDays] BETWEEN 1 AND 30", StringComparison.Ordinal));

        var defaults = new ProcurementPolicy();
        Assert.Equal(SupplierQuoteChannelMode.PortalOptional, defaults.SupplierQuoteChannelMode);
        Assert.Equal(7, defaults.InvitationValidityDays);
        Assert.Equal(3, defaults.MaximumSupplierRevisionCount);
        Assert.True(defaults.AllowSupplierDraftSave);
    }

    [Fact]
    public void Procurement_request_lines_persist_line_level_status_with_lookup_index()
    {
        using var context = CreateContext();
        var line = Entity<ProcurementRequestLine>(context.GetService<IDesignTimeModel>().Model);

        Assert.NotNull(line.FindProperty(nameof(ProcurementRequestLine.Status)));
        Assert.Contains(line.GetIndexes(), index => index.Properties.Select(x => x.Name).SequenceEqual([
            nameof(ProcurementRequestLine.ProcurementRequestId),
            nameof(ProcurementRequestLine.Status)
        ]));
    }

    [Fact]
    public void Procurement_read_models_expose_audit_and_document_lineage()
    {
        var gridFields = typeof(ProcurementGridRow).GetProperties().Select(x => x.Name).ToHashSet();
        var detailFields = typeof(ProcurementDocumentDetail).GetProperties().Select(x => x.Name).ToHashSet();

        foreach (var field in new[]
                 {
                     nameof(ProcurementGridRow.CreatedBy),
                     nameof(ProcurementGridRow.CreatedByName),
                     nameof(ProcurementGridRow.UpdatedBy),
                     nameof(ProcurementGridRow.UpdatedByName),
                     nameof(ProcurementGridRow.RequestId),
                     nameof(ProcurementGridRow.RfqId),
                     nameof(ProcurementGridRow.QuoteId)
                 })
            Assert.Contains(field, gridFields);

        foreach (var field in new[]
                 {
                     nameof(ProcurementDocumentDetail.CreatedBy),
                     nameof(ProcurementDocumentDetail.CreatedByName),
                     nameof(ProcurementDocumentDetail.RequestNo),
                     nameof(ProcurementDocumentDetail.RfqNo),
                     nameof(ProcurementDocumentDetail.QuoteNo)
                 })
            Assert.Contains(field, detailFields);
    }

    private static IEntityType Entity<TEntity>(IModel model) =>
        model.FindEntityType(typeof(TEntity))
        ?? throw new InvalidOperationException($"{typeof(TEntity).Name} modelde bulunamadı.");

    private static WmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True")
            .Options);
}
