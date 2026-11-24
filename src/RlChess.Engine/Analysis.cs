using System.Diagnostics;
using RlChess.Engine.Players;

namespace RlChess.Engine;

/// <summary>How long to analyse a position for.</summary>
/// <param name="Depth">Stop at this depth (null = no depth limit).</param>
/// <param name="Time">Stop after this long (null = no time limit).</param>
/// <param name="Lines">How many best moves to score (2 lets game review spot "only moves").</param>
public sealed record AnalysisLimit(int? Depth = null, TimeSpan? Time = null, int Lines = 1);

/// <summary>
/// Something that judges positions, for the evaluation bar and game review. The website uses
/// Stockfish by default; <see cref="PlayerAnalysis"/> lets one of your own players do it instead.
/// You could also implement this directly (e.g. a pure evaluation function with a tiny search).
/// </summary>
public interface IAnalysisEngine
{
    string Name { get; }

    /// <summary>
    /// Judges <paramref name="position"/> (scores from the side to move's point of view).
    /// <paramref name="progress"/> may be called with better and better answers along the way.
    /// When cancelled, return the best answer so far (or throw <see cref="OperationCanceledException"/> if there is none).
    /// </summary>
    Task<PositionEval> AnalyseAsync(Position position, AnalysisLimit limit, Action<PositionEval>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Uses one of your players as the analysis engine: it asks the player for a move and takes the
/// score it reports (see <see cref="IReportsThinking"/>). Players that don't report a
/// <see cref="SearchInfo.Score"/> can't be used for this.
/// </summary>
public sealed class PlayerAnalysis(IPlayer player) : IAnalysisEngine
{
    public string Name => player.Name;

    /// <summary>The verdict for a position where the game is already over (checkmate = lost, draws = 0), else null.</summary>
    public static PositionEval? Finished(Position position)
    {
        var result = position.GetResult();
        if (!result.IsOver) return null;
        return new PositionEval(result.Outcome == GameOutcome.Draw ? EvalScore.FromCentipawns(0) : EvalScore.MateIn(0), null);
    }

    /// <summary>True if this player can report scores (it implements <see cref="IReportsThinking"/>).</summary>
    public static bool CanAnalyse(IPlayer p) => p is IReportsThinking;

    public async Task<PositionEval> AnalyseAsync(Position position, AnalysisLimit limit, Action<PositionEval>? progress, CancellationToken cancellationToken)
    {
        if (player is not IReportsThinking reporter) throw new InvalidOperationException($"{player.Name} doesn't report its thinking.");
        if (Finished(position) is { } done) return done;
        SearchInfo? last = null;
        reporter.Report = info =>
        {
            last = info;
            if (info.Score is EvalScore s) progress?.Invoke(new PositionEval(s, info.BestMove));
        };
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (limit.Time is TimeSpan t) cts.CancelAfter(t);
            var sw = Stopwatch.StartNew();
            Move move;
            try { move = await player.ChooseMoveAsync(position.Clone(), cts.Token); }
            catch (OperationCanceledException) when (last?.Score is not null) { move = last!.BestMove ?? Move.None; }
            var score = last?.Score ?? throw new InvalidOperationException($"{player.Name} didn't report a score.");
            return new PositionEval(score, move.IsNone ? last!.BestMove : move);
        }
        finally
        {
            reporter.Report = null;
        }
    }
}
