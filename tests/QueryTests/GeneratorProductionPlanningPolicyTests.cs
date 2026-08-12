using verii_wms_api_v2.Modules.GeneratorProduction.Application;
using verii_wms_api_v2.Modules.GeneratorProduction.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GeneratorProductionPlanningPolicyTests
{
    [Fact]
    public void Final_assembly_is_selected_after_component_routes()
    {
        var project = new GeneratorProductionProject { HasStator = true, HasRotor = true, HasStiffener = true, IncludeFinalAssembly = true };

        var routes = GeneratorProductionPlanningPolicy.SelectRoutes(project);

        Assert.Equal([GeneratorPartType.Stator, GeneratorPartType.Rotor, GeneratorPartType.Stiffener, GeneratorPartType.FinalAssembly], routes);
    }

    [Fact]
    public void Component_only_project_does_not_create_final_assembly_route()
    {
        var project = new GeneratorProductionProject { HasStator = true, HasRotor = false, HasStiffener = false, IncludeFinalAssembly = false };

        Assert.Equal([GeneratorPartType.Stator], GeneratorProductionPlanningPolicy.SelectRoutes(project));
    }

    [Fact]
    public void Working_minutes_continue_on_monday_after_friday_shift_end()
    {
        var friday = new DateTime(2026, 8, 14, 16, 0, 0, DateTimeKind.Utc);

        var result = GeneratorProductionPlanningPolicy.AddWorkingMinutes(friday, 120, DefaultCalendar(), 30);

        Assert.Equal(new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void Weekend_start_moves_to_monday_morning()
    {
        var saturday = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc), GeneratorProductionPlanningPolicy.NextWorkingInstant(saturday, DefaultCalendar(), 30));
    }

    [Fact]
    public void Calendar_exception_skips_a_normally_working_day()
    {
        var monday = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);
        var calendar = new GeneratorStationCalendar(new TimeOnly(8, 0), new TimeOnly(17, 0), 31, 480,
            new Dictionary<DateOnly, GeneratorWorkingDayOverride>
            {
                [new DateOnly(2026, 8, 17)] = new(false, null)
            });

        Assert.Equal(new DateTime(2026, 8, 18, 8, 0, 0, DateTimeKind.Utc), GeneratorProductionPlanningPolicy.NextWorkingInstant(monday, calendar, 30));
    }

    [Fact]
    public void Finish_to_finish_calculation_can_move_back_across_weekend()
    {
        var monday = new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc);

        var result = GeneratorProductionPlanningPolicy.SubtractWorkingMinutes(monday, 120, DefaultCalendar(), 30);

        Assert.Equal(new DateTime(2026, 8, 14, 15, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void Shared_resource_reserves_the_earliest_required_capacity_lanes()
    {
        var availability = new[]
        {
            new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc)
        };

        Assert.Equal([1, 2], GeneratorProductionPlanningPolicy.SelectEarliestCapacityLanes(availability, 2));
    }

    private static GeneratorStationCalendar DefaultCalendar() =>
        new(new TimeOnly(8, 0), new TimeOnly(17, 0), 31, 480,
            new Dictionary<DateOnly, GeneratorWorkingDayOverride>());
}
