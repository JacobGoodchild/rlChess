using RlChess.Engine;

namespace RlChess.Web.Game;

/// <summary>How each game-review label looks: a short symbol in a coloured circle (our own design).</summary>
public static class Badges
{
    public static string Css(MoveLabel l) => "badge-" + l.ToString().ToLowerInvariant();

    public static string Glyph(MoveLabel l) => l switch
    {
        MoveLabel.Brilliant => "!!",
        MoveLabel.Great => "!",
        MoveLabel.Best => "★",
        MoveLabel.Excellent => "✓",
        MoveLabel.Good => "✓",
        MoveLabel.Book => "≡",
        MoveLabel.Inaccuracy => "?!",
        MoveLabel.Mistake => "?",
        MoveLabel.Miss => "✕",
        MoveLabel.Blunder => "??",
        _ => "",
    };

    /// <summary>One line explaining the label (tooltips and the summary).</summary>
    public static string Meaning(MoveLabel l) => l switch
    {
        MoveLabel.Brilliant => "A sound sacrifice: gives up material and is still the best move or close to it",
        MoveLabel.Great => "The only good move: every other move was much worse",
        MoveLabel.Best => "The engine's top choice",
        MoveLabel.Excellent => "Almost as good as the best move",
        MoveLabel.Good => "A decent move that gives away a little",
        MoveLabel.Book => "A known opening move",
        MoveLabel.Inaccuracy => "Gives away some of your chances",
        MoveLabel.Mistake => "Gives away a lot",
        MoveLabel.Miss => "The opponent made an error and this move didn't take the chance",
        MoveLabel.Blunder => "Throws away a big part of your chances",
        _ => "",
    };

    /// <summary>Order in the summary table.</summary>
    public static readonly MoveLabel[] Order =
    [
        MoveLabel.Brilliant, MoveLabel.Great, MoveLabel.Best, MoveLabel.Excellent, MoveLabel.Good, MoveLabel.Book,
        MoveLabel.Inaccuracy, MoveLabel.Mistake, MoveLabel.Miss, MoveLabel.Blunder,
    ];
}
