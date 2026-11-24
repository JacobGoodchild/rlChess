using RlChess.Engine;
using RlChess.Players;

namespace RlChess.Web.Game;

public enum GameMode
{
    VsComputer,
    TwoPlayers,
    ComputerVsComputer,
}

/// <summary>A computer player picked in the new-game dialog: registry id plus level (if it has levels).</summary>
public sealed class BotChoice
{
    public string Id { get; set; } = "random";
    public int? Level { get; set; }

    public PlayerRegistry.Entry Entry => PlayerRegistry.Find(Id) ?? PlayerRegistry.All[0];
    public int? EffectiveLevel => Entry.Levels is { } r ? r.Clamp(Level ?? r.Default) : null;
    public string Label => Entry.Label(Level);
    public BotChoice Clone() => new() { Id = Id, Level = Level };

    public override string ToString() => Level is int l ? $"{Id}:{l}" : Id;

    public static BotChoice Parse(string? text)
    {
        if (text is not null && PlayerRegistry.TryParse(text, out var e, out var level, out _))
            return new BotChoice { Id = e.Id, Level = level };
        return new BotChoice();
    }
}

/// <summary>Everything chosen in the new-game dialog. Remembered between visits.</summary>
public sealed class GameSetup
{
    public GameMode Mode { get; set; } = GameMode.VsComputer;
    public BotChoice Opponent { get; set; } = new();
    /// <summary>"white", "black" or "random": the human's side against a computer.</summary>
    public string Color { get; set; } = "white";
    public BotChoice WhiteBot { get; set; } = new();
    public BotChoice BlackBot { get; set; } = new();
    /// <summary>A preset key ("none", "3+2", ...) or "custom".</summary>
    public string Clock { get; set; } = "none";
    public double CustomMinutes { get; set; } = 5;
    public int CustomIncrement { get; set; } = 3;

    public static readonly (string Key, string Label, TimeControl? Control)[] ClockPresets =
    [
        ("none", "None", null),
        ("1+0", "1+0", TimeControl.FromMinutes(1)),
        ("3+2", "3+2", TimeControl.FromMinutes(3, 2)),
        ("5+3", "5+3", TimeControl.FromMinutes(5, 3)),
        ("10+0", "10+0", TimeControl.FromMinutes(10)),
        ("custom", "Custom", null),
    ];

    public TimeControl? TimeControl => Clock == "custom"
        ? TimeControl.FromMinutes(Math.Clamp(CustomMinutes, 0.25, 180), Math.Clamp(CustomIncrement, 0, 60))
        : ClockPresets.FirstOrDefault(p => p.Key == Clock).Control;

    public GameSetup Clone() => new()
    {
        Mode = Mode, Opponent = Opponent.Clone(), Color = Color, WhiteBot = WhiteBot.Clone(), BlackBot = BlackBot.Clone(),
        Clock = Clock, CustomMinutes = CustomMinutes, CustomIncrement = CustomIncrement,
    };

    // A tiny "key=value;..." format, so no JSON (and no trimming surprises) is needed.
    public string Serialize() => string.Join(';',
        $"mode={Mode}", $"opp={Opponent}", $"color={Color}", $"white={WhiteBot}", $"black={BlackBot}",
        $"clock={Clock}", $"min={CustomMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture)}", $"inc={CustomIncrement}");

    public static GameSetup Deserialize(string? text)
    {
        var s = new GameSetup();
        if (string.IsNullOrEmpty(text)) return s;
        foreach (var part in text.Split(';'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            var v = kv[1];
            switch (kv[0])
            {
                case "mode" when Enum.TryParse<GameMode>(v, out var m): s.Mode = m; break;
                case "opp": s.Opponent = BotChoice.Parse(v); break;
                case "color" when v is "white" or "black" or "random": s.Color = v; break;
                case "white": s.WhiteBot = BotChoice.Parse(v); break;
                case "black": s.BlackBot = BotChoice.Parse(v); break;
                case "clock" when ClockPresets.Any(p => p.Key == v): s.Clock = v; break;
                case "min" when double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var mins): s.CustomMinutes = mins; break;
                case "inc" when int.TryParse(v, out var inc): s.CustomIncrement = inc; break;
            }
        }
        return s;
    }
}
