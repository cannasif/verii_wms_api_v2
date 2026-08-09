using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;

namespace verii_wms_api_v2.Modules.Kkd.Application;

/// <summary>
/// Dağıtım yaşam döngüsündeki (oluşturma/teslim/iptal) miktar hareketlerini KKD hazırlama
/// görevlerine yansıtır. Bir talep kalemi aynı anda en fazla bir aktif görevde bulunur.
/// </summary>
internal static class KkdPreparationTaskProgress
{
    /// <summary>Dağıtım taslağı oluşturuldu: göreve hazırlanan miktar işlenir, görev "Hazırlanıyor"a geçer ve belgeye bağlanır.</summary>
    public static async Task ApplyPreparationAsync(
        IUnitOfWork uow,
        IReadOnlyDictionary<long, decimal> quantityByRequestLineId,
        KkdDistribution distribution,
        long actor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var taskLines = await ActiveTaskLinesAsync(uow, quantityByRequestLineId.Keys, ct);
        foreach (var taskLine in taskLines)
        {
            var quantity = quantityByRequestLineId[taskLine.RequestLineId];
            taskLine.PreparedQuantity = Math.Min(taskLine.Quantity, taskLine.PreparedQuantity + quantity);
            taskLine.UpdatedBy = actor;
            taskLine.UpdatedDate = now.UtcDateTime;

            var task = taskLine.Task;
            if (task.Status == KkdPreparationTaskStatus.Assigned)
                task.Status = KkdPreparationTaskStatus.InPreparation;
            task.StartedAtUtc ??= now;
            // Görev, kalemlerini hazırlayan ilk dağıtım belgesine bağlanır.
            if (task.DistributionId is null && task.Distribution is null)
                task.Distribution = distribution;
            task.UpdatedBy = actor;
            task.UpdatedDate = now.UtcDateTime;
        }
    }

    /// <summary>Dağıtım tamamlandı (teslim): teslim edilen miktar işlenir, tüm kalemleri teslim edilen görev kapanır.</summary>
    public static async Task ApplyDeliveryAsync(
        IUnitOfWork uow,
        IReadOnlyDictionary<long, decimal> quantityByRequestLineId,
        long actor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var taskLines = await ActiveTaskLinesAsync(uow, quantityByRequestLineId.Keys, ct);
        foreach (var taskLine in taskLines)
        {
            var quantity = quantityByRequestLineId[taskLine.RequestLineId];
            taskLine.DeliveredQuantity = Math.Min(taskLine.Quantity, taskLine.DeliveredQuantity + quantity);
            taskLine.UpdatedBy = actor;
            taskLine.UpdatedDate = now.UtcDateTime;
        }
        foreach (var task in taskLines.Select(x => x.Task).Distinct())
        {
            if (task.Lines.Where(x => !x.IsDeleted).All(x => x.DeliveredQuantity >= x.Quantity))
            {
                task.Status = KkdPreparationTaskStatus.Completed;
                task.CompletedAtUtc = now;
            }
            task.UpdatedBy = actor;
            task.UpdatedDate = now.UtcDateTime;
        }
    }

    /// <summary>Dağıtım iptali: hazırlanan (veya tamamlanmışsa teslim edilen) miktar geri alınır, görev uygun duruma döner.</summary>
    public static async Task RevertAsync(
        IUnitOfWork uow,
        IReadOnlyDictionary<long, decimal> quantityByRequestLineId,
        bool wasCompleted,
        long distributionId,
        long actor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Tamamlanan dağıtımın iptalinde görev Completed durumundan geri açılır.
        var statuses = wasCompleted
            ? new[] { KkdPreparationTaskStatus.Assigned, KkdPreparationTaskStatus.InPreparation, KkdPreparationTaskStatus.Completed }
            : [KkdPreparationTaskStatus.Assigned, KkdPreparationTaskStatus.InPreparation];
        var lineIds = quantityByRequestLineId.Keys.ToArray();
        var taskLines = await uow.Repository<KkdPreparationTaskLine>().Query(true)
            .Include(x => x.Task).ThenInclude(x => x.Lines)
            .Where(x => lineIds.Contains(x.RequestLineId) && statuses.Contains(x.Task.Status))
            .ToListAsync(ct);
        foreach (var taskLine in taskLines)
        {
            var quantity = quantityByRequestLineId[taskLine.RequestLineId];
            if (wasCompleted)
                taskLine.DeliveredQuantity = Math.Max(0, taskLine.DeliveredQuantity - quantity);
            taskLine.PreparedQuantity = Math.Max(0, taskLine.PreparedQuantity - quantity);
            taskLine.UpdatedBy = actor;
            taskLine.UpdatedDate = now.UtcDateTime;
        }
        foreach (var task in taskLines.Select(x => x.Task).Distinct())
        {
            var lines = task.Lines.Where(x => !x.IsDeleted).ToArray();
            task.Status = lines.All(x => x.DeliveredQuantity >= x.Quantity) && lines.Length > 0
                ? KkdPreparationTaskStatus.Completed
                : lines.Any(x => x.PreparedQuantity > 0 || x.DeliveredQuantity > 0)
                    ? KkdPreparationTaskStatus.InPreparation
                    : KkdPreparationTaskStatus.Assigned;
            if (task.Status != KkdPreparationTaskStatus.Completed) task.CompletedAtUtc = null;
            if (task.DistributionId == distributionId && task.Status == KkdPreparationTaskStatus.Assigned)
                task.DistributionId = null;
            task.UpdatedBy = actor;
            task.UpdatedDate = now.UtcDateTime;
        }
    }

    private static async Task<List<KkdPreparationTaskLine>> ActiveTaskLinesAsync(
        IUnitOfWork uow, IEnumerable<long> requestLineIds, CancellationToken ct)
    {
        var lineIds = requestLineIds.ToArray();
        return await uow.Repository<KkdPreparationTaskLine>().Query(true)
            .Include(x => x.Task).ThenInclude(x => x.Lines)
            .Where(x => lineIds.Contains(x.RequestLineId)
                && (x.Task.Status == KkdPreparationTaskStatus.Assigned || x.Task.Status == KkdPreparationTaskStatus.InPreparation))
            .ToListAsync(ct);
    }
}
