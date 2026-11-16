namespace ScheduleRoadmapBuilder.Core;

public static class ItemVisibility
{
    public static IEnumerable<ScheduleItem> Filter(ScheduleProject project, bool summaryOnly = false) =>
        project.Items.Where(i => (project.View.DisplayMode == DisplayMode.All || i.IsVisible) &&
            (!summaryOnly || i.Kind != ItemKind.Task));

    public static void SetChecked(ScheduleProject project, ScheduleItem item, bool value)
    {
        int index = project.Items.IndexOf(item);
        if (index < 0) throw new ArgumentException("項目がプロジェクトにありません。", nameof(item));
        item.IsVisible = value;
        if (item.Kind == ItemKind.Major)
        {
            foreach (var child in project.Children(item)) child.IsVisible = value;
        }
        else if (value)
        {
            var parent = project.Items.Take(index).LastOrDefault(i => i.Kind == ItemKind.Major);
            if (parent is not null) parent.IsVisible = true;
        }
    }
}
