using RlChess.Engine;
using ChessGame = RlChess.Engine.Game;

namespace RlChess.Web.Services;

/// <summary>A game saved in the browser's history.</summary>
public sealed record SavedGame(string Id, DateTime When, string Mode, string White, string Black, ChessGame Game, string Pgn)
{
    public bool Finished => Game.IsOver;
}

/// <summary>
/// Your past games, kept in this browser (newest first, up to <see cref="MaxGames"/>).
/// Each game is stored as PGN, so it's easy to export and read back.
/// </summary>
public sealed class GameHistory(Prefs prefs)
{
    public const int MaxGames = 200;
    private const string Key = "history";
    private const char Separator = '\u001e'; // "record separator": never appears in PGN
    private List<SavedGame>? _games;

    public async Task<IReadOnlyList<SavedGame>> All()
    {
        if (_games is not null) return _games;
        await prefs.LoadAsync();
        var text = await prefs.Get(Key) ?? "";
        _games = new List<SavedGame>();
        foreach (var record in text.Split(Separator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { _games.Add(FromPgn(record)); }
            catch (FormatException e) { Console.WriteLine($"[rlChess] Skipped a saved game that couldn't be read: {e.Message}"); }
        }
        return _games;
    }

    public async Task<SavedGame> Add(ChessGame game, string white, string black, string mode)
    {
        var list = (List<SavedGame>)await All();
        var now = DateTime.Now;
        string id = now.Ticks.ToString();
        string pgn = game.ToPgn(white, black, "rlChess game", now, [("Mode", mode), ("Time", now.ToString("HH:mm")), ("Id", id)]);
        var saved = new SavedGame(id, now, mode, white, black, game, pgn);
        list.Insert(0, saved);
        if (list.Count > MaxGames) list.RemoveRange(MaxGames, list.Count - MaxGames);
        await Save();
        return saved;
    }

    public async Task Remove(string id)
    {
        var list = (List<SavedGame>)await All();
        list.RemoveAll(g => g.Id == id);
        await Save();
    }

    public async Task Clear()
    {
        _games = new List<SavedGame>();
        await Save();
    }

    /// <summary>Every saved game in one PGN text (oldest first), e.g. to download.</summary>
    public async Task<string> ExportPgn() => string.Join("\n", (await All()).Reverse().Select(g => g.Pgn));

    private Task Save() => prefs.Set(Key, string.Join(Separator, _games!.Select(g => g.Pgn)));

    private static SavedGame FromPgn(string pgn)
    {
        var parsed = Pgn.Parse(pgn);
        var t = parsed.Tags;
        var date = DateTime.TryParseExact($"{t.GetValueOrDefault("Date")} {t.GetValueOrDefault("Time", "00:00")}", "yyyy.MM.dd HH:mm",
            null, System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.MinValue;
        return new SavedGame(t.GetValueOrDefault("Id", Guid.NewGuid().ToString("N")), date, t.GetValueOrDefault("Mode", ""),
            t.GetValueOrDefault("White", "?"), t.GetValueOrDefault("Black", "?"), parsed.Game, pgn);
    }
}
