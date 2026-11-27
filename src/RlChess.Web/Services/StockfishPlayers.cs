using Microsoft.JSInterop;
using RlChess.Engine;
using RlChess.Engine.Players;

namespace RlChess.Web.Services;

/// <summary>
/// Background Stockfish workers for Stockfish opponents (separate from the one used for analysis).
/// A couple of workers are shared by every Stockfish player, so a website Arena with many boards
/// doesn't start dozens of engines; moves simply wait their turn.
/// </summary>
public sealed class StockfishPool(IJSRuntime js)
{
    public const int Size = 2;
    private readonly SemaphoreSlim _free = new(Size, Size);
    private readonly Stack<StockfishEngine> _idle = new();
    private int _created;

    public async Task<T> Use<T>(Func<StockfishEngine, Task<T>> work, CancellationToken cancellationToken)
    {
        await _free.WaitAsync(cancellationToken);
        StockfishEngine engine;
        lock (_idle) engine = _idle.Count > 0 ? _idle.Pop() : CreateEngine();
        try { return await work(engine); }
        finally
        {
            if (engine.Error is null) lock (_idle) _idle.Push(engine);
            _free.Release();
        }
    }

    private StockfishEngine CreateEngine()
    {
        _created++;
        return new StockfishEngine(js);
    }
}

/// <summary>Stockfish as an opponent on the website (a reference to measure your bots against).</summary>
public sealed class WebStockfishPlayer(StockfishPool pool, StockfishStrength strength) : IAsyncPlayer, IReportsThinking
{
    public string Name => strength.ToString();
    public Action<SearchInfo>? Report { get; set; }

    public Move ChooseMove(Position position) =>
        throw new NotSupportedException("On the website Stockfish only plays through ChooseMoveAsync.");

    public Task<Move> ChooseMoveAsync(Position position, CancellationToken cancellationToken) =>
        pool.Use(engine => engine.PlayAsync(position, strength, e =>
            Report?.Invoke(new SearchInfo { Depth = engine.Depth, Score = e.Score, BestMove = e.BestMove })
        , cancellationToken), cancellationToken);
}
