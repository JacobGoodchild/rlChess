namespace RlChess.Engine.Tests;

public class ClockTests
{
    private TimeSpan _now;
    private ChessClock NewClock(double minutes, double inc) => new(TimeControl.FromMinutes(minutes, inc), () => _now);
    private void Wait(double seconds) => _now += TimeSpan.FromSeconds(seconds);

    [Fact]
    public void OnlyTheRunningSideLosesTime()
    {
        var c = NewClock(1, 0);
        c.Start(Color.White);
        Wait(10);
        Assert.Equal(TimeSpan.FromSeconds(50), c.Remaining(Color.White));
        Assert.Equal(TimeSpan.FromSeconds(60), c.Remaining(Color.Black));
    }

    [Fact]
    public void PressAddsIncrementAndSwitches()
    {
        var c = NewClock(3, 2);
        c.Start(Color.White);
        Wait(5);
        Assert.True(c.Press(Color.White));
        Assert.Equal(TimeSpan.FromSeconds(177), c.Remaining(Color.White)); // 180 - 5 + 2
        Assert.Equal(Color.Black, c.Running);
        Wait(30);
        c.Press(Color.Black);
        Assert.Equal(TimeSpan.FromSeconds(152), c.Remaining(Color.Black)); // 180 - 30 + 2
        Assert.Equal(TimeSpan.FromSeconds(177), c.Remaining(Color.White));
    }

    [Fact]
    public void StopAndResumeKeepsTime()
    {
        var c = NewClock(1, 0);
        c.Start(Color.Black);
        Wait(20);
        c.Stop();
        Wait(100); // paused: nothing is used
        Assert.Equal(TimeSpan.FromSeconds(40), c.Remaining(Color.Black));
        c.Start(Color.Black);
        Wait(5);
        Assert.Equal(TimeSpan.FromSeconds(35), c.Remaining(Color.Black));
    }

    [Fact]
    public void FlagFallsAndNoIncrementAfterwards()
    {
        var c = NewClock(0.5, 5);
        c.Start(Color.White);
        Wait(31);
        Assert.True(c.IsFlagged(Color.White));
        Assert.Equal(TimeSpan.Zero, c.Remaining(Color.White));
        Assert.False(c.Press(Color.White)); // too late: no increment rescue
        Assert.Equal(TimeSpan.Zero, c.Remaining(Color.White));
    }

    [Fact]
    public void TimeControlName() => Assert.Equal("3+2", TimeControl.FromMinutes(3, 2).Name);

    [Theory]
    [InlineData("4k3/8/8/8/8/8/8/4K2Q w - - 0 1", Color.White, GameOutcome.Draw)]      // Black (lone king) can't mate
    [InlineData("4k3/8/8/8/8/8/8/4K2Q w - - 0 1", Color.Black, GameOutcome.WhiteWins)] // White has a queen
    [InlineData("4kn2/8/8/8/8/8/8/4K3 w - - 0 1", Color.White, GameOutcome.Draw)]      // K+N vs bare K
    [InlineData("4kn2/8/8/8/8/8/P7/4K3 w - - 0 1", Color.White, GameOutcome.BlackWins)] // K+N vs K+P: helpmate possible
    [InlineData("4kb2/8/8/8/8/8/8/4K3 w - - 0 1", Color.White, GameOutcome.Draw)]      // K+B vs bare K
    [InlineData("4kb2/8/8/8/8/8/8/2B1K3 w - - 0 1", Color.White, GameOutcome.Draw)]    // bishops on the same colour (f8, c1 both dark)
    [InlineData("4kb2/8/8/8/8/8/8/3BK3 w - - 0 1", Color.White, GameOutcome.BlackWins)] // opposite-coloured bishop can block
    [InlineData("4k3/8/8/8/8/8/8/4K1NN w - - 0 1", Color.Black, GameOutcome.WhiteWins)] // two knights can (help)mate
    [InlineData("4k3/8/8/8/8/8/8/R3K3 w - - 0 1", Color.Black, GameOutcome.WhiteWins)]
    public void TimeoutResult(string fen, Color flagged, GameOutcome expected)
    {
        var r = TimeoutRules.Result(Position.FromFen(fen), flagged);
        Assert.Equal(expected, r.Outcome);
        Assert.Equal(expected == GameOutcome.Draw ? GameEndReason.TimeoutVsInsufficientMaterial : GameEndReason.Timeout, r.Reason);
    }
}
