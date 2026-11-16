using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace ScheduleRoadmapBuilder.Core;

public static class PptExporter
{
    private const double TimelineX = 2.6, TimelineWidth = 10.35, BodyY = 1.85, BodyHeight = 5.2;
    private static long Emu(double inches) => (long)Math.Round(inches * 914400);

    public static List<string> Warnings(ScheduleProject project, PptSettings settings)
    {
        var (start, end) = Period(project, settings);
        var warnings = new List<string>();
        if (Rows(project).Count > 12) warnings.Add("PPTの行数が12行を超えます。行の高さを縮めて出力します。");
        if (settings.Mode == DayMode.AllDays && TextWidth("30", 4) > DayCellWidth(end.DayNumber - start.DayNumber + 1))
            warnings.Add("4ptでも全日付の文字が列幅に収まりません。「月曜のみ」への切り替えを推奨します。");
        return warnings;
    }

    private static double TextWidth(string text, double points)
    {
        using var bitmap = new System.Drawing.Bitmap(1, 1);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        using var font = new System.Drawing.Font("Meiryo UI", (float)points);
        return graphics.MeasureString(text, font, int.MaxValue, System.Drawing.StringFormat.GenericTypographic).Width * 72 / graphics.DpiX;
    }

    private static double DayCellWidth(int days) => Math.Floor(TimelineWidth * 914400 / days) / 12700;

    private static double FittingDayFont(int days)
    {
        double width = DayCellWidth(days);
        for (double points = 10; points > 4; points -= .5)
            if (TextWidth("30", points) <= width) return points;
        return 4;
    }

    private static (DateOnly Start, DateOnly End) Period(ScheduleProject project, PptSettings settings)
    {
        DateOnly start = settings.Start ?? project.View.Start, end = settings.End ?? project.View.End;
        if (end < start) throw new ArgumentException("PPTの終了日は開始日以降にしてください。");
        return (start, end);
    }

    private static List<ScheduleItem?> Rows(ScheduleProject project)
    {
        var rows = new List<ScheduleItem?>();
        var visible = ItemVisibility.Filter(project, true).ToHashSet();
        if (project.Items.TakeWhile(i => i.Kind != ItemKind.Major).Any(i => i.Kind == ItemKind.Milestone && i.ShowInPpt && visible.Contains(i)))
            rows.Add(null);
        rows.AddRange(project.Items.Where(i => i.Kind == ItemKind.Major && i.ShowInPpt && visible.Contains(i)));
        return rows;
    }

    public static void Export(string path, ScheduleProject project, PptSettings settings)
    {
        var (start, end) = Period(project, settings);
        var results = Scheduler.Calculate(project);
        var rows = Rows(project);
        var visible = ItemVisibility.Filter(project, true).ToHashSet();
        int days = end.DayNumber - start.DayNumber + 1;
        double dayWidth = TimelineWidth / days, rowHeight = BodyHeight / Math.Max(1, rows.Count);
        using var document = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentation = document.AddPresentationPart();
        presentation.Presentation = new P.Presentation();
        var master = presentation.AddNewPart<SlideMasterPart>();
        var layout = master.AddNewPart<SlideLayoutPart>();
        var theme = master.AddNewPart<ThemePart>();
        theme.Theme = CreateTheme();
        master.SlideMaster = new P.SlideMaster(
            new P.CommonSlideData(EmptyTree()),
            new P.ColorMap { Background1 = A.ColorSchemeIndexValues.Light1, Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2, Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1, Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3, Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5, Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink, FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink },
            new P.SlideLayoutIdList(new P.SlideLayoutId { Id = 2147483649, RelationshipId = master.GetIdOfPart(layout) }),
            new P.TextStyles(new P.TitleStyle(), new P.BodyStyle(), new P.OtherStyle()));
        layout.SlideLayout = new P.SlideLayout(new P.CommonSlideData(EmptyTree()),
            new P.ColorMapOverride(new A.MasterColorMapping())) { Type = P.SlideLayoutValues.Blank, Preserve = true };
        layout.AddPart(master);
        var slide = presentation.AddNewPart<SlidePart>();
        slide.AddPart(layout);
        var tree = EmptyTree();
        slide.Slide = new P.Slide(new P.CommonSlideData(tree), new P.ColorMapOverride(new A.MasterColorMapping()));
        presentation.Presentation.Append(
            new P.SlideMasterIdList(new P.SlideMasterId { Id = 2147483648, RelationshipId = presentation.GetIdOfPart(master) }),
            new P.SlideIdList(new P.SlideId { Id = 256, RelationshipId = presentation.GetIdOfPart(slide) }),
            new P.SlideSize { Cx = (int)Emu(13.333333), Cy = (int)Emu(7.5), Type = P.SlideSizeValues.Screen16x9 },
            new P.NotesSize { Cx = Emu(7.5), Cy = Emu(10) });
        uint id = 2;
        void Shape(string name, double x, double y, double w, double h, string text = "",
            string? fill = null, A.ShapeTypeValues? geometry = null, double font = 10, string? border = null, bool dashed = false)
        {
            var properties = new P.ShapeProperties(
                new A.Transform2D(new A.Offset { X = Emu(x), Y = Emu(y) }, new A.Extents { Cx = Math.Max(1, Emu(w)), Cy = Math.Max(1, Emu(h)) }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = geometry ?? A.ShapeTypeValues.Rectangle });
            if (fill is not null) properties.Append(new A.SolidFill(new A.RgbColorModelHex { Val = fill }));
            else properties.Append(new A.NoFill());
            var outline = new A.Outline { Width = 6350 };
            if (border is not null) outline.Append(new A.SolidFill(new A.RgbColorModelHex { Val = border }));
            else outline.Append(new A.NoFill());
            if (dashed) outline.Append(new A.PresetDash { Val = A.PresetLineDashValues.Dash });
            properties.Append(outline);
            var shape = new P.Shape(
                new P.NonVisualShapeProperties(new P.NonVisualDrawingProperties { Id = id++, Name = name },
                    new P.NonVisualShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
                properties);
            if (text.Length > 0)
            {
                var paragraph = new A.Paragraph(
                    new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Left },
                    new A.Run(new A.RunProperties(
                        new A.SolidFill(new A.RgbColorModelHex { Val = "202020" }),
                        new A.LatinFont { Typeface = "Meiryo UI" }, new A.EastAsianFont { Typeface = "Meiryo UI" })
                        { Language = "ja-JP", FontSize = (int)(font * 100) }, new A.Text(text)));
                shape.Append(new P.TextBody(new A.BodyProperties
                {
                    Wrap = A.TextWrappingValues.None, Anchor = A.TextAnchoringTypeValues.Center,
                    LeftInset = 14000, RightInset = 0, TopInset = 0, BottomInset = 0
                }, new A.ListStyle(), paragraph));
            }
            tree.Append(shape);
        }

        Shape("タイトル", .35, .2, 12.6, .5, $"{project.Name}（{DateTime.Today:yyyy/MM/dd}時点）", font: 20);
        tree.Append(CreateCalendarTable(id++, project, settings, start, days, rows, rowHeight));
        for (int r = 0; r < rows.Count; r++)
        {
            var major = rows[r];
            double y = BodyY + r * rowHeight;
            if (major is not null && results[major.Id] is { Start: { } s, Finish: { } f } && s <= end && f >= start)
            {
                DateOnly clippedStart = s < start ? start : s, clippedEnd = f > end ? end : f;
                string color = ColorHex(project, major);
                Shape($"工程 {major.Id}", TimelineX + (clippedStart.DayNumber - start.DayNumber) * dayWidth, y + rowHeight * .25,
                    (Emu(TimelineX + (clippedEnd.DayNumber - start.DayNumber + 1) * dayWidth) -
                        Emu(TimelineX + (clippedStart.DayNumber - start.DayNumber) * dayWidth)) / 914400.0, rowHeight * .5,
                    fill: color, geometry: A.ShapeTypeValues.RightArrow);
            }
        }
        for (int r = 0; r < rows.Count; r++)
        {
            var major = rows[r];
            double y = BodyY + r * rowHeight;
            IEnumerable<ScheduleItem> children = major is null
                ? project.Items.TakeWhile(i => i.Kind != ItemKind.Major) : project.Children(major);
            foreach (var milestone in children.Where(i => i.Kind == ItemKind.Milestone && i.ShowInPpt && visible.Contains(i)))
            {
                if (results[milestone.Id].Start is not { } date || date < start || date > end) continue;
                double size = rowHeight < .12 ? rowHeight * .8 : .12;
                double center = TimelineX + (date.DayNumber - start.DayNumber + .5) * dayWidth;
                Shape($"節目 {milestone.Id}", center - size / 2, y + rowHeight * .5 - size / 2, size, size,
                    fill: ColorHex(project, milestone), geometry: A.ShapeTypeValues.Diamond, border: "FFFFFF");
                double font = Math.Clamp(rowHeight * 25, 6, 9);
                double labelWidth = Math.Min(TimelineWidth, TextWidth(milestone.Name, font) / 72 + .04);
                double labelHeight = font * 1.5 / 72;
                bool beside = center + size / 2 + labelWidth <= TimelineX + TimelineWidth;
                Shape($"節目名 {milestone.Id}",
                    beside ? center + size / 2 : Math.Clamp(center - labelWidth / 2, TimelineX, TimelineX + TimelineWidth - labelWidth),
                    beside ? y + rowHeight / 2 - labelHeight / 2 : y + rowHeight / 2 - size / 2 - labelHeight,
                    labelWidth, labelHeight, milestone.Name, font: font);
            }
        }
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        if (settings.ShowToday && today >= start && today <= end)
        {
            double x = TimelineX + (today.DayNumber - start.DayNumber + .5) * dayWidth;
            Shape("今日", x, BodyY, .001, BodyHeight, geometry: A.ShapeTypeValues.Line, border: "FF0000", dashed: true);
        }
        var errors = new OpenXmlValidator().Validate(document).Take(5).ToList();
        if (errors.Count > 0) throw new InvalidDataException("PPT検証に失敗しました: " + string.Join("\n", errors.Select(e => e.Description)));
        presentation.Presentation.Save(); master.SlideMaster.Save(); layout.SlideLayout.Save(); slide.Slide.Save(); theme.Theme.Save();
    }

    private static P.GraphicFrame CreateCalendarTable(uint id, ScheduleProject project, PptSettings settings,
        DateOnly start, int days, List<ScheduleItem?> rows, double rowHeight)
    {
        double dayWidth = TimelineWidth / days;
        double dailyFont = settings.Mode == DayMode.Mondays ? 8 : FittingDayFont(days);
        var table = new A.Table(new A.TableProperties());
        var grid = new A.TableGrid(new A.GridColumn { Width = Emu(TimelineX) - Emu(.35) });
        for (int d = 0; d < days; d++)
            grid.Append(new A.GridColumn { Width = Emu(TimelineX + (d + 1) * dayWidth) - Emu(TimelineX + d * dayWidth) });
        table.Append(grid);
        var calendar = new WorkingCalendar(project.Settings.Calendar);
        for (int r = 0; r < 3 + rows.Count; r++)
        {
            double y = r < 3 ? .9 + r * (BodyY - .9) / 3 : BodyY + (r - 3) * rowHeight;
            double nextY = r < 3 ? .9 + (r + 1) * (BodyY - .9) / 3 : BodyY + (r - 2) * rowHeight;
            var row = new A.TableRow { Height = Emu(nextY) - Emu(y) };
            var name = TableCell(r >= 3 ? rows[r - 3]?.Name ?? "マイルストーン" : "",
                Math.Clamp(rowHeight * 40, 6, 12), false, "FFFFFF", "D0D0D0", "D0D0D0");
            if (r == 0) name.RowSpan = 3;
            if (r is 1 or 2) name.VerticalMerge = true;
            row.Append(name);
            int groupEnd = -1;
            for (int d = 0; d < days; d++)
            {
                var date = start.AddDays(d);
                string text = "";
                int span = 1;
                bool grouped = r < 2 || r == 2 && settings.Mode == DayMode.Mondays;
                bool merged = grouped && d <= groupEnd;
                if (grouped && !merged)
                {
                    int last = d;
                    while (last + 1 < days && (r == 0
                        ? start.AddDays(last + 1).Month == date.Month
                        : start.AddDays(last + 1).DayOfWeek != DayOfWeek.Monday)) last++;
                    span = last - d + 1;
                    groupEnd = last;
                    if (span >= 5) text = r == 0 ? date.ToString("yyyy/MM") : r == 1
                        ? $"CW{CalendarRules.Week(date)}"
                        : date.AddDays(-((int)date.DayOfWeek + 6) % 7).Day.ToString();
                }
                else if (r == 2 && (settings.Mode == DayMode.AllDays || date.DayOfWeek == DayOfWeek.Monday))
                    text = date.Day.ToString();
                string? left = r < 2 ? "D0D0D0" : date.DayOfWeek == DayOfWeek.Monday ? "B0B0B0" :
                    settings.Mode == DayMode.AllDays || d == 0 ? "D0D0D0" : null;
                string? right = r < 2 ? "D0D0D0" : date.DayOfWeek == DayOfWeek.Sunday ? "B0B0B0" :
                    settings.Mode == DayMode.AllDays || d == days - 1 ? "D0D0D0" : null;
                var cell = TableCell(text, r == 2 ? dailyFont : r == 1 ? 8 : 10,
                    !(r == 2 && settings.Mode == DayMode.Mondays),
                    r >= 2 && !calendar.IsWorking(date) ? "EFEFEF" : "FFFFFF", left, right);
                if (span > 1) cell.GridSpan = span;
                if (merged) cell.HorizontalMerge = true;
                row.Append(cell);
            }
            table.Append(row);
        }
        return new P.GraphicFrame(
            new P.NonVisualGraphicFrameProperties(new P.NonVisualDrawingProperties { Id = id, Name = "日程表" },
                new P.NonVisualGraphicFrameDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
            new P.Transform(new A.Offset { X = Emu(.35), Y = Emu(.9) },
                new A.Extents { Cx = Emu(TimelineX + TimelineWidth) - Emu(.35),
                    Cy = Emu(BodyY + (rows.Count == 0 ? 0 : BodyHeight)) - Emu(.9) }),
            new A.Graphic(new A.GraphicData(table) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/table" }));
    }

    private static A.TableCell TableCell(string text, double font, bool centered, string fill, string? left, string? right)
    {
        var properties = new A.TableCellProperties
        {
            LeftMargin = 0, RightMargin = 0, TopMargin = 0, BottomMargin = 0,
            Anchor = A.TextAnchoringTypeValues.Center
        };
        void Border(A.LinePropertiesType line, string? color)
        {
            line.Width = 6350;
            if (color is null) line.Append(new A.NoFill());
            else line.Append(new A.SolidFill(new A.RgbColorModelHex { Val = color }));
            properties.Append(line);
        }
        Border(new A.LeftBorderLineProperties(), left);
        Border(new A.RightBorderLineProperties(), right);
        Border(new A.TopBorderLineProperties(), "D0D0D0");
        Border(new A.BottomBorderLineProperties(), "D0D0D0");
        properties.Append(new A.SolidFill(new A.RgbColorModelHex { Val = fill }));
        return new A.TableCell(
            new A.TextBody(new A.BodyProperties { Wrap = A.TextWrappingValues.None },
                new A.ListStyle(), new A.Paragraph(
                    new A.ParagraphProperties { Alignment = centered ? A.TextAlignmentTypeValues.Center : A.TextAlignmentTypeValues.Left },
                    new A.Run(new A.RunProperties(new A.SolidFill(new A.RgbColorModelHex { Val = "202020" }),
                        new A.LatinFont { Typeface = "Meiryo UI" }, new A.EastAsianFont { Typeface = "Meiryo UI" })
                        { Language = "ja-JP", FontSize = (int)Math.Floor(font * 100) }, new A.Text(text)))),
            properties);
    }

    private static string ColorHex(ScheduleProject project, ScheduleItem item)
    {
        var color = ScheduleAppearance.ItemColor(project, item);
        return $"{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static P.ShapeTree EmptyTree() => new(
        new P.NonVisualGroupShapeProperties(new P.NonVisualDrawingProperties { Id = 1, Name = "" },
            new P.NonVisualGroupShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
        new P.GroupShapeProperties(new A.TransformGroup(
            new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 0, Cy = 0 },
            new A.ChildOffset { X = 0, Y = 0 }, new A.ChildExtents { Cx = 0, Cy = 0 })));

    private static A.Theme CreateTheme()
    {
        var colors = new A.ColorScheme(
            new A.Dark1Color(new A.RgbColorModelHex { Val = "000000" }), new A.Light1Color(new A.RgbColorModelHex { Val = "FFFFFF" }),
            new A.Dark2Color(new A.RgbColorModelHex { Val = "202020" }), new A.Light2Color(new A.RgbColorModelHex { Val = "EEEEEE" }),
            new A.Accent1Color(new A.RgbColorModelHex { Val = "4472C4" }), new A.Accent2Color(new A.RgbColorModelHex { Val = "ED7D31" }),
            new A.Accent3Color(new A.RgbColorModelHex { Val = "70AD47" }), new A.Accent4Color(new A.RgbColorModelHex { Val = "8064A2" }),
            new A.Accent5Color(new A.RgbColorModelHex { Val = "00A6A6" }), new A.Accent6Color(new A.RgbColorModelHex { Val = "FFC000" }),
            new A.Hyperlink(new A.RgbColorModelHex { Val = "0000FF" }), new A.FollowedHyperlinkColor(new A.RgbColorModelHex { Val = "800080" }))
            { Name = "Schedule" };
        var fonts = new A.FontScheme(
            new A.MajorFont(new A.LatinFont { Typeface = "Meiryo UI" }, new A.EastAsianFont { Typeface = "Meiryo UI" }, new A.ComplexScriptFont { Typeface = "Meiryo UI" }),
            new A.MinorFont(new A.LatinFont { Typeface = "Meiryo UI" }, new A.EastAsianFont { Typeface = "Meiryo UI" }, new A.ComplexScriptFont { Typeface = "Meiryo UI" }))
            { Name = "Schedule" };
        var fills = new A.FillStyleList();
        var lines = new A.LineStyleList();
        var effects = new A.EffectStyleList();
        var backgrounds = new A.BackgroundFillStyleList();
        for (int i = 0; i < 3; i++)
        {
            fills.Append(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }));
            lines.Append(new A.Outline(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 6350 });
            effects.Append(new A.EffectStyle(new A.EffectList()));
            backgrounds.Append(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }));
        }
        return new A.Theme(new A.ThemeElements(colors, fonts,
            new A.FormatScheme(fills, lines, effects, backgrounds) { Name = "Schedule" })) { Name = "Schedule" };
    }
}
