using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ScheduleRoadmapBuilder.App;
using ScheduleRoadmapBuilder.Core;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;

namespace ScheduleRoadmapBuilder.Tests;

public sealed class SampleTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void ActualSampleProducesPngAndBothPptModes()
    {
        var project = ProjectStorage.Load(Path.Combine(Root, "samples", "sample.srb.json"));
        Assert.Equal(3, project.Items.Count(i => i.Kind == ItemKind.Major));
        Assert.Equal(8, project.Items.Count(i => i.Kind == ItemKind.Task));
        Assert.Equal(2, project.Items.Count(i => i.Kind == ItemKind.Milestone));
        var results = Scheduler.Calculate(project);
        Assert.All(results.Values, r => { Assert.Empty(r.Errors); Assert.NotNull(r.Start); Assert.NotNull(r.Finish); });
        Assert.Equal(2, results[project.Items.Last().Id].Finish!.Value.Month);
        string output = Path.Combine(Root, "samples", "output");
        Directory.CreateDirectory(output);
        string png = Path.Combine(output, "sample.png");
        PngExporter.Export(png, project, false);
        using var image = Image.FromFile(png);
        Assert.True(image.Width > 0 && image.Height > 0);
        string excel = Path.Combine(output, "sample.xlsx");
        ExcelExporter.Export(excel, project, false);
        using (var doc = SpreadsheetDocument.Open(excel, false))
            Assert.Empty(new OpenXmlValidator().Validate(doc));
        foreach (var mode in Enum.GetValues<DayMode>())
        {
            string path = Path.Combine(output, $"sample-{mode}.pptx");
            PptExporter.Export(path, project, new()
            {
                Start = project.View.Start, End = project.View.End,
                Mode = mode, ShowToday = true
            });
            using var doc = PresentationDocument.Open(path, false);
            Assert.Empty(new OpenXmlValidator().Validate(doc));
            var slide = Assert.Single(doc.PresentationPart!.SlideParts);
            Assert.Equal(3, slide.Slide.Descendants<A.PresetGeometry>().Count(g => g.Preset?.Value == A.ShapeTypeValues.RightArrow));
        }
        project.View.DisplayMode = DisplayMode.CheckedOnly;
        string checkedExcel = Path.Combine(output, "sample-Checked.xlsx");
        ExcelExporter.Export(checkedExcel, project, false);
        using (var doc = SpreadsheetDocument.Open(checkedExcel, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(doc));
            Assert.Equal(5 + project.Items.Count(i => i.IsVisible),
                Assert.Single(doc.WorkbookPart!.WorksheetParts).Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>().Count());
        }
        string checkedPpt = Path.Combine(output, "sample-Mondays-Checked.pptx");
        PptExporter.Export(checkedPpt, project, new() { Start = project.View.Start, End = project.View.End, Mode = DayMode.Mondays });
        using (var doc = PresentationDocument.Open(checkedPpt, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(doc));
            Assert.Equal(3 + project.Items.Count(i => i.Kind == ItemKind.Major && i.IsVisible && i.ShowInPpt),
                Assert.Single(doc.PresentationPart!.SlideParts).Slide.Descendants<A.TableRow>().Count());
        }
    }

    [Fact]
    public void MainWindowLoadsSampleAndRecalculatesAfterEditingAndReordering()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm();
                var type = typeof(MainForm);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var project = ProjectStorage.Load(Path.Combine(Root, "samples", "sample.srb.json"));
                type.GetField("project", flags)!.SetValue(form, project);
                type.GetMethod("RefreshAll", flags)!.Invoke(form, null);
                form.Show();
                Application.DoEvents();
                var grid = (DataGridView)type.GetField("grid", flags)!.GetValue(form)!;
                Assert.Equal(13, grid.Rows.Count);
                Assert.All(grid.Columns.Cast<DataGridViewColumn>().Take(11), c => Assert.True(c.Frozen));
                Assert.Equal(0, grid.Columns["表示"]!.DisplayIndex);
                Assert.DoesNotContain("", ((DataGridViewComboBoxColumn)grid.Columns[1]).Items.Cast<string>());
                Assert.Equal(131, grid.Columns.Count);
                Assert.Equal("■ 設計", grid.Rows[0].Cells[2].Value);
                grid.CurrentCell = grid.Rows[1].Cells[9];
                Assert.True(grid.BeginEdit(false));
                var previousResults = type.GetField("results", flags)!.GetValue(form);
                ((ComboBox)grid.EditingControl!).SelectedItem = "B";
                Assert.Equal("B", project.Items[1].Assignee);
                var recalculated = (Dictionary<Guid, ScheduleResult>)type.GetField("results", flags)!.GetValue(form)!;
                Assert.NotSame(previousResults, recalculated);
                Assert.NotNull(recalculated[project.Items[1].Id].Finish);
                Application.DoEvents();
                grid.CurrentCell = grid.Rows[1].Cells[4];
                Assert.True(grid.BeginEdit(false));
                ((ComboBox)grid.EditingControl!).SelectedItem = "済";
                Assert.Equal("済", project.Items[1].Status);
                Assert.Equal(Color.DimGray, ScheduleAppearance.ItemColor(project, project.Items[1]));
                Application.DoEvents();
                type.GetMethod("ChangeZoom", flags)!.Invoke(form, [1]);
                Assert.Equal(33, grid.Rows[0].Height);
                Assert.Equal(31, grid.Columns[11].Width);
                type.GetMethod("ChangeZoom", flags)!.Invoke(form, [0]);
                Assert.Equal(30, grid.Rows[0].Height);
                grid.CurrentCell = grid.Rows[1].Cells[6];
                Assert.True(grid.BeginEdit(false));
                ((TextBox)grid.EditingControl!).Text = "8";
                Assert.True(grid.EndEdit());
                Application.DoEvents();
                Assert.Equal(8, project.Items[1].Effort);
                Assert.Equal("2026-11-12", grid.Rows[1].Cells[8].Value);
                Guid id = project.Items[1].Id;
                type.GetMethod("MoveRow", flags)!.Invoke(form, [1]);
                Application.DoEvents();
                Assert.Equal(id, project.Items[2].Id);
                Assert.Contains(id, project.Items[1].Predecessors);
                Assert.Equal("2", grid.Rows[1].Cells[3].Value);
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                string output = Path.Combine(Root, "samples", "output");
                Directory.CreateDirectory(output);
                bitmap.Save(Path.Combine(output, "window.png"));
                grid.CurrentCell = grid.Rows[12].Cells[1];
                Assert.True(grid.BeginEdit(false));
                ((ComboBox)grid.EditingControl!).SelectedItem = "小項目";
                Assert.Equal(ItemKind.Task, project.Items[12].Kind);
                Application.DoEvents();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "UI検証がタイムアウトしました。");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void VisibilityCheckboxCommitsImmediatelyAndFilteredRowsKeepTheirIdentity()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(MainForm);
                var p = new ScheduleProject { Items = [
                    new() { Kind = ItemKind.Major, Name = "工程" },
                    new() { Name = "非表示作業", IsVisible = false, ManualStart = new(2026, 11, 2), Effort = 3 },
                    new() { Name = "表示作業", ManualStart = new(2026, 11, 9), Effort = 1 },
                    new() { Kind = ItemKind.Milestone, Name = "確認", ManualStart = new(2026, 11, 10) }
                ] };
                type.GetField("project", flags)!.SetValue(form, p);
                type.GetMethod("RefreshAll", flags)!.Invoke(form, null);
                form.Show(); Application.DoEvents();
                var grid = (DataGridView)type.GetField("grid", flags)!.GetValue(form)!;
                var mode = (ToolStripComboBox)type.GetField("displayMode", flags)!.GetValue(form)!;
                var status = (ToolStripStatusLabel)type.GetField("visibilityStatus", flags)!.GetValue(form)!;
                void ClickCheck(int row)
                {
                    grid.CurrentCell = grid.Rows[row].Cells["表示"];
                    Assert.True(grid.BeginEdit(false));
                    typeof(DataGridViewCheckBoxCell).GetMethod("OnContentClick", flags)!
                        .Invoke(grid.CurrentCell, [new DataGridViewCellEventArgs(grid.CurrentCell!.ColumnIndex, row)]);
                }
                ClickCheck(0);
                Assert.All(p.Items, i => Assert.False(i.IsVisible));
                Application.DoEvents();
                ClickCheck(2);
                Assert.True(p.Items[0].IsVisible);
                Assert.True(p.Items[2].IsVisible);
                Assert.False(p.Items[1].IsVisible);
                Assert.False(p.Items[3].IsVisible);
                Application.DoEvents();
                mode.SelectedIndex = 1;
                Assert.Equal(DisplayMode.CheckedOnly, p.View.DisplayMode);
                Assert.Equal(2, grid.Rows.Count);
                Assert.Equal("表示：チェック済みのみ（2 / 全4行）", status.Text);
                Assert.Same(p.Items[2], grid.Rows[1].Tag);
                Assert.Equal("2", grid.Rows[1].Cells[0].Value);
                grid.CurrentCell = grid.Rows[1].Cells[2];
                Assert.True(grid.BeginEdit(false));
                ((TextBox)grid.EditingControl!).Text = "編集後作業";
                Assert.True(grid.EndEdit());
                Application.DoEvents();
                Assert.Equal("編集後作業", p.Items[2].Name);
                Assert.Equal("非表示作業", p.Items[1].Name);
                type.GetMethod("MoveRow", flags)!.Invoke(form, [1]);
                Application.DoEvents();
                Assert.Equal("編集後作業", p.Items[3].Name);
                Assert.Same(p.Items[3], grid.CurrentRow!.Tag);
                ClickCheck(1);
                Assert.False(p.Items[3].IsVisible);
                Assert.True(p.Items[0].IsVisible);
                Application.DoEvents();
                Assert.Single(grid.Rows.Cast<DataGridViewRow>());
                Assert.Equal("表示：チェック済みのみ（1 / 全4行）", status.Text);
                ClickCheck(0);
                Application.DoEvents();
                Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
                mode.SelectedIndex = 0;
                Assert.Equal(4, grid.Rows.Count);
                Assert.Equal("", status.Text);
                ClickCheck(0);
                Assert.All(p.Items, i => Assert.True(i.IsVisible));
                Application.DoEvents();
                mode.SelectedIndex = 1;
                grid.CurrentCell = grid.Rows[1].Cells[2];
                type.GetMethod("Add", flags)!.Invoke(form, [ItemKind.Task, true]);
                Application.DoEvents();
                Assert.Equal(5, grid.Rows.Count);
                Assert.Same(p.Items[1], grid.CurrentRow!.Tag);
                Assert.Equal("小項目", p.Items[1].Name);
                Assert.True((bool)type.GetField("dirty", flags)!.GetValue(form)!);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "表示チェック検証がタイムアウトしました。");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void ZoomRemainsResponsiveAndKeepsTopRowWithTwoHundredRowsAndSixMonths()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(MainForm);
                var project = new ScheduleProject
                {
                    Items = Enumerable.Range(1, 200).Select(i => new ScheduleItem
                    {
                        Name = $"作業{i}", ManualStart = new(2026, 11, 2), Effort = 5, Assignee = "A"
                    }).ToList()
                };
                project.View.SetPeriod(new(2026, 11, 1), new(2027, 4, 30));
                type.GetField("project", flags)!.SetValue(form, project);
                type.GetMethod("RefreshAll", flags)!.Invoke(form, null);
                form.Show(); Application.DoEvents();
                var grid = (DataGridView)type.GetField("grid", flags)!.GetValue(form)!;
                grid.FirstDisplayedScrollingRowIndex = 100;
                var change = type.GetMethod("ChangeZoom", flags)!;
                var times = new List<double>();
                foreach (int step in Enumerable.Repeat(1, 12).Concat(Enumerable.Repeat(-1, 17)).Append(0))
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    change.Invoke(form, [step]);
                    grid.Update();
                    clock.Stop();
                    times.Add(clock.Elapsed.TotalMilliseconds);
                    Assert.Equal(100, grid.FirstDisplayedScrollingRowIndex);
                }
                output.WriteLine($"200 rows / 6 months / {times.Count} zoom operations: max {times.Max():F1} ms, mean {times.Average():F1} ms");
                Assert.True(times.Max() < 250, $"ズーム1回が250msを超えました: {times.Max():F1}ms");
                Assert.Equal(28, grid.Columns[11].Width);
                Assert.Equal(30, grid.Rows[100].Height);
                Assert.False((bool)type.GetField("dirty", flags)!.GetValue(form)!);
                var key = type.GetMethod("ProcessCmdKey", flags)!;
                foreach (Keys plus in new[] { Keys.Oemplus, Keys.Add })
                {
                    Assert.True((bool)key.Invoke(form, [new Message(), Keys.Control | plus])!);
                    Assert.Equal(31, grid.Columns[11].Width);
                    Assert.True((bool)key.Invoke(form, [new Message(), Keys.Control | Keys.D0])!);
                    Assert.Equal(28, grid.Columns[11].Width);
                }
                foreach (Keys minus in new[] { Keys.OemMinus, Keys.Subtract })
                {
                    Assert.True((bool)key.Invoke(form, [new Message(), Keys.Control | minus])!);
                    Assert.Equal(25, grid.Columns[11].Width);
                    Assert.True((bool)key.Invoke(form, [new Message(), Keys.Control | Keys.NumPad0])!);
                    Assert.Equal(28, grid.Columns[11].Width);
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "ズーム検証がタイムアウトしました。");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
