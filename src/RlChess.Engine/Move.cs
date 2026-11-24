namespace RlChess.Engine;

[Flags]
public enum MoveFlags : byte
{
    None = 0,
    Capture = 1,
    EnPassant = 2,
    Castling = 4,
    DoublePawnPush = 8,
}

/// <summary>
/// A chess move, packed into a single int so it's cheap to copy and store.
/// Castling is stored as the king's move (e.g. e1 to g1), like UCI.
/// </summary>
public readonly struct Move : IEquatable<Move>
{
    private readonly int _value;

    public static readonly Move None = default;

    public Move(int from, int to, PieceType promotion = PieceType.None, MoveFlags flags = MoveFlags.None)
    {
        _value = from | (to << 6) | ((int)promotion << 12) | ((int)flags << 16);
    }

    public int From => _value & 63;
    public int To => (_value >> 6) & 63;
    public PieceType Promotion => (PieceType)((_value >> 12) & 7);
    public MoveFlags Flags => (MoveFlags)((_value >> 16) & 15);

    public bool IsNone => _value == 0;
    public bool IsCapture => (Flags & MoveFlags.Capture) != 0;
    public bool IsEnPassant => (Flags & MoveFlags.EnPassant) != 0;
    public bool IsCastling => (Flags & MoveFlags.Castling) != 0;
    public bool IsPromotion => Promotion != PieceType.None;

    /// <summary>The move in UCI form, e.g. "e2e4" or "e7e8q".</summary>
    public string ToUci()
    {
        if (IsNone) return "0000";
        var s = Square.Name(From) + Square.Name(To);
        return IsPromotion ? s + char.ToLowerInvariant(Promotion.ToLetter()) : s;
    }

    public override string ToString() => ToUci();

    public bool Equals(Move other) => _value == other._value;
    public override bool Equals(object? obj) => obj is Move m && Equals(m);
    public override int GetHashCode() => _value;
    public static bool operator ==(Move a, Move b) => a._value == b._value;
    public static bool operator !=(Move a, Move b) => a._value != b._value;
}
