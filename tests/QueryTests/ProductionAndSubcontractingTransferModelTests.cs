using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.SubcontractingTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class ProductionAndSubcontractingTransferModelTests
{
    [Fact]
    public void Transfer_header_persists_business_context_and_supports_same_warehouse_location_moves()
    {
        using var context = CreateContext();
        var entity = AssertEntity<WarehouseTransferHeader>(context.GetService<IDesignTimeModel>().Model);
        var property = entity.FindProperty(nameof(WarehouseTransferHeader.BusinessContext));

        Assert.NotNull(property);
        Assert.Equal(40, property!.GetMaxLength());
        Assert.DoesNotContain(entity.GetCheckConstraints(), x => x.Name == "CK_RII_WT_HEADER_WAREHOUSE");
    }

    [Fact]
    public void Production_links_are_one_to_one_with_physical_transfer_rows()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var header = AssertEntity<ProductionTransferHeaderLink>(model);
        var line = AssertEntity<ProductionTransferLineLink>(model);

        AssertUniqueFilteredIndex(header, nameof(ProductionTransferHeaderLink.WarehouseTransferHeaderId));
        AssertUniqueFilteredIndex(line, nameof(ProductionTransferLineLink.WarehouseTransferLineId));
        Assert.Contains(line.GetCheckConstraints(), x =>
            x.Name == "CK_RII_PT_LINE_LINK_REQUIRED_QTY" && x.Sql.Contains("> 0", StringComparison.Ordinal));
    }

    [Fact]
    public void Subcontracting_chain_has_supplier_parent_issue_and_source_line_foreign_keys()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var header = AssertEntity<SubcontractingTransferHeaderLink>(model);
        var line = AssertEntity<SubcontractingTransferLineLink>(model);

        Assert.Contains(header.GetForeignKeys(), x => x.Properties.Single().Name == nameof(SubcontractingTransferHeaderLink.SupplierId));
        Assert.Contains(header.GetForeignKeys(), x => x.Properties.Single().Name == nameof(SubcontractingTransferHeaderLink.ParentIssueTransferId));
        Assert.Contains(line.GetForeignKeys(), x => x.Properties.Single().Name == nameof(SubcontractingTransferLineLink.SourceIssueLineId));
        Assert.Contains(line.GetCheckConstraints(), x =>
            x.Name == "CK_RII_ST_LINE_LINK_QTY"
            && x.Sql.Contains("[ScrapQuantity] <= [ExpectedQuantity]", StringComparison.Ordinal));
    }

    private static IEntityType AssertEntity<TEntity>(IModel model) =>
        model.FindEntityType(typeof(TEntity))
        ?? throw new InvalidOperationException($"{typeof(TEntity).Name} modelde bulunamadı.");

    private static void AssertUniqueFilteredIndex(IEntityType entity, string propertyName)
    {
        var index = Assert.Single(entity.GetIndexes(), x => x.Properties.Count == 1 && x.Properties[0].Name == propertyName);
        Assert.True(index.IsUnique);
        Assert.Equal("[IsDeleted] = 0", index.GetFilter());
    }

    private static WmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True")
            .Options);
}
