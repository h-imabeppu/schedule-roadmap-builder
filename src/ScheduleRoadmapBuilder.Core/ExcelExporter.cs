using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ScheduleRoadmapBuilder.Core;

public static class ExcelExporter
{
    public static void Export(string path, ScheduleProject project, bool summaryOnly)
    {
        int days = project.View.End.DayNumber - project.View.Start.DayNumber + 1;
        var items = ItemVisibility.Filter(project, summaryOnly).ToList();
        if (days < 1 || days + 11 > 16384 || items.Count + 5 > 1048576)
            throw new ArgumentException("表示期間または項目数がExcelのシート上限を超えています。");
        var results = Scheduler.Calculate(project);
        var numbers = project.Numbers();
        var parents = new Dictionary<Guid, string>();
        string parent = "";
        foreach (var item in project.Items)
        {
            if (item.Kind == ItemKind.Major) parent = item.Name;
            parents[item.Id] = parent;
        }

        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new S.Workbook();
        var stylesPart = workbook.AddNewPart<WorkbookStylesPart>();
        var fills = new S.Fills(
            new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),
            new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 }));
        var formats = new S.CellFormats();
        var fillIds = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var styleIds = new Dictionary<(string Fill, bool Bold, bool Date, bool Left), uint>();
        uint Style(string color, bool bold = false, bool date = false, bool left = false)
        {
            var key = (color.ToUpperInvariant(), bold, date, left);
            if (styleIds.TryGetValue(key, out uint existing)) return existing;
            uint fillId = 0;
            if (color.Length > 0 && !fillIds.TryGetValue(color, out fillId))
            {
                fillId = (uint)fills.ChildElements.Count;
                fills.Append(new S.Fill(new S.PatternFill(
                    new S.ForegroundColor { Rgb = "FF" + color },
                    new S.BackgroundColor { Indexed = 64 }) { PatternType = S.PatternValues.Solid }));
                fillIds.Add(color, fillId);
            }
            uint id = (uint)formats.ChildElements.Count;
            var format = new S.CellFormat
            {
                FontId = bold ? 1U : 0U, FillId = fillId, BorderId = 0, FormatId = 0,
                NumberFormatId = date ? 164U : 0U, ApplyFill = true, ApplyFont = true, ApplyNumberFormat = date
            };
            if (left)
            {
                format.Alignment = new S.Alignment { Horizontal = S.HorizontalAlignmentValues.Left };
                format.ApplyAlignment = true;
            }
            formats.Append(format);
            styleIds.Add(key, id);
            return id;
        }
        Style("");
        foreach (var assignee in project.Settings.Assignees)
        {
            var color = System.Drawing.ColorTranslator.FromHtml(assignee.Color);
            Style($"{color.R:X2}{color.G:X2}{color.B:X2}");
        }
        stylesPart.Stylesheet = new S.Stylesheet(
            new S.NumberingFormats(new S.NumberingFormat { NumberFormatId = 164, FormatCode = "yyyy/mm/dd" }),
            new S.Fonts(
                new S.Font(new S.FontSize { Val = 10 }, new S.FontName { Val = "Meiryo UI" }),
                new S.Font(new S.Bold(), new S.FontSize { Val = 10 }, new S.FontName { Val = "Meiryo UI" })),
            fills, new S.Borders(new S.Border()), new S.CellStyleFormats(new S.CellFormat()),
            formats, new S.CellStyles(new S.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }));
        var worksheet = workbook.AddNewPart<WorksheetPart>();
        var data = new S.SheetData();
        var columns = new S.Columns();
        double[] widths = [6, 18, 24, 28, 12, 14, 32, 8, 14, 14, 16];
        for (uint c = 1; c <= 11; c++)
            columns.Append(new S.Column { Min = c, Max = c, Width = widths[c - 1], CustomWidth = true });
        columns.Append(new S.Column { Min = 12, Max = (uint)(11 + days), Width = 3, CustomWidth = true });
        worksheet.Worksheet = new S.Worksheet(
            new S.SheetViews(new S.SheetView(
                new S.Pane { HorizontalSplit = 11, VerticalSplit = 5, TopLeftCell = "L6",
                    ActivePane = S.PaneValues.BottomRight, State = S.PaneStateValues.Frozen },
                new S.Selection { Pane = S.PaneValues.BottomRight, ActiveCell = "L6", SequenceOfReferences = new ListValue<StringValue> { InnerText = "L6" } })
                { WorkbookViewId = 0 }),
            columns, data,
            new S.AutoFilter { Reference = $"A5:K{Math.Max(5, items.Count + 5)}" });
        workbook.Workbook.Append(new S.Sheets(new S.Sheet
        {
            Id = workbook.GetIdOfPart(worksheet), SheetId = 1, Name = "日程"
        }));
        var calendar = new WorkingCalendar(project.Settings.Calendar);
        S.Cell TextCell(int column, int row, string value, uint style) => new()
        {
            CellReference = $"{ColumnName(column)}{row}", StyleIndex = style,
            DataType = S.CellValues.InlineString,
            InlineString = new S.InlineString(new S.Text(value) { Space = SpaceProcessingModeValues.Preserve })
        };
        S.Cell NumberCell(int column, int row, double value, uint style) => new()
        {
            CellReference = $"{ColumnName(column)}{row}", StyleIndex = style,
            DataType = S.CellValues.Number, CellValue = new S.CellValue(value.ToString("R", CultureInfo.InvariantCulture))
        };
        var merges = new S.MergeCells();
        for (int h = 0; h < 4; h++)
        {
            var row = new S.Row { RowIndex = (uint)(h + 1) };
            int groupEnd = -1;
            for (int d = 0; d < days; d++)
            {
                var date = project.View.Start.AddDays(d);
                var labels = ScheduleAppearance.Header(date, project.View.Start, project.View.Mode, project.Settings);
                bool continuation = h < 2 && d <= groupEnd;
                if (h < 2 && !continuation)
                {
                    labels[h] = h == 0 ? date.ToString("yyyy/MM") : $"CW{CalendarRules.Week(date)}";
                    groupEnd = d;
                    while (groupEnd + 1 < days && (h == 0
                        ? project.View.Start.AddDays(groupEnd + 1).Month == date.Month
                        : project.View.Start.AddDays(groupEnd + 1).DayOfWeek != DayOfWeek.Monday)) groupEnd++;
                    if (groupEnd > d)
                        merges.Append(new S.MergeCell
                        {
                            Reference = $"{ColumnName(12 + d)}{h + 1}:{ColumnName(12 + groupEnd)}{h + 1}"
                        });
                }
                row.Append(TextCell(12 + d, h + 1, continuation ? "" : labels[h], Style("E8E8E8", true, left: h < 2)));
            }
            data.Append(row);
        }
        string[] headings = ["No", "種別", "大項目", "項目", "前工程", "ステータス", "備考", "工数", "開始日", "完了予定日", "担当"];
        var header = new S.Row { RowIndex = 5 };
        for (int c = 0; c < headings.Length; c++) header.Append(TextCell(c + 1, 5, headings[c], Style("E8E8E8", true)));
        data.Append(header);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var result = results[item.Id];
            bool bold = item.Kind == ItemKind.Major;
            bool aggregate = bold && project.Children(item).Count > 0;
            int r = i + 6;
            var row = new S.Row { RowIndex = (uint)r };
            uint plain = Style("", bold);
            if (numbers.TryGetValue(item.Id, out int number)) row.Append(NumberCell(1, r, number, plain));
            else row.Append(TextCell(1, r, "", plain));
            string[] values = [
                ScheduleAppearance.KindName(item.Kind), parents[item.Id], item.Name,
                string.Join(",", item.Predecessors.Select(id => numbers.TryGetValue(id, out int n) ? n.ToString() : "?")),
                item.Status ?? "", item.Notes
            ];
            for (int c = 0; c < values.Length; c++) row.Append(TextCell(c + 2, r, values[c], plain));
            row.Append(!aggregate && item.Kind != ItemKind.Milestone && item.Effort is { } effort
                ? NumberCell(8, r, effort, plain) : TextCell(8, r, "", plain));
            DateOnly?[] dates = [result.Start, result.Finish];
            for (int c = 0; c < dates.Length; c++)
                row.Append(dates[c] is { } date
                    ? NumberCell(c + 9, r, date.ToDateTime(TimeOnly.MinValue).ToOADate(), Style("", bold, true))
                    : TextCell(c + 9, r, "", plain));
            row.Append(TextCell(11, r, item.Assignee ?? "", plain));
            var color = ScheduleAppearance.ItemColor(project, item);
            string bandColor = $"{color.R:X2}{color.G:X2}{color.B:X2}";
            for (int d = 0; d < days; d++)
            {
                var date = project.View.Start.AddDays(d);
                bool band = result.Start is { } s && result.Finish is { } f && date >= s && date <= f;
                row.Append(TextCell(12 + d, r, band && item.Kind == ItemKind.Milestone ? "◆" : "",
                    Style(band ? bandColor : calendar.IsWorking(date) ? "" : "E8E8E8", bold)));
            }
            data.Append(row);
        }
        if (merges.HasChildren) worksheet.Worksheet.Append(merges);
        var errors = new OpenXmlValidator().Validate(document).Take(5).ToList();
        if (errors.Count > 0) throw new InvalidDataException("Excel検証に失敗しました: " + string.Join("\n", errors.Select(e => e.Description)));
        stylesPart.Stylesheet.Save(); worksheet.Worksheet.Save(); workbook.Workbook.Save();
    }

    private static string ColumnName(int column)
    {
        string name = "";
        while (column > 0) { column--; name = (char)('A' + column % 26) + name; column /= 26; }
        return name;
    }
}
