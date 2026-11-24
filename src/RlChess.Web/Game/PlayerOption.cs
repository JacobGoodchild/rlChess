using RlChess.Engine.Players;
using RlChess.Players;

namespace RlChess.Web.Game;

/// <summary>Who controls a side on the website: a person, or one of the computer players.</summary>
public sealed record PlayerOption(string Id, string Name, string Description, Func<IPlayer>? CreateComputer)
{
    public const string HumanId = "human";
    public bool IsHuman => CreateComputer is null;

    public static readonly PlayerOption Human = new(HumanId, "Two players", "Pass and play on this device.", null);

    /// <summary>Every computer player in the engine's PlayerRegistry shows up here automatically.</summary>
    public static IReadOnlyList<PlayerOption> Computers { get; } =
        PlayerRegistry.All.Select(e => new PlayerOption(e.Id, e.DisplayName, e.Description, e.Create)).ToList();

    public static PlayerOption Find(string? id) =>
        Computers.FirstOrDefault(c => c.Id == id) ?? (id == HumanId ? Human : Computers[0]);
}
