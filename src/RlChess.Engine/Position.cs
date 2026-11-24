using System.Text;

namespace RlChess.Engine;

[Flags]
public enum CastlingRights : byte
{
    None = 0,
    WhiteKingside = 1,
    WhiteQueenside = 2,
    BlackKingside = 4,
    BlackQueenside = 8,
    All = 15,
}

/// <summary>
/// A chess position: where the pieces are, whose turn it is, castling rights, en passant,
/// and the move clocks. It is mutable: <see cref="MakeMove"/> plays a move and
/// <see cref="UndoMove"/> takes it back. It also remembers the moves made on it, so it can
/// detect threefold repetition.
///
/// Use <see cref="Clone"/> to get an independent copy (e.g. before handing it to a player).
/// </summary>
public sealed class Position
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    /// <summary>More than enough room for the moves in any legal chess position (max is 218).</summary>
    public const int MaxMoves = 256;

    private readonly Piece[] _board = new Piece[64];
    private readonly int[] _kingSquare = new int[2];
    private UndoInfo[] _history = new UndoInfo[256];
    private int _historyCount;

    private struct UndoInfo
    {
        public Move Move;
        public Piece Captured;
        public CastlingRights Castling;
        public int EnPassant;
        public int HalfmoveClock;
        public ulong Hash;
    }

    public Color SideToMove { get; private set; }
    public CastlingRights Castling { get; private set; }

    /// <summary>The square a pawn can capture onto en passant, or <see cref="Square.None"/>.</summary>
    public int EnPassantSquare { get; private set; } = Square.None;

    /// <summary>Half-moves since the last capture or pawn move (for the fifty-move rule).</summary>
    public int HalfmoveClock { get; private set; }

    public int FullmoveNumber { get; private set; } = 1;

    /// <summary>A 64-bit fingerprint of the position (Zobrist hash). Equal positions have equal hashes.</summary>
    public ulong Hash { get; private set; }

    /// <summary>Number of moves made on this object that can be undone.</summary>
    public int PlyCount => _historyCount;

    /// <summary>The most recent move made on this object, or <see cref="Move.None"/>.</summary>
    public Move LastMove => _historyCount > 0 ? _history[_historyCount - 1].Move : Move.None;

    public Piece this[int square] => _board[square];

    public int KingSquare(Color c) => _kingSquare[(int)c];

    /// <summary>Creates the standard starting position.</summary>
    public Position() : this(StartFen) { }

    public Position(string fen) => LoadFen(fen);

    private Position(Position other)
    {
        Array.Copy(other._board, _board, 64);
        Array.Copy(other._kingSquare, _kingSquare, 2);
        _history = (UndoInfo[])other._history.Clone();
        _historyCount = other._historyCount;
        SideToMove = other.SideToMove;
        Castling = other.Castling;
        EnPassantSquare = other.EnPassantSquare;
        HalfmoveClock = other.HalfmoveClock;
        FullmoveNumber = other.FullmoveNumber;
        Hash = other.Hash;
    }

    /// <summary>An independent copy, including move history (so repetition detection still works).</summary>
    public Position Clone() => new(this);

    public static Position FromFen(string fen) => new(fen);

    // ------------------------------------------------------------------ FEN

    private void LoadFen(string fen)
    {
        var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) throw new FormatException("FEN needs at least 4 fields.");

        Array.Clear(_board);
        _kingSquare[0] = _kingSquare[1] = Square.None;
        _historyCount = 0;

        var rows = parts[0].Split('/');
        if (rows.Length != 8) throw new FormatException("FEN board must have 8 ranks.");
        for (int r = 0; r < 8; r++)
        {
            int rank = 7 - r, file = 0;
            foreach (char ch in rows[r])
            {
                if (ch is >= '1' and <= '8') { file += ch - '0'; continue; }
                var p = PieceExtensions.PieceFromFenChar(ch);
                if (p == Piece.None || file > 7) throw new FormatException($"Bad FEN board: '{rows[r]}'.");
                _board[Square.At(file, rank)] = p;
                if (p.Type() == PieceType.King)
                {
                    if (_kingSquare[(int)p.Color()] != Square.None) throw new FormatException("FEN has two kings of one colour.");
                    _kingSquare[(int)p.Color()] = Square.At(file, rank);
                }
                file++;
            }
            if (file != 8) throw new FormatException($"Bad FEN rank: '{rows[r]}'.");
        }
        if (_kingSquare[0] == Square.None || _kingSquare[1] == Square.None)
            throw new FormatException("FEN must have one king of each colour.");

        SideToMove = parts[1] switch
        {
            "w" => Color.White,
            "b" => Color.Black,
            _ => throw new FormatException("FEN side to move must be 'w' or 'b'."),
        };

        Castling = CastlingRights.None;
        if (parts[2] != "-")
        {
            foreach (char ch in parts[2])
            {
                Castling |= ch switch
                {
                    'K' => CastlingRights.WhiteKingside,
                    'Q' => CastlingRights.WhiteQueenside,
                    'k' => CastlingRights.BlackKingside,
                    'q' => CastlingRights.BlackQueenside,
                    _ => throw new FormatException($"Bad FEN castling field '{parts[2]}'."),
                };
            }
        }
        // Drop rights that can't be real (king or rook not on its home square).
        if (_board[Square.E1] != Piece.WhiteKing) Castling &= ~(CastlingRights.WhiteKingside | CastlingRights.WhiteQueenside);
        if (_board[Square.H1] != Piece.WhiteRook) Castling &= ~CastlingRights.WhiteKingside;
        if (_board[Square.A1] != Piece.WhiteRook) Castling &= ~CastlingRights.WhiteQueenside;
        if (_board[Square.E8] != Piece.BlackKing) Castling &= ~(CastlingRights.BlackKingside | CastlingRights.BlackQueenside);
        if (_board[Square.H8] != Piece.BlackRook) Castling &= ~CastlingRights.BlackKingside;
        if (_board[Square.A8] != Piece.BlackRook) Castling &= ~CastlingRights.BlackQueenside;

        for (int file = 0; file < 8; file++)
        {
            if (_board[Square.At(file, 0)].Type() == PieceType.Pawn || _board[Square.At(file, 7)].Type() == PieceType.Pawn)
                throw new FormatException("Pawns can't be on the first or last rank.");
        }

        EnPassantSquare = Square.None;
        if (parts[3] != "-")
        {
            int ep = Square.Parse(parts[3]);
            if (ep == Square.None) throw new FormatException($"Bad FEN en passant square '{parts[3]}'.");
            // It must be the square just behind a pawn that has just made a double step.
            var them = SideToMove.Opponent();
            int expectedRank = SideToMove == Color.White ? 5 : 2;
            int pawnSq = SideToMove == Color.White ? ep - 8 : ep + 8;
            int originSq = SideToMove == Color.White ? ep + 8 : ep - 8;
            bool plausible = Square.Rank(ep) == expectedRank
                             && _board[pawnSq] == PieceType.Pawn.Of(them)
                             && _board[ep] == Piece.None
                             && _board[originSq] == Piece.None;
            // Only keep it if a pawn could actually capture there (keeps hashes and FEN output consistent).
            if (plausible && EnPassantCaptureIsPossible(ep, SideToMove)) EnPassantSquare = ep;
        }

        // The side that just moved can't have left its own king in check.
        if (IsSquareAttacked(_kingSquare[(int)SideToMove.Opponent()], SideToMove))
            throw new FormatException("The side not to move is in check, so this position can't happen.");

        HalfmoveClock = parts.Length > 4 && int.TryParse(parts[4], out var hm) && hm >= 0 ? hm : 0;
        FullmoveNumber = parts.Length > 5 && int.TryParse(parts[5], out var fm) && fm > 0 ? fm : 1;

        Hash = ComputeHash();
    }

    public string ToFen()
    {
        var sb = new StringBuilder(90);
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                var p = _board[Square.At(file, rank)];
                if (p == Piece.None) { empty++; continue; }
                if (empty > 0) { sb.Append(empty); empty = 0; }
                sb.Append(p.ToFenChar());
            }
            if (empty > 0) sb.Append(empty);
            if (rank > 0) sb.Append('/');
        }
        sb.Append(SideToMove == Color.White ? " w " : " b ");
        if (Castling == CastlingRights.None) sb.Append('-');
        else
        {
            if ((Castling & CastlingRights.WhiteKingside) != 0) sb.Append('K');
            if ((Castling & CastlingRights.WhiteQueenside) != 0) sb.Append('Q');
            if ((Castling & CastlingRights.BlackKingside) != 0) sb.Append('k');
            if ((Castling & CastlingRights.BlackQueenside) != 0) sb.Append('q');
        }
        sb.Append(' ').Append(Square.Name(EnPassantSquare));
        sb.Append(' ').Append(HalfmoveClock).Append(' ').Append(FullmoveNumber);
        return sb.ToString();
    }

    public override string ToString() => ToFen();

    /// <summary>
    /// A text picture of the board, White at the bottom (upper case = White), handy when debugging:
    /// <code>Console.WriteLine(position.ToAscii());</code>
    /// </summary>
    public string ToAscii()
    {
        var sb = new StringBuilder();
        for (int rank = 7; rank >= 0; rank--)
        {
            sb.Append(rank + 1).Append(' ');
            for (int file = 0; file < 8; file++)
                sb.Append(' ').Append(_board[Square.At(file, rank)].ToFenChar());
            sb.Append('\n');
        }
        sb.Append("   a b c d e f g h\n");
        sb.Append(SideToMove == Color.White ? "White" : "Black").Append(" to move");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ Hashing

    private ulong ComputeHash()
    {
        ulong h = 0;
        for (int sq = 0; sq < 64; sq++)
            if (_board[sq] != Piece.None) h ^= Zobrist.Pieces[(int)_board[sq], sq];
        h ^= Zobrist.Castling[(int)Castling];
        if (EnPassantSquare != Square.None) h ^= Zobrist.EnPassantFile[Square.File(EnPassantSquare)];
        if (SideToMove == Color.Black) h ^= Zobrist.BlackToMove;
        return h;
    }

    // ------------------------------------------------------------------ Attacks

    /// <summary>Is <paramref name="sq"/> attacked by any piece of colour <paramref name="by"/>?</summary>
    public bool IsSquareAttacked(int sq, Color by)
    {
        var b = _board;

        // Pawns: look "backwards" from the square to where an attacking pawn would stand.
        var pawn = PieceType.Pawn.Of(by);
        foreach (int from in Tables.PawnAttacks[(int)by.Opponent(), sq])
            if (b[from] == pawn) return true;

        var knight = PieceType.Knight.Of(by);
        foreach (int from in Tables.KnightMoves[sq])
            if (b[from] == knight) return true;

        var king = PieceType.King.Of(by);
        foreach (int from in Tables.KingMoves[sq])
            if (b[from] == king) return true;

        var queen = PieceType.Queen.Of(by);
        var rook = PieceType.Rook.Of(by);
        var bishop = PieceType.Bishop.Of(by);
        for (int dir = 0; dir < 8; dir++)
        {
            var straightAttacker = dir < 4 ? rook : bishop;
            foreach (int s in Tables.Rays[dir, sq])
            {
                var p = b[s];
                if (p == Piece.None) continue;
                if (p == queen || p == straightAttacker) return true;
                break;
            }
        }
        return false;
    }

    /// <summary>Is the side to move in check?</summary>
    public bool IsInCheck => IsSquareAttacked(_kingSquare[(int)SideToMove], SideToMove.Opponent());

    // ------------------------------------------------------------------ Move generation

    /// <summary>All legal moves in this position, as a new list.</summary>
    public List<Move> GetLegalMoves()
    {
        Span<Move> buffer = stackalloc Move[MaxMoves];
        int n = GenerateLegalMoves(buffer);
        var list = new List<Move>(n);
        for (int i = 0; i < n; i++) list.Add(buffer[i]);
        return list;
    }

    /// <summary>
    /// Writes all legal moves into <paramref name="moves"/> (which needs room for <see cref="MaxMoves"/>)
    /// and returns how many there are. Allocation-free: use this in hot loops.
    /// </summary>
    public int GenerateLegalMoves(Span<Move> moves)
    {
        int n = GeneratePseudoLegalMoves(moves);
        var us = SideToMove;
        bool inCheck = IsInCheck;
        ulong pinned = PinnedPieces(us);
        int legal = 0;
        for (int i = 0; i < n; i++)
        {
            if (IsPseudoLegalMoveLegal(moves[i], us, inCheck, pinned)) moves[legal++] = moves[i];
        }
        return legal;
    }

    /// <summary>True if the side to move has at least one legal move.</summary>
    public bool HasLegalMoves()
    {
        Span<Move> moves = stackalloc Move[MaxMoves];
        int n = GeneratePseudoLegalMoves(moves);
        var us = SideToMove;
        bool inCheck = IsInCheck;
        ulong pinned = PinnedPieces(us);
        for (int i = 0; i < n; i++)
        {
            if (IsPseudoLegalMoveLegal(moves[i], us, inCheck, pinned)) return true;
        }
        return false;
    }

    /// <summary>
    /// Does this pseudo-legal move leave our king safe? Shortcut: when we're not in check, a move by
    /// a piece that isn't the king, isn't pinned and isn't en passant can't expose the king, so it's
    /// legal without trying it. Everything else is played on the board and checked.
    /// </summary>
    private bool IsPseudoLegalMoveLegal(Move m, Color us, bool inCheck, ulong pinned)
    {
        if (!inCheck && !m.IsEnPassant && m.From != _kingSquare[(int)us] && (pinned & (1UL << m.From)) == 0)
            return true;
        MakeMoveInternal(m, updateHash: false);
        bool ok = !IsSquareAttacked(_kingSquare[(int)us], us.Opponent());
        UnmakeMoveInternal();
        return ok;
    }

    /// <summary>Bit mask of our pieces that stand between our king and an enemy rook/bishop/queen.</summary>
    private ulong PinnedPieces(Color us)
    {
        ulong pinned = 0;
        int king = _kingSquare[(int)us];
        var them = us.Opponent();
        var queen = PieceType.Queen.Of(them);
        for (int dir = 0; dir < 8; dir++)
        {
            var slider = (dir < 4 ? PieceType.Rook : PieceType.Bishop).Of(them);
            int candidate = -1;
            foreach (int sq in Tables.Rays[dir, king])
            {
                var p = _board[sq];
                if (p == Piece.None) continue;
                if (candidate < 0)
                {
                    if (p.Color() != us) break; // first piece is an enemy: no pin on this line
                    candidate = sq;
                    continue;
                }
                if (p == queen || p == slider) pinned |= 1UL << candidate;
                break;
            }
        }
        return pinned;
    }

    /// <summary>Is <paramref name="move"/> one of the legal moves here? (Matches from, to and promotion.)</summary>
    public bool IsLegal(Move move) => TryFindLegalMove(move.From, move.To, move.Promotion, out _);

    /// <summary>
    /// Finds the legal move going from <paramref name="from"/> to <paramref name="to"/>
    /// (with the given promotion piece, if any). Handy for UIs, which only know the squares.
    /// </summary>
    public bool TryFindLegalMove(int from, int to, PieceType promotion, out Move move)
    {
        Span<Move> moves = stackalloc Move[MaxMoves];
        int n = GenerateLegalMoves(moves);
        for (int i = 0; i < n; i++)
        {
            if (moves[i].From == from && moves[i].To == to && moves[i].Promotion == promotion)
            {
                move = moves[i];
                return true;
            }
        }
        move = Move.None;
        return false;
    }

    private int GeneratePseudoLegalMoves(Span<Move> moves)
    {
        int n = 0;
        var b = _board;
        var us = SideToMove;
        var them = us.Opponent();

        for (int from = 0; from < 64; from++)
        {
            var p = b[from];
            if (!p.IsColor(us)) continue;

            switch (p.Type())
            {
                case PieceType.Pawn:
                    n = GeneratePawnMoves(from, us, moves, n);
                    break;
                case PieceType.Knight:
                    foreach (int to in Tables.KnightMoves[from]) n = AddStep(from, to, them, moves, n);
                    break;
                case PieceType.King:
                    foreach (int to in Tables.KingMoves[from]) n = AddStep(from, to, them, moves, n);
                    break;
                case PieceType.Bishop:
                    n = AddSlides(from, 4, 8, them, moves, n);
                    break;
                case PieceType.Rook:
                    n = AddSlides(from, 0, 4, them, moves, n);
                    break;
                case PieceType.Queen:
                    n = AddSlides(from, 0, 8, them, moves, n);
                    break;
            }
        }

        n = GenerateCastling(us, moves, n);
        return n;
    }

    private int AddStep(int from, int to, Color them, Span<Move> moves, int n)
    {
        var target = _board[to];
        if (target == Piece.None) moves[n++] = new Move(from, to);
        else if (target.Color() == them) moves[n++] = new Move(from, to, PieceType.None, MoveFlags.Capture);
        return n;
    }

    private int AddSlides(int from, int dirStart, int dirEnd, Color them, Span<Move> moves, int n)
    {
        for (int dir = dirStart; dir < dirEnd; dir++)
        {
            foreach (int to in Tables.Rays[dir, from])
            {
                var target = _board[to];
                if (target == Piece.None) { moves[n++] = new Move(from, to); continue; }
                if (target.Color() == them) moves[n++] = new Move(from, to, PieceType.None, MoveFlags.Capture);
                break;
            }
        }
        return n;
    }

    private int GeneratePawnMoves(int from, Color us, Span<Move> moves, int n)
    {
        int forward = us == Color.White ? 8 : -8;
        int startRank = us == Color.White ? 1 : 6;
        int promoRank = us == Color.White ? 7 : 0;
        var them = us.Opponent();

        int one = from + forward;
        if (_board[one] == Piece.None)
        {
            if (Square.Rank(one) == promoRank) n = AddPromotions(from, one, MoveFlags.None, moves, n);
            else
            {
                moves[n++] = new Move(from, one);
                if (Square.Rank(from) == startRank)
                {
                    int two = one + forward;
                    if (_board[two] == Piece.None) moves[n++] = new Move(from, two, PieceType.None, MoveFlags.DoublePawnPush);
                }
            }
        }

        foreach (int to in Tables.PawnAttacks[(int)us, from])
        {
            var target = _board[to];
            if (target.IsColor(them))
            {
                if (Square.Rank(to) == promoRank) n = AddPromotions(from, to, MoveFlags.Capture, moves, n);
                else moves[n++] = new Move(from, to, PieceType.None, MoveFlags.Capture);
            }
            else if (to == EnPassantSquare)
            {
                moves[n++] = new Move(from, to, PieceType.None, MoveFlags.Capture | MoveFlags.EnPassant);
            }
        }
        return n;
    }

    private static int AddPromotions(int from, int to, MoveFlags flags, Span<Move> moves, int n)
    {
        moves[n++] = new Move(from, to, PieceType.Queen, flags);
        moves[n++] = new Move(from, to, PieceType.Rook, flags);
        moves[n++] = new Move(from, to, PieceType.Bishop, flags);
        moves[n++] = new Move(from, to, PieceType.Knight, flags);
        return n;
    }

    private int GenerateCastling(Color us, Span<Move> moves, int n)
    {
        var them = us.Opponent();
        if (us == Color.White)
        {
            if ((Castling & (CastlingRights.WhiteKingside | CastlingRights.WhiteQueenside)) == 0) return n;
            if (IsSquareAttacked(Square.E1, them)) return n; // can't castle out of check
            if ((Castling & CastlingRights.WhiteKingside) != 0
                && _board[Square.F1] == Piece.None && _board[Square.G1] == Piece.None
                && !IsSquareAttacked(Square.F1, them)) // can't pass through check (landing square is checked later)
                moves[n++] = new Move(Square.E1, Square.G1, PieceType.None, MoveFlags.Castling);
            if ((Castling & CastlingRights.WhiteQueenside) != 0
                && _board[Square.D1] == Piece.None && _board[Square.C1] == Piece.None && _board[Square.B1] == Piece.None
                && !IsSquareAttacked(Square.D1, them))
                moves[n++] = new Move(Square.E1, Square.C1, PieceType.None, MoveFlags.Castling);
        }
        else
        {
            if ((Castling & (CastlingRights.BlackKingside | CastlingRights.BlackQueenside)) == 0) return n;
            if (IsSquareAttacked(Square.E8, them)) return n;
            if ((Castling & CastlingRights.BlackKingside) != 0
                && _board[Square.F8] == Piece.None && _board[Square.G8] == Piece.None
                && !IsSquareAttacked(Square.F8, them))
                moves[n++] = new Move(Square.E8, Square.G8, PieceType.None, MoveFlags.Castling);
            if ((Castling & CastlingRights.BlackQueenside) != 0
                && _board[Square.D8] == Piece.None && _board[Square.C8] == Piece.None && _board[Square.B8] == Piece.None
                && !IsSquareAttacked(Square.D8, them))
                moves[n++] = new Move(Square.E8, Square.C8, PieceType.None, MoveFlags.Castling);
        }
        return n;
    }

    private bool EnPassantCaptureIsPossible(int epSquare, Color capturer)
    {
        // The capturing pawn stands beside the pawn that just moved, i.e. diagonally behind the ep square.
        var pawn = PieceType.Pawn.Of(capturer);
        foreach (int from in Tables.PawnAttacks[(int)capturer.Opponent(), epSquare])
            if (_board[from] == pawn) return true;
        return false;
    }

    // ------------------------------------------------------------------ Make / undo

    /// <summary>
    /// Plays a move. The move should be legal (e.g. from <see cref="GetLegalMoves"/>);
    /// use <see cref="TryFindLegalMove"/> or <see cref="IsLegal"/> to check moves from outside.
    /// </summary>
    public void MakeMove(Move move) => MakeMoveInternal(move, updateHash: true);

    /// <summary>Takes back the last move made with <see cref="MakeMove"/>.</summary>
    public void UndoMove()
    {
        if (_historyCount == 0) throw new InvalidOperationException("No move to undo.");
        UnmakeMoveInternal();
    }

    private void MakeMoveInternal(Move m, bool updateHash)
    {
        if (_historyCount == _history.Length) Array.Resize(ref _history, _history.Length * 2);
        ref var undo = ref _history[_historyCount++];
        undo.Move = m;
        undo.Castling = Castling;
        undo.EnPassant = EnPassantSquare;
        undo.HalfmoveClock = HalfmoveClock;
        undo.Hash = Hash;

        var b = _board;
        int from = m.From, to = m.To;
        var piece = b[from];
        var us = SideToMove;
        ulong h = Hash;

        if (updateHash && EnPassantSquare != Square.None) h ^= Zobrist.EnPassantFile[Square.File(EnPassantSquare)];
        EnPassantSquare = Square.None;

        // Captures
        Piece captured;
        if (m.IsEnPassant)
        {
            int capSq = us == Color.White ? to - 8 : to + 8;
            captured = b[capSq];
            b[capSq] = Piece.None;
            if (updateHash) h ^= Zobrist.Pieces[(int)captured, capSq];
        }
        else
        {
            captured = b[to];
            if (captured != Piece.None && updateHash) h ^= Zobrist.Pieces[(int)captured, to];
        }
        undo.Captured = captured;

        // Move the piece (promoting if needed)
        var placed = m.IsPromotion ? m.Promotion.Of(us) : piece;
        b[from] = Piece.None;
        b[to] = placed;
        if (updateHash) h ^= Zobrist.Pieces[(int)piece, from] ^ Zobrist.Pieces[(int)placed, to];

        if (piece.Type() == PieceType.King)
        {
            _kingSquare[(int)us] = to;
            if (m.IsCastling)
            {
                int rookFrom, rookTo;
                if (to > from) { rookFrom = from + 3; rookTo = from + 1; }
                else { rookFrom = from - 4; rookTo = from - 1; }
                var rook = b[rookFrom];
                b[rookFrom] = Piece.None;
                b[rookTo] = rook;
                if (updateHash) h ^= Zobrist.Pieces[(int)rook, rookFrom] ^ Zobrist.Pieces[(int)rook, rookTo];
            }
        }

        // Castling rights: lost when the king or a rook moves, or a rook is captured on its home square.
        var newCastling = Castling & Tables.CastlingMask[from] & Tables.CastlingMask[to];
        if (updateHash && newCastling != Castling) h ^= Zobrist.Castling[(int)Castling] ^ Zobrist.Castling[(int)newCastling];
        Castling = newCastling;

        HalfmoveClock = piece.Type() == PieceType.Pawn || captured != Piece.None ? 0 : HalfmoveClock + 1;

        if ((m.Flags & MoveFlags.DoublePawnPush) != 0)
        {
            int ep = (from + to) >> 1;
            if (EnPassantCaptureIsPossible(ep, us.Opponent()))
            {
                EnPassantSquare = ep;
                if (updateHash) h ^= Zobrist.EnPassantFile[Square.File(ep)];
            }
        }

        if (us == Color.Black) FullmoveNumber++;
        SideToMove = us.Opponent();
        if (updateHash) h ^= Zobrist.BlackToMove;
        Hash = h;
    }

    private void UnmakeMoveInternal()
    {
        ref var undo = ref _history[--_historyCount];
        var m = undo.Move;
        var b = _board;
        int from = m.From, to = m.To;

        SideToMove = SideToMove.Opponent();
        var us = SideToMove;
        if (us == Color.Black) FullmoveNumber--;

        var moved = b[to];
        var original = m.IsPromotion ? PieceType.Pawn.Of(us) : moved;
        b[from] = original;
        b[to] = Piece.None;

        if (m.IsEnPassant)
        {
            b[us == Color.White ? to - 8 : to + 8] = undo.Captured;
        }
        else
        {
            b[to] = undo.Captured;
        }

        if (original.Type() == PieceType.King)
        {
            _kingSquare[(int)us] = from;
            if (m.IsCastling)
            {
                int rookFrom, rookTo;
                if (to > from) { rookFrom = from + 3; rookTo = from + 1; }
                else { rookFrom = from - 4; rookTo = from - 1; }
                b[rookFrom] = b[rookTo];
                b[rookTo] = Piece.None;
            }
        }

        Castling = undo.Castling;
        EnPassantSquare = undo.EnPassant;
        HalfmoveClock = undo.HalfmoveClock;
        Hash = undo.Hash;
    }

    // ------------------------------------------------------------------ Game-ending rules

    public bool IsCheckmate => IsInCheck && !HasLegalMoves();
    public bool IsStalemate => !IsInCheck && !HasLegalMoves();

    /// <summary>
    /// True if this exact position (same pieces, side to move, castling and en passant rights)
    /// has now occurred at least three times in the moves made on this object.
    /// </summary>
    public bool IsThreefoldRepetition
    {
        get
        {
            int count = 1;
            // Only positions since the last capture/pawn move can repeat, and only with the same side to move.
            int limit = Math.Min(HalfmoveClock, _historyCount);
            for (int i = 2; i <= limit; i += 2)
            {
                if (_history[_historyCount - i].Hash == Hash && ++count >= 3) return true;
            }
            return false;
        }
    }

    public bool IsFiftyMoveRule => HalfmoveClock >= 100;

    /// <summary>
    /// Neither side can possibly checkmate: K v K, K+minor v K, or only bishops all on one square colour.
    /// </summary>
    public bool IsInsufficientMaterial
    {
        get
        {
            int minors = 0, knights = 0;
            bool lightBishop = false, darkBishop = false;
            for (int sq = 0; sq < 64; sq++)
            {
                switch (_board[sq].Type())
                {
                    case PieceType.None:
                    case PieceType.King:
                        break;
                    case PieceType.Knight:
                        minors++; knights++;
                        break;
                    case PieceType.Bishop:
                        minors++;
                        if (Square.IsLight(sq)) lightBishop = true; else darkBishop = true;
                        break;
                    default:
                        return false; // pawn, rook or queen
                }
            }
            if (minors <= 1) return true;
            return knights == 0 && !(lightBishop && darkBishop);
        }
    }

    /// <summary>
    /// The result of the game in this position. Checkmate and stalemate first, then the automatic
    /// draws (insufficient material, fifty-move rule, threefold repetition).
    /// </summary>
    public GameResult GetResult()
    {
        if (!HasLegalMoves())
        {
            return IsInCheck
                ? GameResult.Win(SideToMove.Opponent(), GameEndReason.Checkmate)
                : GameResult.Draw(GameEndReason.Stalemate);
        }
        if (IsInsufficientMaterial) return GameResult.Draw(GameEndReason.InsufficientMaterial);
        if (IsFiftyMoveRule) return GameResult.Draw(GameEndReason.FiftyMoveRule);
        if (IsThreefoldRepetition) return GameResult.Draw(GameEndReason.ThreefoldRepetition);
        return GameResult.Ongoing;
    }
}
