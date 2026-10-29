using WpfTaskBar;
using Xunit;

public sealed class SystemMetricsTests
{
    [Fact]
    public void CpuUsageUsesDeltasAndDoesNotCountIdleTimeTwice()
    {
        var previous = new CpuTimes(1_000, 2_000, 3_000);
        // 差分: Kernel 60（うち Idle 40）、User 40 → 使用率 60%。
        var current = new CpuTimes(1_040, 2_060, 3_040);
        Assert.Equal(60d, current.UsageSince(previous));
    }

    [Theory]
    [InlineData(100, 100, 0, 0)]
    [InlineData(0, 50, 50, 100)]
    [InlineData(25, 50, 50, 75)]
    public void CpuUsageCoversIdleBusyAndMixedWorkloads(ulong idle, ulong kernel, ulong user, double expected)
    {
        Assert.Equal(expected, new CpuTimes(idle, kernel, user).UsageSince(default));
    }

    [Fact]
    public void MissingIntervalsAndResetCountersAreUnavailableRatherThanZero()
    {
        var previous = new CpuTimes(100, 200, 300);
        Assert.Null(previous.UsageSince(previous));
        Assert.Null(new CpuTimes(99, 210, 310).UsageSince(previous));
        Assert.Null(new CpuTimes(110, 199, 310).UsageSince(previous));
        Assert.Null(new CpuTimes(110, 210, 299).UsageSince(previous));
        Assert.Null(new CpuTimes(500, 210, 310).UsageSince(previous));
    }
}
