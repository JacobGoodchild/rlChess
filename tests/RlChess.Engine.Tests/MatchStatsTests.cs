using RlChess.Engine;
using Xunit;

namespace RlChess.Engine.Tests;

public class MatchStatsTests
{
    [Fact]
    public void CountsAndScore()
    {
        var s = new MatchStats();
        s.Add(GameResult.Win(Color.White, GameEndReason.Checkmate), p1White: true);   // P1 wins
        s.Add(GameResult.Win(Color.White, GameEndReason.Checkmate), p1White: false);  // P2 wins
        s.Add(GameResult.Draw(GameEndReason.Stalemate), p1White: true);
        s.Add(GameResult.Win(Color.Black, GameEndReason.Resignation), p1White: false); // P1 (black) wins
        Assert.Equal((2, 1, 1, 4), (s.Wins, s.Draws, s.Losses, s.Games));
        Assert.Equal(0.625, s.Score, 6);
        Assert.Equal([1.0, 0, 0.5, 1], s.Scores);
    }

    [Fact]
    public void EloMatchesKnownValues()
    {
        Assert.Equal(0, MatchStats.ToElo(0.5), 6);
        Assert.Equal(191, MatchStats.ToElo(0.75), 0); // 75% ≈ +191
        var (diff, margin) = MatchStats.EloEstimate(60, 20, 20)!.Value;
        Assert.True(diff > 100 && diff < 200, $"diff {diff}");
        Assert.True(margin > 0 && margin < 100, $"margin {margin}");
        Assert.Null(MatchStats.EloEstimate(5, 0, 0)); // all wins: no finite estimate
        Assert.Null(MatchStats.EloEstimate(0, 0, 0));
    }

    [Fact]
    public void IntervalNarrowsWithMoreGames()
    {
        var (lo10, hi10) = MatchStats.ScoreInterval(5, 0, 5);
        var (lo1000, hi1000) = MatchStats.ScoreInterval(500, 0, 500);
        Assert.True(hi10 - lo10 > hi1000 - lo1000);
        Assert.InRange(0.5, lo1000, hi1000);
    }

    [Fact]
    public void ChartIsSvgEvenWithNoGames()
    {
        Assert.StartsWith("<svg", new MatchStats().ScoreChartSvg());
        var s = new MatchStats();
        for (int i = 0; i < 1000; i++) s.Add(i % 3 == 0 ? 1 : 0.5);
        var svg = s.ScoreChartSvg();
        Assert.Contains("After game 1000", svg);
        Assert.EndsWith("</svg>", svg);
    }
}

public class TrainingDataTests
{
    [Fact]
    public void OneRowPerPositionWithResultForTheMover()
    {
        var g = new Game();
        foreach (var m in new[] { "f3", "e5", "g4", "Qh4#" }) g.Play(m);
        var lines = TrainingData.ToCsv([g]).TrimEnd('\n').Split('\n');
        Assert.Equal(TrainingData.CsvHeader, lines[0]);
        Assert.Equal(5, lines.Length);
        Assert.Equal($"1,0,{Position.StartFen},f2f3,-1", lines[1]); // white to move, white lost
        Assert.EndsWith(",d8h4,1", lines[4]);                         // black's mating move, black won
    }

    [Fact]
    public void DrawsScoreZero()
    {
        var g = new Game();
        g.Play("e4");
        g.End(GameResult.Draw(GameEndReason.Agreement));
        Assert.EndsWith(",e2e4,0", TrainingData.ToCsv([g]).TrimEnd('\n'));
    }
}
