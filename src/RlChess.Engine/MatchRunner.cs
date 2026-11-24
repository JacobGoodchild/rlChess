using System.Diagnostics;
using RlChess.Engine.Players;

namespace RlChess.Engine;

/// <summary>What happened on one move of a <see cref="MatchRunner"/> game (for printing or logging).</summary>
/// <param name="Game">The game, with the move already played.</param>
/// <param name="Player">The player who moved.</param>
/// <param name="Thinking">The player's last report while choosing this move (null if it doesn't report).</param>
/// <param name="Elapsed">How long it took to choose the move.</param>
public sealed record MoveReport(Game Game, Move Move, Color Mover, IPlayer Player, SearchInfo? Thinking, TimeSpan Elapsed);

/// <summary>Plays one game between two players, start to finish.</summary>
public static class MatchRunner
{
    /// <param name="maxPlies">Safety cap; the game is drawn ("Move limit reached") if it gets this long.</param>
    /// <param name="recordSan">Keep move names (needed for PGN). Off is faster.</param>
    /// <param name="onMove">Called after every move, e.g. to print what the player was thinking.</param>
    public static Game PlayGame(IPlayer white, IPlayer black, string? startFen = null, int maxPlies = 1000, bool recordSan = true,
        Action<MoveReport>? onMove = null)
    {
        var game = new Game(startFen, recordSan);
        while (!game.IsOver)
        {
            if (game.Moves.Count >= maxPlies)
            {
                game.End(GameResult.Draw(GameEndReason.MoveLimit));
                break;
            }
            var mover = game.SideToMove;
            var player = mover == Color.White ? white : black;
            if (onMove is null)
            {
                game.Play(player.ChooseMove(game.Position.Clone()));
                continue;
            }

            SearchInfo? last = null;
            var sw = Stopwatch.StartNew();
            var reporter = player as IReportsThinking;
            if (reporter is not null) reporter.Report = info => last = info with { Elapsed = sw.Elapsed };
            Move move;
            try { move = player.ChooseMove(game.Position.Clone()); }
            finally { if (reporter is not null) reporter.Report = null; }
            sw.Stop();
            game.Play(move);
            onMove(new MoveReport(game, move, mover, player, last, sw.Elapsed));
        }
        return game;
    }
}
