using System.Diagnostics;
using RlChess.Engine;
using RlChess.Engine.Players;
using RlChess.Web.Game;
using ChessGame = RlChess.Engine.Game;

namespace RlChess.Web.Services;

/// <summary>One game being played on one of the Arena page's boards.</summary>
public sealed class ArenaBoard
{
    public int Slot { get; init; }
    /// <summary>Game number in the match, from 1.</summary>
    public int Number { get; set; }
    public ChessGame Game { get; set; } = new();
    public bool P1White { get; set; }
    public PieceTracker Tracker { get; } = new();
    public bool Active { get; set; }
}

/// <summary>A finished Arena game.</summary>
public sealed record ArenaResult(int Number, ChessGame Game, bool P1White, string White, string Black);

/// <summary>
/// A match on the website's Arena page: many games between two computer players, several boards
/// at once, with a running score. It lives in a service (not the page), so the results are still
/// there after visiting another page. It pauses when you leave the Arena page.
/// </summary>
public sealed class ArenaSession
{
    public BotChoice P1 { get; set; } = new();
    public BotChoice P2 { get; set; } = new();
    public int TotalGames { get; set; } = 20;
    public int BoardCount { get; set; } = 6;
    /// <summary>"slow", "normal", "fast" or "max".</summary>
    public string Speed { get; set; } = "normal";
    /// <summary>Half-moves before a game is called a draw.</summary>
    public int MaxPlies { get; set; } = 600;
    public string? StartFen { get; set; }
    public TimeSpan? ThinkLimit { get; set; } = TimeSpan.FromSeconds(10);

    // Labels and settings frozen when the match starts.
    public string P1Label { get; private set; } = "";
    public string P2Label { get; private set; } = "";
    private BotChoice _p1 = new(), _p2 = new();

    public List<ArenaBoard> Boards { get; } = new();
    public List<ArenaResult> Results { get; } = new();
    public MatchStats Stats { get; private set; } = new();
    public bool Started { get; private set; }
    public bool Paused { get; private set; }
    public bool Finished => Started && Results.Count >= _total;
    public string? Error { get; private set; }
    /// <summary>The match was paused because the person went to another page.</summary>
    public bool PausedByLeaving { get; set; }
    public int GamesStarted => _nextGame;
    public TimeSpan Elapsed => _clock.Elapsed;

    /// <summary>Goes up whenever something visible changes (the page re-renders when it does).</summary>
    public int Version { get; private set; }

    private int _total;
    private int _nextGame;
    private int _runId;
    private CancellationTokenSource _cts = new();
    private TaskCompletionSource _resume = Completed();
    private readonly Stopwatch _clock = new();

    private static TaskCompletionSource Completed() { var t = new TaskCompletionSource(); t.SetResult(); return t; }

    public void Start()
    {
        Stop();
        _runId++;
        _cts = new CancellationTokenSource();
        _resume = Completed();
        _p1 = P1.Clone();
        _p2 = P2.Clone();
        P1Label = _p1.Label;
        P2Label = _p2.Label;
        if (P1Label == P2Label) { P1Label += " (P1)"; P2Label += " (P2)"; }
        _total = Math.Max(1, TotalGames);
        _nextGame = 0;
        Results.Clear();
        Stats = new MatchStats();
        Error = null;
        Started = true;
        Paused = false;
        Boards.Clear();
        int boards = Math.Clamp(BoardCount, 1, _total);
        for (int i = 0; i < boards; i++) Boards.Add(new ArenaBoard { Slot = i });
        _clock.Restart();
        foreach (var b in Boards) _ = RunSlot(b, _runId, _cts.Token);
        Version++;
    }

    /// <summary>Ends the match (results so far are kept until the next start).</summary>
    public void Stop()
    {
        _cts.Cancel();
        _resume.TrySetResult();
        _clock.Stop();
        foreach (var b in Boards) b.Active = false;
        Paused = false;
        if (Started && !Finished) _total = Results.Count; // what was played is the match
        Version++;
    }

    public void Reset()
    {
        Stop();
        Started = false;
        Boards.Clear();
        Results.Clear();
        Stats = new MatchStats();
        Version++;
    }

    public void Pause()
    {
        if (!Started || Finished || Paused) return;
        Paused = true;
        _resume = new TaskCompletionSource();
        _clock.Stop();
        Version++;
    }

    public void Resume()
    {
        if (!Paused) return;
        Paused = false;
        _clock.Start();
        _resume.TrySetResult();
        Version++;
    }

    public int Total => _total;
    public bool Running => Started && !Finished && !Paused && Error is null && !_cts.IsCancellationRequested;

    private int DelayMs => Speed switch { "slow" => 900, "normal" => 300, "fast" => 60, _ => 0 };

    private async Task RunSlot(ArenaBoard board, int runId, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _nextGame < _total)
            {
                int index = _nextGame++;
                bool p1White = index % 2 == 0; // colours alternate, like the console Arena
                var p1 = _p1.Entry.CreatePlayer(_p1.EffectiveLevel);
                var p2 = _p2.Entry.CreatePlayer(_p2.EffectiveLevel);
                var white = p1White ? p1 : p2;
                var black = p1White ? p2 : p1;
                board.Number = index + 1;
                board.P1White = p1White;
                board.Game = new ChessGame(StartFen);
                board.Tracker.Reset(board.Game.Position);
                board.Active = true;
                Version++;

                await PlayOut(board, white, black, token);
                if (token.IsCancellationRequested || runId != _runId) return;

                Results.Add(new ArenaResult(board.Number, board.Game, p1White,
                    p1White ? P1Label : P2Label, p1White ? P2Label : P1Label));
                Stats.Add(board.Game.Result, p1White);
                Version++;
                // Let the final position show for a moment before the next game starts on this board.
                await Task.Delay(Speed == "max" ? 1 : Math.Max(DelayMs * 2, 400), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (runId == _runId)
        {
            Console.WriteLine($"[rlChess] Arena: {e}");
            Error = e.Message;
            _cts.Cancel();
            _clock.Stop();
        }
        finally
        {
            board.Active = false;
            if (runId == _runId)
            {
                if (Finished) _clock.Stop();
                Version++;
            }
        }
    }

    private async Task PlayOut(ArenaBoard board, IPlayer white, IPlayer black, CancellationToken token)
    {
        var game = board.Game;
        int sinceYield = 0;
        while (!game.IsOver)
        {
            if (Paused) await _resume.Task;
            token.ThrowIfCancellationRequested();
            if (game.Moves.Count >= MaxPlies)
            {
                game.End(GameResult.Draw(GameEndReason.MoveLimit));
                break;
            }
            var side = game.SideToMove;
            var player = side == Color.White ? white : black;
            var move = await ChooseMove(player, game.Position.Clone(), token);
            if (move is null)
            {
                game.End(GameResult.Win(side.Opponent(), GameEndReason.ThinkTimeExceeded));
                break;
            }
            if (!game.Position.IsLegal(move.Value))
                throw new InvalidOperationException($"{player.Name} chose an illegal move ({move.Value.ToUci()}) in {game.Position.ToFen()}");
            var before = game.Position.Clone();
            game.Play(move.Value);
            board.Tracker.Apply(before, move.Value);
            Version++;

            int delay = DelayMs;
            if (delay > 0) await Task.Delay(delay, token);
            else if (++sinceYield >= 8) { sinceYield = 0; await Task.Delay(1, token); } // let the page draw
        }
    }

    /// <summary>The player's move, or null if it went over the think-time limit.</summary>
    private async Task<Move?> ChooseMove(IPlayer player, Position position, CancellationToken token)
    {
        var limit = ThinkLimit;
        using var thinkCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var sw = Stopwatch.StartNew();
        Task<Move> task;
        try { task = player.ChooseMoveAsync(position, thinkCts.Token); }
        catch (Exception e) when (e is not OperationCanceledException) { task = Task.FromException<Move>(e); }

        if (limit is TimeSpan lim && !task.IsCompleted)
        {
            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var first = await Task.WhenAny(task, Task.Delay(lim + TimeSpan.FromMilliseconds(30), delayCts.Token));
            delayCts.Cancel();
            token.ThrowIfCancellationRequested();
            if (first != task)
            {
                thinkCts.Cancel(); // ask for its best move so far, and give it a second to answer
                if (await Task.WhenAny(task, Task.Delay(1000, token)) != task || !task.IsCompletedSuccessfully) return null;
            }
        }
        var move = await task;
        if (limit is TimeSpan l && sw.Elapsed > l + TimeSpan.FromSeconds(1)) return null; // a player that can't be interrupted
        return move;
    }

    /// <summary>All finished games as PGN.</summary>
    public string ToPgn() => string.Join("\n", Results.OrderBy(r => r.Number).Select(r =>
        r.Game.ToPgn(r.White, r.Black, "rlChess Arena (website)", extraTags: [("Game", r.Number.ToString())])));

    /// <summary>All finished games as training data (see <see cref="TrainingData"/>).</summary>
    public string ToCsv() => TrainingData.ToCsv(Results.OrderBy(r => r.Number).Select(r => r.Game));
}
