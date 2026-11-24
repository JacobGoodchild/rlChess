using RlChess.Engine;
using ChessGame = RlChess.Engine.Game;

namespace RlChess.Web.Services;

/// <summary>A finished game review: the engine's view of every position, and every move labelled.</summary>
public sealed class GameReviewResult
{
    public required string Engine { get; init; }
    /// <summary>One per position (start position first), from the side to move's point of view.</summary>
    public required List<PositionEval> Evals { get; init; }
    public required List<ReviewedMove> Moves { get; init; }
    /// <summary>White's win% at each position (for the graph).</summary>
    public required List<double> WhiteWin { get; init; }
    public double WhiteAccuracy { get; init; }
    public double BlackAccuracy { get; init; }

    public int Count(Color c, MoveLabel l) => Moves.Count(m => m.Mover == c && m.Label == l);

    /// <summary>The score at a position from White's point of view.</summary>
    public EvalScore WhiteScore(ChessGame game, int ply) => Evals[ply].Score.ForWhite(game.PositionAt(ply).SideToMove);
}

/// <summary>Runs a game review: analyses each position in turn, then labels the moves.</summary>
public static class GameReviewer
{
    /// <param name="progress">Called with (positions done, total) as it goes.</param>
    public static async Task<GameReviewResult> Run(ChessGame game, IAnalysisEngine engine, AnalysisLimit limit, OpeningBook? book,
        Action<int, int> progress, CancellationToken cancellationToken)
    {
        int n = game.Moves.Count;
        var evals = new List<PositionEval>(n + 1);
        var position = Position.FromFen(game.StartFen);
        for (int ply = 0; ply <= n; ply++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress(ply, n + 1);
            evals.Add(await engine.AnalyseAsync(position, limit, null, cancellationToken));
            await Task.Yield();
            if (ply < n) position.MakeMove(game.Moves[ply]);
        }
        progress(n + 1, n + 1);

        int bookPlies = book?.BookPlies(game) ?? 0;
        var moves = new List<ReviewedMove>(n);
        position = Position.FromFen(game.StartFen);
        for (int ply = 0; ply < n; ply++)
        {
            var last = ply > 0 ? moves[ply - 1].Label : MoveLabel.None;
            moves.Add(MoveReview.Classify(position, game.Moves[ply], evals[ply], evals[ply + 1], isBook: ply < bookPlies, opponentLastLabel: last));
            position.MakeMove(game.Moves[ply]);
        }

        var whiteWin = new List<double>(n + 1);
        position = Position.FromFen(game.StartFen);
        for (int ply = 0; ply <= n; ply++)
        {
            double w = evals[ply].Score.WinPercent;
            whiteWin.Add(position.SideToMove == Color.White ? w : 100 - w);
            if (ply < n) position.MakeMove(game.Moves[ply]);
        }
        // A game decided by resignation, time, etc. keeps the engine's view of the final position.

        return new GameReviewResult
        {
            Engine = engine.Name,
            Evals = evals,
            Moves = moves,
            WhiteWin = whiteWin,
            WhiteAccuracy = MoveReview.GameAccuracy(whiteWin, moves, Color.White),
            BlackAccuracy = MoveReview.GameAccuracy(whiteWin, moves, Color.Black),
        };
    }
}
