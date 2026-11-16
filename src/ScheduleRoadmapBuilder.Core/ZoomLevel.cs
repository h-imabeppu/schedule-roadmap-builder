namespace ScheduleRoadmapBuilder.Core;

public sealed class ZoomLevel
{
    public int Percent { get; private set; } = 100;
    public void Step(int steps) => Percent = (int)Math.Clamp(Percent + (long)steps * 10, 50, 200);
    public void Reset() => Percent = 100;
    public int Scale(int value) => (int)Math.Round(value * Percent / 100.0);
}
