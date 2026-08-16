using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualityInspectionWorkSessionTests
{
    [Fact]
    public void Work_summary_accumulates_multiple_operators_and_keeps_one_active_owner()
    {
        var now = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var inspection = new QualityInspection
        {
            Status = QualityInspectionStatus.InProgress,
            WorkSessions =
            [
                new QualityInspectionWorkSession
                {
                    WorkerUserId = 11,
                    WorkerNameSnapshot = "X Kullanıcısı",
                    StartedAtUtc = now.AddHours(-15),
                    EndedAtUtc = now.AddHours(-10),
                    DurationSeconds = 5 * 60 * 60,
                    StopReason = QualityInspectionWorkStopReason.Handover
                },
                new QualityInspectionWorkSession
                {
                    WorkerUserId = 22,
                    WorkerNameSnapshot = "Y Kullanıcısı",
                    StartedAtUtc = now.AddHours(-10)
                }
            ]
        };

        var result = QualityService.BuildWorkSummary(
            inspection, 22, canExecute: true, canSupervise: false, canDecide: true,
            receiptReady: true, now);

        Assert.Equal(QualityInspectionWorkState.Running, result.State);
        Assert.Equal(15 * 60 * 60, result.TotalWorkedSeconds);
        Assert.Equal(10 * 60 * 60, result.CurrentUserWorkedSeconds);
        Assert.Equal(2, result.SessionCount);
        Assert.Equal(2, result.ParticipantCount);
        Assert.Equal(22, result.ActiveWorkerUserId);
        Assert.False(result.CanStart);
        Assert.True(result.CanPause);
        Assert.True(result.CanApplyDecision);
    }

    [Fact]
    public void Another_operator_cannot_apply_decision_but_supervisor_can_stop_active_session()
    {
        var now = DateTimeOffset.UtcNow;
        var inspection = new QualityInspection
        {
            Status = QualityInspectionStatus.InProgress,
            WorkSessions =
            [
                new QualityInspectionWorkSession
                {
                    WorkerUserId = 11,
                    WorkerNameSnapshot = "Aktif Kullanıcı",
                    StartedAtUtc = now.AddMinutes(-30)
                }
            ]
        };

        var result = QualityService.BuildWorkSummary(
            inspection, 22, canExecute: true, canSupervise: true, canDecide: true,
            receiptReady: true, now);

        Assert.True(result.CanPause);
        Assert.False(result.CanApplyDecision);
        Assert.False(result.CanStart);
    }

    [Fact]
    public void Closing_session_records_exact_duration_reason_and_actor()
    {
        var start = new DateTimeOffset(2026, 8, 14, 8, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(5).AddSeconds(9);
        var key = Guid.NewGuid();
        var session = new QualityInspectionWorkSession
        {
            StartedAtUtc = start,
            WorkerUserId = 11,
            WorkerNameSnapshot = "Kalite Operatörü"
        };

        QualityService.CloseWorkSession(
            session, end, QualityInspectionWorkStopReason.ShiftEnd, "Vardiya devri", key, 11);

        Assert.Equal(end, session.EndedAtUtc);
        Assert.Equal(18_009, session.DurationSeconds);
        Assert.Equal(QualityInspectionWorkStopReason.ShiftEnd, session.StopReason);
        Assert.Equal("Vardiya devri", session.StopNote);
        Assert.Equal(key, session.EndIdempotencyKey);
        Assert.Equal(11, session.EndedByUserId);
    }

    [Fact]
    public void Closed_inspection_reports_completed_and_cannot_start_again()
    {
        var now = DateTimeOffset.UtcNow;
        var inspection = new QualityInspection
        {
            Status = QualityInspectionStatus.Passed,
            WorkSessions =
            [
                new QualityInspectionWorkSession
                {
                    WorkerUserId = 11,
                    WorkerNameSnapshot = "Kalite Operatörü",
                    StartedAtUtc = now.AddMinutes(-20),
                    EndedAtUtc = now,
                    DurationSeconds = 1200,
                    StopReason = QualityInspectionWorkStopReason.DecisionApplied
                }
            ]
        };

        var result = QualityService.BuildWorkSummary(
            inspection, 11, canExecute: true, canSupervise: true, canDecide: true,
            receiptReady: true, now);

        Assert.Equal(QualityInspectionWorkState.Completed, result.State);
        Assert.False(result.CanStart);
        Assert.False(result.CanPause);
        Assert.False(result.CanApplyDecision);
    }

    [Fact]
    public void Pausing_idle_in_progress_inspection_returns_it_to_pending()
    {
        var now = DateTimeOffset.UtcNow;
        var inspection = new QualityInspection
        {
            Status = QualityInspectionStatus.InProgress,
            WorkSessions =
            [
                new QualityInspectionWorkSession
                {
                    WorkerUserId = 11,
                    WorkerNameSnapshot = "Kalite Operatörü",
                    StartedAtUtc = now.AddMinutes(-20),
                    EndedAtUtc = now,
                    DurationSeconds = 1200,
                    StopReason = QualityInspectionWorkStopReason.Break
                }
            ]
        };

        Assert.True(QualityService.TryRevertIdleInProgress(inspection));
        Assert.Equal(QualityInspectionStatus.Pending, inspection.Status);
    }

    [Fact]
    public void Running_or_partially_decided_inspection_does_not_revert_to_pending()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new QualityInspection
        {
            Status = QualityInspectionStatus.InProgress,
            WorkSessions =
            [
                new QualityInspectionWorkSession
                {
                    WorkerUserId = 11,
                    WorkerNameSnapshot = "Kalite Operatörü",
                    StartedAtUtc = now.AddMinutes(-10)
                }
            ]
        };
        var partial = new QualityInspection
        {
            Status = QualityInspectionStatus.PartiallyDecided,
            WorkSessions =
            [
                new QualityInspectionWorkSession
                {
                    WorkerUserId = 11,
                    WorkerNameSnapshot = "Kalite Operatörü",
                    StartedAtUtc = now.AddMinutes(-20),
                    EndedAtUtc = now,
                    DurationSeconds = 1200,
                    StopReason = QualityInspectionWorkStopReason.Break
                }
            ]
        };

        Assert.False(QualityService.TryRevertIdleInProgress(running));
        Assert.Equal(QualityInspectionStatus.InProgress, running.Status);
        Assert.False(QualityService.TryRevertIdleInProgress(partial));
        Assert.Equal(QualityInspectionStatus.PartiallyDecided, partial.Status);
    }

    [Fact]
    public void Last_work_actors_use_latest_session_starter_and_stopper()
    {
        var now = DateTimeOffset.UtcNow;
        var inspection = new QualityInspection
        {
            WorkSessions =
            [
                new QualityInspectionWorkSession
                {
                    SequenceNo = 1,
                    WorkerUserId = 11,
                    WorkerNameSnapshot = "Ali Yılmaz",
                    StartedAtUtc = now.AddHours(-2),
                    EndedAtUtc = now.AddHours(-1),
                    EndedByUserId = 11
                },
                new QualityInspectionWorkSession
                {
                    SequenceNo = 2,
                    WorkerUserId = 22,
                    WorkerNameSnapshot = "Ayşe Demir",
                    StartedAtUtc = now.AddMinutes(-20),
                    EndedAtUtc = now,
                    EndedByUserId = 33
                }
            ]
        };

        var actors = QualityService.ResolveLastWorkActors(inspection);

        Assert.Equal("Ayşe Demir", actors.StartedByName);
        Assert.Equal(22, actors.WorkerUserId);
        Assert.Equal(33, actors.StoppedByUserId);
    }

    [Fact]
    public void Running_session_has_starter_but_no_stopper()
    {
        var now = DateTimeOffset.UtcNow;
        var inspection = new QualityInspection
        {
            WorkSessions =
            [
                new QualityInspectionWorkSession
                {
                    SequenceNo = 1,
                    WorkerUserId = 11,
                    WorkerNameSnapshot = "Ali Yılmaz",
                    StartedAtUtc = now.AddMinutes(-10)
                }
            ]
        };

        var actors = QualityService.ResolveLastWorkActors(inspection);

        Assert.Equal("Ali Yılmaz", actors.StartedByName);
        Assert.Equal(11, actors.WorkerUserId);
        Assert.Null(actors.StoppedByUserId);
    }

    [Fact]
    public void Progress_control_quantity_reduces_remaining_inspectable_amount()
    {
        var line = new QualityInspectionLine
        {
            Quantity = 100,
            SampleQuantity = 10,
            InspectedQuantity = 4
        };

        line.InspectedQuantity += 6;

        Assert.Equal(10m, line.InspectedQuantity);
        Assert.Equal(90m, QualityService.RemainingInspectableQuantity(line));
        Assert.Equal(0m, QualityService.RequiredControlQuantityForDecision(line));
    }
}
