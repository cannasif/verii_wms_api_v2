using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class ProductionWorkOrderLazyRecipeContractTests
{
    [Fact]
    public void Work_order_list_disables_bulk_recipe_materialization()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var servicePath = Path.Combine(
            repositoryRoot,
            "Modules",
            "Production",
            "Application",
            "ProductionService.cs");
        var snapshotPath = Path.Combine(
            repositoryRoot,
            "Modules",
            "Production",
            "Application",
            "ProductionService.WorkOrderAssignmentSnapshot.cs");

        var serviceSource = File.ReadAllText(servicePath);
        var snapshotSource = File.ReadAllText(snapshotPath);

        Assert.Contains("loadRecipes: false", serviceSource, StringComparison.Ordinal);
        Assert.Contains("if (loadRecipes)", snapshotSource, StringComparison.Ordinal);
        Assert.Contains(
            "recipesByWorkOrder = await LoadRecipeMaterialsByWorkOrderAsync",
            snapshotSource,
            StringComparison.Ordinal);
    }
}
