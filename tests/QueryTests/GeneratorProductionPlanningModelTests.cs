using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using verii_wms_api_v2.Modules.GeneratorProduction.Application;
using verii_wms_api_v2.Modules.GeneratorProduction.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Production.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GeneratorProductionPlanningModelTests
{
    [Fact]
    public void Product_route_capability_and_material_override_are_persisted_as_generator_master_data()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal("RII_GP_PRODUCT", Entity<GeneratorProductionProduct>(model).GetTableName());
        Assert.Equal("RII_GP_PRODUCT_ROUTE", Entity<GeneratorProductionProductRoute>(model).GetTableName());
        Assert.Equal("RII_GP_STATION_CAPABILITY", Entity<GeneratorProductionStationCapability>(model).GetTableName());
        Assert.Equal("RII_GP_OPERATION_MATERIAL", Entity<GeneratorProductionOperationMaterial>(model).GetTableName());

        AssertUniqueIndex(Entity<GeneratorProductionProductRoute>(model),
            nameof(GeneratorProductionProductRoute.ProductId), nameof(GeneratorProductionProductRoute.PartType));
        AssertUniqueIndex(Entity<GeneratorProductionStationCapability>(model),
            nameof(GeneratorProductionStationCapability.ProductId),
            nameof(GeneratorProductionStationCapability.RouteOperationId),
            nameof(GeneratorProductionStationCapability.StationId));
    }

    [Fact]
    public void Project_can_reuse_production_plan_and_product_master_without_duplicating_header()
    {
        using var context = CreateContext();
        var project = Entity<GeneratorProductionProject>(context.GetService<IDesignTimeModel>().Model);

        AssertForeignKey<ProductionHeader>(project, nameof(GeneratorProductionProject.ProductionHeaderId));
        AssertForeignKey<GeneratorProductionProduct>(project, nameof(GeneratorProductionProject.ProductId));
    }

    [Fact]
    public void Manual_gantt_fields_and_material_buffer_are_concurrency_safe_and_bounded()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var operation = Entity<GeneratorProductionOperation>(model);
        var policy = Entity<GeneratorProductionPolicy>(model);

        Assert.NotNull(operation.FindProperty(nameof(GeneratorProductionOperation.IsScheduleLocked)));
        Assert.Equal(1000, operation.FindProperty(nameof(GeneratorProductionOperation.ManualScheduleReason))!.GetMaxLength());
        Assert.True(operation.FindProperty(nameof(GeneratorProductionOperation.RowVersion))!.IsConcurrencyToken);
        Assert.Equal(2, policy.FindProperty(nameof(GeneratorProductionPolicy.InboundQualityBufferDays))!.GetDefaultValue());
        Assert.Contains(policy.GetCheckConstraints(), x => x.Name == "CK_RII_GP_POLICY_REFRESH"
            && x.Sql.Contains("[InboundQualityBufferDays] BETWEEN 0 AND 365", StringComparison.Ordinal));
    }

    [Fact]
    public void Planning_contract_exposes_material_coverage_suggestions_and_manual_locks()
    {
        var previewFields = typeof(GeneratorPlanPreviewResult).GetProperties().Select(x => x.Name).ToHashSet();
        var itemFields = typeof(GeneratorPlanItem).GetProperties().Select(x => x.Name).ToHashSet();

        Assert.Contains(nameof(GeneratorPlanPreviewResult.MaterialCoverage), previewFields);
        Assert.Contains(nameof(GeneratorPlanPreviewResult.Suggestions), previewFields);
        Assert.Contains(nameof(GeneratorPlanItem.MaterialAvailableAtUtc), itemFields);
        Assert.Contains(nameof(GeneratorPlanItem.IsScheduleLocked), itemFields);
    }

    [Fact]
    public void Critical_operation_quality_gate_is_persisted_as_one_to_one_concurrency_safe_state()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var operation = Entity<GeneratorProductionOperation>(model);
        var gate = Entity<GeneratorProductionQualityGate>(model);

        Assert.Equal("RII_GP_QUALITY_GATE", gate.GetTableName());
        AssertForeignKey<GeneratorProductionOperation>(gate, nameof(GeneratorProductionQualityGate.OperationId));
        AssertUniqueIndex(gate, nameof(GeneratorProductionQualityGate.OperationId));
        Assert.True(gate.FindProperty(nameof(GeneratorProductionQualityGate.RowVersion))!.IsConcurrencyToken);
        Assert.NotNull(operation.FindNavigation(nameof(GeneratorProductionOperation.QualityGate)));
    }

    [Fact]
    public void Execution_contract_exposes_release_completion_and_quality_decision_inputs()
    {
        var scheduleFields = typeof(GeneratorScheduleRow).GetProperties().Select(x => x.Name).ToHashSet();
        var completionFields = typeof(GeneratorOperationTransitionRequest).GetProperties().Select(x => x.Name).ToHashSet();

        Assert.Contains(nameof(GeneratorScheduleRow.QualityStatus), scheduleFields);
        Assert.Contains(nameof(GeneratorScheduleRow.QualityRowVersion), scheduleFields);
        Assert.Contains(nameof(GeneratorOperationTransitionRequest.GoodQuantity), completionFields);
        Assert.Contains(nameof(GeneratorOperationTransitionRequest.DefectQuantity), completionFields);
        Assert.Contains(nameof(GeneratorOperationTransitionRequest.ScrapQuantity), completionFields);
        Assert.Equal([GeneratorQualityGateStatus.Pending, GeneratorQualityGateStatus.Passed, GeneratorQualityGateStatus.Rejected],
            Enum.GetValues<GeneratorQualityGateStatus>());
    }

    private static IEntityType Entity<TEntity>(IModel model) =>
        model.FindEntityType(typeof(TEntity))
        ?? throw new InvalidOperationException($"{typeof(TEntity).Name} modelde bulunamadı.");

    private static void AssertUniqueIndex(IEntityType entity, params string[] properties)
    {
        var index = Assert.Single(entity.GetIndexes(), candidate =>
            candidate.Properties.Select(x => x.Name).SequenceEqual(properties));
        Assert.True(index.IsUnique);
        Assert.Equal("[IsDeleted] = 0", index.GetFilter());
    }

    private static void AssertForeignKey<TPrincipal>(IEntityType entity, string property)
    {
        var foreignKey = Assert.Single(entity.GetForeignKeys(), candidate =>
            candidate.Properties.Count == 1 && candidate.Properties[0].Name == property);
        Assert.Equal(typeof(TPrincipal), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    private static WmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True")
            .Options);
}
