namespace RlChess.Engine.Tests;

/// <summary>
/// Perft results from https://www.chessprogramming.org/Perft_Results.
/// The fast run covers every position to a few hundred thousand / few million nodes.
/// Deeper runs are tagged "Slow": run them with  dotnet test --filter Category=Slow
/// </summary>
public class PerftTests
{
    public const string Start = Position.StartFen;
    public const string Kiwipete = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";
    public const string Pos3 = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1";
    public const string Pos4 = "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1";
    public const string Pos4Mirrored = "r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1";
    public const string Pos5 = "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8";
    public const string Pos6 = "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10";

    [Theory]
    [InlineData(Start, 1, 20L)]
    [InlineData(Start, 2, 400L)]
    [InlineData(Start, 3, 8902L)]
    [InlineData(Start, 4, 197281L)]
    [InlineData(Kiwipete, 1, 48L)]
    [InlineData(Kiwipete, 2, 2039L)]
    [InlineData(Kiwipete, 3, 97862L)]
    [InlineData(Pos3, 1, 14L)]
    [InlineData(Pos3, 2, 191L)]
    [InlineData(Pos3, 3, 2812L)]
    [InlineData(Pos3, 4, 43238L)]
    [InlineData(Pos3, 5, 674624L)]
    [InlineData(Pos4, 1, 6L)]
    [InlineData(Pos4, 2, 264L)]
    [InlineData(Pos4, 3, 9467L)]
    [InlineData(Pos4, 4, 422333L)]
    [InlineData(Pos4Mirrored, 1, 6L)]
    [InlineData(Pos4Mirrored, 2, 264L)]
    [InlineData(Pos4Mirrored, 3, 9467L)]
    [InlineData(Pos4Mirrored, 4, 422333L)]
    [InlineData(Pos5, 1, 44L)]
    [InlineData(Pos5, 2, 1486L)]
    [InlineData(Pos5, 3, 62379L)]
    [InlineData(Pos6, 1, 46L)]
    [InlineData(Pos6, 2, 2079L)]
    [InlineData(Pos6, 3, 89890L)]
    public void Fast(string fen, int depth, long expected) => Check(fen, depth, expected);

    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(Start, 5, 4865609L)]
    [InlineData(Start, 6, 119060324L)]
    [InlineData(Kiwipete, 4, 4085603L)]
    [InlineData(Kiwipete, 5, 193690690L)]
    [InlineData(Pos3, 6, 11030083L)]
    [InlineData(Pos4, 5, 15833292L)]
    [InlineData(Pos5, 4, 2103487L)]
    [InlineData(Pos5, 5, 89941194L)]
    [InlineData(Pos6, 4, 3894594L)]
    [InlineData(Pos6, 5, 164075551L)]
    public void Slow(string fen, int depth, long expected) => Check(fen, depth, expected);

    private static void Check(string fen, int depth, long expected)
    {
        var pos = Position.FromFen(fen);
        var fenBefore = pos.ToFen();
        var hashBefore = pos.Hash;
        Assert.Equal(expected, Perft.Count(pos, depth));
        // Make/undo must leave the position exactly as it was.
        Assert.Equal(fenBefore, pos.ToFen());
        Assert.Equal(hashBefore, pos.Hash);
    }
}
