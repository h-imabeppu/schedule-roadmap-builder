using System.Globalization;
using System.Text.Json;
using ScheduleRoadmapBuilder.Core;

namespace ScheduleRoadmapBuilder.App;

public sealed class MainForm : Form
{
    private ScheduleProject project = new();
    private Dictionary<Guid, ScheduleResult> results = [];
    private readonly ScheduleGrid grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToOrderColumns = false, RowHeadersVisible = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect, AutoGenerateColumns = false,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        ColumnHeadersHeight = 88, RowTemplate = { Height = 30 }, EnableHeadersVisualStyles = false
    };
    private readonly ToolStripTextBox projectName = new() { AutoSize = false, Width = 180 };
    private readonly DateTimePicker start = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly DateTimePicker end = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly ToolStripComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 90 };
    private readonly ToolStripComboBox displayMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 180 };
    private readonly ToolStripStatusLabel status = new();
    private readonly ToolStripStatusLabel visibilityStatus = new();
    private readonly ToolStripStatusLabel zoomStatus = new() { Text = "100%" };
    private readonly ZoomLevel zoom = new();
    private string? filePath;
    private bool dirty, refreshing;
    private Font majorFont;
    private Font? gridFont;

    public MainForm()
    {
        Text = "Schedule Roadmap Builder";
        Width = 1650; Height = 850; MinimumSize = new(1100, 500);
        Font = new("Meiryo UI", 9);
        majorFont = new(Font, FontStyle.Bold);
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("ファイル");
        AddMenu(file, "新規", () => { if (CanLeave()) { project = new(); filePath = null; dirty = false; RefreshAll(); } });
        AddMenu(file, "開く", Open);
        AddMenu(file, "保存", () => Save(false), Keys.Control | Keys.S);
        AddMenu(file, "名前を付けて保存", () => Save(true));
        AddMenu(file, "PNG出力", ExportPng);
        AddMenu(file, "Excel出力", ExportExcel);
        AddMenu(file, "PPT出力", ExportPpt);
        AddMenu(file, "終了", Close);
        menu.Items.Add(file);
        var settings = new ToolStripMenuItem("設定");
        AddMenu(settings, "設定を編集", EditSettings);
        AddMenu(settings, "他のプロジェクトから設定を取り込む", ImportSettings);
        menu.Items.Add(settings);
        var help = new ToolStripMenuItem("ヘルプ");
        AddMenu(help, "バージョン", () => MessageBox.Show(this, "Schedule Roadmap Builder 1.0\n.NET 9 / WinForms", "バージョン"));
        menu.Items.Add(help);
        MainMenuStrip = menu;
        var rows = new ToolStrip();
        rows.Items.Add(new ToolStripLabel("プロジェクト名"));
        rows.Items.Add(projectName);
        foreach (var kind in Enum.GetValues<ItemKind>())
        {
            ItemKind captured = kind;
            rows.Items.Add(new ToolStripButton($"{ScheduleAppearance.KindName(kind)}追加", null, (_, _) => Add(captured, false)));
            rows.Items.Add(new ToolStripButton("挿入", null, (_, _) => Add(captured, true)));
        }
        rows.Items.Add(new ToolStripButton("削除", null, (_, _) => Delete()));
        rows.Items.Add(new ToolStripButton("↑", null, (_, _) => MoveRow(-1)));
        rows.Items.Add(new ToolStripButton("↓", null, (_, _) => MoveRow(1)));
        rows.Items.Add(new ToolStripButton("PPT表示 ON/OFF", null, (_, _) => TogglePpt()));
        var period = new ToolStrip();
        period.Items.Add(new ToolStripLabel("表示開始"));
        period.Items.Add(new ToolStripControlHost(start));
        period.Items.Add(new ToolStripLabel("終了"));
        period.Items.Add(new ToolStripControlHost(end));
        period.Items.Add(new ToolStripButton("期間を適用", null, (_, _) => Run(() =>
        {
            project.View.SetPeriod(DateOnly.FromDateTime(start.Value), DateOnly.FromDateTime(end.Value));
            Changed(); RefreshAll();
        })));
        period.Items.Add(new ToolStripButton("自動", null, (_, _) => Run(() => { CalendarRules.AutoPeriod(project, results); Changed(); RefreshAll(); })));
        mode.Items.AddRange(["全日付", "月曜のみ"]);
        period.Items.Add(mode);
        displayMode.Items.AddRange(["全表示", "チェック済みだけ表示"]);
        period.Items.Add(displayMode);
        var bottom = new StatusStrip(); bottom.Items.Add(status); bottom.Items.Add(zoomStatus); bottom.Items.Add(visibilityStatus);
        Controls.Add(grid); Controls.Add(bottom); Controls.Add(period); Controls.Add(rows); Controls.Add(menu);
        projectName.TextChanged += (_, _) => { if (!refreshing) { project.Name = projectName.Text; Changed(); } };
        mode.SelectedIndexChanged += (_, _) =>
        {
            if (!refreshing && mode.SelectedIndex >= 0)
            {
                project.View.Mode = (DayMode)mode.SelectedIndex;
                Changed(); grid.Invalidate();
            }
        };
        displayMode.SelectedIndexChanged += (_, _) =>
        {
            if (!refreshing && displayMode.SelectedIndex >= 0 && EndEdit())
            {
                project.View.DisplayMode = (DisplayMode)displayMode.SelectedIndex;
                Changed(); RefreshAll();
            }
        };
        grid.CellPainting += PaintCell;
        grid.Paint += PaintSpanningHeaders;
        grid.CellValidating += ValidateCell;
        grid.CellEndEdit += (_, e) => { if (e.ColumnIndex is not (1 or 4 or 9 or 10)) CommitCell(grid, e); };
        grid.CellValueChanged += (_, e) => { if (e.ColumnIndex is 1 or 4 or 9 or 10) CommitCell(grid, e); };
        grid.ZoomRequested += steps => ChangeZoom(steps);
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewComboBoxCell or DataGridViewCheckBoxCell)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.DataError += (_, e) => { e.ThrowException = false; MessageBox.Show(this, e.Exception?.Message ?? "入力値が不正です。", "入力エラー"); };
        FormClosing += (_, e) => { if (!CanLeave()) e.Cancel = true; };
        RefreshAll();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { majorFont.Dispose(); gridFont?.Dispose(); start.Dispose(); end.Dispose(); }
        base.Dispose(disposing);
    }

    private static void AddMenu(ToolStripMenuItem parent, string text, Action action, Keys keys = Keys.None)
    {
        var entry = new ToolStripMenuItem(text, null, (_, _) => action()) { ShortcutKeys = keys };
        parent.DropDownItems.Add(entry);
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or FormatException or JsonException or System.Xml.XmlException or
            System.Runtime.InteropServices.ExternalException)
        { MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private int SelectedIndex => grid.CurrentRow?.Tag is ScheduleItem item ? project.Items.IndexOf(item) : -1;
    private void Changed() { dirty = true; UpdateTitle(); }
    private void UpdateTitle() => Text = $"{(dirty ? "*" : "")}{(filePath is null ? "新規" : Path.GetFileName(filePath))} - Schedule Roadmap Builder";

    private bool EndEdit() => grid.EndEdit();

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.Control) != 0)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.D0: case Keys.NumPad0: ChangeZoom(0); return true;
                case Keys.Add: case Keys.Oemplus: ChangeZoom(1); return true;
                case Keys.Subtract: case Keys.OemMinus: ChangeZoom(-1); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ChangeZoom(int steps)
    {
        if (steps == 0) zoom.Reset(); else zoom.Step(steps);
        int top = grid.FirstDisplayedScrollingRowIndex;
        grid.SuspendLayout();
        try
        {
            var oldFont = gridFont;
            var oldMajor = majorFont;
            gridFont = new Font(Font.FontFamily, Font.Size * zoom.Percent / 100f);
            majorFont = new Font(gridFont, FontStyle.Bold);
            grid.Font = gridFont;
            grid.ColumnHeadersHeight = zoom.Scale(88);
            grid.RowTemplate.Height = zoom.Scale(30);
            foreach (DataGridViewColumn col in grid.Columns)
                col.Width = zoom.Scale(col.Tag is int width ? width : 28);
            foreach (DataGridViewRow row in grid.Rows)
            {
                row.Height = zoom.Scale(30);
                row.DefaultCellStyle.Font = row.Tag is ScheduleItem { Kind: ItemKind.Major } ? majorFont : gridFont;
            }
            oldFont?.Dispose(); oldMajor.Dispose();
            if (top >= 0) grid.FirstDisplayedScrollingRowIndex = top;
            zoomStatus.Text = $"{zoom.Percent}%";
        }
        finally { grid.ResumeLayout(); grid.Invalidate(); }
    }

    private void RefreshAll()
    {
        if (IsDisposed) return;
        int selected = SelectedIndex;
        int column = grid.CurrentCell?.ColumnIndex ?? 2;
        int top = grid.FirstDisplayedScrollingRowIndex;
        int horizontal = grid.HorizontalScrollingOffset;
        refreshing = true;
        try
        {
            results = Scheduler.Calculate(project);
            projectName.Text = project.Name;
            start.Value = project.View.Start.ToDateTime(TimeOnly.MinValue);
            end.Value = project.View.End.ToDateTime(TimeOnly.MinValue);
            mode.SelectedIndex = (int)project.View.Mode;
            displayMode.SelectedIndex = (int)project.View.DisplayMode;
            grid.Rows.Clear(); grid.Columns.Clear();
            string[] names = ["No", "種別", "項目", "前工程", "ステータス", "備考", "工数", "開始日", "完了予定日", "担当"];
            int[] widths = [38, 95, 155, 65, 75, 120, 45, 93, 93, 75];
            for (int c = 0; c < names.Length; c++)
            {
                DataGridViewColumn col;
                if (c is 1 or 4 or 9)
                {
                    var combo = new DataGridViewComboBoxColumn { FlatStyle = FlatStyle.Flat };
                    if (c != 1) combo.Items.Add("");
                    combo.Items.AddRange(c == 1 ? ["大項目", "小項目", "マイルストーン"] :
                        c == 4 ? project.Settings.Statuses.Select(s => (object)s.Name).ToArray() :
                        project.Settings.Assignees.Select(a => (object)a.Name).ToArray());
                    col = combo;
                }
                else col = new DataGridViewTextBoxColumn();
                col.Name = names[c]; col.HeaderText = names[c]; col.Tag = widths[c]; col.Width = zoom.Scale(widths[c]);
                col.Frozen = true; col.SortMode = DataGridViewColumnSortMode.NotSortable;
                col.ReadOnly = c is 0 or 8;
                grid.Columns.Add(col);
            }
            grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "表示", HeaderText = "表示", Width = zoom.Scale(45), Tag = 45,
                Frozen = true, SortMode = DataGridViewColumnSortMode.NotSortable, DisplayIndex = 0
            });
            int days = project.View.End.DayNumber - project.View.Start.DayNumber + 1;
            for (int d = 0; d < days; d++)
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = $"day{d}", Width = zoom.Scale(28), ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable,
                    Resizable = DataGridViewTriState.False, Tag = project.View.Start.AddDays(d)
                });
            var numbers = project.Numbers();
            foreach (var item in ItemVisibility.Filter(project))
            {
                var r = results[item.Id];
                bool aggregate = item.Kind == ItemKind.Major && project.Children(item).Count > 0;
                int index = grid.Rows.Add(numbers.TryGetValue(item.Id, out int n) ? n.ToString() : "",
                    ScheduleAppearance.KindName(item.Kind), item.Kind == ItemKind.Major ? "■ " + item.Name : item.Name,
                    string.Join(",", item.Predecessors.Select(id => numbers.TryGetValue(id, out int no) ? no.ToString() : "?")),
                    project.Settings.Statuses.Any(s => s.Name == item.Status) ? item.Status ?? "" : "",
                    item.Notes, aggregate || item.Kind == ItemKind.Milestone ? "" : item.Effort?.ToString() ?? "",
                    (aggregate ? r.Start : item.ManualStart ?? r.Start)?.ToString("yyyy-MM-dd") ?? "",
                    r.Finish?.ToString("yyyy-MM-dd") ?? "",
                    project.Settings.Assignees.Any(a => a.Name == item.Assignee) ? item.Assignee ?? "" : "", item.IsVisible);
                var row = grid.Rows[index]; row.Tag = item;
                row.Cells[2].ToolTipText = item.Kind != ItemKind.Task ? $"PPTに表示: {(item.ShowInPpt ? "ON" : "OFF")}" : "";
                if (item.Kind == ItemKind.Major) row.DefaultCellStyle.Font = majorFont;
                foreach (int c in new[] { 3, 6, 7 }) row.Cells[c].ReadOnly = aggregate || (c == 6 && item.Kind == ItemKind.Milestone);
                row.Cells[8].Style.BackColor = Color.Gainsboro;
                if (aggregate || item.ManualStart is null) row.Cells[7].Style.BackColor = Color.Gainsboro;
                if (r.Errors.Count > 0 || r.Warnings.Count > 0)
                {
                    string message = string.Join("\n", r.Errors.Concat(r.Warnings));
                    foreach (int c in new[] { 2, 3, 6, 7, 8 })
                    {
                        row.Cells[c].Style.BackColor = r.Errors.Count > 0 ? Color.MistyRose : Color.LemonChiffon;
                        row.Cells[c].ToolTipText = message;
                    }
                    row.ErrorText = message;
                }
            }
            if (grid.Rows.Count > 0)
            {
                var selectedRow = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r =>
                    r.Tag is ScheduleItem item && project.Items.IndexOf(item) == selected)
                    ?? grid.Rows[Math.Clamp(selected, 0, grid.Rows.Count - 1)];
                grid.CurrentCell = selectedRow.Cells[Math.Clamp(column, 0, grid.Columns.Count - 1)];
                if (top >= 0) grid.FirstDisplayedScrollingRowIndex = Math.Min(top, grid.Rows.Count - 1);
            }
            grid.HorizontalScrollingOffset = horizontal;
            status.Text = $"エラー {results.Values.Sum(r => r.Errors.Count)} / 警告 {results.Values.Sum(r => r.Warnings.Count)}　{project.Items.Count}行";
            visibilityStatus.Text = project.View.DisplayMode == DisplayMode.CheckedOnly
                ? $"表示：チェック済みのみ（{grid.Rows.Count} / 全{project.Items.Count}行）" : "";
            UpdateTitle();
        }
        finally { refreshing = false; }
    }

    private void PaintCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.ColumnIndex < 11 || e.ColumnIndex >= grid.Columns.Count ||
            grid.Columns[e.ColumnIndex].Tag is not DateOnly date || e.Graphics is null) return;
        if (e.RowIndex == -1)
        {
            e.Graphics.FillRectangle(SystemBrushes.Control, e.CellBounds);
            string[] labels = ScheduleAppearance.Header(date, project.View.Start, project.View.Mode, project.Settings);
            for (int line = 2; line < 4; line++)
            {
                var bounds = new Rectangle(e.CellBounds.Left, e.CellBounds.Top + line * zoom.Scale(22), e.CellBounds.Width, zoom.Scale(22));
                TextRenderer.DrawText(e.Graphics, labels[line], grid.Font, bounds, Color.Black,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
        else if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].Tag is ScheduleItem item)
        {
            ScheduleAppearance.DrawCalendarCell(e.Graphics, e.CellBounds, project, item, results[item.Id], date,
                (e.State & DataGridViewElementStates.Selected) != 0);
        }
        e.Handled = true;
    }

    private void PaintSpanningHeaders(object? sender, PaintEventArgs e)
    {
        if (grid.Columns.Count <= 11) return;
        int left = grid.Columns.Cast<DataGridViewColumn>().Take(11).Sum(c => c.Width);
        var state = e.Graphics.Save();
        e.Graphics.SetClip(new Rectangle(left, 0, Math.Max(0, grid.ClientSize.Width - left), grid.ColumnHeadersHeight));
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (column.Tag is not DateOnly date) continue;
            var bounds = grid.GetColumnDisplayRectangle(column.Index, false);
            if (bounds.Right < left - 90 || bounds.Left > grid.ClientSize.Width) continue;
            string[] labels = ScheduleAppearance.Header(date, project.View.Start, project.View.Mode, project.Settings);
            for (int line = 0; line < 2; line++)
                TextRenderer.DrawText(e.Graphics, labels[line], grid.Font, new Rectangle(bounds.Left, line * zoom.Scale(22), zoom.Scale(90), zoom.Scale(22)),
                    Color.Black, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        e.Graphics.Restore(state);
    }

    private void ValidateCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (refreshing || e.RowIndex < 0 || e.ColumnIndex >= 10 || grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly) return;
        var item = (ScheduleItem)grid.Rows[e.RowIndex].Tag!;
        string text = Convert.ToString(e.FormattedValue, CultureInfo.CurrentCulture) ?? "";
        try
        {
            if (e.ColumnIndex == 2 && string.IsNullOrWhiteSpace(text)) throw new ArgumentException("項目名は必須です。");
            if (e.ColumnIndex == 3) project.ParsePredecessors(item, text);
            if (e.ColumnIndex == 7 && text.Length > 0 && !DateOnly.TryParse(text, out _))
                throw new ArgumentException("開始日は日付として入力してください。空欄で手入力を解除できます。");
        }
        catch (ArgumentException ex) { e.Cancel = true; grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = ex.Message; status.Text = ex.Message; }
    }

    private void CommitCell(object? sender, DataGridViewCellEventArgs e)
    {
        if (refreshing || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count || e.ColumnIndex < 0 || e.ColumnIndex >= 11) return;
        var item = (ScheduleItem)grid.Rows[e.RowIndex].Tag!;
        string text = Convert.ToString(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value, CultureInfo.CurrentCulture) ?? "";
        switch (e.ColumnIndex)
        {
            case 1:
                item.Kind = text == "大項目" ? ItemKind.Major : text == "マイルストーン" ? ItemKind.Milestone : ItemKind.Task;
                if (item.Kind == ItemKind.Milestone) item.Effort = null;
                break;
            case 2: item.Name = item.Kind == ItemKind.Major && text.StartsWith("■ ") ? text[2..] : text; break;
            case 3: item.Predecessors = project.ParsePredecessors(item, text); break;
            case 4: item.Status = text.Length == 0 ? null : text; break;
            case 5: item.Notes = text; break;
            case 6: item.Effort = int.TryParse(text, out int effort) ? effort : null; break;
            case 7: item.ManualStart = DateOnly.TryParse(text, out var date) ? date : null; break;
            case 9: item.Assignee = text.Length == 0 ? null : text; break;
            case 10: ItemVisibility.SetChecked(project, item, (bool)grid.Rows[e.RowIndex].Cells[10].Value!); break;
            default: return;
        }
        Changed();
        results = Scheduler.Calculate(project);
        grid.Invalidate();
        BeginInvoke((Action)RefreshAll);
    }

    private void Add(ItemKind kind, bool insert)
    {
        if (!EndEdit()) return;
        int index = insert && SelectedIndex >= 0 ? SelectedIndex : project.Items.Count;
        var item = new ScheduleItem { Kind = kind, Name = ScheduleAppearance.KindName(kind), Effort = kind == ItemKind.Milestone ? null : 1 };
        project.Items.Insert(index, item);
        Changed(); RefreshAll(); SelectItem(item);
    }

    private void Delete()
    {
        if (!EndEdit() || SelectedIndex < 0) return;
        var item = project.Items[SelectedIndex];
        var numbers = project.Numbers();
        string refs = string.Join(", ", project.Items.Where(i => i.Predecessors.Contains(item.Id))
            .Select(i => numbers.TryGetValue(i.Id, out int n) ? n.ToString() : $"大項目「{i.Name}」"));
        if (MessageBox.Show(this, $"「{item.Name}」を削除しますか？\n参照元No: {(refs.Length == 0 ? "なし" : refs)}\n参照も解除されます。",
            "削除確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        project.Delete(item); Changed(); RefreshAll();
    }

    private void MoveRow(int delta)
    {
        if (!EndEdit()) return;
        int from = SelectedIndex, to = from + delta;
        if (from < 0 || to < 0 || to >= project.Items.Count) return;
        (project.Items[from], project.Items[to]) = (project.Items[to], project.Items[from]);
        Changed(); RefreshAll(); SelectItem(project.Items[to]);
    }

    private void SelectItem(ScheduleItem item)
    {
        var row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ReferenceEquals(r.Tag, item));
        if (row is not null) grid.CurrentCell = row.Cells[2];
    }

    private void TogglePpt()
    {
        if (!EndEdit() || SelectedIndex < 0) return;
        var item = project.Items[SelectedIndex];
        if (item.Kind == ItemKind.Task) return;
        item.ShowInPpt = !item.ShowInPpt; Changed(); RefreshAll();
    }

    private bool CanLeave()
    {
        if (!EndEdit()) return false;
        if (!dirty) return true;
        var answer = MessageBox.Show(this, "変更を保存しますか？", "未保存の変更", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return answer == DialogResult.No || answer == DialogResult.Yes && Save(false);
    }

    private void Open()
    {
        if (!CanLeave()) return;
        using var dialog = new OpenFileDialog { Filter = "SRBプロジェクト|*.srb.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Run(() => { var loaded = ProjectStorage.Load(dialog.FileName); project = loaded; filePath = dialog.FileName; dirty = false; RefreshAll(); });
    }

    private bool Save(bool saveAs)
    {
        if (!EndEdit()) return false;
        string? path = filePath;
        if (saveAs || path is null)
        {
            using var dialog = new SaveFileDialog { Filter = "SRBプロジェクト|*.srb.json", FileName = SafeName(project.Name) + ".srb.json", DefaultExt = "srb.json" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
        }
        bool saved = false;
        Run(() => { ProjectStorage.Save(path, project); filePath = path; dirty = false; UpdateTitle(); saved = true; });
        return saved;
    }

    internal static string SafeName(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private void EditSettings()
    {
        if (!EndEdit()) return;
        using var dialog = new SettingsForm(project);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        project.Settings = dialog.Result.Settings;
        for (int i = 0; i < project.Items.Count; i++)
        {
            project.Items[i].Status = dialog.Result.Items[i].Status;
            project.Items[i].Assignee = dialog.Result.Items[i].Assignee;
        }
        Changed(); RefreshAll();
    }

    private void ImportSettings()
    {
        if (!EndEdit()) return;
        using var dialog = new OpenFileDialog { Filter = "SRBプロジェクト|*.srb.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Run(() =>
        {
            var source = ProjectStorage.Load(dialog.FileName);
            var answer = MessageBox.Show(this, "設定を置換しますか？\n「はい」: 置換（存在しなくなる担当・ステータスは空欄）\n「いいえ」: 追加（重複は除外）",
                "設定取り込み", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return;
            ProjectStorage.ImportSettings(project, source.Settings, answer == DialogResult.Yes);
            Changed(); RefreshAll();
        });
    }

    private void ExportPng()
    {
        if (!EndEdit()) return;
        var scope = MessageBox.Show(this, "全項目を出力しますか？\n「はい」: 全項目\n「いいえ」: 大項目とマイルストーンのみ",
            "PNG出力範囲", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (scope == DialogResult.Cancel) return;
        using var dialog = new SaveFileDialog { Filter = "PNG画像|*.png", FileName = $"{SafeName(project.Name)}_日程_{DateTime.Today:yyyyMMdd}.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Run(() => { PngExporter.Export(dialog.FileName, project, scope == DialogResult.No); status.Text = "PNGを出力しました。"; });
    }

    private void ExportExcel()
    {
        if (!EndEdit()) return;
        var scope = MessageBox.Show(this, "全項目を出力しますか？\n「はい」: 全項目\n「いいえ」: 大項目とマイルストーンのみ",
            "Excel出力範囲", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (scope == DialogResult.Cancel) return;
        using var dialog = new SaveFileDialog { Filter = "Excel|*.xlsx", FileName = $"{SafeName(project.Name)}_日程_{DateTime.Today:yyyyMMdd}.xlsx" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Run(() => { ExcelExporter.Export(dialog.FileName, project, scope == DialogResult.No); status.Text = "Excelを出力しました。"; });
    }

    private void ExportPpt()
    {
        if (!EndEdit()) return;
        using var settings = new PptExportForm(project);
        if (settings.ShowDialog(this) != DialogResult.OK) return;
        using var dialog = new SaveFileDialog { Filter = "PowerPoint|*.pptx", FileName = $"{SafeName(project.Name)}_全体日程_{DateTime.Today:yyyyMMdd}.pptx" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Run(() =>
        {
            PptExporter.Export(dialog.FileName, project, settings.Settings);
            project.Ppt = settings.Settings; Changed(); status.Text = "PPTを出力しました。";
        });
    }
}
