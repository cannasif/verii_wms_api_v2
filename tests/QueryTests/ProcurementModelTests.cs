using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
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
        Assert.Equal(
            ["Draft", "PendingApproval", "Approved", "Rejected", "Converted", "Cancelled"],
            Enum.GetNames<ProcurementRequestStatus>());
        Assert.Contains(nameof(ProcurementOrderStatus.PartiallyReceived), Enum.GetNames<ProcurementOrderStatus>());
        Assert.Contains(nameof(ProcurementOrderStatus.Received), Enum.GetNames<ProcurementOrderStatus>());
    }

    private static IEntityType Entity<TEntity>(IModel model) =>
        model.FindEntityType(typeof(TEntity))
        ?? throw new InvalidOperationException($"{typeof(TEntity).Name} modelde bulunamadı.");

    private static WmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True")
            .Options);
}
