using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.SubcontractingTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
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
        Assert.Equal(50, entity.FindProperty(nameof(WarehouseTransferHeader.ProjectCode))?.GetMaxLength());
        Assert.Contains(nameof(WarehouseTransferStatus.PartiallyShipped), Enum.GetNames<WarehouseTransferStatus>());
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
    public void Transfer_cancellation_policy_and_selected_return_location_are_persisted()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var header = AssertEntity<WarehouseTransferHeader>(model);
        var productionPolicy = AssertEntity<ProductionTransferPolicy>(model);

        Assert.Equal(40, header.FindProperty(nameof(WarehouseTransferHeader.CancellationReturnPolicy))?.GetMaxLength());
        Assert.NotNull(header.FindProperty(nameof(WarehouseTransferHeader.CancellationReturnLocationId)));
        Assert.Equal(40, productionPolicy.FindProperty(nameof(ProductionTransferPolicy.CancellationReturnPolicy))?.GetMaxLength());
        Assert.Equal(new[] { "OriginalSourceLocation", "WarehouseDefaultReturnLocation", "ManagerSelectionRequired" },
            Enum.GetNames<WarehouseTransferCancellationReturnPolicy>());
        Assert.Contains(nameof(WarehouseTransferTaskType.CancellationReturn), Enum.GetNames<WarehouseTransferTaskType>());
    }

    [Fact]
    public void Warehouse_default_transfer_return_location_is_optional_and_set_null_on_delete()
    {
        using var context = CreateContext();
        var warehouse = AssertEntity<WarehouseEntity>(context.GetService<IDesignTimeModel>().Model);
        var foreignKey = Assert.Single(warehouse.GetForeignKeys(), x =>
            x.Properties.Single().Name == nameof(WarehouseEntity.DefaultTransferReturnLocationId));

        Assert.False(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.SetNull, foreignKey.DeleteBehavior);
        Assert.Equal(typeof(WarehouseLocation), foreignKey.PrincipalEntityType.ClrType);
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

    [Fact]
    public void Kkd_excess_approval_and_employee_group_preference_are_persisted()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var policy = AssertEntity<KkdPolicy>(model);
        var distribution = AssertEntity<KkdDistribution>(model);
        var preference = AssertEntity<KkdEmployeeStockPreference>(model);

        Assert.NotNull(policy.FindProperty(nameof(KkdPolicy.EnableMaterialRequestOrderFlow)));
        Assert.NotNull(policy.FindProperty(nameof(KkdPolicy.RequireManagerApprovalForExcess)));
        Assert.Equal(30, distribution.FindProperty(nameof(KkdDistribution.ExcessApprovalStatus))?.GetMaxLength());
        Assert.Equal(1000, distribution.FindProperty(nameof(KkdDistribution.ExcessApprovalReason))?.GetMaxLength());
        var uniquePreference = Assert.Single(preference.GetIndexes(), x =>
            x.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(KkdEmployeeStockPreference.BranchCode),
                nameof(KkdEmployeeStockPreference.EmployeeId),
                nameof(KkdEmployeeStockPreference.GroupCode)
            }));
        Assert.True(uniquePreference.IsUnique);
        Assert.Equal("[IsDeleted] = 0", uniquePreference.GetFilter());
    }

    [Fact]
    public void Warehouse_outbound_keeps_operation_and_line_project_metadata()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var header = AssertEntity<WarehouseOutboundHeader>(model);
        var line = AssertEntity<WarehouseOutboundLine>(model);

        Assert.Equal(50, header.FindProperty(nameof(WarehouseOutboundHeader.ProjectCode))?.GetMaxLength());
        Assert.Equal(100, header.FindProperty(nameof(WarehouseOutboundHeader.CostCenterCode))?.GetMaxLength());
        Assert.Equal(50, header.FindProperty(nameof(WarehouseOutboundHeader.MovementTypeCode))?.GetMaxLength());
        Assert.Equal(100, header.FindProperty(nameof(WarehouseOutboundHeader.ExitLocationCode))?.GetMaxLength());
        Assert.Equal(50, line.FindProperty(nameof(WarehouseOutboundLine.ProjectCode))?.GetMaxLength());
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
