using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualityInspectionPriorityTests
{
    [Fact]
    public void Toggle_priority_changes_open_inspection_both_directions_and_records_actor()
    {
        var inspection = new QualityInspection { Status = QualityInspectionStatus.Pending };
        var assignedAt = DateTimeOffset.Parse("2026-08-15T20:00:00Z");

        Assert.True(QualityService.TogglePriority(inspection, 41, assignedAt));
        Assert.True(inspection.IsPriority);
        Assert.Equal(assignedAt, inspection.PriorityAssignedAtUtc);
        Assert.Equal(41, inspection.UpdatedBy);
        Assert.Equal(assignedAt.UtcDateTime, inspection.UpdatedDate);

        Assert.False(QualityService.TogglePriority(inspection, 42, assignedAt.AddMinutes(1)));
        Assert.False(inspection.IsPriority);
        Assert.Null(inspection.PriorityAssignedAtUtc);
        Assert.Equal(42, inspection.UpdatedBy);
    }

    [Fact]
    public void List_sort_appends_newly_prioritized_rows_after_existing_priorities()
    {
        var firstAssigned = DateTimeOffset.Parse("2026-08-15T10:00:00Z");
        var laterAssigned = DateTimeOffset.Parse("2026-08-15T12:00:00Z");
        var earlyQueue = DateTimeOffset.Parse("2026-08-01T08:00:00Z");
        var lateQueue = DateTimeOffset.Parse("2026-08-14T08:00:00Z");
        var rows = new[]
        {
            new QualityInspectionGridRow
            {
                Id = 1, InspectionNo = "QC-C", IsPriority = true, PriorityAssignedAtUtc = firstAssigned, QueuedAtUtc = lateQueue,
            },
            new QualityInspectionGridRow
            {
                Id = 2, InspectionNo = "QC-A", IsPriority = false, QueuedAtUtc = earlyQueue,
            },
            new QualityInspectionGridRow
            {
                Id = 3, InspectionNo = "QC-B", IsPriority = true, PriorityAssignedAtUtc = laterAssigned, QueuedAtUtc = earlyQueue,
            },
        };

        var defaultOrder = QualityService.ApplyInspectionListSort(rows.AsQueryable(), new PagedRequest())
            .Select(row => row.Id)
            .ToArray();
        var columnOrder = QualityService.ApplyInspectionListSort(
                rows.AsQueryable(),
                new PagedRequest { SortBy = nameof(QualityInspectionGridRow.InspectionNo), SortDirection = "asc" })
            .Select(row => row.Id)
            .ToArray();

        Assert.Equal([1, 3, 2], defaultOrder);
        Assert.Equal([1, 3, 2], columnOrder);
    }

    [Fact]
    public void Priority_ranks_follow_assignment_order_per_branch_and_status()
    {
        var first = DateTimeOffset.Parse("2026-08-15T10:00:00Z");
        var second = DateTimeOffset.Parse("2026-08-15T12:00:00Z");
        var ranks = QualityService.BuildPriorityRanks(
        [
            (3, "7", QualityInspectionStatus.Pending.ToString(), second, DateTimeOffset.Parse("2026-08-01T08:00:00Z")),
            (1, "7", QualityInspectionStatus.Pending.ToString(), first, DateTimeOffset.Parse("2026-08-14T08:00:00Z")),
            (9, "8", QualityInspectionStatus.Quarantined.ToString(), first, null),
            (5, "7", QualityInspectionStatus.Quarantined.ToString(), first, null),
        ]);

        Assert.Equal(1, ranks[1]);
        Assert.Equal(2, ranks[3]);
        Assert.Equal(1, ranks[9]);
        Assert.Equal(1, ranks[5]);
    }

    [Fact]
    public void Reorder_priority_ids_moves_item_to_target_rank()
    {
        var reordered = QualityService.ReorderPriorityIds([10L, 20L, 30L, 40L], 30L, 1);
        Assert.Equal([30L, 10L, 20L, 40L], reordered);

        reordered = QualityService.ReorderPriorityIds([10L, 20L, 30L, 40L], 10L, 4);
        Assert.Equal([20L, 30L, 40L, 10L], reordered);
    }

    [Fact]
    public void Apply_priority_order_rewrites_assignment_times_in_sequence()
    {
        var assignedAt = DateTimeOffset.Parse("2026-08-15T10:00:00Z");
        var inspections = new[]
        {
            new QualityInspection { Id = 1, IsPriority = true },
            new QualityInspection { Id = 2, IsPriority = true },
            new QualityInspection { Id = 3, IsPriority = true },
        };

        QualityService.ApplyPriorityOrder(inspections, [3, 1, 2], 77, assignedAt);

        Assert.Equal(assignedAt, inspections.Single(x => x.Id == 3).PriorityAssignedAtUtc);
        Assert.Equal(assignedAt.AddMinutes(1), inspections.Single(x => x.Id == 1).PriorityAssignedAtUtc);
        Assert.Equal(assignedAt.AddMinutes(2), inspections.Single(x => x.Id == 2).PriorityAssignedAtUtc);
        Assert.All(inspections, inspection => Assert.Equal(77, inspection.UpdatedBy));
    }

    [Theory]
    [InlineData(QualityInspectionStatus.Pending, true)]
    [InlineData(QualityInspectionStatus.InProgress, true)]
    [InlineData(QualityInspectionStatus.PartiallyDecided, true)]
    [InlineData(QualityInspectionStatus.Quarantined, true)]
    [InlineData(QualityInspectionStatus.Passed, false)]
    [InlineData(QualityInspectionStatus.Failed, false)]
    [InlineData(QualityInspectionStatus.Released, false)]
    [InlineData(QualityInspectionStatus.Cancelled, false)]
    public void Only_open_inspections_can_be_prioritized(QualityInspectionStatus status, bool expected)
    {
        Assert.Equal(expected, QualityService.CanPrioritize(status));
    }

    [Fact]
    public void Status_catalog_is_derived_from_domain_enum_and_defaults_to_pending()
    {
        var catalog = QualityService.BuildInspectionStatusCatalog();

        Assert.Equal(QualityInspectionStatus.Pending.ToString(), catalog.DefaultValue);
        Assert.Equal(Enum.GetValues<QualityInspectionStatus>().Length, catalog.Items.Count);
        Assert.DoesNotContain(catalog.Items, item => item.Value == "Queued");
        Assert.True(catalog.Items.Single(item => item.Value == "Pending").IsDefault);
        Assert.True(catalog.Items.Single(item => item.Value == "Pending").CanPrioritize);
        Assert.False(catalog.Items.Single(item => item.Value == "Passed").CanPrioritize);
        Assert.True(catalog.Items.Single(item => item.Value == "Passed").IsTerminal);
    }
}
