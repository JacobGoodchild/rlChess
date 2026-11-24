using RlChess.Engine;
using RlChess.Engine.Players;
using RlChess.Web.Game;
using ChessGame = RlChess.Engine.Game;

namespace RlChess.Web.Services;

/// <summary>The game on the Play page, kept while you visit other pages so you can carry on.</summary>
public sealed class SavedPlay
{
    public required GameSetup Setup { get; init; }
    public required GameMode Mode { get; init; }
    public required ChessGame Game { get; init; }
    public required IPlayer?[] Bots { get; init; }
    public required string[] BotLabels { get; init; }
    public ChessClock? Clock { get; init; }
    public Color Orientation { get; init; }
    public bool HistorySaved { get; init; }
    public string? WhiteName { get; init; }
    public string? BlackName { get; init; }
    public bool Paused { get; init; }
    public string Speed { get; init; } = "normal";
}

/// <summary>A finished or saved game to open on the Play page for review.</summary>
public sealed record ReviewRequest(ChessGame Game, string White, string Black, string Note);

/// <summary>State shared between pages (one instance for the whole visit).</summary>
public sealed class AppState
{
    public ArenaSession Arena { get; } = new();

    /// <summary>The Play page's game while another page is open.</summary>
    public SavedPlay? Play { get; set; }

    /// <summary>Set by the Games/Arena pages: the Play page opens this game.</summary>
    public ReviewRequest? Review { get; set; }

    /// <summary>Set by the board editor: the Play page opens the new-game dialog starting from this position.</summary>
    public string? PlayFromFen { get; set; }

    /// <summary>The board editor's position, kept between visits.</summary>
    public string? EditorFen { get; set; }
}
