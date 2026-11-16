using ScheduleRoadmapBuilder.Core;

namespace ScheduleRoadmapBuilder.App;

internal sealed class PptExportForm : Form
{
    public PptSettings Settings { get; private set; } = new();

    public PptExportForm(ScheduleProject project)
    {
        Text = "PPT出力設定"; Width = 420; Height = 250; StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var start = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = project.View.Start.ToDateTime(TimeOnly.MinValue), Width = 150 };
        var end = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = project.View.End.ToDateTime(TimeOnly.MinValue), Width = 150 };
        var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        mode.Items.AddRange(["全日付", "月曜のみ"]); mode.SelectedIndex = (int)project.View.Mode;
        var today = new CheckBox { Text = "今日の線を表示", Checked = project.Ppt.ShowToday, AutoSize = true };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new(12) };
        layout.Controls.Add(new Label { Text = "開始日", AutoSize = true }, 0, 0); layout.Controls.Add(start, 1, 0);
        layout.Controls.Add(new Label { Text = "終了日", AutoSize = true }, 0, 1); layout.Controls.Add(end, 1, 1);
        layout.Controls.Add(new Label { Text = "表示モード", AutoSize = true }, 0, 2); layout.Controls.Add(mode, 1, 2);
        layout.Controls.Add(today, 1, 3);
        var ok = new Button { Text = "出力" }; var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            if (end.Value.Date < start.Value.Date) { MessageBox.Show(this, "終了日は開始日以降にしてください。"); return; }
            Settings = new() { Start = DateOnly.FromDateTime(start.Value), End = DateOnly.FromDateTime(end.Value),
                Mode = (DayMode)mode.SelectedIndex, ShowToday = today.Checked };
            var warnings = PptExporter.Warnings(project, Settings);
            if (warnings.Count > 0 && MessageBox.Show(this, string.Join("\n", warnings) + "\nこの設定で出力しますか？",
                "PPT出力の警告", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            DialogResult = DialogResult.OK;
        };
        layout.Controls.Add(cancel, 0, 4); layout.Controls.Add(ok, 1, 4);
        Controls.Add(layout); AcceptButton = ok; CancelButton = cancel;
    }
}
