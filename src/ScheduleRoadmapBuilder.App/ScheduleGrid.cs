namespace ScheduleRoadmapBuilder.App;

internal sealed class ScheduleGrid : DataGridView
{
    public event Action<int>? ZoomRequested;

    public ScheduleGrid() => DoubleBuffered = true;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) != 0)
            ZoomRequested?.Invoke(Math.Sign(e.Delta));
        else if ((ModifierKeys & Keys.Shift) != 0)
        {
            int maximum = Math.Max(0, Columns.GetColumnsWidth(DataGridViewElementStates.Visible) -
                ClientSize.Width + SystemInformation.VerticalScrollBarWidth);
            HorizontalScrollingOffset = Math.Clamp(HorizontalScrollingOffset - Math.Sign(e.Delta) * 84, 0, maximum);
        }
        else base.OnMouseWheel(e);
    }
}
