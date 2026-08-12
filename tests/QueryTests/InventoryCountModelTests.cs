using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.InventoryCount.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class InventoryCountModelTests
{
    [Fact]
    public void Inventory_count_aggregate_uses_RII_tables_and_optimistic_concurrency()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal("RII_INVENTORY_COUNT_HEADER", Entity<InventoryCountHeader>(model).GetTableName());
        Assert.Equal("RII_INVENTORY_COUNT_SCOPE", Entity<InventoryCountScope>(model).GetTableName());
        Assert.Equal("RII_INVENTORY_COUNT_TASK", Entity<InventoryCountTask>(model).GetTableName());
        Assert.Equal("RII_INVENTORY_COUNT_LINE", Entity<InventoryCountLine>(model).GetTableName());
        Assert.Equal("RII_INVENTORY_COUNT_ENTRY", Entity<InventoryCountEntry>(model).GetTableName());
        Assert.Equal("RII_INVENTORY_COUNT_SCAN_EVENT", Entity<InventoryCountScanEvent>(model).GetTableName());
        Assert.Equal("RII_INVENTORY_COUNT_REVIEW", Entity<InventoryCountReview>(model).GetTableName());
        Assert.Equal("RII_INVENTORY_COUNT_ADJUSTMENT", Entity<InventoryCountAdjustment>(model).GetTableName());
        Assert.Equal("RII_INVENTORY_COUNT_POLICY", Entity<InventoryCountPolicy>(model).GetTableName());

        Assert.True(Entity<InventoryCountHeader>(model).FindProperty(nameof(InventoryCountHeader.RowVersion))!.IsConcurrencyToken);
        Assert.True(Entity<InventoryCountTask>(model).FindProperty(nameof(InventoryCountTask.RowVersion))!.IsConcurrencyToken);
        Assert.True(Entity<InventoryCountLine>(model).FindProperty(nameof(InventoryCountLine.RowVersion))!.IsConcurrencyToken);
    }

    [Fact]
    public void Count_and_scan_commands_are_idempotent_and_document_numbers_are_branch_scoped()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        AssertUniqueIndex(Entity<InventoryCountHeader>(model), "UX_RII_IC_HEADER_BRANCH_DOCUMENT", nameof(InventoryCountHeader.BranchCode), nameof(InventoryCountHeader.DocumentNo));
        AssertUniqueIndex(Entity<InventoryCountHeader>(model), "UX_RII_IC_HEADER_RELEASE_IDEMPOTENCY", nameof(InventoryCountHeader.ReleaseIdempotencyKey));
        AssertUniqueIndex(Entity<InventoryCountEntry>(model), "UX_RII_IC_ENTRY_IDEMPOTENCY", nameof(InventoryCountEntry.IdempotencyKey));
        AssertUniqueIndex(Entity<InventoryCountScanEvent>(model), "UX_RII_IC_SCAN_EVENT_IDEMPOTENCY", nameof(InventoryCountScanEvent.IdempotencyKey));
    }

    [Fact]
    public void Approved_adjustment_is_linked_to_immutable_stock_movement_operation()
    {
        using var context = CreateContext();
        var adjustment = Entity<InventoryCountAdjustment>(context.GetService<IDesignTimeModel>().Model);
        var foreignKey = Assert.Single(adjustment.GetForeignKeys(), candidate =>
            candidate.Properties.Count == 1
            && candidate.Properties[0].Name == nameof(InventoryCountAdjustment.StockMovementOperationId));

        Assert.Equal(typeof(StockMovementOperation), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Count_state_machine_contains_review_recount_approval_and_posting_gates()
    {
        Assert.True((int)InventoryCountStatus.AwaitingReview < (int)InventoryCountStatus.RecountRequired);
        Assert.True((int)InventoryCountStatus.RecountRequired < (int)InventoryCountStatus.AwaitingApproval);
        Assert.True((int)InventoryCountStatus.AwaitingApproval < (int)InventoryCountStatus.Posting);
        Assert.True((int)InventoryCountStatus.Posting < (int)InventoryCountStatus.Completed);
        Assert.Equal(3, (int)InventoryCountMode.DoubleBlind);
        Assert.Equal(2, (int)InventoryCountMovementPolicy.SnapshotWithMovementReconciliation);
    }

    private static IEntityType Entity<TEntity>(IModel model) =>
        model.FindEntityType(typeof(TEntity))
        ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not part of the EF model.");

    private static void AssertUniqueIndex(IEntityType entity, string databaseName, params string[] properties)
    {
        var index = Assert.Single(entity.GetIndexes(), candidate => candidate.GetDatabaseName() == databaseName);
        Assert.True(index.IsUnique);
        Assert.Equal(properties, index.Properties.Select(property => property.Name));
    }

    private static WmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True")
            .Options);
}
