using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.Identity.Application;

public sealed record UserWarehouseAccess(
    bool IsRestricted,
    IReadOnlyList<long> WarehouseIds,
    IReadOnlyList<int> WarehouseCodes);

public static class UserWarehouseAccessService
{
    public static async Task<UserWarehouseAccess> ResolveAsync(
        IUnitOfWork unitOfWork,
        long userId,
        string branchCode,
        CancellationToken cancellationToken)
    {
        var role = await unitOfWork.Repository<User>().Query()
            .Where(x => x.Id == userId && x.IsActive)
            .Select(x => x.Role)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw AppException.Forbidden("Aktif kullanıcı bulunamadı.");
        if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || role.Equals("superadmin", StringComparison.OrdinalIgnoreCase))
            return new(false, [], []);

        var assignedIds = await unitOfWork.Repository<UserWarehouseAssignment>().Query()
            .Where(x => x.UserId == userId)
            .Select(x => x.WarehouseId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (assignedIds.Count == 0)
            return new(false, [], []);

        var warehouses = await unitOfWork.Repository<WarehouseEntity>().Query()
            .Where(x => assignedIds.Contains(x.Id) && x.BranchCode == branchCode)
            .Select(x => new { x.Id, x.WarehouseCode })
            .ToListAsync(cancellationToken);
        return new(
            true,
            warehouses.Select(x => x.Id).OrderBy(x => x).ToArray(),
            warehouses.Select(x => x.WarehouseCode).Distinct().OrderBy(x => x).ToArray());
    }

    public static async Task EnsureAsync(
        IUnitOfWork unitOfWork,
        long userId,
        string branchCode,
        IEnumerable<long> warehouseIds,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(unitOfWork, userId, branchCode, cancellationToken);
        if (!access.IsRestricted)
            return;
        var denied = warehouseIds.Distinct().Where(x => !access.WarehouseIds.Contains(x)).ToArray();
        if (denied.Length > 0)
            throw AppException.Forbidden("Seçilen depo kullanıcıya tanımlı değildir; bu depoda mal kabul işlemi yapılamaz.");
    }
}
