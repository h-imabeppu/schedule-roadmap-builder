using System.Globalization;

namespace ScheduleRoadmapBuilder.Core;

public sealed class WorkingCalendar(IEnumerable<HolidaySetting> holidays)
{
    private readonly HashSet<DateOnly> nonWorking = holidays.Select(h => h.Date).ToHashSet();
    public bool IsWorking(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !nonWorking.Contains(date);

    public DateOnly OnOrAfter(DateOnly date)
    {
        while (!IsWorking(date)) date = date.AddDays(1);
        return date;
    }

    public DateOnly Next(DateOnly date) => OnOrAfter(date.AddDays(1));

    public DateOnly Finish(DateOnly start, int effort)
    {
        if (effort < 1) throw new ArgumentOutOfRangeException(nameof(effort));
        DateOnly date = OnOrAfter(start);
        for (int i = 1; i < effort; i++) date = Next(date);
        return date;
    }
}

public static class CalendarRules
{
    public static int Week(DateOnly date) => ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));
    public static DayMode DefaultMode(DateOnly start, DateOnly end) =>
        end.DayNumber - start.DayNumber + 1 <= 92 ? DayMode.AllDays : DayMode.Mondays;

    public static void AutoPeriod(ScheduleProject project, IReadOnlyDictionary<Guid, ScheduleResult> results)
    {
        var dates = results.Values.SelectMany(r => new[] { r.Start, r.Finish }).OfType<DateOnly>().ToList();
        if (dates.Count == 0) throw new InvalidOperationException("日程が未設定です。");
        DateOnly first = dates.Min(), last = dates.Max();
        project.View.SetPeriod(new(first.Year, first.Month, 1),
            new DateOnly(last.Year, last.Month, DateTime.DaysInMonth(last.Year, last.Month)));
    }
}

public static class Scheduler
{
    public static Dictionary<Guid, ScheduleResult> Calculate(ScheduleProject project)
    {
        var items = project.Items.ToDictionary(i => i.Id);
        var results = items.Keys.ToDictionary(id => id, _ => new ScheduleResult());
        var states = new Dictionary<Guid, int>();
        var stack = new List<Guid>();
        var calendar = new WorkingCalendar(project.Settings.Calendar);

        foreach (var item in project.Items.Where(i => i.Kind != ItemKind.Major))
        {
            var pending = new Stack<Guid>(item.Predecessors);
            var visited = new HashSet<Guid>();
            while (pending.TryPop(out Guid next))
            {
                if (next == item.Id)
                {
                    results[item.Id].Errors.Add("前工程が循環しています。");
                    break;
                }
                if (visited.Add(next) && items.TryGetValue(next, out var previous) && previous.Kind != ItemKind.Major)
                    foreach (Guid predecessor in previous.Predecessors) pending.Push(predecessor);
            }
        }

        void Visit(Guid id)
        {
            if (states.TryGetValue(id, out int state))
            {
                if (state == 1)
                    foreach (Guid cyclic in stack.Skip(stack.IndexOf(id)))
                        if (!results[cyclic].Errors.Contains("前工程が循環しています。"))
                            results[cyclic].Errors.Add("前工程が循環しています。");
                return;
            }
            states[id] = 1;
            stack.Add(id);
            ScheduleItem item = items[id];
            ScheduleResult result = results[id];
            var children = item.Kind == ItemKind.Major ? project.Children(item) : [];
            if (string.IsNullOrWhiteSpace(item.Name)) result.Errors.Add("項目名は必須です。");
            if (children.Count > 0)
            {
                foreach (var child in children) Visit(child.Id);
                var starts = children.Select(c => results[c.Id].Start).OfType<DateOnly>().ToList();
                var finishes = children.Select(c => results[c.Id].Finish).OfType<DateOnly>().ToList();
                result.Start = starts.Count > 0 ? starts.Min() : null;
                result.Finish = finishes.Count > 0 ? finishes.Max() : null;
            }
            else
            {
                DateOnly? earliest = null;
                bool missing = false;
                foreach (Guid predecessor in item.Predecessors)
                {
                    if (!items.TryGetValue(predecessor, out var previous) || previous.Kind == ItemKind.Major)
                    {
                        result.Errors.Add("前工程の参照が不正です。");
                        missing = true;
                        continue;
                    }
                    Visit(predecessor);
                    DateOnly? finish = results[predecessor].Finish;
                    if (finish is null) missing = true;
                    else if (earliest is null || finish > earliest) earliest = finish;
                }
                if (item.Kind != ItemKind.Milestone && item.Effort is not > 0)
                    result.Errors.Add("工数は1以上の整数を入力してください。");
                if (missing) result.Errors.Add("前工程の日程が未設定です。");
                if (!missing && result.Errors.Count == 0)
                {
                    DateOnly? start = earliest is { } e ? calendar.Next(e) : null;
                    if (item.ManualStart is { } manual)
                    {
                        if (start is null || manual >= start) start = manual;
                        else result.Warnings.Add("手入力の開始日は前工程からの計算日より前です。");
                    }
                    if (start is { } actual)
                    {
                        if (item.Kind != ItemKind.Milestone || item.Predecessors.Count > 0)
                        {
                            DateOnly adjusted = calendar.OnOrAfter(actual);
                            if (adjusted != actual) result.Warnings.Add("開始日を次の稼働日に補正しました。");
                            start = adjusted;
                        }
                        result.Start = start;
                        result.Finish = item.Kind == ItemKind.Milestone ? start : calendar.Finish(start!.Value, item.Effort!.Value);
                    }
                }
            }
            if (result.Errors.Count > 0) result.Start = result.Finish = null;
            stack.RemoveAt(stack.Count - 1);
            states[id] = 2;
        }

        foreach (var item in project.Items) Visit(item.Id);
        return results;
    }
}
