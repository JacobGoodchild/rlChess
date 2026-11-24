using Microsoft.JSInterop;
using RlChess.Web.Game;

namespace RlChess.Web.Services;

/// <summary>
/// The person's settings (board, pieces, sound, animation, ...), saved in the browser and shared
/// by every page. Also owns the browser-side helper module (board.js) for sounds and storage.
/// </summary>
public sealed class Prefs(IJSRuntime js)
{
    private Task? _loading;

    /// <summary>The board.js module (available after <see cref="LoadAsync"/>).</summary>
    public IJSObjectReference? Module { get; private set; }

    public string BoardTheme { get; private set; } = "brown";
    public string PieceSet { get; private set; } = "cburnett";
    public bool Muted { get; private set; }
    /// <summary>0 to 100.</summary>
    public int Volume { get; private set; } = 80;
    /// <summary>"off", "fast", "normal" or "slow".</summary>
    public string Animation { get; private set; } = "normal";
    /// <summary>Show where a piece can go when the mouse hovers over it.</summary>
    public bool HoverDots { get; private set; } = true;
    /// <summary>Per computer move, for games without a clock (null = no limit).</summary>
    public TimeSpan? ThinkLimit { get; private set; } = TimeSpan.FromSeconds(10);

    public bool Loaded { get; private set; }

    /// <summary>Raised after any setting changes (the layout re-renders with the new theme).</summary>
    public event Action? Changed;

    public Task LoadAsync() => _loading ??= Load();

    private async Task Load()
    {
        Module = await js.InvokeAsync<IJSObjectReference>("import", "./js/board.js");
        await Module.InvokeVoidAsync("preloadSounds", (object)new[] { "move", "capture", "check", "gameover" });
        Muted = await Get("muted") == "1";
        var board = await Get("board");
        if (Settings.BoardThemes.Any(t => t.Name == board)) BoardTheme = board!;
        var pieces = await Get("pieces");
        if (Settings.PieceSets.Any(p => p.Id == pieces)) PieceSet = pieces!;
        if (int.TryParse(await Get("volume"), out var v)) Volume = Math.Clamp(v, 0, 100);
        var anim = await Get("animation");
        if (Settings.AnimationSpeeds.Any(a => a.Key == anim)) Animation = anim!;
        HoverDots = await Get("hoverdots") != "0";
        ThinkLimit = Settings.ParseThinkLimit(await Get("thinklimit"));
        Loaded = true;
        Changed?.Invoke();
    }

    public Task SetBoardTheme(string v) { BoardTheme = v; return Save("board", v); }
    public Task SetPieceSet(string v) { PieceSet = v; return Save("pieces", v); }
    public Task SetMuted(bool v) { Muted = v; return Save("muted", v ? "1" : "0"); }
    public Task SetVolume(int v) { Volume = Math.Clamp(v, 0, 100); if (Volume > 0) Muted = false; return Save("volume", Volume.ToString()); }
    public Task SetAnimation(string v) { Animation = v; return Save("animation", v); }
    public Task SetHoverDots(bool v) { HoverDots = v; return Save("hoverdots", v ? "1" : "0"); }
    public Task SetThinkLimit(TimeSpan? v) { ThinkLimit = v; return Save("thinklimit", Settings.FormatThinkLimit(v)); }

    private async Task Save(string key, string value)
    {
        Changed?.Invoke();
        await Set(key, value);
    }

    /// <summary>Raw access to the browser's saved values (keys are prefixed with "rlchess.").</summary>
    public async Task<string?> Get(string key) => Module is null ? null : await Module.InvokeAsync<string?>("getSetting", key);

    public async Task Set(string key, string value)
    {
        if (Module is not null) await Module.InvokeVoidAsync("setSetting", key, value);
    }

    public async Task PlaySound(string name, int delayMs = 0)
    {
        if (Muted || Volume == 0 || Module is null) return;
        if (delayMs > 0) await Task.Delay(delayMs);
        await Module.InvokeVoidAsync("playSound", name, Volume / 100.0);
    }

    public async Task<bool> CopyText(string text) => Module is not null && await Module.InvokeAsync<bool>("copyText", text);

    public async Task Download(string fileName, string text, string type = "application/x-chess-pgn")
    {
        if (Module is not null) await Module.InvokeVoidAsync("downloadText", fileName, text, type);
    }
}
