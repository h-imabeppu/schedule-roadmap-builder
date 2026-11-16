using ScheduleRoadmapBuilder.Core;
using Xunit;

namespace ScheduleRoadmapBuilder.Tests;

public sealed class SchedulingTests
{
    private static DateOnly D(string value) => DateOnly.Parse(value);
    private static ScheduleItem Task(string start, int effort = 1) =>
        new() { Name = "設計", ManualStart = D(start), Effort = effort };

    [Theory]
    [InlineData("2026-11-19", 2, "2026-11-20")]
    [InlineData("2026-11-20", 2, "2026-11-23")]
    [InlineData("2026-11-21", 1, "2026-11-23")]
    public void EffortCountsWorkingDays(string start, int effort, string finish)
    {
        var item = Task(start, effort);
        var result = Scheduler.Calculate(new() { Items = [item] })[item.Id];
        Assert.Equal(D(finish), result.Finish);
        Assert.Equal(start == "2026-11-21" ? 1 : 0, result.Warnings.Count);
    }

    [Fact]
    public void HolidaysAndDaysOffCrossYear()
    {
        var calendar = new WorkingCalendar([
            new() { Date = D("2026-12-31"), Kind = HolidayKind.DayOff },
            new() { Date = D("2027-01-01"), Kind = HolidayKind.Holiday }]);
        Assert.Equal(D("2027-01-04"), calendar.Finish(D("2026-12-30"), 2));
    }

    [Theory]
    [InlineData("2026-11-18", "2026-11-24", true)]
    [InlineData("2026-11-25", "2026-11-25", false)]
    public void MultiplePredecessorsAndManualDate(string manual, string expected, bool warned)
    {
        var a = Task("2026-11-19", 2);
        var b = Task("2026-11-23");
        var c = Task(manual);
        c.Predecessors = [a.Id, b.Id];
        var result = Scheduler.Calculate(new() { Items = [a, b, c] })[c.Id];
        Assert.Equal(D(expected), result.Start);
        Assert.Equal(warned, result.Warnings.Count > 0);
    }

    [Fact]
    public void CyclesIncludeAllMembersAndBlockDependents()
    {
        var a = Task("2026-11-16");
        var b = Task("2026-11-16");
        var c = Task("2026-11-16");
        a.Predecessors = [b.Id]; b.Predecessors = [a.Id]; c.Predecessors = [b.Id];
        var results = Scheduler.Calculate(new() { Items = [a, b, c] });
        Assert.All(results.Values, r => { Assert.Null(r.Finish); Assert.NotEmpty(r.Errors); });
        Assert.Contains("前工程が循環しています。", results[a.Id].Errors);
        Assert.Contains("前工程が循環しています。", results[b.Id].Errors);
    }

    [Fact]
    public void BranchingCycleMarksEveryMemberIncludingAlreadyVisitedBranches()
    {
        var a = Task("2026-11-16");
        var b = Task("2026-11-16");
        var c = Task("2026-11-16");
        a.Predecessors = [b.Id, c.Id];
        b.Predecessors = [a.Id];
        c.Predecessors = [b.Id];
        var result = Scheduler.Calculate(new() { Items = [a, b, c] });
        Assert.All(result.Values, r =>
        {
            Assert.Contains("前工程が循環しています。", r.Errors);
            Assert.Null(r.Start);
            Assert.Null(r.Finish);
        });
    }

    [Fact]
    public void MajorAggregatesAndStandaloneMajorCalculates()
    {
        var major = new ScheduleItem { Kind = ItemKind.Major, Name = "設計" };
        var a = Task("2026-11-16", 2);
        var milestone = new ScheduleItem { Kind = ItemKind.Milestone, Name = "確認", ManualStart = D("2026-11-21") };
        var standalone = Task("2026-11-23", 2); standalone.Kind = ItemKind.Major;
        var result = Scheduler.Calculate(new() { Items = [major, a, milestone, standalone] });
        Assert.Equal(D("2026-11-16"), result[major.Id].Start);
        Assert.Equal(D("2026-11-21"), result[major.Id].Finish);
        Assert.Equal(D("2026-11-24"), result[standalone.Id].Finish);
    }

    [Fact]
    public void MilestonesKeepManualWeekendButDependenciesUseNextWorkingDay()
    {
        var a = new ScheduleItem { Kind = ItemKind.Milestone, Name = "確認", ManualStart = D("2026-11-21"), Effort = null };
        var b = new ScheduleItem { Kind = ItemKind.Milestone, Name = "納期", Predecessors = [a.Id], Effort = null };
        var result = Scheduler.Calculate(new() { Items = [a, b] });
        Assert.Equal(D("2026-11-21"), result[a.Id].Finish);
        Assert.Equal(D("2026-11-23"), result[b.Id].Finish);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidEffortIsExplicit(int? effort)
    {
        var a = Task("2026-11-16"); a.Effort = effort;
        var result = Scheduler.Calculate(new() { Items = [a] })[a.Id];
        Assert.Null(result.Finish);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void UnsetPredecessorBlocksSuccessor()
    {
        var a = new ScheduleItem { Name = "設計" };
        var b = Task("2026-11-16"); b.Predecessors = [a.Id];
        var result = Scheduler.Calculate(new() { Items = [a, b] });
        Assert.Null(result[b.Id].Finish);
        Assert.NotEmpty(result[b.Id].Errors);
    }

    [Theory]
    [InlineData("2026-12-28", 53)]
    [InlineData("2027-01-01", 53)]
    [InlineData("2027-01-04", 1)]
    public void IsoWeeks(string date, int week) => Assert.Equal(week, CalendarRules.Week(D(date)));

    [Theory]
    [InlineData(92, DayMode.AllDays)]
    [InlineData(93, DayMode.Mondays)]
    public void ModeBoundary(int days, DayMode mode) =>
        Assert.Equal(mode, CalendarRules.DefaultMode(D("2026-11-01"), D("2026-11-01").AddDays(days - 1)));
}
