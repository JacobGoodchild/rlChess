namespace RlChess.Web.Game;

/// <summary>Choices in the settings menu.</summary>
public static class Settings
{
    public static readonly (string Name, string Light, string Dark)[] BoardThemes =
    [
        ("brown", "#ecd9b9", "#ae8968"),
        ("blue", "#dee3e6", "#8ca2ad"),
        ("green", "#eeeed2", "#769656"),
        ("grey", "#e2e2e0", "#a4a39f"),
        // Textured boards: images in wwwroot/boards (made by tools/make-boards.py); colours are for the coordinates.
        ("wood", "#e3c597", "#8f5a33"),
        ("marble", "#e6e4de", "#7f938b"),
    ];

    /// <summary>Piece sets: folder name under wwwroot/pieces, display name, credit.</summary>
    public static readonly (string Id, string Name, string Credit)[] PieceSets =
    [
        ("cburnett", "Classic", "Colin M.L. Burnett (BSD)"),
        ("fantasy", "Fantasy", "Maurizio Monge (MIT)"),
        ("spatial", "Spatial", "Maurizio Monge (MIT)"),
        ("chessnut", "Chessnut", "Alexis Luengas (Apache 2.0)"),
    ];

    /// <summary>How fast pieces slide: key (saved), label, CSS duration.</summary>
    public static readonly (string Key, string Label, string Duration)[] AnimationSpeeds =
    [
        ("off", "Off", "0s"),
        ("fast", "Fast", ".12s"),
        ("normal", "Normal", ".2s"),
        ("slow", "Slow", ".35s"),
    ];

    public static readonly TimeSpan?[] ThinkLimits =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), null];

    public static string ThinkLimitLabel(TimeSpan? t) =>
        t is null ? "No limit" : t.Value.TotalSeconds < 60 ? $"{t.Value.TotalSeconds:0} s" : $"{t.Value.TotalMinutes:0} min";

    public static string FormatThinkLimit(TimeSpan? t) => t is null ? "none" : ((int)t.Value.TotalSeconds).ToString();

    public static TimeSpan? ParseThinkLimit(string? s) =>
        s == "none" ? null : int.TryParse(s, out var secs) && secs > 0 ? TimeSpan.FromSeconds(secs) : TimeSpan.FromSeconds(10);
}
