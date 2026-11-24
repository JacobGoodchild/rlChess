using System.Globalization;
using Microsoft.JSInterop;
using RlChess.Engine;

namespace RlChess.Web.Services;

/// <summary>
/// Stockfish running in a background worker (see wwwroot/js/engine.js), spoken to in UCI.
/// One analysis at a time; a new request waits for the previous one to stop.
/// </summary>
public sealed class StockfishEngine(IJSRuntime js) : IAnalysisEngine, IAsyncDisposable
{
    public string Name => "Stockfish";

    private IJSObjectReference? _module, _worker;
    private DotNetObjectReference<StockfishEngine>? _self;
    private Task? _starting;
    private TaskCompletionSource? _waitFor;      // uciok / readyok
    private string _waitToken = "";
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _multiPv = 1;

    // The analysis in progress.
    private Position? _position;
    private Action<PositionEval>? _progress;
    private TaskCompletionSource<PositionEval>? _done;
    private readonly Dictionary<int, (EvalScore Score, Move? First, int Depth)> _lines = new();
    private PositionEval? _latest;

    /// <summary>Set if the engine couldn't be loaded (e.g. offline).</summary>
    public string? Error { get; private set; }

    /// <summary>Depth reached by the analysis in progress (or the last one).</summary>
    public int Depth { get; private set; }

    public bool Started => _worker is not null && Error is null;

    private Task EnsureStarted() => _starting ??= Start();

    private async Task Start()
    {
        _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/engine.js");
        _self = DotNetObjectReference.Create(this);
        _worker = await _module.InvokeAsync<IJSObjectReference>("createStockfish", _self, null);
        await Command("uci", "uciok", TimeSpan.FromSeconds(40));
        await Command("isready", "readyok", TimeSpan.FromSeconds(20));
    }

    private async Task Command(string command, string waitFor, TimeSpan timeout)
    {
        _waitFor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _waitToken = waitFor;
        await Send(command);
        var finished = await Task.WhenAny(_waitFor.Task, Task.Delay(timeout));
        if (finished != _waitFor.Task)
        {
            Error ??= "Stockfish didn't start (it may be blocked, or you're offline).";
            throw new InvalidOperationException(Error);
        }
        await _waitFor.Task;
    }

    private async Task Send(string command)
    {
        if (_worker is not null) await _worker.InvokeVoidAsync("send", command);
    }

    public async Task<PositionEval> AnalyseAsync(Position position, AnalysisLimit limit, Action<PositionEval>? progress, CancellationToken cancellationToken)
    {
        // Finished games need no engine.
        if (PlayerAnalysis.Finished(position) is { } done) return done;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (Error is not null) throw new InvalidOperationException(Error);
            await EnsureStarted();
            cancellationToken.ThrowIfCancellationRequested();
            int lines = Math.Clamp(limit.Lines, 1, 5);
            if (lines != _multiPv)
            {
                await Send($"setoption name MultiPV value {lines}");
                await Command("isready", "readyok", TimeSpan.FromSeconds(10));
                _multiPv = lines;
            }

            _position = position.Clone();
            _progress = progress;
            _lines.Clear();
            _latest = null;
            Depth = 0;
            _done = new TaskCompletionSource<PositionEval>(TaskCreationOptions.RunContinuationsAsynchronously);
            await Send($"position fen {position.ToFen()}");
            var go = "go";
            if (limit.Depth is int d) go += $" depth {d}";
            if (limit.Time is TimeSpan t) go += $" movetime {(int)t.TotalMilliseconds}";
            if (go == "go") go += " infinite";
            await Send(go);

            // Cancelling sends "stop": Stockfish then answers with its best move so far.
            await using (cancellationToken.Register(() => _ = Send("stop")))
            {
                var eval = await _done.Task;
                cancellationToken.ThrowIfCancellationRequested();
                return eval;
            }
        }
        finally
        {
            _progress = null;
            _lock.Release();
        }
    }

    [JSInvokable]
    public void OnEngineLine(string line)
    {
        if (line == _waitToken || line.StartsWith(_waitToken + " ", StringComparison.Ordinal))
        {
            _waitFor?.TrySetResult();
            return;
        }
        if (line.StartsWith("info ", StringComparison.Ordinal)) OnInfo(line);
        else if (line.StartsWith("bestmove", StringComparison.Ordinal))
        {
            var eval = _latest ?? new PositionEval(EvalScore.FromCentipawns(0), null);
            var parts = line.Split(' ');
            if (parts.Length > 1 && _position is not null && Notation.TryParseUci(_position, parts[1], out var best))
                eval = eval with { BestMove = best };
            _done?.TrySetResult(eval);
        }
    }

    [JSInvokable]
    public void OnEngineError(string message)
    {
        Error = $"Stockfish couldn't be loaded: {message}";
        Console.WriteLine($"[rlChess] {Error}");
        _waitFor?.TrySetException(new InvalidOperationException(Error));
        _done?.TrySetException(new InvalidOperationException(Error));
    }

    /// <summary>Reads "info depth 14 multipv 1 score cp 31 ... pv e2e4 e7e5 ...".</summary>
    private void OnInfo(string line)
    {
        if (_position is null || line.Contains(" lowerbound") || line.Contains(" upperbound")) return;
        var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int depth = 0, multipv = 1;
        EvalScore? score = null;
        Move? first = null;
        for (int i = 1; i < t.Length; i++)
        {
            switch (t[i])
            {
                case "depth" when i + 1 < t.Length: int.TryParse(t[++i], out depth); break;
                case "multipv" when i + 1 < t.Length: int.TryParse(t[++i], out multipv); break;
                case "score" when i + 2 < t.Length:
                    if (int.TryParse(t[i + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                        score = t[i + 1] == "mate" ? EvalScore.MateIn(v) : EvalScore.FromCentipawns(v);
                    i += 2;
                    break;
                case "pv" when i + 1 < t.Length:
                    if (Notation.TryParseUci(_position, t[i + 1], out var m)) first = m;
                    i = t.Length;
                    break;
            }
        }
        if (score is null) return;
        _lines[multipv] = (score.Value, first, depth);
        if (!_lines.TryGetValue(1, out var top)) return;
        EvalScore? second = _lines.TryGetValue(2, out var two) && two.Depth >= top.Depth - 1 ? two.Score : null;
        Depth = top.Depth;
        _latest = new PositionEval(top.Score, top.First, second);
        _progress?.Invoke(_latest);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_worker is not null) { await _worker.InvokeVoidAsync("terminate"); await _worker.DisposeAsync(); }
            if (_module is not null) await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        _self?.Dispose();
    }
}
