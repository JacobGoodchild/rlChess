using Microsoft.JSInterop;
using RlChess.Engine;
using RlChess.Players;
using ChessGame = RlChess.Engine.Game;

namespace RlChess.Web.Services;

/// <summary>
/// The analysis engine for the evaluation bar and game review: Stockfish, or one of your own bots
/// (Settings → Advanced → Analysis by). Nothing is downloaded until it's first used.
/// </summary>
public sealed class Analysis(IJSRuntime js, Prefs prefs) : IAsyncDisposable
{
    private StockfishEngine? _stockfish;
    private (string Id, IAnalysisEngine Engine)? _bot;

    public IAnalysisEngine Engine
    {
        get
        {
            var source = prefs.AnalysisSource;
            if (source != "stockfish" && PlayerRegistry.Find(source) is { } entry && AnalysisBots.All.Contains(entry))
            {
                if (_bot?.Id != entry.Id) _bot = (entry.Id, new PlayerAnalysis(entry.CreatePlayer()));
                return _bot.Value.Engine;
            }
            return _stockfish ??= new StockfishEngine(js);
        }
    }

    public bool IsStockfish => Engine is StockfishEngine;

    /// <summary>Why the engine isn't working, if it isn't.</summary>
    public string? Error => _stockfish is { Error: { } e } && IsStockfish ? e : null;

    /// <summary>A sensible limit for one position: quick for the live bar, a bit deeper for review.</summary>
    public AnalysisLimit LimitFor(bool review) => IsStockfish
        ? (review ? new AnalysisLimit(Depth: 14, Time: TimeSpan.FromMilliseconds(1500), Lines: 2) : new AnalysisLimit(Depth: 20, Time: TimeSpan.FromSeconds(8)))
        : new AnalysisLimit(Time: prefs.ThinkLimit is { } t && t < TimeSpan.FromSeconds(2) ? t : TimeSpan.FromSeconds(1));

    /// <summary>Results of finished game reviews, by game (so coming back to a game keeps its review).</summary>
    public Dictionary<string, GameReviewResult> Reviews { get; } = new();

    public static string GameKey(ChessGame game) => game.StartFen + "|" + string.Join(" ", game.Moves.Select(m => m.ToUci()));

    public async ValueTask DisposeAsync()
    {
        if (_stockfish is not null) await _stockfish.DisposeAsync();
    }
}

/// <summary>Opening names, downloaded the first time they're needed (wwwroot/openings.tsv).</summary>
public sealed class Openings(HttpClient http)
{
    private Task<OpeningBook>? _loading;

    public OpeningBook? Book { get; private set; }

    public Task<OpeningBook> LoadAsync() => _loading ??= Load();

    private async Task<OpeningBook> Load()
    {
        try
        {
            Book = OpeningBook.Parse(await http.GetStringAsync("openings.tsv"));
        }
        catch (Exception e)
        {
            Console.WriteLine($"[rlChess] Opening names couldn't be loaded: {e.Message}");
            Book = OpeningBook.Parse("");
        }
        return Book;
    }
}

/// <summary>Bot ratings from the Arena's ratings command (wwwroot/ratings.json).</summary>
public sealed class BotRatings(HttpClient http)
{
    private Task<RatingsFile>? _loading;

    public RatingsFile? File { get; private set; }

    public Task<RatingsFile> LoadAsync() => _loading ??= Load();

    private async Task<RatingsFile> Load()
    {
        try { File = RatingsFile.Parse(await http.GetStringAsync("ratings.json")); }
        catch (Exception) { File = new RatingsFile(); }
        return File;
    }
}
