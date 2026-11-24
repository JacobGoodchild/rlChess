using RlChess.Engine;
using RlChess.Engine.Players;
using RlChess.Players;
using Xunit;

namespace RlChess.Engine.Tests;

public class WinChanceTests
{
    [Fact]
    public void WinPercentFollowsLichessFormula()
    {
        Assert.Equal(50, WinChances.WinPercent(0), 6);
        Assert.Equal(59.1, WinChances.WinPercent(100), 1);   // a pawn up ≈ 59%
        Assert.Equal(40.9, WinChances.WinPercent(-100), 1);
        Assert.True(WinChances.WinPercent(5000) > 97);     // capped at ±1000 centipawns
        Assert.Equal(100, EvalScore.MateIn(3).WinPercent);
        Assert.Equal(0, EvalScore.MateIn(-2).WinPercent);
        Assert.Equal(0, EvalScore.MateIn(0).WinPercent);   // already checkmated
    }

    [Fact]
    public void MoveAccuracyDropsWithLostChances()
    {
        Assert.Equal(100, WinChances.MoveAccuracy(60, 60), 0);
        Assert.Equal(100, WinChances.MoveAccuracy(60, 70), 0); // improving doesn't go above 100
        Assert.InRange(WinChances.MoveAccuracy(60, 50), 60, 70);
        Assert.InRange(WinChances.MoveAccuracy(90, 10), 0, 3);
    }

    [Fact]
    public void ScoreTextAndPerspective()
    {
        Assert.Equal("+0.35", EvalScore.FromCentipawns(35).ToString());
        Assert.Equal("-1.20", EvalScore.FromCentipawns(-120).ToString());
        Assert.Equal("M3", EvalScore.MateIn(3).ToString());
        Assert.Equal("-M2", EvalScore.MateIn(-2).ToString());
        Assert.Equal("-0.35", EvalScore.FromCentipawns(35).ForWhite(Color.Black).ToString());
        Assert.Equal("-M3", EvalScore.MateIn(3).Negate().ToString());
    }
}

public class MoveReviewTests
{
    private static Move M(Position p, string san) =>
        Notation.TryParseSan(p, san, out var m) ? m : throw new ArgumentException(san);

    private static PositionEval Cp(int cp, Move? best = null, int? second = null) =>
        new(EvalScore.FromCentipawns(cp), best, second is int s ? EvalScore.FromCentipawns(s) : null);

    [Fact]
    public void BestExcellentGoodAndTheBadOnes()
    {
        var p = new Position();
        var e4 = M(p, "e4");
        Assert.Equal(MoveLabel.Best, MoveReview.Classify(p, e4, Cp(30, e4), Cp(-30)).Label);
        var d4 = M(p, "d4");
        // Score from the mover's side: before +30, after the opponent is at -10 (mover +10): tiny loss.
        Assert.Equal(MoveLabel.Excellent, MoveReview.Classify(p, d4, Cp(30, e4), Cp(-10)).Label);
        Assert.Equal(MoveLabel.Good, MoveReview.Classify(p, d4, Cp(30, e4), Cp(10)).Label);
        Assert.Equal(MoveLabel.Inaccuracy, MoveReview.Classify(p, d4, Cp(30, e4), Cp(70)).Label);
        Assert.Equal(MoveLabel.Mistake, MoveReview.Classify(p, d4, Cp(30, e4), Cp(130)).Label);
        Assert.Equal(MoveLabel.Blunder, MoveReview.Classify(p, d4, Cp(30, e4), Cp(400)).Label);
    }

    [Fact]
    public void BookAndForcedMoves()
    {
        var p = new Position();
        var e4 = M(p, "e4");
        Assert.Equal(MoveLabel.Book, MoveReview.Classify(p, e4, Cp(30), Cp(400), isBook: true).Label);
        // Only one legal move (king in check with one escape): always "Best", never praised or blamed.
        var forced = Position.FromFen("7k/8/8/8/8/8/1q6/K7 w - - 0 1");
        var only = forced.GetLegalMoves().Single();
        Assert.Equal(MoveLabel.Best, MoveReview.Classify(forced, only, Cp(-900, only), Cp(900)).Label);
    }

    [Fact]
    public void GreatIsTheOnlyGoodMove()
    {
        var p = new Position();
        var e4 = M(p, "e4");
        Assert.Equal(MoveLabel.Great, MoveReview.Classify(p, e4, Cp(50, e4, second: -300), Cp(-50)).Label);
        Assert.Equal(MoveLabel.Best, MoveReview.Classify(p, e4, Cp(50, e4, second: 20), Cp(-50)).Label);
    }

    [Fact]
    public void BrilliantIsASoundSacrifice()
    {
        // The classic bishop sacrifice on h7: the bishop can simply be taken by the king.
        var p = Position.FromFen("r1bq1rk1/pppn1ppp/4p3/3pP3/3P4/3B1N2/PPP2PPP/R2QK2R w KQ - 0 1");
        var bxh7 = M(p, "Bxh7+");
        Assert.True(MoveReview.IsSacrifice(p, bxh7));
        Assert.Equal(MoveLabel.Brilliant, MoveReview.Classify(p, bxh7, Cp(150, bxh7), Cp(-160)).Label);
        // The same sacrifice when it doesn't work is just a mistake or worse.
        Assert.True(MoveReview.Classify(p, bxh7, Cp(150, bxh7), Cp(250)).Label is MoveLabel.Mistake or MoveLabel.Blunder);
        // Grabbing a pawn while a piece was already attacked isn't a sacrifice (from a real game).
        var g = Pgn.Parse("1. e4 e5 2. Nf3 Nc6 3. Bc4 Nd4 4. Nxe5 Qg5 5. Nxf7").Game;
        Assert.False(MoveReview.IsSacrifice(g.Position, M(g.Position, "Qxg2")));
        // Ordinary developing moves aren't sacrifices; a defended trade isn't either.
        Assert.False(MoveReview.IsSacrifice(new Position(), M(new Position(), "Nf3")));
    }

    [Fact]
    public void MissIsNotPunishingTheOpponentsError()
    {
        var p = new Position();
        var a3 = M(p, "a3");
        // Opponent just blundered: we stood at +300 (≈ 75%), but let it slip to +30.
        var r = MoveReview.Classify(p, a3, Cp(300, M(p, "e4")), Cp(-30), opponentLastLabel: MoveLabel.Blunder);
        Assert.Equal(MoveLabel.Miss, r.Label);
        // Without the opponent's error it's simply a blunder/mistake.
        Assert.NotEqual(MoveLabel.Miss, MoveReview.Classify(p, a3, Cp(300, M(p, "e4")), Cp(-30)).Label);
    }

    [Fact]
    public void CheckmateCountsAsAWin()
    {
        var p = Position.FromFen("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1");
        var mate = M(p, "Ra8#");
        var r = MoveReview.Classify(p, mate, new PositionEval(EvalScore.MateIn(1), mate), null);
        Assert.Equal(100, r.WinAfter);
        Assert.Equal(MoveLabel.Best, r.Label);
    }

    [Fact]
    public void AccuracyIsHighForPerfectPlayAndLowForBlunders()
    {
        var good = new List<ReviewedMove>();
        var bad = new List<ReviewedMove>();
        var wins = new List<double> { 50 };
        for (int i = 0; i < 20; i++)
        {
            var c = i % 2 == 0 ? Color.White : Color.Black;
            good.Add(new ReviewedMove(Move.None, c, MoveLabel.Best, 50, 50, 100, false));
            bad.Add(new ReviewedMove(Move.None, c, MoveLabel.Blunder, 60, 30, WinChances.MoveAccuracy(60, 30), false));
            wins.Add(50);
        }
        Assert.Equal(100, MoveReview.GameAccuracy(wins, good, Color.White), 1);
        Assert.True(MoveReview.GameAccuracy(wins, bad, Color.Black) < 40);
    }
}

public class RatingsTests
{
    [Fact]
    public void AnchorIsFixedAndStrongerPlayerRatesHigher()
    {
        // 0 beats 1 75% of the time; 1 and 2 are even.
        var games = new List<(int, int, double)>();
        for (int i = 0; i < 400; i++) games.Add((0, 1, i % 4 == 0 ? 0 : 1));
        for (int i = 0; i < 400; i++) games.Add((1, 2, i % 2));
        var r = Ratings.Compute(3, games, anchor: 1, anchorRating: 400);
        Assert.Equal(400, r[1].Rating, 3);
        Assert.InRange(r[0].Rating - r[1].Rating, 170, 210); // 75% ≈ +191
        Assert.InRange(r[2].Rating, 380, 420);
        Assert.Equal(0, r[1].Margin);
        Assert.True(r[0].Margin > 0 && r[0].Margin < 60);
    }

    [Fact]
    public void AllWinsStaysFinite()
    {
        var games = Enumerable.Range(0, 20).Select(_ => (0, 1, 1.0));
        var r = Ratings.Compute(2, games, anchor: 1);
        Assert.True(double.IsFinite(r[0].Rating) && r[0].Rating > 800);
    }

    [Fact]
    public void RatingsFileRoundTrip()
    {
        var f = new RatingsFile { GamesPerPairing = 10, Players = [new("random", "Random", 400, 0, 10, 0.5), new("x:3", "X (level 3)", 812.4, 55, 10, 0.9)] };
        var back = RatingsFile.Parse(f.ToJson());
        Assert.Equal(2, back.Players.Count);
        Assert.Equal(812, back.Players[1].Rating);
        Assert.Equal(10, back.GamesPerPairing);
        Assert.Empty(RatingsFile.Parse("not json").Players);
        Assert.NotNull(back.Find(PlayerRegistry.Find("random")!, null));
    }
}

public class OpeningBookTests
{
    private const string Data = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq -\tB00\tKing's Pawn Game\n" +
                                "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq -\tC20\tKing's Pawn Game: Open\n";

    [Fact]
    public void NamesAndBookMoves()
    {
        var book = OpeningBook.Parse(Data);
        var g = new Game();
        foreach (var m in new[] { "e4", "e5", "Nf3" }) g.Play(m);
        Assert.Equal(2, book.BookPlies(g));
        Assert.Equal("King's Pawn Game: Open", book.NameAt(g, 3)!.Name); // stays named after leaving the book
        Assert.Equal("King's Pawn Game", book.NameAt(g, 1)!.Name);
        Assert.Null(book.NameAt(g, 0));
    }
}

public class ThinkingTests
{
    private sealed class Reporter : IPlayer, IReportsThinking
    {
        public string Name => "Reporter";
        public Action<SearchInfo>? Report { get; set; }
        public Move ChooseMove(Position position)
        {
            var best = position.GetLegalMoves()[0];
            Report?.Invoke(new SearchInfo { Depth = 1, PositionsSearched = 20 });
            Report?.Invoke(new SearchInfo { Depth = 2, PositionsSearched = 400, BestMove = best, Score = EvalScore.FromCentipawns(25), Line = [best] });
            return best;
        }
    }

    [Fact]
    public void MatchRunnerPassesOnTheLastReport()
    {
        var reports = new List<MoveReport>();
        MatchRunner.PlayGame(new Reporter(), new RandomPlayer(1), maxPlies: 6, onMove: reports.Add);
        Assert.Equal(6, reports.Count);
        Assert.Equal(2, reports[0].Thinking!.Depth);
        Assert.Null(reports[1].Thinking); // the random player doesn't report
        var text = ThinkingText.Format(reports[0].Thinking!, new Position());
        Assert.Contains("depth 2", text);
        Assert.Contains("400 positions", text);
        Assert.Contains("+0.25", text);
    }

    [Fact]
    public async Task PlayerAnalysisUsesTheReportedScore()
    {
        var eval = await new PlayerAnalysis(new Reporter()).AnalyseAsync(new Position(), new AnalysisLimit(), null, default);
        Assert.Equal(25, eval.Score.Centipawns);
        Assert.NotNull(eval.BestMove);
        Assert.False(PlayerAnalysis.CanAnalyse(new RandomPlayer()));
    }

    [Fact]
    public void CountsAreShort() => Assert.Equal(["999", "1.2k", "45k", "4.5M"],
        new long[] { 999, 1234, 45_000, 4_500_000 }.Select(ThinkingText.Count));
}

public class PlayerAnalysisTests
{
    [Fact]
    public async Task FinishedPositionsDontAskThePlayer()
    {
        var mate = Position.FromFen("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3");
        var eval = await new PlayerAnalysis(new ThrowingReporter()).AnalyseAsync(mate, new AnalysisLimit(), null, default);
        Assert.Equal(0, eval.Score.WinPercent);
    }

    private sealed class ThrowingReporter : IPlayer, IReportsThinking
    {
        public string Name => "Throws";
        public Action<SearchInfo>? Report { get; set; }
        public Move ChooseMove(Position position) => throw new InvalidOperationException("should not be asked");
    }
}
