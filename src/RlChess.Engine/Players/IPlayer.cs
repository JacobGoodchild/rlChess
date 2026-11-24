namespace RlChess.Engine.Players;

/// <summary>
/// A computer (or any) player. This is the plug-in point for your own AI.
///
/// <see cref="ChooseMove"/> receives a private copy of the current position, so you can
/// freely call MakeMove/UndoMove on it while searching. It must return one of the legal
/// moves of that position (e.g. from <c>position.GetLegalMoves()</c>).
/// </summary>
public interface IPlayer
{
    /// <summary>Shown on the website and in Arena results.</summary>
    string Name { get; }

    Move ChooseMove(Position position);
}
