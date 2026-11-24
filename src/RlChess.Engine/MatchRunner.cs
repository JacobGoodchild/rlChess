using RlChess.Engine.Players;

namespace RlChess.Engine;

/// <summary>Plays one game between two players, start to finish.</summary>
public static class MatchRunner
{
    /// <param name="maxPlies">Safety cap; the game is drawn ("Move limit reached") if it gets this long.</param>
    public static Game PlayGame(IPlayer white, IPlayer black, string? startFen = null, int maxPlies = 1000)
    {
        var game = new Game(startFen);
        while (!game.IsOver)
        {
            if (game.Moves.Count >= maxPlies)
            {
                game.End(GameResult.Draw(GameEndReason.MoveLimit));
                break;
            }
            var player = game.SideToMove == Color.White ? white : black;
            var move = player.ChooseMove(game.Position.Clone());
            game.Play(move);
        }
        return game;
    }
}
