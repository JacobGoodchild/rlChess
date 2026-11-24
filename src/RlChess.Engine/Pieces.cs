namespace RlChess.Engine;

/// <summary>The two sides.</summary>
public enum Color : byte
{
    White = 0,
    Black = 1,
}

/// <summary>The kind of piece, without colour.</summary>
public enum PieceType : byte
{
    None = 0,
    Pawn = 1,
    Knight = 2,
    Bishop = 3,
    Rook = 4,
    Queen = 5,
    King = 6,
}

/// <summary>
/// A coloured piece (or <see cref="None"/> for an empty square).
/// Encoded as <c>colour * 8 + type</c>, so it fits in a byte.
/// </summary>
public enum Piece : byte
{
    None = 0,
    WhitePawn = 1, WhiteKnight = 2, WhiteBishop = 3, WhiteRook = 4, WhiteQueen = 5, WhiteKing = 6,
    BlackPawn = 9, BlackKnight = 10, BlackBishop = 11, BlackRook = 12, BlackQueen = 13, BlackKing = 14,
}

public static class PieceExtensions
{
    public static Color Opponent(this Color c) => (Color)((int)c ^ 1);

    public static PieceType Type(this Piece p) => (PieceType)((int)p & 7);

    /// <summary>The colour of a piece. Undefined for <see cref="Piece.None"/>.</summary>
    public static Color Color(this Piece p) => (Color)((int)p >> 3);

    public static bool IsColor(this Piece p, Color c) => p != Piece.None && p.Color() == c;

    public static Piece Of(this PieceType t, Color c) => t == PieceType.None ? Piece.None : (Piece)(((int)c << 3) | (int)t);

    /// <summary>FEN letter: upper case for White, lower case for Black.</summary>
    public static char ToFenChar(this Piece p)
    {
        if (p == Piece.None) return '.';
        char ch = p.Type().ToLetter();
        return p.Color() == Engine.Color.White ? ch : char.ToLowerInvariant(ch);
    }

    /// <summary>Upper-case letter used in FEN/SAN: P N B R Q K.</summary>
    public static char ToLetter(this PieceType t) => t switch
    {
        PieceType.Pawn => 'P',
        PieceType.Knight => 'N',
        PieceType.Bishop => 'B',
        PieceType.Rook => 'R',
        PieceType.Queen => 'Q',
        PieceType.King => 'K',
        _ => '?',
    };

    public static PieceType PieceTypeFromLetter(char ch) => char.ToUpperInvariant(ch) switch
    {
        'P' => PieceType.Pawn,
        'N' => PieceType.Knight,
        'B' => PieceType.Bishop,
        'R' => PieceType.Rook,
        'Q' => PieceType.Queen,
        'K' => PieceType.King,
        _ => PieceType.None,
    };

    public static Piece PieceFromFenChar(char ch)
    {
        var t = PieceTypeFromLetter(ch);
        if (t == PieceType.None) return Piece.None;
        return t.Of(char.IsUpper(ch) ? Engine.Color.White : Engine.Color.Black);
    }
}
