namespace RlChess.Engine;

/// <summary>Pre-computed move tables, built once at start-up.</summary>
internal static class Tables
{
    /// <summary>Knight destinations from each square.</summary>
    public static readonly int[][] KnightMoves = new int[64][];

    /// <summary>King destinations (one step) from each square.</summary>
    public static readonly int[][] KingMoves = new int[64][];

    /// <summary>Squares a pawn of the given colour standing on the square attacks.</summary>
    public static readonly int[,][] PawnAttacks = new int[2, 64][];

    /// <summary>
    /// Rays[dir, sq]: squares in order moving away from sq. Directions 0-3 are straight
    /// (rook-like), 4-7 are diagonal (bishop-like).
    /// </summary>
    public static readonly int[,][] Rays = new int[8, 64][];

    /// <summary>Castling rights that survive a move touching this square.</summary>
    public static readonly CastlingRights[] CastlingMask = new CastlingRights[64];

    private static readonly (int df, int dr)[] Directions =
    [
        (0, 1), (0, -1), (1, 0), (-1, 0),   // straight
        (1, 1), (1, -1), (-1, 1), (-1, -1), // diagonal
    ];

    static Tables()
    {
        (int, int)[] knight = [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];

        for (int sq = 0; sq < 64; sq++)
        {
            int f = Square.File(sq), r = Square.Rank(sq);
            KnightMoves[sq] = Steps(f, r, knight);
            KingMoves[sq] = Steps(f, r, Directions);
            PawnAttacks[(int)Color.White, sq] = Steps(f, r, [(-1, 1), (1, 1)]);
            PawnAttacks[(int)Color.Black, sq] = Steps(f, r, [(-1, -1), (1, -1)]);

            for (int d = 0; d < 8; d++)
            {
                var ray = new List<int>();
                int ff = f + Directions[d].df, rr = r + Directions[d].dr;
                while (ff is >= 0 and < 8 && rr is >= 0 and < 8)
                {
                    ray.Add(Square.At(ff, rr));
                    ff += Directions[d].df;
                    rr += Directions[d].dr;
                }
                Rays[d, sq] = ray.ToArray();
            }

            CastlingMask[sq] = CastlingRights.All;
        }

        CastlingMask[Square.E1] = ~(CastlingRights.WhiteKingside | CastlingRights.WhiteQueenside) & CastlingRights.All;
        CastlingMask[Square.H1] = ~CastlingRights.WhiteKingside & CastlingRights.All;
        CastlingMask[Square.A1] = ~CastlingRights.WhiteQueenside & CastlingRights.All;
        CastlingMask[Square.E8] = ~(CastlingRights.BlackKingside | CastlingRights.BlackQueenside) & CastlingRights.All;
        CastlingMask[Square.H8] = ~CastlingRights.BlackKingside & CastlingRights.All;
        CastlingMask[Square.A8] = ~CastlingRights.BlackQueenside & CastlingRights.All;
    }

    private static int[] Steps(int f, int r, (int df, int dr)[] deltas)
    {
        var list = new List<int>();
        foreach (var (df, dr) in deltas)
        {
            int ff = f + df, rr = r + dr;
            if (ff is >= 0 and < 8 && rr is >= 0 and < 8) list.Add(Square.At(ff, rr));
        }
        return list.ToArray();
    }
}

/// <summary>Random numbers for position hashing (fixed seed, so hashes are stable between runs).</summary>
internal static class Zobrist
{
    public static readonly ulong[,] Pieces = new ulong[16, 64];
    public static readonly ulong[] Castling = new ulong[16];
    public static readonly ulong[] EnPassantFile = new ulong[8];
    public static readonly ulong BlackToMove;

    static Zobrist()
    {
        ulong state = 0x9E3779B97F4A7C15UL;
        ulong Next()
        {
            // SplitMix64
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        for (int p = 0; p < 16; p++)
            for (int sq = 0; sq < 64; sq++)
                Pieces[p, sq] = Next();
        for (int i = 0; i < 16; i++) Castling[i] = Next();
        for (int i = 0; i < 8; i++) EnPassantFile[i] = Next();
        BlackToMove = Next();
    }
}
