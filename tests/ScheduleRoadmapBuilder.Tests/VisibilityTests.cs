using System.Drawing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ScheduleRoadmapBuilder.Core;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ScheduleRoadmapBuilder.Tests;

public sealed class VisibilityTests
{
    [Fact]
    public void ChecksCascadeOnlyToChildrenAndCheckingAChildEnablesOnlyItsParent()
    {
        var orphan = new ScheduleItem { Kind = ItemKind.Milestone };
        var major = new ScheduleItem { Kind = ItemKind.Major };
        var task = new ScheduleItem();
        var milestone = new ScheduleItem { Kind = ItemKind.Milestone };
        var otherMajor = new ScheduleItem { Kind = ItemKind.Major };
        var otherTask = new ScheduleItem();
        var p = new ScheduleProject { Items = [orphan, major, task, milestone, otherMajor, otherTask] };

        ItemVisibility.SetChecked(p, major, false);
        Assert.All(new[] { major, task, milestone }, i => Assert.False(i.IsVisible));
        Assert.All(new[] { orphan, otherMajor, otherTask }, i => Assert.True(i.IsVisible));
        ItemVisibility.SetChecked(p, task, true);
        Assert.True(major.IsVisible);
        Assert.True(task.IsVisible);
        Assert.False(milestone.IsVisible);
        ItemVisibility.SetChecked(p, major, false);
        ItemVisibility.SetChecked(p, milestone, true);
        Assert.True(major.IsVisible);
        Assert.True(milestone.IsVisible);
        Assert.False(task.IsVisible);
        ItemVisibility.SetChecked(p, milestone, false);
        Assert.True(major.IsVisible);
        Assert.False(task.IsVisible);
        ItemVisibility.SetChecked(p, major, true);
        Assert.All(p.Items, i => Assert.True(i.IsVisible));
        ItemVisibility.SetChecked(p, task, false);
        Assert.True(major.IsVisible);
        Assert.True(milestone.IsVisible);
        ItemVisibility.SetChecked(p, orphan, false);
        Assert.False(orphan.IsVisible);
        ItemVisibility.SetChecked(p, orphan, true);
        Assert.False(task.IsVisible);
        Assert.All(new[] { orphan, major, milestone, otherMajor, otherTask }, i => Assert.True(i.IsVisible));
    }

    [Fact]
    public void FilteringKeepsOriginalOrderNumbersDependenciesAndFullMajorPeriod()
    {
        var major = new ScheduleItem { Kind = ItemKind.Major };
        var hidden = new ScheduleItem { ManualStart = new(2026, 11, 2), Effort = 5, IsVisible = false };
        var next = new ScheduleItem { Predecessors = [hidden.Id], Effort = 2 };
        var milestone = new ScheduleItem { Kind = ItemKind.Milestone, Predecessors = [next.Id] };
        var p = new ScheduleProject { Items = [major, hidden, next, milestone] };
        var before = Scheduler.Calculate(p);
        Assert.Equal(p.Items, ItemVisibility.Filter(p));
        p.View.DisplayMode = DisplayMode.CheckedOnly;
        Assert.Equal(new[] { major, next, milestone }, ItemVisibility.Filter(p));
        Assert.Equal(new[] { major, milestone }, ItemVisibility.Filter(p, true));
        Assert.Equal(2, p.Numbers()[next.Id]);
        Assert.Equal(hidden.Id, Assert.Single(next.Predecessors));
        var after = Scheduler.Calculate(p);
        foreach (var item in p.Items)
        {
            Assert.Equal(before[item.Id].Start, after[item.Id].Start);
            Assert.Equal(before[item.Id].Finish, after[item.Id].Finish);
            Assert.Equal(before[item.Id].Errors, after[item.Id].Errors);
            Assert.Equal(before[item.Id].Warnings, after[item.Id].Warnings);
        }
        Assert.Equal(before[hidden.Id].Start, after[major.Id].Start);
        Assert.Equal(before[milestone.Id].Finish, after[major.Id].Finish);
        p.View.DisplayMode = DisplayMode.All;
        Assert.Equal(p.Items, ItemVisibility.Filter(p));
        Assert.False(hidden.IsVisible);
    }

    [Fact]
    public void OldJsonDefaultsToAllCheckedAndNewJsonPersistsModeAndChecks()
    {
        var old = ProjectStorage.Deserialize("""
            { "schemaVersion": 1, "items": [
              { "kind": "Major", "name": "工程" },
              { "kind": "Task", "name": "作業" },
              { "kind": "Milestone", "name": "確認" }
            ] }
            """);
        Assert.All(old.Items, i => Assert.True(i.IsVisible));
        Assert.Equal(DisplayMode.All, old.View.DisplayMode);
        old.Items[1].IsVisible = false;
        old.View.DisplayMode = DisplayMode.CheckedOnly;
        string path = TestFiles.NewPath(".srb.json");
        ProjectStorage.Save(path, old);
        var loaded = ProjectStorage.Load(path);
        Assert.Equal(DisplayMode.CheckedOnly, loaded.View.DisplayMode);
        Assert.Equal(new[] { true, false, true }, loaded.Items.Select(i => i.IsVisible));
        Assert.Equal(old.Items.Select(i => i.Id), loaded.Items.Select(i => i.Id));
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Deserialize(
            """{"view":{"displayMode":99}}"""));
    }

    [Theory]
    [InlineData(DisplayMode.All, false)]
    [InlineData(DisplayMode.All, true)]
    [InlineData(DisplayMode.CheckedOnly, false)]
    [InlineData(DisplayMode.CheckedOnly, true)]
    public void ExportsUseSameVisibilityScopeWithoutChangingCalculations(DisplayMode mode, bool summary)
    {
        var date = new DateOnly(2026, 11, 2);
        var p = new ScheduleProject { Items = [
            new() { Kind = ItemKind.Milestone, Name = "独立", ManualStart = date },
            new() { Kind = ItemKind.Milestone, Name = "独立PPT対象外", ManualStart = date, ShowInPpt = false },
            new() { Kind = ItemKind.Major, Name = "表示工程" },
            new() { Name = "非表示の前工程", ManualStart = date, Effort = 3, IsVisible = false },
            new() { Name = "後工程", Effort = 2 },
            new() { Kind = ItemKind.Milestone, Name = "非表示節目", ManualStart = date, IsVisible = false },
            new() { Kind = ItemKind.Milestone, Name = "PPT対象外節目", ManualStart = date, ShowInPpt = false },
            new() { Kind = ItemKind.Milestone, Name = "表示節目", ManualStart = date },
            new() { Kind = ItemKind.Major, Name = "非表示工程", ManualStart = date, IsVisible = false },
            new() { Kind = ItemKind.Major, Name = "PPT対象外工程", ManualStart = date, ShowInPpt = false },
            new() { Kind = ItemKind.Milestone, Name = "対象外工程の節目", ManualStart = date }
        ] };
        p.Items[4].Predecessors.Add(p.Items[3].Id);
        p.View.SetPeriod(date, date.AddDays(13));
        p.View.DisplayMode = mode;
        var expected = p.Items.Where(i => (mode == DisplayMode.All || i.IsVisible) &&
            (!summary || i.Kind != ItemKind.Task)).ToArray();
        string excel = TestFiles.NewPath(".xlsx");
        ExcelExporter.Export(excel, p, summary);
        using (var doc = SpreadsheetDocument.Open(excel, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(doc));
            var rows = Assert.Single(doc.WorkbookPart!.WorksheetParts).Worksheet.Descendants<S.Row>().Skip(5).ToArray();
            Assert.Equal(expected.Select(i => i.Name), rows.Select(r => r.Elements<S.Cell>().Single(c => c.CellReference!.Value!.StartsWith("D")).InnerText));
            var next = rows.SingleOrDefault(r => r.Elements<S.Cell>().Any(c => c.InnerText == "後工程"));
            if (next is not null)
            {
                Assert.Equal("4", next.Elements<S.Cell>().First().InnerText);
                Assert.Equal("3", next.Elements<S.Cell>().ElementAt(4).InnerText);
                Assert.Equal("表示工程", next.Elements<S.Cell>().ElementAt(2).InnerText);
            }
        }
        string png = TestFiles.NewPath(".png");
        PngExporter.Export(png, p, summary);
        using (var image = Image.FromFile(png)) Assert.Equal(32 + 88 + expected.Length * 32 + 1, image.Height);
        string ppt = TestFiles.NewPath(".pptx");
        PptExporter.Export(ppt, p, new());
        using (var doc = PresentationDocument.Open(ppt, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(doc));
            var slide = Assert.Single(doc.PresentationPart!.SlideParts).Slide;
            Assert.Equal(mode == DisplayMode.All ? 6 : 5, slide.Descendants<A.TableRow>().Count());
            var names = slide.Descendants<P.Shape>().Select(s => s.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name!.Value).ToArray();
            int[] excluded = mode == DisplayMode.All ? [1, 6, 9, 10] : [1, 5, 6, 8, 9, 10];
            foreach (int i in excluded)
            {
                Assert.DoesNotContain($"工程 {p.Items[i].Id}", names);
                Assert.DoesNotContain($"節目 {p.Items[i].Id}", names);
            }
            Assert.Contains($"節目 {p.Items[0].Id}", names);
            Assert.Contains($"節目 {p.Items[7].Id}", names);
            Assert.Contains($"工程 {p.Items[2].Id}", names);
        }
        Assert.Equal(date.AddDays(3), Scheduler.Calculate(p)[p.Items[4].Id].Start);
    }

    [Fact]
    public void CheckedOnlyWithNoCheckedItemsProducesValidEmptyExports()
    {
        var p = new ScheduleProject { Items = [
            new() { Kind = ItemKind.Major, IsVisible = false },
            new() { Kind = ItemKind.Milestone, IsVisible = false }
        ] };
        p.View.DisplayMode = DisplayMode.CheckedOnly;
        string excel = TestFiles.NewPath(".xlsx");
        ExcelExporter.Export(excel, p, false);
        using (var doc = SpreadsheetDocument.Open(excel, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(doc));
            Assert.Equal(5, Assert.Single(doc.WorkbookPart!.WorksheetParts).Worksheet.Descendants<S.Row>().Count());
        }
        string ppt = TestFiles.NewPath(".pptx");
        PptExporter.Export(ppt, p, new());
        using (var doc = PresentationDocument.Open(ppt, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(doc));
            Assert.Equal(3, Assert.Single(doc.PresentationPart!.SlideParts).Slide.Descendants<A.TableRow>().Count());
        }
        string png = TestFiles.NewPath(".png");
        PngExporter.Export(png, p, false);
        using var image = Image.FromFile(png);
        Assert.Equal(32 + 88 + 32 + 1, image.Height);
    }
}
