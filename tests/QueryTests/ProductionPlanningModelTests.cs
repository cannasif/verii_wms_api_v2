using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Production.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class ProductionPlanningModelTests
{
    [Fact]
    public void Production_aggregate_uses_expected_RII_tables_and_concurrency_tokens()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal("RII_PR_HEADER", AssertEntity<ProductionHeader>(model).GetTableName());
        Assert.Equal("RII_PR_ORDER", AssertEntity<ProductionOrder>(model).GetTableName());
        Assert.Equal("RII_PR_MATERIAL", AssertEntity<ProductionMaterialRequirement>(model).GetTableName());
        Assert.Equal("RII_PR_OUTPUT", AssertEntity<ProductionOutputExpectation>(model).GetTableName());
        Assert.Equal("RII_PR_ASSIGNMENT", AssertEntity<ProductionOrderAssignment>(model).GetTableName());
        Assert.Equal("RII_PR_DEPENDENCY", AssertEntity<ProductionOrderDependency>(model).GetTableName());

        Assert.True(AssertEntity<ProductionHeader>(model)
            .FindProperty(nameof(ProductionHeader.RowVersion))!.IsConcurrencyToken);
        Assert.True(AssertEntity<ProductionOrder>(model)
            .FindProperty(nameof(ProductionOrder.RowVersion))!.IsConcurrencyToken);
    }

    [Fact]
    public void Production_identifiers_and_assignments_are_unique_inside_their_business_scope()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var header = AssertEntity<ProductionHeader>(model);
        var order = AssertEntity<ProductionOrder>(model);
        var assignment = AssertEntity<ProductionOrderAssignment>(model);

        AssertUniqueFilteredIndex(header, nameof(ProductionHeader.BranchCode), nameof(ProductionHeader.DocumentNo));
        AssertUniqueFilteredIndex(header, nameof(ProductionHeader.CorrelationId));
        AssertUniqueFilteredIndex(order, nameof(ProductionOrder.BranchCode), nameof(ProductionOrder.OrderNo));
        AssertUniqueFilteredIndex(
            assignment,
            nameof(ProductionOrderAssignment.ProductionOrderId),
            nameof(ProductionOrderAssignment.UserId));
    }

    [Fact]
    public void External_production_source_is_versioned_and_isolated_from_operational_orders()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var order = AssertEntity<ProductionSourceWorkOrder>(model);
        var recipe = AssertEntity<ProductionSourceRecipeLine>(model);

        Assert.Equal("RII_PR_SOURCE_ORDER", order.GetTableName());
        Assert.Equal("RII_PR_SOURCE_RECIPE", recipe.GetTableName());
        AssertUniqueFilteredIndex(order,
            nameof(ProductionSourceWorkOrder.BranchCode),
            nameof(ProductionSourceWorkOrder.SourceSystemCode),
            nameof(ProductionSourceWorkOrder.WorkOrderNumber),
            nameof(ProductionSourceWorkOrder.RevisionNumber));
        AssertUniqueFilteredIndex(recipe,
            nameof(ProductionSourceRecipeLine.ProductionSourceWorkOrderId),
            nameof(ProductionSourceRecipeLine.LineNumber));
        AssertForeignKey<ProductionSourceWorkOrder>(recipe,nameof(ProductionSourceRecipeLine.ProductionSourceWorkOrderId));
        Assert.True(order.FindProperty(nameof(ProductionSourceWorkOrder.RowVersion))!.IsConcurrencyToken);
        Assert.True(recipe.FindProperty(nameof(ProductionSourceRecipeLine.RowVersion))!.IsConcurrencyToken);
    }

    [Fact]
    public void Production_transfer_links_have_real_foreign_keys_to_plan_order_material_and_output()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var headerLink = AssertEntity<ProductionTransferHeaderLink>(model);
        var lineLink = AssertEntity<ProductionTransferLineLink>(model);

        AssertForeignKey<ProductionHeader>(headerLink, nameof(ProductionTransferHeaderLink.ProductionHeaderId));
        AssertForeignKey<ProductionOrder>(headerLink, nameof(ProductionTransferHeaderLink.ProductionOrderId));
        AssertForeignKey<ProductionMaterialRequirement>(
            lineLink,
            nameof(ProductionTransferLineLink.ProductionConsumptionId));
        AssertForeignKey<ProductionOutputExpectation>(
            lineLink,
            nameof(ProductionTransferLineLink.ProductionOutputId));
    }

    [Fact]
    public void Production_policy_supports_combined_sources_and_strict_manual_erp_validation()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var policy = AssertEntity<ProductionTransferPolicy>(model);

        Assert.Equal(3, (int)ProductionOrderSourceType.ErpAndWms);
        Assert.Equal(true, policy
            .FindProperty(nameof(ProductionTransferPolicy.RequireErpMasterDataForManualTransfer))!
            .GetDefaultValue());

        var operationalOrder = AssertEntity<ProductionOrder>(model);
        Assert.Equal(50, operationalOrder
            .FindProperty(nameof(ProductionOrder.ExternalSourceSystemCode))!
            .GetMaxLength());
        Assert.Contains(operationalOrder.GetIndexes(), candidate => candidate.Properties
            .Select(x => x.Name)
            .SequenceEqual(new[]
            {
                nameof(ProductionOrder.BranchCode),
                nameof(ProductionOrder.ExternalSourceSystemCode),
                nameof(ProductionOrder.ExternalOrderNo)
            }));
    }

    private static IEntityType AssertEntity<TEntity>(IModel model) =>
        model.FindEntityType(typeof(TEntity))
        ?? throw new InvalidOperationException($"{typeof(TEntity).Name} modelde bulunamadı.");

    private static void AssertUniqueFilteredIndex(IEntityType entity, params string[] properties)
    {
        var index = Assert.Single(entity.GetIndexes(), candidate =>
            candidate.Properties.Select(x => x.Name).SequenceEqual(properties));
        Assert.True(index.IsUnique);
        Assert.Equal("[IsDeleted] = 0", index.GetFilter());
    }

    private static void AssertForeignKey<TPrincipal>(IEntityType entity, string property)
    {
        var foreignKey = Assert.Single(entity.GetForeignKeys(), candidate =>
            candidate.Properties.Count == 1
            && candidate.Properties[0].Name == property);
        Assert.Equal(typeof(TPrincipal), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    private static WmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True")
            .Options);
}
