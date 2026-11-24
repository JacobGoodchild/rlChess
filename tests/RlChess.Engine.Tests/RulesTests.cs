using RlChess.Engine.Players;

namespace RlChess.Engine.Tests;

public class RulesTests
{
    private static bool HasMove(Position p, string uci) => Notation.TryParseUci(p, uci, out _);

    [Fact]
    public void StartPositionRoundTripsFen()
    {
        var p = new Position();
        Assert.Equal(Position.StartFen, p.ToFen());
        Assert.Equal(20, p.GetLegalMoves().Count);
    }

    [Theory]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")]
    [InlineData("8/8/8/8/8/8/8/K6k b - - 37 90")]
    [InlineData("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3")]
    public void FenRoundTrips(string fen) => Assert.Equal(fen, Position.FromFen(fen).ToFen());

    [Theory]
    [InlineData("")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/9/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    [InlineData("8/8/8/8/8/8/8/8 w - - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x KQkq - 0 1")]
    [InlineData("P3k3/8/8/8/8/8/8/4K3 w - - 0 1")]            // pawn on the last rank
    [InlineData("4k3/8/8/8/8/8/8/4K2p b - - 0 1")]            // pawn on the first rank
    [InlineData("4k3/8/8/8/8/8/8/4K2r b - - 0 1")]            // white (not to move) is in check
    [InlineData("4k3/8/8/8/8/8/8/K1K5 w - - 0 1")]            // two white kings
    public void BadFenThrows(string fen) => Assert.Throws<FormatException>(() => Position.FromFen(fen));

    [Theory]
    [InlineData("4k3/8/8/3pP3/8/8/8/4K3 w - d3 0 1")]   // wrong rank for white to move
    [InlineData("4k3/8/8/4P3/8/8/8/4K3 w - d6 0 1")]    // no black pawn that could have just moved
    [InlineData("4k3/3n4/8/3pP3/8/8/8/4K3 w - d6 0 1")] // the pawn's start square is occupied
    public void ImplausibleEnPassantSquareIsIgnored(string fen)
    {
        var p = Position.FromFen(fen);
        Assert.Equal(Square.None, p.EnPassantSquare);
        Assert.DoesNotContain(p.GetLegalMoves(), m => m.IsEnPassant);
    }

    [Fact]
    public void AsciiBoard()
    {
        var text = new Position().ToAscii();
        Assert.StartsWith("8  r n b q k b n r", text);
        Assert.Contains("1  R N B Q K B N R", text);
        Assert.EndsWith("White to move", text);
    }

    [Fact]
    public void EnPassantSquareOnlyKeptWhenCapturePossible()
    {
        var p = new Position();
        p.MakeMove(Notation.ParseUci(p, "e2e4"));
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", p.ToFen());
    }

    [Fact]
    public void EnPassantCapture()
    {
        var p = Position.FromFen("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3");
        var m = Notation.ParseUci(p, "e5f6");
        Assert.True(m.IsEnPassant);
        p.MakeMove(m);
        Assert.Equal(Piece.None, p[Square.Parse("f5")]);
        Assert.Equal(Piece.WhitePawn, p[Square.Parse("f6")]);
        Assert.False(HasMove(Position.FromFen("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq - 0 3"), "e5f6"));
    }

    [Fact]
    public void PinnedEnPassantIsIllegal()
    {
        // Capturing en passant would take both pawns off the 5th rank and expose the king to the rook.
        var p = Position.FromFen("8/8/8/KPp4r/8/8/8/7k w - c6 0 1");
        Assert.Equal(Square.Parse("c6"), p.EnPassantSquare);
        Assert.False(HasMove(p, "b5c6"));
        Assert.True(HasMove(p, "b5b6"));
    }

    [Fact]
    public void CastlingBothSides()
    {
        var p = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        Assert.True(HasMove(p, "e1g1"));
        Assert.True(HasMove(p, "e1c1"));
        p.MakeMove(Notation.ParseUci(p, "e1g1"));
        Assert.Equal(Piece.WhiteKing, p[Square.G1]);
        Assert.Equal(Piece.WhiteRook, p[Square.F1]);
        Assert.Equal(Piece.None, p[Square.H1]);
        Assert.Equal("r3k2r/8/8/8/8/8/8/R4RK1 b kq - 1 1", p.ToFen());
        p.MakeMove(Notation.ParseUci(p, "e8c8"));
        Assert.Equal("2kr3r/8/8/8/8/8/8/R4RK1 w - - 2 2", p.ToFen());
    }

    [Fact]
    public void CannotCastleOutOfThroughOrIntoCheck()
    {
        // Out of check: rook on e8 attacks the king.
        Assert.False(HasMove(Position.FromFen("4r1k1/8/8/8/8/8/8/R3K2R w KQ - 0 1"), "e1g1"));
        // Through check: f1 attacked.
        var through = Position.FromFen("5rk1/8/8/8/8/8/8/R3K2R w KQ - 0 1");
        Assert.False(HasMove(through, "e1g1"));
        Assert.True(HasMove(through, "e1c1"));
        // Into check: g1 attacked.
        Assert.False(HasMove(Position.FromFen("6r1/1k6/8/8/8/8/8/R3K2R w KQ - 0 1"), "e1g1"));
        // Queenside: b1 attacked is fine (king doesn't cross it), d1 attacked is not.
        Assert.True(HasMove(Position.FromFen("1r4k1/8/8/8/8/8/8/R3K3 w Q - 0 1"), "e1c1"));
        Assert.False(HasMove(Position.FromFen("3r2k1/8/8/8/8/8/8/R3K3 w Q - 0 1"), "e1c1"));
        // Blocked by a piece.
        Assert.False(HasMove(Position.FromFen("6k1/8/8/8/8/8/8/RN2K3 w Q - 0 1"), "e1c1"));
    }

    [Fact]
    public void CastlingRightsLostWhenKingOrRookMovesOrRookCaptured()
    {
        var p = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        p.MakeMove(Notation.ParseUci(p, "h1h2"));
        Assert.Equal(CastlingRights.WhiteQueenside | CastlingRights.BlackKingside | CastlingRights.BlackQueenside, p.Castling);
        p.MakeMove(Notation.ParseUci(p, "e8e7"));
        Assert.Equal(CastlingRights.WhiteQueenside, p.Castling);

        var q = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        q.MakeMove(Notation.ParseUci(q, "a1a8")); // rook takes rook
        Assert.Equal(CastlingRights.WhiteKingside | CastlingRights.BlackKingside, q.Castling);
    }

    [Theory]
    [InlineData("q")]
    [InlineData("r")]
    [InlineData("b")]
    [InlineData("n")]
    public void PromotionToAnyPiece(string piece)
    {
        var p = Position.FromFen("8/P6k/8/8/8/8/8/K7 w - - 0 1");
        Assert.Equal(4, p.GetLegalMoves().Count(m => m.From == Square.Parse("a7")));
        p.MakeMove(Notation.ParseUci(p, "a7a8" + piece));
        Assert.Equal(PieceExtensions.PieceTypeFromLetter(piece[0]).Of(Color.White), p[Square.A8]);
        p.UndoMove();
        Assert.Equal(Piece.WhitePawn, p[Square.Parse("a7")]);
    }

    [Fact]
    public void FoolsMateIsCheckmate()
    {
        var g = new Game();
        foreach (var m in "f3 e5 g4 Qh4#".Split(' ')) g.Play(m);
        Assert.Equal(GameResult.Win(Color.Black, GameEndReason.Checkmate), g.Result);
        Assert.Equal("Qh4#", g.SanMoves[^1]);
        Assert.True(g.Position.IsCheckmate);
    }

    [Fact]
    public void Stalemate()
    {
        var p = Position.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");
        Assert.True(p.IsStalemate);
        Assert.Equal(GameResult.Draw(GameEndReason.Stalemate), p.GetResult());
    }

    [Fact]
    public void FiftyMoveRule()
    {
        var p = Position.FromFen("4k3/8/8/8/8/8/8/R3K3 w - - 99 80");
        Assert.False(p.GetResult().IsOver);
        p.MakeMove(Notation.ParseUci(p, "a1a2"));
        Assert.Equal(GameResult.Draw(GameEndReason.FiftyMoveRule), p.GetResult());
    }

    [Fact]
    public void CheckmateOnTheHundredthHalfMoveBeatsFiftyMoveRule()
    {
        var p = Position.FromFen("7k/8/6K1/8/8/8/8/R7 w - - 99 80");
        p.MakeMove(Notation.ParseUci(p, "a1a8"));
        Assert.Equal(GameEndReason.Checkmate, p.GetResult().Reason);
    }

    [Fact]
    public void ThreefoldRepetition()
    {
        var g = new Game();
        var shuffle = "Nf3 Nf6 Ng1 Ng8 Nf3 Nf6 Ng1".Split(' ');
        foreach (var m in shuffle)
        {
            g.Play(m);
            Assert.False(g.IsOver);
        }
        g.Play("Ng8"); // start position for the third time
        Assert.Equal(GameResult.Draw(GameEndReason.ThreefoldRepetition), g.Result);
    }

    [Fact]
    public void RepetitionRespectsCastlingRights()
    {
        // Moving the rook out and back loses castling rights, so the first position is different.
        var g = new Game("4k3/8/8/8/8/8/8/4K2R w K - 0 1");
        foreach (var m in "Rh2 Kd8 Rh1 Ke8 Rh2 Kd8 Rh1 Ke8".Split(' ')) g.Play(m);
        Assert.False(g.IsOver); // positions with rights-lost: only twice so far
        g.Play("Rh2"); // same position as after White's 1st and 3rd moves: third time
        Assert.Equal(GameEndReason.ThreefoldRepetition, g.Result.Reason);
    }

    [Theory]
    [InlineData("8/8/8/4k3/8/8/8/4K3 w - - 0 1", true)]        // K v K
    [InlineData("8/8/8/4k3/8/8/8/4KN2 w - - 0 1", true)]       // K+N v K
    [InlineData("8/8/8/4k3/8/8/8/4KB2 w - - 0 1", true)]       // K+B v K
    [InlineData("8/8/8/4k1b1/8/8/8/4KB2 w - - 0 1", false)]    // bishops on different colours (g5 dark, f1 light)
    [InlineData("8/8/8/4kb2/8/8/8/4KB2 w - - 0 1", true)]      // bishops on same colour (f5, f1 both light)
    [InlineData("8/8/8/4kn2/8/8/8/4KN2 w - - 0 1", false)]     // K+N v K+N (mate possible)
    [InlineData("8/8/8/4k3/8/8/8/3NKN2 w - - 0 1", false)]     // K+NN v K
    [InlineData("8/8/8/4k3/8/8/4P3/4K3 w - - 0 1", false)]     // pawn
    [InlineData("8/8/8/4k3/8/8/8/4K2R w - - 0 1", false)]      // rook
    public void InsufficientMaterial(string fen, bool expected) =>
        Assert.Equal(expected, Position.FromFen(fen).IsInsufficientMaterial);

    [Fact]
    public void UndoRestoresEverything()
    {
        var p = Position.FromFen(PerftTests.Kiwipete);
        var fen = p.ToFen();
        var hash = p.Hash;
        foreach (var m in p.GetLegalMoves())
        {
            p.MakeMove(m);
            p.UndoMove();
            Assert.Equal(fen, p.ToFen());
            Assert.Equal(hash, p.Hash);
        }
    }

    [Fact]
    public void IncrementalHashMatchesFreshHash()
    {
        var player = new RandomPlayer(seed: 1);
        for (int game = 0; game < 20; game++)
        {
            var p = new Position();
            for (int ply = 0; ply < 200 && p.HasLegalMoves(); ply++)
            {
                p.MakeMove(player.ChooseMove(p));
                Assert.Equal(Position.FromFen(p.ToFen()).Hash, p.Hash);
            }
        }
    }

    [Fact]
    public void RandomGamesAlwaysFinishWithAResult()
    {
        for (int i = 0; i < 50; i++)
        {
            var g = MatchRunner.PlayGame(new RandomPlayer(i), new RandomPlayer(i + 1000));
            Assert.True(g.IsOver);
            Assert.NotEqual(GameEndReason.MoveLimit, g.Result.Reason);
        }
    }

    [Fact]
    public void SeededRandomPlayerIsReproducible()
    {
        var a = MatchRunner.PlayGame(new RandomPlayer(42), new RandomPlayer(43));
        var b = MatchRunner.PlayGame(new RandomPlayer(42), new RandomPlayer(43));
        Assert.Equal(a.Moves, b.Moves);
    }
}
