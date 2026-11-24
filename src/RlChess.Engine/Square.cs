namespace RlChess.Engine;

/// <summary>
/// Squares are plain ints 0..63: a1 = 0, b1 = 1, ... h1 = 7, a2 = 8, ... h8 = 63.
/// So <c>file = square % 8</c> and <c>rank = square / 8</c> (both 0-based).
/// </summary>
public static class Square
{
    public const int None = -1;

    public const int A1 = 0, B1 = 1, C1 = 2, D1 = 3, E1 = 4, F1 = 5, G1 = 6, H1 = 7;
    public const int A8 = 56, B8 = 57, C8 = 58, D8 = 59, E8 = 60, F8 = 61, G8 = 62, H8 = 63;

    public static int File(int sq) => sq & 7;
    public static int Rank(int sq) => sq >> 3;
    public static int At(int file, int rank) => rank * 8 + file;
    public static bool IsLight(int sq) => ((File(sq) + Rank(sq)) & 1) == 1;

    public static string Name(int sq) =>
        sq is < 0 or > 63 ? "-" : $"{(char)('a' + File(sq))}{(char)('1' + Rank(sq))}";

    /// <summary>Parses "e4" etc. Returns <see cref="None"/> if invalid.</summary>
    public static int Parse(ReadOnlySpan<char> s)
    {
        if (s.Length != 2) return None;
        int f = s[0] - 'a', r = s[1] - '1';
        if (f is < 0 or > 7 || r is < 0 or > 7) return None;
        return At(f, r);
    }
}
