using RlChess.Engine.Players;

namespace RlChess.Engine.Tests;

public class GameTests
{
    [Fact]
    public void UndoRestoresMovesSanAndResult()
    {
        var g = new Game();
        foreach (var m in "f3 e5 g4 Qh4#".Split(' ')) g.Play(m);
        Assert.True(g.IsOver);
        Assert.True(g.Undo());
        Assert.False(g.IsOver);
        Assert.Equal(3, g.Moves.Count);
        Assert.Equal(["f3", "e5", "g4"], g.SanMoves);
        Assert.Equal(Color.Black, g.SideToMove);
    }

    [Fact]
    public void UndoOnEmptyGameReturnsFalse() => Assert.False(new Game().Undo());

    [Fact]
    public void CannotPlayAfterGameOverOrIllegalMove()
    {
        var g = new Game();
        Assert.Throws<ArgumentException>(() => g.Play("e5"));
        Assert.Throws<ArgumentException>(() => g.Play(new Move(Square.E1, Square.E8)));
        foreach (var m in "f3 e5 g4 Qh4#".Split(' ')) g.Play(m);
        Assert.Throws<InvalidOperationException>(() => g.Play("a3"));
    }

    [Fact]
    public void PositionAtReplaysFromStart()
    {
        var g = new Game();
        foreach (var m in "e4 e5 Nf3".Split(' ')) g.Play(m);
        Assert.Equal(Position.StartFen, g.PositionAt(0).ToFen());
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", g.PositionAt(1).ToFen());
        Assert.Equal(g.Position.ToFen(), g.PositionAt(3).ToFen());
    }

    [Fact]
    public void AcceptsUciAndSan()
    {
        var g = new Game();
        g.Play("e2e4");
        g.Play("e5");
        g.Play("g1f3");
        Assert.Equal(["e4", "e5", "Nf3"], g.SanMoves);
    }

    [Fact]
    public void EndForcesResult()
    {
        var g = new Game();
        g.End(GameResult.Win(Color.Black, GameEndReason.Resignation));
        Assert.True(g.IsOver);
        Assert.Contains("[Result \"0-1\"]", g.ToPgn());
    }

    [Fact]
    public void WithoutSanRecordingGamesAreIdenticalButPgnIsRefused()
    {
        var withSan = MatchRunner.PlayGame(new RandomPlayer(5), new RandomPlayer(6));
        var withoutSan = MatchRunner.PlayGame(new RandomPlayer(5), new RandomPlayer(6), recordSan: false);
        Assert.Equal(withSan.Moves, withoutSan.Moves);
        Assert.Equal(withSan.Result, withoutSan.Result);
        Assert.Empty(withoutSan.SanMoves);
        Assert.Throws<InvalidOperationException>(() => withoutSan.ToPgn());
    }

    [Fact]
    public void MoveLimitIsApplied()
    {
        var g = MatchRunner.PlayGame(new RandomPlayer(1), new RandomPlayer(2), maxPlies: 10);
        Assert.Equal(10, g.Moves.Count);
        Assert.Equal(GameEndReason.MoveLimit, g.Result.Reason);
    }

    [Fact]
    public void PlayerGetsACopyNotTheRealPosition()
    {
        var spy = new MeddlingPlayer();
        var g = MatchRunner.PlayGame(spy, new RandomPlayer(3), maxPlies: 6);
        Assert.Equal(6, g.Moves.Count); // the meddling didn't break the game
    }

    private sealed class MeddlingPlayer : IPlayer
    {
        public string Name => "Meddler";
        public Move ChooseMove(Position position)
        {
            var moves = position.GetLegalMoves();
            position.MakeMove(moves[0]); // scribble on the position we were given
            return moves[0];
        }
    }
}
