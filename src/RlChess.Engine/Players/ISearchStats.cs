namespace RlChess.Engine.Players;

/// <summary>
/// Optional: a player that searches (e.g. minimax) can report how many positions it looked at
/// while choosing its last move. If your player implements this, the Arena's <c>bench</c> command
/// shows positions per second as well as time per move.
/// </summary>
/// <example>
/// <code>
/// public sealed class MinimaxPlayer : IPlayer, ISearchStats
/// {
///     public long PositionsSearched { get; private set; }
///     public Move ChooseMove(Position position)
///     {
///         PositionsSearched = 0;
///         // ... add 1 to PositionsSearched every time the search visits a position ...
///     }
/// }
/// </code>
/// </example>
public interface ISearchStats
{
    /// <summary>Positions visited while choosing the most recent move.</summary>
    long PositionsSearched { get; }
}
