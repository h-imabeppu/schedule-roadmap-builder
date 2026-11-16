using System.Drawing;
using System.Drawing.Imaging;

namespace ScheduleRoadmapBuilder.Core;

public static class PngExporter
{
    public static void Export(string path, ScheduleProject project, bool summaryOnly)
    {
        var items = ItemVisibility.Filter(project, summaryOnly).ToList();
        var results = Scheduler.Calculate(project);
        var numbers = project.Numbers();
        const int dayWidth = 28, rowHeight = 32, titleHeight = 32, headerHeight = 88;
        int[] widths = [40, 100, 200, 90, 90, 200, 50, 100, 100, 100];
        string[] headings = ["No", "種別", "項目", "前工程", "ステータス", "備考", "工数", "開始日", "完了予定日", "担当"];
        int tableWidth = widths.Sum();
        int days = project.View.End.DayNumber - project.View.Start.DayNumber + 1;
        if (days < 1) throw new ArgumentException("表示期間が不正です。");
        long width = tableWidth + (long)days * dayWidth + 1;
        int height = titleHeight + headerHeight + rowHeight * Math.Max(1, items.Count) + 1;
        if (width > 65000 || width * height > 150_000_000)
            throw new InvalidOperationException("画像が大きすぎます。表示期間を短くしてください。");
        using var bitmap = new Bitmap((int)width, height);
        using var g = Graphics.FromImage(bitmap);
        using var font = new Font("Meiryo UI", 9);
        using var bold = new Font(font, FontStyle.Bold);
        using var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        g.Clear(Color.White);
        g.DrawString(project.Name, bold, Brushes.Black, new RectangleF(4, 0, bitmap.Width - 8, titleHeight), format);
        int x = 0;
        for (int c = 0; c < widths.Length; c++)
        {
            var rect = new Rectangle(x, titleHeight, widths[c], headerHeight);
            g.FillRectangle(SystemBrushes.Control, rect);
            g.DrawRectangle(Pens.LightGray, rect);
            g.DrawString(headings[c], font, Brushes.Black, rect, format);
            x += widths[c];
        }
        for (int d = 0; d < days; d++)
        {
            var date = project.View.Start.AddDays(d);
            var labels = ScheduleAppearance.Header(date, project.View.Start, project.View.Mode, project.Settings);
            x = tableWidth + d * dayWidth;
            g.FillRectangle(SystemBrushes.Control, x, titleHeight, dayWidth, headerHeight);
            for (int line = 2; line < 4; line++)
                g.DrawString(labels[line], font, Brushes.Black, new RectangleF(x, titleHeight + line * 22, dayWidth, 22), format);
        }
        // Draw spanning labels after daily backgrounds so month and CW text remain visible.
        for (int d = 0; d < days; d++)
        {
            var labels = ScheduleAppearance.Header(project.View.Start.AddDays(d), project.View.Start, project.View.Mode, project.Settings);
            x = tableWidth + d * dayWidth;
            for (int line = 0; line < 2; line++)
                g.DrawString(labels[line], font, Brushes.Black, new RectangleF(x, titleHeight + line * 22, Math.Min(90, bitmap.Width - x), 22), format);
        }
        for (int index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var result = results[item.Id];
            int y = titleHeight + headerHeight + index * rowHeight;
            bool aggregate = item.Kind == ItemKind.Major && project.Children(item).Count > 0;
            string[] values = [
                numbers.TryGetValue(item.Id, out int number) ? number.ToString() : "",
                ScheduleAppearance.KindName(item.Kind), (item.Kind == ItemKind.Major ? "■ " : "") + item.Name,
                string.Join(",", item.Predecessors.Select(id => numbers.TryGetValue(id, out int n) ? n.ToString() : "?")),
                item.Status ?? "", item.Notes, aggregate || item.Kind == ItemKind.Milestone ? "" : item.Effort?.ToString() ?? "",
                (aggregate ? result.Start : item.ManualStart ?? result.Start)?.ToString("yyyy-MM-dd") ?? "",
                result.Finish?.ToString("yyyy-MM-dd") ?? "", item.Assignee ?? ""
            ];
            x = 0;
            for (int c = 0; c < widths.Length; c++)
            {
                var rect = new Rectangle(x, y, widths[c], rowHeight);
                if (result.Errors.Count > 0) g.FillRectangle(Brushes.MistyRose, rect);
                else if (result.Warnings.Count > 0) g.FillRectangle(Brushes.LemonChiffon, rect);
                g.DrawRectangle(Pens.LightGray, rect);
                g.DrawString(values[c], item.Kind == ItemKind.Major ? bold : font, Brushes.Black,
                    new RectangleF(x + 2, y, widths[c] - 4, rowHeight), format);
                x += widths[c];
            }
            for (int d = 0; d < days; d++)
                ScheduleAppearance.DrawCalendarCell(g, new(tableWidth + d * dayWidth, y, dayWidth, rowHeight),
                    project, item, result, project.View.Start.AddDays(d));
        }
        bitmap.Save(path, ImageFormat.Png);
    }
}
