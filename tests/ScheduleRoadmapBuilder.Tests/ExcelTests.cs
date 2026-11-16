using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ScheduleRoadmapBuilder.Core;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ScheduleRoadmapBuilder.Tests;

public sealed class ExcelTests
{
    [Fact]
    public void ExcelMergesMonthsAndWeeksWithinCalendarColumnsAndLeftAlignsLabels()
    {
        var p = new ScheduleProject();
        p.View.SetPeriod(new(2026, 11, 25), new(2026, 12, 8));
        string path = TestFiles.NewPath(".xlsx");
        ExcelExporter.Export(path, p, false);
        using var doc = SpreadsheetDocument.Open(path, false);
        Assert.Empty(new OpenXmlValidator().Validate(doc));
        var sheet = Assert.Single(doc.WorkbookPart!.WorksheetParts).Worksheet;
        var merges = sheet.GetFirstChild<S.MergeCells>()!.Elements<S.MergeCell>().Select(c => c.Reference!.Value).ToArray();
        Assert.Equal(new[] { "L1:Q1", "R1:Y1", "L2:P2", "Q2:W2", "X2:Y2" }, merges);
        var formats = doc.WorkbookPart.WorkbookStylesPart!.Stylesheet.CellFormats!.Elements<S.CellFormat>().ToArray();
        foreach (var reference in new[] { "L1", "R1", "L2", "Q2", "X2" })
        {
            var cell = sheet.Descendants<S.Cell>().Single(c => c.CellReference == reference);
            Assert.NotEmpty(cell.InnerText);
            Assert.Equal(S.HorizontalAlignmentValues.Left, formats[cell.StyleIndex!.Value].Alignment!.Horizontal!.Value);
        }
        Assert.Equal("", sheet.Descendants<S.Cell>().Single(c => c.CellReference == "M1").InnerText);
    }

    [Theory]
    [InlineData(false, DayMode.AllDays)]
    [InlineData(true, DayMode.Mondays)]
    public void ExcelHasFilterFrozenPanesTypedDatesParentNamesAndColoredBands(bool summary, DayMode mode)
    {
        var p = new ScheduleProject { Items = [
            new() { Kind = ItemKind.Milestone, Name = "独立", ManualStart = new(2026, 11, 1) },
            new() { Kind = ItemKind.Major, Name = "設計", Assignee = "A" },
            new() { Name = "作業", Assignee = "B", ManualStart = new(2026, 11, 2), Effort = 5 },
            new() { Kind = ItemKind.Milestone, Name = "確認", ManualStart = new(2026, 11, 7) },
            new() { Kind = ItemKind.Major, Name = "完了工程", Assignee = "A", Status = "済", ManualStart = new(2026, 11, 9), Effort = 1 }
        ] };
        p.View.SetPeriod(new(2026, 11, 1), new(2026, 11, 10));
        p.View.Mode = mode;
        p.Settings.Calendar.Add(new() { Date = new(2026, 11, 3), Name = "架空休日" });
        string path = TestFiles.NewPath(".xlsx");
        ExcelExporter.Export(path, p, summary);
        using var doc = SpreadsheetDocument.Open(path, false);
        Assert.Empty(new OpenXmlValidator().Validate(doc));
        var workbook = doc.WorkbookPart!;
        Assert.Equal("日程", Assert.Single(workbook.Workbook.Sheets!.Elements<S.Sheet>()).Name!.Value);
        var sheet = Assert.Single(workbook.WorksheetParts).Worksheet;
        var rows = sheet.GetFirstChild<S.SheetData>()!.Elements<S.Row>().ToArray();
        Assert.Equal(summary ? 9 : 10, rows.Length);
        Assert.Equal($"A5:K{rows.Length}", sheet.GetFirstChild<S.AutoFilter>()!.Reference!.Value);
        var pane = Assert.Single(sheet.Descendants<S.Pane>());
        Assert.Equal(11, pane.HorizontalSplit!.Value);
        Assert.Equal(5, pane.VerticalSplit!.Value);
        Assert.Equal("L6", pane.TopLeftCell!.Value);
        Assert.Equal(S.PaneStateValues.Frozen, pane.State!.Value);
        S.Cell Cell(string reference) => sheet.Descendants<S.Cell>().Single(c => c.CellReference == reference);
        var styles = workbook.WorkbookStylesPart!.Stylesheet;
        S.CellFormat Format(S.Cell cell) => styles.CellFormats!.Elements<S.CellFormat>().ElementAt((int)cell.StyleIndex!.Value);
        string Fill(string reference) => styles.Fills!.Elements<S.Fill>().ElementAt((int)Format(Cell(reference)).FillId!.Value)
            .PatternFill!.ForegroundColor!.Rgb!.Value!;
        Assert.Equal("大項目", Cell("C5").InnerText);
        Assert.Equal("", Cell("C6").InnerText);
        Assert.Equal("設計", Cell("C7").InnerText);
        Assert.Equal("設計", Cell("C8").InnerText);
        var date = Cell("I7");
        Assert.Equal(S.CellValues.Number, date.DataType!.Value);
        Assert.Equal(new DateTime(2026, 11, 2).ToOADate(), double.Parse(date.InnerText, CultureInfo.InvariantCulture));
        var dateFormat = styles.NumberingFormats!.Elements<S.NumberingFormat>()
            .Single(f => f.NumberFormatId == Format(date).NumberFormatId);
        Assert.Equal("yyyy/mm/dd", dateFormat.FormatCode!.Value);
        Assert.Equal(Format(date).NumberFormatId!.Value, Format(Cell("J7")).NumberFormatId!.Value);
        Assert.Equal(1U, Format(Cell("D7")).FontId!.Value);
        Assert.Equal("FFED7D31", Fill("M7"));
        Assert.Equal("FFE8E8E8", Fill("L7"));
        Assert.Equal("FFED7D31", Fill("N7"));
        Assert.Equal("◆", Cell("L6").InnerText);
        Assert.Equal("FF696969", Fill($"T{rows.Length}"));
        if (!summary)
        {
            Assert.Equal(S.CellValues.Number, Cell("H8").DataType!.Value);
            Assert.Equal("5", Cell("H8").InnerText);
            Assert.Equal("FF8064A2", Fill("M8"));
        }
        Assert.Equal(mode == DayMode.AllDays ? "1" : "", Cell("L3").InnerText);
        Assert.Equal("2", Cell("M3").InnerText);
        Assert.Empty(sheet.Descendants<S.CellFormula>());
        Assert.Empty(sheet.Descendants<S.ConditionalFormatting>());
    }

    [Fact]
    public void ExcelSupportsEmptyProjectsAndRejectsOversizedPeriods()
    {
        var p = new ScheduleProject();
        string path = TestFiles.NewPath(".xlsx");
        ExcelExporter.Export(path, p, false);
        using (var doc = SpreadsheetDocument.Open(path, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(doc));
            Assert.Equal("A5:K5", Assert.Single(doc.WorkbookPart!.WorksheetParts).Worksheet.GetFirstChild<S.AutoFilter>()!.Reference!.Value);
        }
        p.View.SetPeriod(new(2026, 1, 1), new(2090, 1, 1));
        Assert.Throws<ArgumentException>(() => ExcelExporter.Export(TestFiles.NewPath(".xlsx"), p, false));
    }
}
