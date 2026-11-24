using System.Globalization;

namespace RlChess.Engine;

/// <summary>
/// How good a position is, from the point of view of the side to move: either a score in
/// centipawns (100 = about a pawn better) or "mate in N" (positive N = the side to move mates,
/// negative = it gets mated).
/// </summary>
public readonly record struct EvalScore
{
    public int Centipawns { get; }
    /// <summary>Moves to mate (null = no mate found). Positive: side to move mates; negative: it is mated.</summary>
    public int? Mate { get; }

    private EvalScore(int centipawns, int? mate) { Centipawns = centipawns; Mate = mate; }

    public static EvalScore FromCentipawns(int centipawns) => new(centipawns, null);

    /// <summary>Mate in <paramref name="moves"/> (negative = being mated). 0 means "already checkmated".</summary>
    public static EvalScore MateIn(int moves) => new(moves > 0 ? 100_000 : -100_000, moves);

    public bool IsMate => Mate is not null;

    /// <summary>The same score seen from the other side.</summary>
    public EvalScore Negate() => Mate is int m ? new(-Centipawns, -m) : new(-Centipawns, null);

    /// <summary>The score from White's point of view, given who is to move in the position it describes.</summary>
    public EvalScore ForWhite(Color sideToMove) => sideToMove == Color.White ? this : Negate();

    /// <summary>Chance of winning for the side this score belongs to, 0 to 100 (see <see cref="WinChances"/>).</summary>
    public double WinPercent => WinChances.WinPercent(this);

    /// <summary>"+0.35", "-1.20", "M3" (mating in 3) or "-M2" (being mated in 2).</summary>
    public override string ToString()
    {
        if (Mate is int m) return m >= 0 ? $"M{m}" : $"-M{-m}";
        return (Centipawns / 100.0).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Turns engine scores into winning chances, using lichess's published formula. Winning chances
/// are what game review compares: losing 2 pawns when you're 10 pawns up hardly matters, but
/// losing 2 pawns in an equal position is a big deal.
/// </summary>
public static class WinChances
{
    /// <summary>lichess's constant (fitted to real games).</summary>
    public const double K = 0.00368208;

    /// <summary>0 to 100: 50 = even, 100 = certain win, for the side the score belongs to.</summary>
    public static double WinPercent(EvalScore score)
    {
        if (score.Mate is int m) return m > 0 || (m == 0 && score.Centipawns > 0) ? 100 : 0;
        return WinPercent(score.Centipawns);
    }

    public static double WinPercent(int centipawns)
    {
        double cp = Math.Clamp(centipawns, -1000, 1000);
        return 50 + 50 * (2 / (1 + Math.Exp(-K * cp)) - 1);
    }

    /// <summary>
    /// How accurate a move was, 0 to 100, from how much it dropped the mover's win% (lichess's formula):
    /// no drop = 100, a 10% drop ≈ 66, a 30% drop ≈ 25.
    /// </summary>
    public static double MoveAccuracy(double winBefore, double winAfter)
    {
        double drop = Math.Max(0, winBefore - winAfter);
        return Math.Clamp(103.1668 * Math.Exp(-0.04354 * drop) - 3.1669, 0, 100);
    }
}
