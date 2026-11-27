using System.Diagnostics;
using RlChess.Engine;
using RlChess.Players;
using Xunit;

namespace RlChess.Engine.Tests;

public class StockfishStrengthTests
{
    [Fact]
    public void CalibratedCommands()
    {
        var s = StockfishStrength.AtElo(1500);
        Assert.True(s.IsCalibrated);
        Assert.Equal(["setoption name Skill Level value 20", "setoption name UCI_LimitStrength value true", "setoption name UCI_Elo value 1500"], s.SetupCommands());
        Assert.Equal("go movetime 1000", s.GoCommand());
        Assert.Equal("go movetime 250", (s with { MoveTime = TimeSpan.FromMilliseconds(250) }).GoCommand());
        Assert.Equal(1320, StockfishStrength.AtElo(900).Elo);   // clamped to Stockfish's range
        Assert.Equal(3190, StockfishStrength.AtElo(5000).Elo);
    }

    [Fact]
    public void SkillLevelsAreUncalibrated()
    {
        var s = StockfishStrength.AtSkill(3);
        Assert.False(s.IsCalibrated);
        Assert.Equal(["setoption name UCI_LimitStrength value false", "setoption name Skill Level value 3"], s.SetupCommands());
        Assert.Contains("uncalibrated", s.ToString());
    }

    [Fact]
    public void ReadsBestMove()
    {
        var p = new Position();
        Assert.True(StockfishStrength.TryParseBestMove("bestmove e2e4 ponder e7e5", p, out var m));
        Assert.Equal("e2e4", m.ToUci());
        Assert.False(StockfishStrength.TryParseBestMove("bestmove (none)", p, out _));
        Assert.False(StockfishStrength.TryParseBestMove("info depth 3", p, out _));
    }

    [Fact]
    public void RegistrySpecs()
    {
        Assert.True(PlayerRegistry.TryParse("stockfish:1500", out var e, out var level, out _));
        Assert.Equal(PlayerCategory.Reference, e.Category);
        Assert.Equal(1500, e.KnownRating(level));
        Assert.Equal("Stockfish (Elo 1500)", e.Label(level));
        Assert.True(PlayerRegistry.TryParse("stockfish:1000", out _, out var clamped, out _));
        Assert.Equal(1320, clamped);
        Assert.True(PlayerRegistry.TryParse("stockfish-skill:4", out var skill, out var sl, out _));
        Assert.Null(skill.KnownRating(sl));
        Assert.DoesNotContain(PlayerRegistry.OwnBots, b => b.Category == PlayerCategory.Reference);
    }
}

public class RatingEstimateTests
{
    [Fact]
    public void EvenScoreGivesTheOpponentsRating()
    {
        var e = RatingEstimate.Performance(1500, 10, 0, 10)!;
        Assert.Equal(1500, e.Rating, 0);
        Assert.InRange(e.Margin, 100, 250);
        Assert.False(e.OffTheScale);
    }

    [Fact]
    public void ScoreMapsToRatingDifference()
    {
        // 75% against 1320 ≈ +190 (the prior pulls it in slightly).
        var e = RatingEstimate.Performance(1320, 150, 0, 50)!;
        Assert.InRange(e.Rating, 1495, 1515);
        Assert.True(RatingEstimate.Performance(1320, 1, 2, 17)!.Rating < 1320);
    }

    [Fact]
    public void AllLossesStaysFiniteAndIsFlagged()
    {
        var e = RatingEstimate.Performance(1320, 0, 0, 10)!;
        Assert.True(double.IsFinite(e.Rating) && e.Rating < 1320);
        Assert.True(e.OffTheScale);
        Assert.Null(RatingEstimate.Performance(1320, 0, 0, 0));
    }

    [Fact]
    public void CombinesSeveralLevels()
    {
        // 50% against 1500 and 1700 → somewhere between, with a tighter range than either alone.
        var both = RatingEstimate.Performance([new(1500, 10, 0, 10), new(1700, 10, 0, 10)])!;
        var one = RatingEstimate.Performance(1500, 10, 0, 10)!;
        Assert.InRange(both.Rating, 1550, 1650);
        Assert.True(both.Margin < one.Margin);
        Assert.Equal(40, both.Games);
    }

    [Fact]
    public void SeveralFixedRatingsInTheRoundRobin()
    {
        // Player 2 scores 50% against fixed players at 1300 and 1700 → about 1500.
        var games = new List<(int, int, double)>();
        for (int i = 0; i < 100; i++) { games.Add((2, 0, i % 2)); games.Add((2, 1, i % 2)); }
        var r = Ratings.Compute(3, games, new Dictionary<int, double> { [0] = 1300, [1] = 1700 });
        Assert.Equal(1300, r[0].Rating);
        Assert.Equal(1700, r[1].Rating);
        Assert.InRange(r[2].Rating, 1480, 1520);
    }
}

/// <summary>Runs only if Stockfish is installed on this computer (e.g. sudo apt install stockfish).</summary>
public sealed class StockfishInstalledFactAttribute : FactAttribute
{
    public static readonly string? Path = new[] { Environment.GetEnvironmentVariable("STOCKFISH_PATH"), "/usr/games/stockfish", "/usr/bin/stockfish", "/usr/local/bin/stockfish" }
        .FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));

    public StockfishInstalledFactAttribute()
    {
        if (Path is null) Skip = "Stockfish isn't installed";
    }
}

public class StockfishProcessTests
{
    [StockfishInstalledFact]
    public void PlaysALegalMoveAtALimitedStrength()
    {
        using var p = Process.Start(new ProcessStartInfo(StockfishInstalledFactAttribute.Path!)
        { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false })!;
        var position = new Position();
        var strength = StockfishStrength.AtElo(1320) with { MoveTime = TimeSpan.FromMilliseconds(50) };
        p.StandardInput.WriteLine("uci");
        foreach (var c in strength.SetupCommands()) p.StandardInput.WriteLine(c);
        p.StandardInput.WriteLine("position fen " + position.ToFen());
        p.StandardInput.WriteLine(strength.GoCommand());
        p.StandardInput.Flush();
        string? line;
        Move move = Move.None;
        while ((line = p.StandardOutput.ReadLine()) is not null)
            if (StockfishStrength.TryParseBestMove(line, position, out move)) break;
        p.StandardInput.WriteLine("quit");
        Assert.True(position.IsLegal(move));
    }
}
