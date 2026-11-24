using RlChess.Engine.Players;

namespace RlChess.Engine.Tests;

public class PgnTests
{
    [Fact]
    public void ParsesTagsCommentsVariationsAndNags()
    {
        const string pgn = """
            [Event "Casual"]
            [White "Alice"]
            [Black "Bob \"the rook\""]
            [Result "1-0"]

            1. e4 {best by test} e5 2. Nf3 (2. f4 exf4 (2... d5) 3. Nf3) Nc6 $1 3.Bc4 Nd4?! ; a dubious move
            4. Nxe5 Qg5 5. Nxf7 Qxg2 6. Rf1 Qxe4+ 7. Be2 Nf3# 0-1
            """;
        var g = Pgn.Parse(pgn);
        Assert.Equal("Alice", g.Tags["White"]);
        Assert.Equal("Bob \"the rook\"", g.Tags["Black"]);
        Assert.Equal(14, g.Game.Moves.Count);
        Assert.Equal("Nf3#", g.Game.SanMoves[^1]);
        Assert.Equal(GameResult.Win(Color.Black, GameEndReason.Checkmate), g.Game.Result); // the moves win over the tag
    }

    [Fact]
    public void RoundTripsArenaOutputIncludingCustomStart()
    {
        var games = new List<Game>();
        for (int i = 0; i < 20; i++)
            games.Add(MatchRunner.PlayGame(new RandomPlayer(i), new RandomPlayer(100 + i)));
        games.Add(MatchRunner.PlayGame(new RandomPlayer(7), new RandomPlayer(8), "4k3/8/8/8/8/8/4P3/4K3 b - - 0 40", maxPlies: 30));

        var text = string.Join("\n", games.Select(g => g.ToPgn("A", "B")));
        var parsed = Pgn.ParseAll(text);

        Assert.Equal(games.Count, parsed.Count);
        for (int i = 0; i < games.Count; i++)
        {
            Assert.Equal(games[i].Moves, parsed[i].Game.Moves);
            Assert.Equal(games[i].Result.Outcome, parsed[i].Game.Result.Outcome);
            Assert.Equal(games[i].StartFen, parsed[i].Game.StartFen);
        }
    }

    [Fact]
    public void ResignationFromResultTag()
    {
        var g = Pgn.Parse("[Result \"0-1\"]\n\n1. f3 e5 0-1");
        Assert.Equal(GameResult.Win(Color.Black, GameEndReason.Resignation), g.Game.Result);
    }

    [Fact]
    public void MovesWithoutTagsWork()
    {
        var g = Pgn.Parse("1.e4 e5 2.Nf3 *");
        Assert.Equal(["e4", "e5", "Nf3"], g.Game.SanMoves);
        Assert.False(g.Game.IsOver);
    }

    [Theory]
    [InlineData("1. e4 e4")]
    [InlineData("1. e4 Nf3")]
    [InlineData("1. f3 e5 2. g4 Qh4# 3. a3")]
    public void BadMovesThrow(string pgn) => Assert.Throws<FormatException>(() => Pgn.Parse(pgn));
}
