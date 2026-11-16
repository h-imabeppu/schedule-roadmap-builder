using ScheduleRoadmapBuilder.Core;
using Xunit;

namespace ScheduleRoadmapBuilder.Tests;

public sealed class ZoomTests
{
    [Fact]
    public void ZoomStepsClampAtLimitsAndReset()
    {
        var zoom = new ZoomLevel();
        Assert.Equal(100, zoom.Percent);
        zoom.Step(1);
        Assert.Equal(110, zoom.Percent);
        Assert.Equal(33, zoom.Scale(30));
        zoom.Step(-2);
        Assert.Equal(90, zoom.Percent);
        zoom.Step(int.MinValue);
        Assert.Equal(50, zoom.Percent);
        zoom.Step(-1);
        Assert.Equal(50, zoom.Percent);
        zoom.Step(int.MaxValue);
        Assert.Equal(200, zoom.Percent);
        zoom.Step(1);
        Assert.Equal(200, zoom.Percent);
        zoom.Reset();
        Assert.Equal(100, zoom.Percent);
    }
}
