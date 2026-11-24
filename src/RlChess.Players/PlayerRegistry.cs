using RlChess.Engine.Players;

namespace RlChess.Players;

/// <summary>
/// The list of computer players offered by the website and the Arena.
/// To add your own player, add one line to <see cref="All"/>.
///
/// Your player classes can live in this project, or in their own project that this one references
/// (e.g. one with machine-learning packages). The engine itself stays free of players.
/// </summary>
public static class PlayerRegistry
{
    public sealed record Entry(string Id, string DisplayName, string Description, Func<IPlayer> Create);

    public static readonly IReadOnlyList<Entry> All =
    [
        new("random", "Random", "Plays a random legal move.", () => new RandomPlayer()),
        // new("minimax", "Minimax", "Alpha-beta search, depth 4.", () => new MyMinimaxPlayer()),
    ];

    public static Entry? Find(string id) =>
        All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
}
