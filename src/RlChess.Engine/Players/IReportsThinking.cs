namespace RlChess.Engine.Players;

/// <summary>
/// What a player is thinking, as much as it wants to share. Every field is optional:
/// fill in what your player knows (e.g. just <see cref="Depth"/> and <see cref="PositionsSearched"/>).
/// </summary>
public sealed record SearchInfo
{
    /// <summary>How many moves ahead it has looked (e.g. the minimax depth just finished).</summary>
    public int? Depth { get; init; }
    /// <summary>Positions looked at so far for this move.</summary>
    public long? PositionsSearched { get; init; }
    /// <summary>The move it would play right now.</summary>
    public Move? BestMove { get; init; }
    /// <summary>The line it expects: its move, the reply, its next move, ... (the "principal variation").</summary>
    public IReadOnlyList<Move>? Line { get; init; }
    /// <summary>How good it thinks the position is for the side to move.</summary>
    public EvalScore? Score { get; init; }
    /// <summary>Anything else worth showing, in a few words (optional).</summary>
    public string? Note { get; init; }

    /// <summary>Filled in by the website/Arena: time since this move's thinking started.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Positions per second, when both positions and time are known.</summary>
    public double? PositionsPerSecond =>
        PositionsSearched is long n && Elapsed.TotalSeconds > 0.001 ? n / Elapsed.TotalSeconds : null;
}

/// <summary>
/// Optional: a player that tells the website and Arena what it's thinking while it chooses a move.
/// The host sets <see cref="Report"/> before asking for a move (it may be null: then nobody is
/// listening, so always call it as <c>Report?.Invoke(...)</c>). Call it as often as you like, for
/// example once per finished depth. Players that don't implement this work exactly as before.
/// </summary>
/// <example>
/// <code>
/// public sealed class MyPlayer : IPlayer, IReportsThinking
/// {
///     public Action&lt;SearchInfo&gt;? Report { get; set; }
///     ...
///     // inside your search, e.g. after finishing each depth:
///     Report?.Invoke(new SearchInfo { Depth = depth, PositionsSearched = positions, BestMove = best });
/// }
/// </code>
/// </example>
public interface IReportsThinking
{
    Action<SearchInfo>? Report { get; set; }
}
