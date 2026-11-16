using System.Drawing;

namespace ScheduleRoadmapBuilder.Core;

public static class ScheduleAppearance
{
    public static string KindName(ItemKind kind) => kind switch
    {
        ItemKind.Major => "大項目", ItemKind.Task => "小項目", _ => "マイルストーン"
    };

    public static Color ItemColor(ScheduleProject project, ScheduleItem item) =>
        project.Settings.Statuses.Any(s => s.Name == item.Status && s.IsComplete)
            ? Color.DimGray
            : ColorTranslator.FromHtml(project.Settings.Assignees.FirstOrDefault(a => a.Name == item.Assignee)?.Color ?? "#4472C4");

    public static string[] Header(DateOnly date, DateOnly first, DayMode mode, ProjectSettings settings)
    {
        var holiday = settings.Calendar.FirstOrDefault(h => h.Date == date);
        bool showDay = mode == DayMode.AllDays || date.DayOfWeek == DayOfWeek.Monday;
        return [
            date == first || date.Day == 1 ? date.ToString("yyyy/MM") : "",
            date.DayOfWeek == DayOfWeek.Monday ? $"CW{CalendarRules.Week(date)}" : "",
            showDay ? date.Day.ToString() : "",
            holiday is not null ? holiday.Kind == HolidayKind.Holiday ? "祝" : "休" :
                showDay ? "日月火水木金土"[(int)date.DayOfWeek].ToString() : ""
        ];
    }

    public static void DrawCalendarCell(Graphics g, Rectangle bounds, ScheduleProject project,
        ScheduleItem item, ScheduleResult result, DateOnly date, bool selected = false)
    {
        bool working = new WorkingCalendar(project.Settings.Calendar).IsWorking(date);
        using var background = new SolidBrush(selected ? Color.FromArgb(224, 236, 250) :
            working ? Color.White : Color.FromArgb(232, 232, 232));
        g.FillRectangle(background, bounds);
        if (result.Start is { } start && result.Finish is { } finish && date >= start && date <= finish)
        {
            using var brush = new SolidBrush(ItemColor(project, item));
            int height = Math.Max(1, bounds.Height * (item.Kind == ItemKind.Major ? 10 : 18) / 30);
            if (item.Kind == ItemKind.Milestone)
            {
                int x = bounds.Left + bounds.Width / 2, y = bounds.Top + bounds.Height / 2;
                int size = Math.Max(1, bounds.Height * 8 / 30);
                g.FillPolygon(brush, [new(x, y - size), new(x + size, y), new(x, y + size), new(x - size, y)]);
            }
            else
            {
                var band = new Rectangle(bounds.Left, bounds.Top + (bounds.Height - height) / 2, bounds.Width, height);
                g.FillRectangle(brush, band);
                if (item.Kind == ItemKind.Major)
                {
                    g.DrawLine(Pens.Black, band.Left, band.Top, band.Right, band.Top);
                    g.DrawLine(Pens.Black, band.Left, band.Bottom, band.Right, band.Bottom);
                    if (date == start) g.DrawLine(Pens.Black, band.Left, band.Top, band.Left, band.Bottom);
                    if (date == finish) g.DrawLine(Pens.Black, band.Right - 1, band.Top, band.Right - 1, band.Bottom);
                }
            }
        }
        g.DrawLine(Pens.LightGray, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
        if (date == DateOnly.FromDateTime(DateTime.Today))
        {
            using var pen = new Pen(Color.Crimson, 2);
            int x = bounds.Left + bounds.Width / 2;
            g.DrawLine(pen, x, bounds.Top, x, bounds.Bottom);
        }
    }
}
