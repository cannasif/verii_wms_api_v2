using verii_wms_api_v2.Modules.GeneratorProduction.Domain;

namespace verii_wms_api_v2.Modules.GeneratorProduction.Application;

internal sealed record GeneratorWorkingDayOverride(bool IsWorking, int? CapacityMinutes);
internal sealed record GeneratorStationCalendar(
    TimeOnly StartTime, TimeOnly EndTime, int WeekdayMask, int DailyCapacityMinutes,
    IReadOnlyDictionary<DateOnly, GeneratorWorkingDayOverride> Overrides);

internal static class GeneratorProductionPlanningPolicy
{
    public static string? NormalizeProjectCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    public static bool CanUseProjectSupply(string? supplyProjectCode, string projectCode)
    {
        var normalizedSupply = NormalizeProjectCode(supplyProjectCode);
        return normalizedSupply is null || normalizedSupply == NormalizeProjectCode(projectCode);
    }

    public static IReadOnlyList<GeneratorPartType> SelectRoutes(GeneratorProductionProject project)
    {
        var parts = new List<GeneratorPartType>(4);
        if (project.HasStator) parts.Add(GeneratorPartType.Stator);
        if (project.HasRotor) parts.Add(GeneratorPartType.Rotor);
        if (project.HasStiffener) parts.Add(GeneratorPartType.Stiffener);
        if (project.IncludeFinalAssembly) parts.Add(GeneratorPartType.FinalAssembly);
        return parts;
    }

    public static DateTime NextWorkingInstant(DateTime value, GeneratorStationCalendar calendar, int searchLimitDays)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(searchLimitDays);
        var cursor = AsUtc(value);
        for (var dayOffset = 0; dayOffset < searchLimitDays; dayOffset++)
        {
            var date = DateOnly.FromDateTime(cursor);
            if (IsWorkingDay(date, calendar))
            {
                var start = AtUtc(date, calendar.StartTime);
                var end = DayEnd(date, calendar);
                if (cursor <= start) return start;
                if (cursor < end) return cursor;
            }
            cursor = DateTime.SpecifyKind(cursor.Date.AddDays(1), DateTimeKind.Utc);
        }
        throw new InvalidOperationException("Çalışılabilir takvim günü bulunamadı.");
    }

    public static DateTime AddWorkingMinutes(DateTime start, int minutes, GeneratorStationCalendar calendar, int searchLimitDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutes);
        var current = NextWorkingInstant(start, calendar, searchLimitDays); var remaining = minutes;
        while (remaining > 0)
        {
            var available = (int)(DayEnd(DateOnly.FromDateTime(current), calendar) - current).TotalMinutes;
            if (remaining <= available) return current.AddMinutes(remaining);
            remaining -= Math.Max(0, available);
            current = NextWorkingInstant(DateTime.SpecifyKind(current.Date.AddDays(1), DateTimeKind.Utc), calendar, searchLimitDays);
        }
        return current;
    }

    public static DateTime SubtractWorkingMinutes(DateTime end, int minutes, GeneratorStationCalendar calendar, int searchLimitDays)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentOutOfRangeException.ThrowIfNegative(minutes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(searchLimitDays);
        var cursor = AsUtc(end);
        var remaining = minutes;
        if (remaining == 0) return cursor;

        for (var dayOffset = 0; dayOffset < searchLimitDays; dayOffset++)
        {
            var date = DateOnly.FromDateTime(cursor);
            if (IsWorkingDay(date, calendar))
            {
                var start = AtUtc(date, calendar.StartTime);
                var dayEnd = DayEnd(date, calendar);
                var effectiveEnd = cursor < dayEnd ? cursor : dayEnd;
                if (effectiveEnd > start)
                {
                    var available = (int)(effectiveEnd - start).TotalMinutes;
                    if (remaining <= available) return effectiveEnd.AddMinutes(-remaining);
                    remaining -= available;
                }
            }
            cursor = DateTime.SpecifyKind(cursor.Date.AddTicks(-1), DateTimeKind.Utc);
        }
        throw new InvalidOperationException("Çalışılabilir takvim günü bulunamadı.");
    }

    public static int[] SelectEarliestCapacityLanes(IReadOnlyList<DateTime> availability, int requiredQuantity)
    {
        ArgumentNullException.ThrowIfNull(availability);
        if (requiredQuantity < 1 || requiredQuantity > availability.Count)
            throw new ArgumentOutOfRangeException(nameof(requiredQuantity));
        return Enumerable.Range(0, availability.Count)
            .OrderBy(index => availability[index])
            .Take(requiredQuantity)
            .ToArray();
    }

    private static bool IsWorkingDay(DateOnly date, GeneratorStationCalendar calendar)
    {
        if (calendar.Overrides.TryGetValue(date, out var exception)) return exception.IsWorking;
        var dayIndex = date.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)date.DayOfWeek - 1;
        return (calendar.WeekdayMask & (1 << dayIndex)) != 0;
    }

    private static DateTime DayEnd(DateOnly date, GeneratorStationCalendar calendar)
    {
        var start = AtUtc(date, calendar.StartTime);
        var nominalMinutes = calendar.EndTime > calendar.StartTime
            ? (int)(calendar.EndTime - calendar.StartTime).TotalMinutes
            : 1440 - (int)calendar.StartTime.ToTimeSpan().TotalMinutes + (int)calendar.EndTime.ToTimeSpan().TotalMinutes;
        var exceptionCapacity = calendar.Overrides.TryGetValue(date, out var exception) ? exception.CapacityMinutes : null;
        var capacity = exceptionCapacity ?? calendar.DailyCapacityMinutes;
        return start.AddMinutes(Math.Max(1, Math.Min(nominalMinutes, capacity)));
    }

    private static DateTime AtUtc(DateOnly date, TimeOnly time) => DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Utc);

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
