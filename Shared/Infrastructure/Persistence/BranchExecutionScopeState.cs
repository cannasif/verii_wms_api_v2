namespace verii_wms_api_v2.Shared.Infrastructure.Persistence;

public sealed class BranchExecutionScopeState
{
    private bool _isOverridden;
    private string? _branchCode;

    public bool IsOverridden => _isOverridden;
    public string? BranchCode => _branchCode;

    public IDisposable Begin(string? branchCode)
    {
        var previousOverride = _isOverridden;
        var previousBranch = _branchCode;
        _isOverridden = true;
        _branchCode = string.IsNullOrWhiteSpace(branchCode) ? null : branchCode.Trim();
        return new ScopeLease(this, previousOverride, previousBranch);
    }

    private sealed class ScopeLease(
        BranchExecutionScopeState owner,
        bool previousOverride,
        string? previousBranch) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            owner._isOverridden = previousOverride;
            owner._branchCode = previousBranch;
            _disposed = true;
        }
    }
}
