using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed record KkdPolicyDto(
    long Id,
    string BranchCode,
    bool RequireOpenOrder,
    bool AllowOpenOrderExcess,
    bool AllowMultipleOrdersPerDistribution,
    bool RequireEmployeeUserLink,
    bool AllowFutureDatedDistribution,
    long? UpdatedBy,
    DateTime? UpdatedDate);

public sealed record UpdateKkdPolicyRequest(
    bool RequireOpenOrder,
    bool AllowOpenOrderExcess,
    bool AllowMultipleOrdersPerDistribution,
    bool RequireEmployeeUserLink,
    bool AllowFutureDatedDistribution);

public interface IKkdPolicyService
{
    Task<KkdPolicyDto> GetAsync(string branchCode, CancellationToken ct = default);
    Task<KkdPolicyDto> UpdateAsync(string branchCode, UpdateKkdPolicyRequest request, long actor, CancellationToken ct = default);
}

public sealed class KkdPolicyService(IUnitOfWork uow) : IKkdPolicyService
{
    private const string DefaultKey = "DEFAULT";

    public async Task<KkdPolicyDto> GetAsync(string branchCode, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        var entity = await uow.Repository<KkdPolicy>().Query()
            .SingleOrDefaultAsync(x => x.BranchCode == branch && x.PolicyKey == DefaultKey, ct);
        return Map(entity ?? NewDefault(branch));
    }

    public async Task<KkdPolicyDto> UpdateAsync(
        string branchCode,
        UpdateKkdPolicyRequest request,
        long actor,
        CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        var repository = uow.Repository<KkdPolicy>();
        var entity = await repository.Query(true)
            .SingleOrDefaultAsync(x => x.BranchCode == branch && x.PolicyKey == DefaultKey, ct);
        var now = DateTime.UtcNow;
        if (entity is null)
        {
            entity = NewDefault(branch);
            entity.CreatedBy = actor;
            entity.CreatedDate = now;
            await repository.AddAsync(entity, ct);
        }

        entity.RequireOpenOrder = request.RequireOpenOrder;
        entity.AllowOpenOrderExcess = request.AllowOpenOrderExcess;
        entity.AllowMultipleOrdersPerDistribution = request.AllowMultipleOrdersPerDistribution;
        entity.RequireEmployeeUserLink = request.RequireEmployeeUserLink;
        entity.AllowFutureDatedDistribution = request.AllowFutureDatedDistribution;
        entity.UpdatedBy = actor;
        entity.UpdatedDate = now;
        await uow.SaveChangesAsync(ct);
        return Map(entity);
    }

    private static KkdPolicy NewDefault(string branchCode) => new()
    {
        BranchCode = branchCode,
        PolicyKey = DefaultKey,
        RequireOpenOrder = true,
        AllowOpenOrderExcess = true,
        AllowMultipleOrdersPerDistribution = true,
        RequireEmployeeUserLink = false,
        AllowFutureDatedDistribution = false
    };

    private static KkdPolicyDto Map(KkdPolicy x) => new(
        x.Id, x.BranchCode, x.RequireOpenOrder, x.AllowOpenOrderExcess,
        x.AllowMultipleOrdersPerDistribution, x.RequireEmployeeUserLink,
        x.AllowFutureDatedDistribution, x.UpdatedBy, x.UpdatedDate);

    private static string NormalizeBranch(string branchCode) =>
        string.IsNullOrWhiteSpace(branchCode) ? "0" : branchCode.Trim();
}
