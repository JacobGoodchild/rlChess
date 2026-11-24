namespace RlChess.Engine;

/// <summary>
/// "Perft" counts every possible sequence of legal moves to a given depth. Comparing the counts
/// with published numbers is the standard way to prove a move generator is correct.
/// </summary>
public static class Perft
{
    public static long Count(Position position, int depth)
    {
        if (depth == 0) return 1;
        Span<Move> moves = stackalloc Move[Position.MaxMoves];
        int n = position.GenerateLegalMoves(moves);
        if (depth == 1) return n;
        long total = 0;
        for (int i = 0; i < n; i++)
        {
            position.MakeMove(moves[i]);
            total += Count(position, depth - 1);
            position.UndoMove();
        }
        return total;
    }

    /// <summary>Per-move breakdown ("divide"), useful for tracking down a wrong count.</summary>
    public static IReadOnlyList<(Move Move, long Nodes)> Divide(Position position, int depth)
    {
        var result = new List<(Move, long)>();
        foreach (var m in position.GetLegalMoves())
        {
            position.MakeMove(m);
            result.Add((m, Count(position, depth - 1)));
            position.UndoMove();
        }
        return result;
    }
}
