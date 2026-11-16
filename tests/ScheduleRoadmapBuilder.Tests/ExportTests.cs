using System.Drawing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using ScheduleRoadmapBuilder.Core;
using Xunit;

namespace ScheduleRoadmapBuilder.Tests;

public sealed class ExportTests
{
    [Theory]
    [InlineData(120, DayMode.AllDays, 0)]
    [InlineData(14, DayMode.AllDays, 0)]
    [InlineData(365, DayMode.AllDays, 0)]
    [InlineData(120, DayMode.Mondays, 0)]
    [InlineData(14, DayMode.Mondays, 2)]
    public void PptDayHeadersNeverWrapAndFitOrMergeByWeek(int days, DayMode mode, int startOffset)
    {
        var start = new DateOnly(2026, 11, 1).AddDays(startOffset);
        string path = TestFiles.NewPath(".pptx");
        var p = new ScheduleProject();
        var settings = new PptSettings { Start = start, End = start.AddDays(days - 1), Mode = mode };
        PptExporter.Export(path, p, settings);
        using var doc = PresentationDocument.Open(path, false);
        Assert.Empty(new OpenXmlValidator().Validate(doc));
        var table = Assert.Single(Assert.Single(doc.PresentationPart!.SlideParts).Slide.Descendants<A.Table>());
        var rows = table.Elements<A.TableRow>().ToArray();
        var cells = rows[2].Elements<A.TableCell>().Skip(1).ToArray();
        Assert.All(cells, c => Assert.Equal(A.TextWrappingValues.None, c.TextBody!.BodyProperties!.Wrap!.Value));
        if (mode == DayMode.AllDays)
        {
            double points = cells[0].Descendants<A.RunProperties>().Single().FontSize!.Value / 100.0;
            Assert.InRange(points, 4, 10);
            Assert.Equal(Math.Round(points * 2), points * 2);
            using var bitmap = new Bitmap(1, 1);
            using var graphics = Graphics.FromImage(bitmap);
            double Width(double size)
            {
                using var font = new Font("Meiryo UI", (float)size);
                return graphics.MeasureString("30", font, int.MaxValue, StringFormat.GenericTypographic).Width * 72 / graphics.DpiX;
            }
            double width = table.TableGrid!.Elements<A.GridColumn>().Skip(1).Min(c => c.Width!.Value) / 12700.0;
            if (points > 4) Assert.True(Width(points) <= width);
            if (points < 10) Assert.True(Width(points + .5) > width);
            Assert.Equal(Width(4) > width, PptExporter.Warnings(p, settings).Count > 0);
            Assert.All(cells, c => Assert.Equal(points * 100, c.Descendants<A.RunProperties>().Single().FontSize!.Value));
        }
        else
        {
            for (int d = 0; d < days;)
            {
                int span = 1;
                while (d + span < days && start.AddDays(d + span).DayOfWeek != DayOfWeek.Monday) span++;
                var cell = cells[d];
                Assert.Equal(span, cell.GridSpan?.Value ?? 1);
                var monday = start.AddDays(d).AddDays(-((int)start.AddDays(d).DayOfWeek + 6) % 7);
                Assert.Equal(span < 5 ? "" : monday.Day.ToString(), cell.InnerText);
                Assert.Equal(A.TextAlignmentTypeValues.Left, cell.Descendants<A.ParagraphProperties>().Single().Alignment!.Value);
                Assert.Equal(rows[1].Elements<A.TableCell>().ElementAt(d + 1).Descendants<A.RunProperties>().Single().FontSize!.Value,
                    cell.Descendants<A.RunProperties>().Single().FontSize!.Value);
                Assert.All(cells.Skip(d + 1).Take(span - 1), c => Assert.True(c.HorizontalMerge!.Value));
                d += span;
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    public void PptDiamondHasFixedSizeUnlessRowIsSmaller(int count)
    {
        var date = new DateOnly(2026, 11, 16);
        var p = new ScheduleProject();
        for (int i = 0; i < count; i++)
            p.Items.Add(new() { Kind = ItemKind.Major, Name = $"工程{i}" });
        p.Items.Add(new() { Kind = ItemKind.Milestone, Name = "確認", ManualStart = date });
        string path = TestFiles.NewPath(".pptx");
        PptExporter.Export(path, p, new() { Start = date, End = date.AddDays(10) });
        using var doc = PresentationDocument.Open(path, false);
        var diamond = Assert.Single(Assert.Single(doc.PresentationPart!.SlideParts).Slide.Descendants<P.Shape>(),
            s => s.ShapeProperties!.GetFirstChild<A.PresetGeometry>()!.Preset!.Value == A.ShapeTypeValues.Diamond);
        var size = diamond.ShapeProperties!.Transform2D!.Extents!;
        long expected = (long)Math.Round((count == 50 ? 5.2 / count * .8 : .12) * 914400);
        Assert.Equal(expected, size.Cx!.Value);
        Assert.Equal(expected, size.Cy!.Value);
    }

    [Theory]
    [InlineData(DayMode.AllDays)]
    [InlineData(DayMode.Mondays)]
    public void PptUsesOneMergedCalendarTableAndAlignedLayeredOverlays(DayMode mode)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = new DateOnly(2026, 11, 1);
        var p = new ScheduleProject { Items = [
            new() { Kind = ItemKind.Milestone, Name = "開始", ManualStart = start.AddDays(1) },
            new() { Kind = ItemKind.Major, Name = "設計" },
            new() { Name = "作業", ManualStart = start.AddDays(-10), Effort = 90 },
            new() { Kind = ItemKind.Milestone, Name = "節目", ManualStart = start.AddDays(15) },
            new() { Kind = ItemKind.Milestone, Name = "最終日", ManualStart = new(2027, 1, 31) }
        ] };
        p.Settings.Calendar.Add(new() { Date = start.AddDays(3), Name = "架空休日" });
        var end = new DateOnly(2027, 1, 31);
        string path = TestFiles.NewPath(".pptx");
        PptExporter.Export(path, p, new() { Start = start, End = end, Mode = mode });
        using var doc = PresentationDocument.Open(path, false);
        Assert.Empty(new OpenXmlValidator().Validate(doc));
        var slide = Assert.Single(doc.PresentationPart!.SlideParts).Slide;
        var frame = Assert.Single(slide.Descendants<P.GraphicFrame>());
        var table = Assert.Single(frame.Descendants<A.Table>());
        var rows = table.Elements<A.TableRow>().ToArray();
        var columns = table.GetFirstChild<A.TableGrid>()!.Elements<A.GridColumn>().ToArray();
        Assert.Equal(5, rows.Length);
        Assert.Equal(93, columns.Length);
        Assert.True(columns.Length > 75);
        Assert.All(rows, r => Assert.Equal(93, r.Elements<A.TableCell>().Count()));
        Assert.Equal(3, rows[0].Elements<A.TableCell>().First().RowSpan!.Value);
        Assert.True(rows[1].Elements<A.TableCell>().First().VerticalMerge!.Value);
        Assert.Empty(rows[1].Elements<A.TableCell>().ElementAt(1).InnerText);
        Assert.Equal("CW45", rows[1].Elements<A.TableCell>().ElementAt(2).InnerText);
        Assert.Equal(7, rows[1].Elements<A.TableCell>().ElementAt(2).GridSpan!.Value);
        Assert.True(rows[1].Elements<A.TableCell>().ElementAt(3).HorizontalMerge!.Value);
        foreach (int row in new[] { 2, 3, 4 })
            foreach (int col in new[] { 1, 4 })
                Assert.Equal("EFEFEF", rows[row].Elements<A.TableCell>().ElementAt(col)
                    .TableCellProperties!.GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value);
        if (mode == DayMode.Mondays)
        {
            var tuesday = rows[4].Elements<A.TableCell>().ElementAt(3).TableCellProperties!;
            Assert.NotNull(tuesday.GetFirstChild<A.LeftBorderLineProperties>()!.GetFirstChild<A.NoFill>());
            Assert.NotNull(tuesday.GetFirstChild<A.RightBorderLineProperties>()!.GetFirstChild<A.NoFill>());
        }
        Assert.DoesNotContain(slide.Descendants<P.Shape>(), s =>
            s.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name!.Value!.StartsWith("非稼働日"));
        Assert.All(slide.Descendants<P.Shape>().Where(s =>
            s.ShapeProperties!.GetFirstChild<A.PresetGeometry>()!.Preset!.Value == A.ShapeTypeValues.Rectangle),
            shape =>
            {
                string name = shape.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name!.Value!;
                Assert.True(name == "タイトル" || name.StartsWith("節目名 "));
                Assert.NotNull(shape.ShapeProperties!.GetFirstChild<A.NoFill>());
            });
        Assert.All(slide.Descendants<P.Shape>().Where(s =>
            s.ShapeProperties!.GetFirstChild<A.PresetGeometry>()!.Preset!.Value == A.ShapeTypeValues.Line),
            shape => Assert.Equal("今日", shape.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name!.Value));
        var arrow = Assert.Single(slide.Descendants<P.Shape>(), s =>
            s.ShapeProperties!.GetFirstChild<A.PresetGeometry>()!.Preset!.Value == A.ShapeTypeValues.RightArrow);
        long left = frame.Transform!.Offset!.X!.Value + columns[0].Width!.Value;
        var transform = arrow.ShapeProperties!.Transform2D!;
        Assert.Equal(left, transform.Offset!.X!.Value);
        Assert.Equal(left + columns.Skip(1).Sum(c => c.Width!.Value), transform.Offset.X.Value + transform.Extents!.Cx!.Value);
        var shapes = slide.Descendants<P.Shape>().ToArray();
        foreach (var diamond in shapes.Where(s => s.ShapeProperties!.GetFirstChild<A.PresetGeometry>()!.Preset!.Value == A.ShapeTypeValues.Diamond))
        {
            string id = diamond.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name!.Value!["節目 ".Length..];
            var label = Assert.Single(shapes, s => s.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name == $"節目名 {id}");
            var d = diamond.ShapeProperties!.Transform2D!;
            var l = label.ShapeProperties!.Transform2D!;
            bool beside = Math.Abs(l.Offset!.X!.Value - d.Offset!.X!.Value - d.Extents!.Cx!.Value) <= 1;
            bool above = Math.Abs(l.Offset!.Y!.Value + l.Extents!.Cy!.Value - d.Offset!.Y!.Value) <= 1;
            Assert.True(beside || above);
            Assert.Equal("FFFFFF", diamond.ShapeProperties.GetFirstChild<A.Outline>()!.GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value);
            Assert.True(Array.IndexOf(shapes, diamond) > Array.IndexOf(shapes, arrow));
        }
        var tree = slide.CommonSlideData!.ShapeTree!;
        Assert.True(tree.ChildElements.ToList().IndexOf(frame) < tree.ChildElements.ToList().IndexOf(arrow));
        var todayShape = shapes.SingleOrDefault(s => s.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name == "今日");
        if (today >= start && today <= end) Assert.Same(todayShape, tree.LastChild);
    }

    [Fact]
    public void PptOmitsShortMonthAndWeekFragmentsAndAlignsInteriorArrow()
    {
        var start = new DateOnly(2026, 11, 28);
        var p = new ScheduleProject { Items = [
            new() { Kind = ItemKind.Major, Name = "工程", ManualStart = new(2026, 12, 1), Effort = 2 }
        ] };
        string path = TestFiles.NewPath(".pptx");
        PptExporter.Export(path, p, new() { Start = start, End = new(2026, 12, 10) });
        using var doc = PresentationDocument.Open(path, false);
        Assert.Empty(new OpenXmlValidator().Validate(doc));
        var slide = Assert.Single(doc.PresentationPart!.SlideParts).Slide;
        var table = Assert.Single(slide.Descendants<A.Table>());
        var rows = table.Elements<A.TableRow>().ToArray();
        Assert.Empty(rows[0].Elements<A.TableCell>().ElementAt(1).InnerText);
        Assert.Empty(rows[1].Elements<A.TableCell>().ElementAt(1).InnerText);
        var frame = Assert.Single(slide.Descendants<P.GraphicFrame>());
        var widths = table.GetFirstChild<A.TableGrid>()!.Elements<A.GridColumn>().Select(c => c.Width!.Value).ToArray();
        var arrow = Assert.Single(slide.Descendants<P.Shape>(), s =>
            s.ShapeProperties!.GetFirstChild<A.PresetGeometry>()!.Preset!.Value == A.ShapeTypeValues.RightArrow);
        long startX = frame.Transform!.Offset!.X!.Value + widths.Take(4).Sum();
        var transform = arrow.ShapeProperties!.Transform2D!;
        Assert.Equal(startX, transform.Offset!.X!.Value);
        Assert.Equal(widths.Skip(4).Take(2).Sum(), transform.Extents!.Cx!.Value);
    }

    [Fact]
    public void TodayIsOneContinuousVerticalLineInsteadOfCellOutlines()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var p = new ScheduleProject();
        var item = new ScheduleItem { ManualStart = today, Effort = 1 };
        using var bitmap = new Bitmap(40, 90);
        using (var graphics = Graphics.FromImage(bitmap))
            for (int r = 0; r < 3; r++)
                ScheduleAppearance.DrawCalendarCell(graphics, new(0, r * 30, 40, 30), p, item, new(), today);
        for (int y = 0; y < 90; y++)
        {
            Assert.Equal(Color.Crimson.ToArgb(), bitmap.GetPixel(20, y).ToArgb());
            Assert.NotEqual(Color.Crimson.ToArgb(), bitmap.GetPixel(1, y).ToArgb());
            Assert.NotEqual(Color.Crimson.ToArgb(), bitmap.GetPixel(38, y).ToArgb());
        }
    }

    [Fact]
    public void PptIsValidSingleSlideWithEditableClippedArrowsAndMilestones()
    {
        var p = new ScheduleProject { Name = "全体日程", Items = [
            new() { Kind = ItemKind.Milestone, Name = "開始確認", ManualStart = new(2026, 11, 16) },
            new() { Kind = ItemKind.Major, Name = "設計", ManualStart = new(2026, 11, 1), Effort = 30 },
            new() { Kind = ItemKind.Major, Name = "実装" },
            new() { Name = "実装A", ManualStart = new(2026, 11, 20), Effort = 50 },
            new() { Kind = ItemKind.Milestone, Name = "確認", ManualStart = new(2026, 11, 25) }
        ] };
        p.View.SetPeriod(new(2026, 11, 16), new(2026, 12, 31));
        string path = TestFiles.NewPath(".pptx");
        PptExporter.Export(path, p, new());
        using var doc = PresentationDocument.Open(path, false);
        Assert.Empty(new OpenXmlValidator().Validate(doc));
        var slide = Assert.Single(doc.PresentationPart!.SlideParts);
        Assert.Equal(2, slide.Slide.Descendants<A.PresetGeometry>().Count(g => g.Preset?.Value == A.ShapeTypeValues.RightArrow));
        Assert.Equal(2, slide.Slide.Descendants<A.PresetGeometry>().Count(g => g.Preset?.Value == A.ShapeTypeValues.Diamond));
        Assert.Empty(slide.ImageParts);
        Assert.DoesNotContain(slide.Slide.Descendants<A.Offset>(), o => o.X?.Value < 0);
        Assert.Equal(12192000, doc.PresentationPart.Presentation.SlideSize!.Cx!.Value);
        Assert.Contains(slide.Slide.Descendants<A.Text>(), t => t.Text.Contains("全体日程"));
        Assert.NotEmpty(slide.Slide.Descendants<P.Shape>());
    }

    [Fact]
    public void PptTodayLineIsLastAndCanBeDisabled()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var p = new ScheduleProject { Items = [
            new() { Kind = ItemKind.Major, Name = "工程" },
            new() { Kind = ItemKind.Milestone, Name = "節目", ManualStart = today },
            new() { Kind = ItemKind.Major, Name = "工程2", ManualStart = today, Effort = 1 }
        ] };
        foreach (bool show in new[] { true, false })
        {
            string path = TestFiles.NewPath(".pptx");
            PptExporter.Export(path, p, new() { Start = today.AddDays(-7), End = today.AddDays(7), ShowToday = show });
            using var doc = PresentationDocument.Open(path, false);
            Assert.Empty(new OpenXmlValidator().Validate(doc));
            var tree = Assert.Single(doc.PresentationPart!.SlideParts).Slide.CommonSlideData!.ShapeTree!;
            var names = tree.Elements<P.Shape>().Select(s => s.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name!.Value!).ToArray();
            if (show) Assert.Equal("今日", names.Last());
            else Assert.DoesNotContain("今日", names);
            int lastArrow = Array.FindLastIndex(names, n => n.StartsWith("工程 "));
            int firstMilestone = Array.FindIndex(names, n => n.StartsWith("節目 "));
            Assert.True(lastArrow < firstMilestone);
        }
    }

    [Fact]
    public void PptWarningsCoverRowAndFontLimits()
    {
        var p = new ScheduleProject { Items = Enumerable.Range(1, 13).Select(i =>
            new ScheduleItem { Kind = ItemKind.Major, Name = $"設計{i}" }).ToList() };
        var settings = new PptSettings { Start = new(2026, 1, 1), End = new(2026, 12, 31), Mode = DayMode.AllDays };
        Assert.Equal(2, PptExporter.Warnings(p, settings).Count);
        settings.Mode = DayMode.Mondays;
        Assert.Single(PptExporter.Warnings(p, settings));
    }

    [Fact]
    public void PngExportsAllRowsOrSummaryWithPositiveDimensions()
    {
        var p = new ScheduleProject { Items = [
            new() { Kind = ItemKind.Major, Name = "設計" },
            new() { Name = "設計A", ManualStart = new(2026, 11, 16), Effort = 5 },
            new() { Kind = ItemKind.Milestone, Name = "確認", ManualStart = new(2026, 11, 23) }
        ] };
        string path = TestFiles.NewPath(".png");
        PngExporter.Export(path, p, false);
        using var full = Image.FromFile(path);
        Assert.True(full.Width > 0);
        Assert.Equal(32 + 88 + 3 * 32 + 1, full.Height);
        string summary = TestFiles.NewPath(".png");
        PngExporter.Export(summary, p, true);
        using var image = Image.FromFile(summary);
        Assert.Equal(full.Height - 32, image.Height);
        Assert.Equal(full.Width, image.Width);
    }
}
