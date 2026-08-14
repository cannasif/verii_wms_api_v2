namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed record KkdDefinitionWorkbookCategoryResult(
    int Created,
    int Updated,
    int Unchanged)
{
    public int Processed => Created + Updated + Unchanged;
}

public sealed record KkdDefinitionWorkbookImportResult(
    int TotalRows,
    int Created,
    int Updated,
    int Unchanged,
    KkdDefinitionWorkbookCategoryResult Departments,
    KkdDefinitionWorkbookCategoryResult Roles,
    KkdDefinitionWorkbookCategoryResult Employees,
    KkdDefinitionWorkbookCategoryResult Matrices,
    IReadOnlyList<string> Warnings);

public interface IKkdDefinitionWorkbookService
{
    Task<byte[]> CreateTemplateAsync(string branchCode, CancellationToken ct = default);

    Task<KkdDefinitionWorkbookImportResult> ImportAsync(
        Stream workbookStream,
        string branchCode,
        long actor,
        CancellationToken ct = default);
}
