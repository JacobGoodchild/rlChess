using System.Text;

namespace RlChess.Engine;

/// <summary>Converting moves to and from SAN ("Nf3", "exd5", "O-O", "e8=Q+") and UCI ("e2e4", "e7e8q").</summary>
public static class Notation
{
    // ------------------------------------------------------------------ UCI

    public static string ToUci(Move move) => move.ToUci();

    /// <summary>Parses a UCI move ("e2e4", "e7e8q") and returns the matching legal move.</summary>
    public static bool TryParseUci(Position position, string text, out Move move)
    {
        move = Move.None;
        var s = text.Trim();
        if (s.Length is not (4 or 5)) return false;
        int from = Square.Parse(s.AsSpan(0, 2)), to = Square.Parse(s.AsSpan(2, 2));
        if (from == Square.None || to == Square.None) return false;
        var promo = PieceType.None;
        if (s.Length == 5)
        {
            promo = PieceExtensions.PieceTypeFromLetter(s[4]);
            if (promo is not (PieceType.Queen or PieceType.Rook or PieceType.Bishop or PieceType.Knight)) return false;
        }
        return position.TryFindLegalMove(from, to, promo, out move);
    }

    public static Move ParseUci(Position position, string text) =>
        TryParseUci(position, text, out var m) ? m : throw new FormatException($"'{text}' is not a legal UCI move here.");

    // ------------------------------------------------------------------ SAN

    /// <summary>SAN for a legal move in this position, including "+" or "#".</summary>
    public static string ToSan(Position position, Move move)
    {
        var sb = new StringBuilder(8);
        AppendSanWithoutSuffix(position, move, position.GetLegalMoves(), sb);

        position.MakeMove(move);
        if (position.IsInCheck) sb.Append(position.HasLegalMoves() ? '+' : '#');
        position.UndoMove();
        return sb.ToString();
    }

    private static void AppendSanWithoutSuffix(Position position, Move move, List<Move> legal, StringBuilder sb)
    {
        if (move.IsCastling)
        {
            sb.Append(move.To > move.From ? "O-O" : "O-O-O");
            return;
        }

        var type = position[move.From].Type();
        if (type == PieceType.Pawn)
        {
            if (move.IsCapture) sb.Append((char)('a' + Square.File(move.From))).Append('x');
            sb.Append(Square.Name(move.To));
            if (move.IsPromotion) sb.Append('=').Append(move.Promotion.ToLetter());
            return;
        }

        sb.Append(type.ToLetter());

        // Disambiguation: another piece of the same kind could also go to the same square.
        bool ambiguous = false, sameFile = false, sameRank = false;
        foreach (var other in legal)
        {
            if (other.To != move.To || other.From == move.From) continue;
            if (position[other.From].Type() != type) continue;
            ambiguous = true;
            if (Square.File(other.From) == Square.File(move.From)) sameFile = true;
            if (Square.Rank(other.From) == Square.Rank(move.From)) sameRank = true;
        }
        if (ambiguous)
        {
            if (!sameFile) sb.Append((char)('a' + Square.File(move.From)));
            else if (!sameRank) sb.Append((char)('1' + Square.Rank(move.From)));
            else sb.Append(Square.Name(move.From));
        }

        if (move.IsCapture) sb.Append('x');
        sb.Append(Square.Name(move.To));
    }

    /// <summary>
    /// Parses SAN and returns the matching legal move. Fairly forgiving: accepts missing or extra
    /// "+"/"#", annotations like "!?", "0-0" for castling, "e8Q" without "=", and missing "x".
    /// </summary>
    public static bool TryParseSan(Position position, string text, out Move move)
    {
        move = Move.None;
        var wanted = Normalise(text);
        if (wanted.Length == 0) return false;

        var legal = position.GetLegalMoves();
        var sb = new StringBuilder(8);
        Move loose = Move.None;
        int looseMatches = 0;
        foreach (var m in legal)
        {
            sb.Clear();
            AppendSanWithoutSuffix(position, m, legal, sb);
            var san = sb.ToString();
            if (san == wanted) { move = m; return true; }
            // Second chance: ignore "x" and over-specified squares (e.g. "Ngf3" when not needed).
            if (Loosely(san) == Loosely(wanted) || OverSpecified(position, m, wanted)) { loose = m; looseMatches++; }
        }
        if (looseMatches == 1) { move = loose; return true; }
        return false;
    }

    public static Move ParseSan(Position position, string text) =>
        TryParseSan(position, text, out var m) ? m : throw new FormatException($"'{text}' is not a legal SAN move here.");

    private static string Normalise(string text)
    {
        var s = text.Trim().TrimEnd('+', '#', '!', '?').Replace("0-0-0", "O-O-O").Replace("0-0", "O-O");
        // "e8Q" -> "e8=Q"
        if (s.Length >= 3 && char.IsAsciiLetterUpper(s[^1]) && char.IsAsciiDigit(s[^2]) && s[0] is >= 'a' and <= 'h')
            s = s[..^1] + "=" + s[^1];
        return s;
    }

    private static string Loosely(string san) => san.Replace("x", "");

    private static bool OverSpecified(Position position, Move m, string wanted)
    {
        var type = position[m.From].Type();
        if (type == PieceType.Pawn || m.IsCastling) return false;
        var full = $"{type.ToLetter()}{Square.Name(m.From)}{Square.Name(m.To)}";
        var w = Loosely(wanted);
        return w == full
            || w == $"{type.ToLetter()}{(char)('a' + Square.File(m.From))}{Square.Name(m.To)}"
            || w == $"{type.ToLetter()}{(char)('1' + Square.Rank(m.From))}{Square.Name(m.To)}";
    }
}
