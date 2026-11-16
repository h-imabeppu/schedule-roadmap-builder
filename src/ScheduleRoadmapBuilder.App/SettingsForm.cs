using ScheduleRoadmapBuilder.Core;

namespace ScheduleRoadmapBuilder.App;

internal sealed class SettingsForm : Form
{
    public ScheduleProject Result { get; }
    private readonly DataGridView statuses = CreateGrid();
    private readonly DataGridView assignees = CreateGrid();
    private readonly DataGridView calendar = CreateGrid();
    private readonly TextBox paste = new() { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both };

    public SettingsForm(ScheduleProject project)
    {
        Result = ProjectStorage.Deserialize(ProjectStorage.Serialize(project));
        Text = "設定"; Width = 780; Height = 600; StartPosition = FormStartPosition.CenterParent;
        var tabs = new TabControl { Dock = DockStyle.Fill };
        statuses.Columns.Add("name", "名前"); statuses.Columns.Add(new DataGridViewCheckBoxColumn { Name = "complete", HeaderText = "完了扱い" });
        assignees.Columns.Add("name", "名前"); assignees.Columns.Add("color", "色 (#RRGGBB)");
        assignees.Columns.Add(new DataGridViewTextBoxColumn { Name = "swatch", HeaderText = "色見本", ReadOnly = true });
        assignees.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 2 || e.Graphics is null) return;
            e.PaintBackground(e.ClipBounds, true);
            string value = Convert.ToString(assignees.Rows[e.RowIndex].Cells[1].Value) ?? "";
            if (value.Length == 7 && value[0] == '#' &&
                int.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int rgb))
            {
                using var brush = new SolidBrush(Color.FromArgb(255, Color.FromArgb(rgb)));
                var bounds = Rectangle.Inflate(e.CellBounds, -5, -4);
                e.Graphics.FillRectangle(brush, bounds);
                e.Graphics.DrawRectangle(Pens.Gray, bounds);
            }
            e.Handled = true;
        };
        assignees.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 1) assignees.InvalidateCell(2, e.RowIndex);
        };
        calendar.Columns.Add("date", "日付"); calendar.Columns.Add("name", "名称");
        var kind = new DataGridViewComboBoxColumn { Name = "kind", HeaderText = "区分" }; kind.Items.AddRange("祝", "休"); calendar.Columns.Add(kind);
        foreach (var s in Result.Settings.Statuses) { int i = statuses.Rows.Add(s.Name, s.IsComplete); statuses.Rows[i].Tag = s.Name; }
        foreach (var a in Result.Settings.Assignees) { int i = assignees.Rows.Add(a.Name, a.Color); assignees.Rows[i].Tag = a.Name; }
        FillCalendar();
        AddTab(tabs, "ステータス", statuses, true);
        AddTab(tabs, "担当者", assignees, true);
        AddTab(tabs, "カレンダー", calendar, false);
        var holidays = new TabPage("祝日一括取り込み");
        var importButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42 };
        var fromText = new Button { Text = "貼り付けを取り込む", AutoSize = true };
        var fromFile = new Button { Text = "CSVファイル", AutoSize = true };
        fromText.Click += (_, _) => ImportHolidays(() => paste.Text);
        fromFile.Click += (_, _) =>
        {
            using var file = new OpenFileDialog { Filter = "CSV / テキスト|*.csv;*.txt|すべて|*.*" };
            if (file.ShowDialog(this) == DialogResult.OK) ImportHolidays(() => HolidayImporter.ReadFile(file.FileName));
        };
        importButtons.Controls.AddRange([fromText, fromFile]);
        holidays.Controls.Add(paste); holidays.Controls.Add(importButtons);
        holidays.Controls.Add(new Label { Text = "yyyy-MM-dd,名称 または yyyy/M/d,名称（UTF-8 / Shift_JIS）", Dock = DockStyle.Top, Height = 28 });
        tabs.TabPages.Add(holidays);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "OK" }; var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            try { Apply(); DialogResult = DialogResult.OK; }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidDataException)
            { MessageBox.Show(this, ex.Message, "設定エラー"); }
        };
        footer.Controls.AddRange([cancel, ok]); Controls.Add(tabs); Controls.Add(footer); CancelButton = cancel;
        foreach (var grid in new[] { statuses, assignees, calendar })
            grid.DataError += (_, e) => { e.ThrowException = false; MessageBox.Show(this, e.Exception?.Message ?? "入力値が不正です。", "入力エラー"); };
        assignees.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 1) return;
            using var color = new ColorDialog { FullOpen = true };
            string value = Convert.ToString(assignees.Rows[e.RowIndex].Cells[1].Value) ?? "#4472C4";
            try { color.Color = ColorTranslator.FromHtml(value); }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "色の入力エラー"); }
            if (color.ShowDialog(this) == DialogResult.OK)
                assignees.Rows[e.RowIndex].Cells[1].Value = $"#{color.Color.R:X2}{color.Color.G:X2}{color.Color.B:X2}";
        };
    }

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };

    private void AddTab(TabControl tabs, string name, DataGridView grid, bool reorder)
    {
        var tab = new TabPage(name);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40 };
        var add = new Button { Text = "追加" };
        add.Click += (_, _) => grid.Rows.Add(grid == calendar ? [DateTime.Today.ToString("yyyy-MM-dd"), "休日", "休"] :
            grid == assignees ? ["新規担当", "#4472C4"] : ["新規ステータス", false]);
        var delete = new Button { Text = "削除" };
        delete.Click += (_, _) =>
        {
            if (grid.CurrentRow is not { } row) return;
            string? old = row.Tag as string;
            bool used = old is not null && Result.Items.Any(i => grid == statuses ? i.Status == old : i.Assignee == old);
            if (used && MessageBox.Show(this, "使用中です。削除すると該当項目の値が空欄になります。続行しますか？",
                "削除確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            grid.Rows.Remove(row);
        };
        buttons.Controls.AddRange([add, delete]);
        if (reorder)
            foreach (int delta in new[] { -1, 1 })
            {
                var move = new Button { Text = delta < 0 ? "↑" : "↓" };
                move.Click += (_, _) =>
                {
                    grid.EndEdit();
                    if (grid.CurrentRow is not { } row) return;
                    int index = row.Index, target = index + delta;
                    if (target < 0 || target >= grid.Rows.Count) return;
                    grid.Rows.Remove(row); grid.Rows.Insert(target, row); grid.CurrentCell = row.Cells[0];
                };
                buttons.Controls.Add(move);
            }
        tab.Controls.Add(grid); tab.Controls.Add(buttons); tabs.TabPages.Add(tab);
    }

    private static string TextAt(DataGridViewRow row, int cell) => Convert.ToString(row.Cells[cell].Value)?.Trim() ?? "";

    private List<HolidaySetting> ReadCalendar()
    {
        calendar.EndEdit();
        var list = new List<HolidaySetting>();
        foreach (DataGridViewRow row in calendar.Rows)
        {
            if (!DateOnly.TryParse(TextAt(row, 0), out var date)) throw new FormatException("カレンダーの日付が不正です。");
            string kind = TextAt(row, 2);
            if (kind is not ("祝" or "休")) throw new FormatException("区分は「祝」「休」から選んでください。");
            list.Add(new() { Date = date, Name = TextAt(row, 1), Kind = kind == "祝" ? HolidayKind.Holiday : HolidayKind.DayOff });
        }
        if (list.Select(h => h.Date).Distinct().Count() != list.Count) throw new FormatException("カレンダーの日付が重複しています。");
        return list;
    }

    private void FillCalendar()
    {
        calendar.Rows.Clear();
        foreach (var h in Result.Settings.Calendar) calendar.Rows.Add(h.Date.ToString("yyyy-MM-dd"), h.Name, h.Kind == HolidayKind.Holiday ? "祝" : "休");
    }

    private void ImportHolidays(Func<string> read)
    {
        try
        {
            var settings = new ProjectSettings { Calendar = ReadCalendar() };
            var count = HolidayImporter.Import(read(), settings);
            Result.Settings.Calendar = settings.Calendar; FillCalendar();
            MessageBox.Show(this, $"取り込み {count.Imported}件 / スキップ {count.Skipped}件", "祝日取り込み");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        { MessageBox.Show(this, ex.Message, "祝日取り込みエラー"); }
    }

    private void Apply()
    {
        statuses.EndEdit(); assignees.EndEdit();
        var settings = new ProjectSettings
        {
            Statuses = statuses.Rows.Cast<DataGridViewRow>().Select(r => new StatusSetting
                { Name = TextAt(r, 0), IsComplete = r.Cells[1].Value is true }).ToList(),
            Assignees = assignees.Rows.Cast<DataGridViewRow>().Select(r => new AssigneeSetting
                { Name = TextAt(r, 0), Color = TextAt(r, 1) }).ToList(),
            Calendar = ReadCalendar()
        };
        var candidate = ProjectStorage.Deserialize(ProjectStorage.Serialize(Result));
        candidate.Settings = settings;
        ProjectStorage.Deserialize(ProjectStorage.Serialize(candidate));
        foreach (var item in Result.Items)
        {
            item.Status = Renamed(statuses, item.Status);
            item.Assignee = Renamed(assignees, item.Assignee);
        }
        Result.Settings = settings;
    }

    private static string? Renamed(DataGridView grid, string? name) => name is null ? null :
        grid.Rows.Cast<DataGridViewRow>().Where(r => r.Tag as string == name).Select(r => TextAt(r, 0)).FirstOrDefault();
}
