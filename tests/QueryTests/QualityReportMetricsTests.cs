using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualityReportMetricsTests
{
    [Fact]
    public void Inspection_report_separates_active_work_elapsed_time_and_pauses()
    {
        var start = new DateTimeOffset(2026, 8, 15, 8, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(3);
        var row = new QualityInspectionReportRow { DecidedAtUtc = end };
        var sessions = new[]
        {
            Session(1, 11, "Ayşe", start, start.AddMinutes(45), 45 * 60,
                QualityInspectionWorkStopReason.Break, "Çay molası"),
            Session(2, 11, "Ayşe", start.AddHours(1), start.AddHours(2), 60 * 60,
                QualityInspectionWorkStopReason.MaterialWait, "Numune bekleniyor"),
            Session(3, 22, "Mehmet", start.AddHours(2).AddMinutes(30), end, 30 * 60,
                QualityInspectionWorkStopReason.DecisionApplied, null)
        };

        QualityReportService.ApplyWorkMetrics(row, sessions, end);

        Assert.Equal(2 * 60 * 60 + 15 * 60, row.ActiveWorkSeconds);
        Assert.Equal(3 * 60 * 60, row.ElapsedSeconds);
        Assert.Equal(45 * 60, row.PauseSeconds);
        Assert.Equal(2, row.PauseCount);
        Assert.Equal(1, row.BreakCount);
        Assert.Equal(2, row.ParticipantCount);
        Assert.Equal("Ayşe · Mehmet", row.Participants);
    }

    [Fact]
    public void Pause_timeline_keeps_reason_note_and_gap_until_next_operator()
    {
        var start = new DateTimeOffset(2026, 8, 15, 8, 0, 0, TimeSpan.Zero);
        var sessions = new[]
        {
            Session(1, 11, "Ayşe", start, start.AddMinutes(30), 1800,
                QualityInspectionWorkStopReason.Handover, "Vardiya devri"),
            Session(2, 22, "Mehmet", start.AddMinutes(50), start.AddHours(1), 600,
                QualityInspectionWorkStopReason.DecisionApplied, null)
        };

        var pauses = QualityReportService.BuildPauseMetrics(sessions, start.AddHours(1));

        var pause = Assert.Single(pauses);
        Assert.Equal(QualityInspectionWorkStopReason.Handover, pause.Reason);
        Assert.Equal("Vardiya devri", pause.Note);
        Assert.Equal(20 * 60, pause.PauseSecondsUntilNextSession);
    }

    [Fact]
    public void Worker_report_accumulates_each_workers_sessions_without_double_counting()
    {
        var start = new DateTimeOffset(2026, 8, 15, 8, 0, 0, TimeSpan.Zero);
        var sessions = new[]
        {
            Session(1, 11, "Ayşe", start, start.AddMinutes(20), 1200,
                QualityInspectionWorkStopReason.Break, null),
            Session(2, 11, "Ayşe", start.AddMinutes(30), start.AddMinutes(50), 1200,
                QualityInspectionWorkStopReason.Handover, null),
            Session(3, 22, "Mehmet", start.AddHours(1), start.AddHours(1).AddMinutes(10), 600,
                QualityInspectionWorkStopReason.DecisionApplied, null)
        };

        var workers = QualityReportService.BuildWorkerMetrics(sessions);

        Assert.Equal(2, workers.Count);
        Assert.Equal(2400, workers[0].ActiveWorkSeconds);
        Assert.Equal(2, workers[0].SessionCount);
        Assert.Equal(600, workers[1].ActiveWorkSeconds);
    }

    private static QualityInspectionWorkSession Session(
        int sequence,
        long userId,
        string userName,
        DateTimeOffset started,
        DateTimeOffset ended,
        long durationSeconds,
        QualityInspectionWorkStopReason reason,
        string? note) => new()
        {
            SequenceNo = sequence,
            WorkerUserId = userId,
            WorkerNameSnapshot = userName,
            StartedAtUtc = started,
            EndedAtUtc = ended,
            DurationSeconds = durationSeconds,
            StopReason = reason,
            StopNote = note
        };
}
