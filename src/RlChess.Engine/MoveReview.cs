namespace RlChess.Engine;

/// <summary>Labels for moves in a game review (best first, then the bad ones).</summary>
public enum MoveLabel
{
    None,
    Brilliant,
    Great,
    Best,
    Excellent,
    Good,
    Book,
    Inaccuracy,
    Mistake,
    Miss,
    Blunder,
}

/// <summary>An engine's verdict on one position, from the point of view of the side to move.</summary>
/// <param name="Score">The score of the best move.</param>
/// <param name="BestMove">The engine's best move (null if the game is over).</param>
/// <param name="SecondScore">The score of the second-best move, if the engine looked at two (used for "only move").</param>
public sealed record PositionEval(EvalScore Score, Move? BestMove, EvalScore? SecondScore = null);

/// <summary>The review of one move.</summary>
/// <param name="WinBefore">Mover's win% before the move (with best play).</param>
/// <param name="WinAfter">Mover's win% after the move actually played.</param>
public sealed record ReviewedMove(Move Move, Color Mover, MoveLabel Label, double WinBefore, double WinAfter, double Accuracy, bool Sacrifice)
{
    /// <summary>How much winning chance the move gave away (0 = none).</summary>
    public double Loss => Math.Max(0, WinBefore - WinAfter);
}

/// <summary>
/// Game review: labels each move by how much it changed the mover's winning chances
/// (see <see cref="WinChances"/>), and works out each player's accuracy. The rules are listed in the
/// README ("Game review").
/// </summary>
public static class MoveReview
{
    /// <summary>Win% given away for each label (lichess uses the same steps: 5 / 10 / 15).</summary>
    public const double ExcellentBelow = 2, GoodBelow = 5, InaccuracyBelow = 10, MistakeBelow = 15;
    /// <summary>A move is the "only move" if the next best one would lose at least this much win%.</summary>
    public const double OnlyMoveGap = 15;

    /// <summary>Labels one move.</summary>
    /// <param name="before">The position before the move.</param>
    /// <param name="move">The move played.</param>
    /// <param name="evalBefore">The engine's view of <paramref name="before"/> (side to move = the mover).</param>
    /// <param name="evalAfter">The engine's view after the move (side to move = the opponent). Ignored if the game ended.</param>
    /// <param name="isBook">The move is a known opening move.</param>
    /// <param name="opponentLastLabel">The label of the opponent's previous move (to spot a missed chance).</param>
    public static ReviewedMove Classify(Position before, Move move, PositionEval evalBefore, PositionEval? evalAfter,
        bool isBook = false, MoveLabel opponentLastLabel = MoveLabel.None)
    {
        var mover = before.SideToMove;
        var after = before.Clone();
        after.MakeMove(move);

        double winBefore = evalBefore.Score.WinPercent;
        double winAfter = AfterWin(after, evalAfter);
        double loss = Math.Max(0, winBefore - winAfter);
        double accuracy = WinChances.MoveAccuracy(winBefore, winAfter);
        int legalMoves = before.GetLegalMoves().Count;
        bool sacrifice = IsSacrifice(before, move);
        // The engine's own choice counts as best, unless deeper analysis after it disagrees a lot.
        bool isBest = evalBefore.BestMove is Move b && b == move && loss < GoodBelow || loss < 0.5;

        MoveLabel label;
        if (isBook) label = MoveLabel.Book;
        else if (legalMoves == 1) label = MoveLabel.Best; // forced: nothing to choose
        else if (sacrifice && loss < ExcellentBelow && winAfter >= 50 && winBefore < 95) label = MoveLabel.Brilliant;
        else if (loss < ExcellentBelow && evalBefore.SecondScore is EvalScore second
                 && winAfter - second.WinPercent >= OnlyMoveGap && winAfter >= 40 && winBefore < 97) label = MoveLabel.Great;
        else if (isBest) label = MoveLabel.Best;
        else if (loss < ExcellentBelow) label = MoveLabel.Excellent;
        else if (loss < GoodBelow) label = MoveLabel.Good;
        else if (loss >= InaccuracyBelow && opponentLastLabel is MoveLabel.Mistake or MoveLabel.Blunder && winAfter >= 35 && winBefore >= 60)
            label = MoveLabel.Miss; // the opponent handed over a chance, and this move let most of it go
        else if (loss < InaccuracyBelow) label = MoveLabel.Inaccuracy;
        else if (loss < MistakeBelow) label = MoveLabel.Mistake;
        else label = MoveLabel.Blunder;

        return new ReviewedMove(move, mover, label, winBefore, winAfter, accuracy, sacrifice);
    }

    /// <summary>Mover's win% after the move: from the game result if it ended, else the engine's view (flipped).</summary>
    private static double AfterWin(Position after, PositionEval? evalAfter)
    {
        var result = after.GetResult();
        if (result.IsOver)
        {
            if (result.Outcome == GameOutcome.Draw) return 50;
            return 100; // the mover can only have won by moving (checkmate)
        }
        return evalAfter is null ? 50 : 100 - evalAfter.Score.WinPercent;
    }

    private static int Value(PieceType t) => t switch
    {
        PieceType.Pawn => 1, PieceType.Knight => 3, PieceType.Bishop => 3, PieceType.Rook => 5, PieceType.Queen => 9, _ => 0,
    };

    /// <summary>
    /// True if the move gives up material: after it, the opponent can win at least 2 points more than
    /// the move captured (a piece left where it can be taken for free, or by something much cheaper).
    /// Only knights, bishops, rooks and queens count as sacrificed (pawns don't).
    /// </summary>
    public static bool IsSacrifice(Position before, Move move)
    {
        var mover = before.SideToMove;
        int captured = move.IsEnPassant ? 1 : Value(before[move.To].Type());
        var after = before.Clone();
        after.MakeMove(move);
        if (after.GetResult().IsOver) return false;
        return MaxLoss(after, mover) - captured >= 2;
    }

    /// <summary>The most material <paramref name="victim"/> can lose to a single capture by the side to move.</summary>
    private static int MaxLoss(Position p, Color victim)
    {
        int worst = 0;
        foreach (var m in p.GetLegalMoves())
        {
            if (!m.IsCapture || m.IsEnPassant) continue;
            int v = Value(p[m.To].Type());
            if (v < 3) continue;
            int attacker = Value(p[m.From].Type());
            // Defended: they lose their capturing piece back (a king can't take a defended piece at all).
            var test = p.Clone();
            test.MakeMove(m);
            bool defended = test.IsSquareAttacked(m.To, victim);
            int loss = defended ? v - attacker : v;
            worst = Math.Max(worst, loss);
        }
        return worst;
    }

    /// <summary>
    /// A player's accuracy over a game (0 to 100), like lichess: the average of a volatility-weighted
    /// mean and a harmonic mean of their move accuracies, so a single blunder in a sharp position
    /// counts for more than slack moves in a dead-drawn one.
    /// </summary>
    /// <param name="whiteWin">White's win% at each position of the game (start position first).</param>
    /// <param name="moves">The reviewed moves, in order.</param>
    public static double GameAccuracy(IReadOnlyList<double> whiteWin, IReadOnlyList<ReviewedMove> moves, Color player)
    {
        if (moves.Count == 0) return 100;
        int window = Math.Clamp(moves.Count / 10, 2, 8);
        double weightSum = 0, weighted = 0, inverseSum = 0;
        int n = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            if (moves[i].Mover != player) continue;
            // Volatility: how much the evaluation swings around this move.
            int lo = Math.Max(0, i + 1 - window), hi = Math.Min(whiteWin.Count - 1, Math.Max(lo + window, i + 1));
            var slice = new List<double>();
            for (int k = lo; k <= hi; k++) slice.Add(whiteWin[k]);
            double mean = slice.Average();
            double sd = Math.Sqrt(slice.Sum(x => (x - mean) * (x - mean)) / slice.Count);
            double w = Math.Clamp(sd, 0.5, 12);
            double acc = moves[i].Accuracy;
            weighted += acc * w;
            weightSum += w;
            inverseSum += 1 / Math.Max(acc, 1);
            n++;
        }
        if (n == 0) return 100;
        double harmonic = n / inverseSum;
        return Math.Clamp((weighted / weightSum + harmonic) / 2, 0, 100);
    }
}
