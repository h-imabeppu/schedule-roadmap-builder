using System.Text.Json.Serialization;

namespace ScheduleRoadmapBuilder.Core;

public enum ItemKind { Major, Task, Milestone }
public enum DayMode { AllDays, Mondays }
public enum DisplayMode { All, CheckedOnly }
public enum HolidayKind { Holiday, DayOff }

public sealed class ScheduleItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ItemKind Kind { get; set; } = ItemKind.Task;
    public string Name { get; set; } = "";
    public List<Guid> Predecessors { get; set; } = [];
    public string? Status { get; set; }
    public string Notes { get; set; } = "";
    public int? Effort { get; set; } = 1;
    public DateOnly? ManualStart { get; set; }
    public string? Assignee { get; set; }
    public bool ShowInPpt { get; set; } = true;
    public bool IsVisible { get; set; } = true;
}

public sealed class StatusSetting
{
    public string Name { get; set; } = "";
    public bool IsComplete { get; set; }
}

public sealed class AssigneeSetting
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#4472C4";
}

public sealed class HolidaySetting
{
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public HolidayKind Kind { get; set; }
}

public sealed class ProjectSettings
{
    public List<StatusSetting> Statuses { get; set; } =
        [new() { Name = "未着手" }, new() { Name = "着手" }, new() { Name = "済", IsComplete = true }];
    public List<AssigneeSetting> Assignees { get; set; } =
    [
        new() { Name = "プロジェクト", Color = "#4472C4" },
        new() { Name = "全員", Color = "#70AD47" },
        new() { Name = "A", Color = "#ED7D31" },
        new() { Name = "B", Color = "#8064A2" },
        new() { Name = "C", Color = "#00A6A6" }
    ];
    public List<HolidaySetting> Calendar { get; set; } = [];
}

public sealed class ViewSettings
{
    public DateOnly Start { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateOnly End { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddMonths(1);
    public DayMode Mode { get; set; }
    public DisplayMode DisplayMode { get; set; }

    public void SetPeriod(DateOnly start, DateOnly end)
    {
        if (end < start) throw new ArgumentException("終了日は開始日以降を指定してください。");
        Start = start;
        End = end;
        Mode = CalendarRules.DefaultMode(start, end);
    }
}

public sealed class PptSettings
{
    public DateOnly? Start { get; set; }
    public DateOnly? End { get; set; }
    public DayMode Mode { get; set; }
    public bool ShowToday { get; set; } = true;
}

public sealed class ScheduleProject
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "新規プロジェクト";
    public List<ScheduleItem> Items { get; set; } = [];
    public ProjectSettings Settings { get; set; } = new();
    public ViewSettings View { get; set; } = new();
    public PptSettings Ppt { get; set; } = new();

    public Dictionary<Guid, int> Numbers()
    {
        int number = 0;
        return Items.Where(i => i.Kind != ItemKind.Major).ToDictionary(i => i.Id, _ => ++number);
    }

    public List<ScheduleItem> Children(ScheduleItem major)
    {
        int index = Items.IndexOf(major);
        return Items.Skip(index + 1).TakeWhile(i => i.Kind != ItemKind.Major).ToList();
    }

    public void Delete(ScheduleItem item)
    {
        Items.Remove(item);
        foreach (var other in Items) other.Predecessors.Remove(item.Id);
    }

    public List<Guid> ParsePredecessors(ScheduleItem item, string text)
    {
        var byNumber = Numbers().ToDictionary(p => p.Value, p => p.Key);
        var result = new List<Guid>();
        if (item.Kind == ItemKind.Major && Children(item).Count > 0 && !string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("配下のある大項目には前工程を設定できません。");
        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out int number) || !byNumber.TryGetValue(number, out Guid id))
                throw new ArgumentException($"前工程No「{part}」は存在しません。");
            if (!result.Contains(id)) result.Add(id);
        }
        return result;
    }
}

public sealed class ScheduleResult
{
    public DateOnly? Start { get; set; }
    public DateOnly? Finish { get; set; }
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
}
