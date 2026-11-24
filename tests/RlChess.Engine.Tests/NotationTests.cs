namespace RlChess.Engine.Tests;

public class NotationTests
{
    private static string San(string fen, string uci)
    {
        var p = Position.FromFen(fen);
        return Notation.ToSan(p, Notation.ParseUci(p, uci));
    }

    [Theory]
    [InlineData(Position.StartFen, "g1f3", "Nf3")]
    [InlineData(Position.StartFen, "e2e4", "e4")]
    [InlineData("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2", "e4d5", "exd5")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1g1", "O-O")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1c1", "O-O-O")]
    [InlineData("8/4P3/8/8/8/8/k7/4K3 w - - 0 1", "e7e8q", "e8=Q")]
    [InlineData("8/4P3/k7/8/8/8/8/4K3 w - - 0 1", "e7e8n", "e8=N")]
    [InlineData("1k6/4P3/8/8/8/8/8/4K3 w - - 0 1", "e7e8q", "e8=Q+")]
    [InlineData("rnbqkbnr/pppp1ppp/8/4p3/6P1/5P2/PPPPP2P/RNBQKBNR b KQkq - 0 2", "d8h4", "Qh4#")]
    [InlineData("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3", "e5f6", "exf6")]
    // Disambiguation by file, by rank, and by both.
    [InlineData("4k3/8/8/8/8/8/8/1N2KN2 w - - 0 1", "b1d2", "Nbd2")]
    [InlineData("4k3/8/8/R7/8/8/8/R3K3 w - - 0 1", "a1a3", "R1a3")]
    [InlineData("4k3/8/8/8/8/Q1Q5/8/Q3K3 w - - 0 1", "a3b2", "Qa3b2")]
    // Capture + promotion + check / mate, and castling that gives check.
    [InlineData("3r3k/4P3/8/8/8/8/8/4K3 w - - 0 1", "e7d8q", "exd8=Q+")]
    [InlineData("3r2k1/4Pppp/8/8/8/8/8/4K3 w - - 0 1", "e7d8q", "exd8=Q#")]
    [InlineData("5k2/8/8/8/8/8/8/4K2R w K - 0 1", "e1g1", "O-O+")]
    // Two knights on the same file: disambiguate by rank.
    [InlineData("4k3/8/8/8/3N4/8/3N4/4K3 w - - 0 1", "d2f3", "N2f3")]
    // A pinned piece doesn't count as a second candidate.
    [InlineData("4k3/4r3/8/8/8/8/4N3/1N2K3 w - - 0 1", "b1c3", "Nc3")]
    public void ToSan(string fen, string uci, string expected) => Assert.Equal(expected, San(fen, uci));

    [Theory]
    [InlineData(Position.StartFen, "Nf3", "g1f3")]
    [InlineData(Position.StartFen, "e4", "e2e4")]
    [InlineData(Position.StartFen, "Ng1f3", "g1f3")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "O-O", "e1g1")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "0-0-0", "e1c1")]
    [InlineData("8/4P3/8/8/8/8/k7/4K3 w - - 0 1", "e8=Q", "e7e8q")]
    [InlineData("8/4P3/8/8/8/8/k7/4K3 w - - 0 1", "e8N", "e7e8n")]
    [InlineData("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2", "exd5!?", "e4d5")]
    [InlineData("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2", "ed5", "e4d5")]
    public void ParseSan(string fen, string san, string expectedUci)
    {
        var p = Position.FromFen(fen);
        Assert.Equal(expectedUci, Notation.ParseSan(p, san).ToUci());
    }

    [Theory]
    [InlineData("Nf4")]
    [InlineData("e5")]
    [InlineData("O-O")]
    [InlineData("hello")]
    public void ParseSanRejectsIllegal(string san) =>
        Assert.False(Notation.TryParseSan(new Position(), san, out _));

    [Fact]
    public void AmbiguousSanIsRejected() =>
        Assert.False(Notation.TryParseSan(Position.FromFen("4k3/8/8/8/8/8/8/1N2KN2 w - - 0 1"), "Nd2", out _));

    [Theory]
    [InlineData("e2e5")]
    [InlineData("e7e8")]
    [InlineData("zz")]
    [InlineData("e2e4x")]
    public void ParseUciRejectsIllegal(string uci) =>
        Assert.False(Notation.TryParseUci(new Position(), uci, out _));

    [Fact]
    public void SanOfEveryMoveParsesBack()
    {
        foreach (var fen in new[] { PerftTests.Kiwipete, PerftTests.Pos3, PerftTests.Pos4, PerftTests.Pos5, PerftTests.Pos6 })
        {
            var p = Position.FromFen(fen);
            foreach (var m in p.GetLegalMoves())
                Assert.Equal(m, Notation.ParseSan(p, Notation.ToSan(p, m)));
        }
    }

    [Fact]
    public void PgnExport()
    {
        var g = new Game();
        foreach (var m in "e4 e5 Qh5 Nc6 Bc4 Nf6 Qxf7#".Split(' ')) g.Play(m);
        var pgn = g.ToPgn("Alice", "Bob", date: new DateTime(2026, 1, 2));
        Assert.Contains("[White \"Alice\"]", pgn);
        Assert.Contains("[Result \"1-0\"]", pgn);
        Assert.Contains("[Date \"2026.01.02\"]", pgn);
        Assert.Contains("1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 4. Qxf7# 1-0", pgn);
    }

    [Fact]
    public void PgnFromCustomStartIncludesFen()
    {
        var g = new Game("4k3/8/8/8/8/8/8/R3K3 b Q - 0 1");
        g.Play("Kd7");
        var pgn = g.ToPgn();
        Assert.Contains("[SetUp \"1\"]", pgn);
        Assert.Contains("1... Kd7 *", pgn);
    }
}
