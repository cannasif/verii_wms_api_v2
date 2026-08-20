namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed record KkdSimpleMatrixWorkbookIssue(
    string Code,
    string Message,
    string? Sheet = null,
    int? Row = null,
    string? Cell = null);

public sealed record KkdSimpleMatrixWorkbookPreview(
    string FileHash,
    string StateHash,
    bool CanCommit,
    int SourceRowCount,
    int MatrixCount,
    int RuleCount,
    int PhaseCount,
    int CreateCount,
    int UpdateCount,
    int DuplicateRowCount,
    IReadOnlyList<KkdSimpleMatrixWorkbookIssue> Errors,
    IReadOnlyList<KkdSimpleMatrixWorkbookIssue> Warnings);

public sealed record KkdSimpleMatrixWorkbookImportResult(
    string FileHash,
    int Created,
    int Updated,
    int Processed,
    IReadOnlyList<string> Warnings);

public interface IKkdSimpleMatrixWorkbookService
{
    Task<byte[]> CreateTemplateAsync(
        long customerId,
        string branchCode,
        CancellationToken ct = default);

    Task<KkdSimpleMatrixWorkbookPreview> PreviewAsync(
        Stream workbookStream,
        long customerId,
        DateOnly effectiveFrom,
        string branchCode,
        CancellationToken ct = default);

    Task<KkdSimpleMatrixWorkbookImportResult> ImportAsync(
        Stream workbookStream,
        long customerId,
        DateOnly effectiveFrom,
        string branchCode,
        string previewHash,
        string stateHash,
        long actor,
        CancellationToken ct = default);
}
