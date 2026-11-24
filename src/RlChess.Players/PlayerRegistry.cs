using RlChess.Engine.Players;

namespace RlChess.Players;

/// <summary>How a player is grouped on the website.</summary>
public enum PlayerCategory
{
    /// <summary>Plays at random (a baseline to test against).</summary>
    Random,
    /// <summary>Search-based players, e.g. minimax / alpha-beta.</summary>
    Computer,
    /// <summary>Learned players, e.g. trained by reinforcement learning.</summary>
    AI,
}

/// <summary>The difficulty levels a player supports, e.g. 1 to 10, starting at <see cref="Default"/>.</summary>
public sealed record LevelRange(int Min, int Max, int Default)
{
    public int Clamp(int level) => Math.Clamp(level, Min, Max);
}

/// <summary>
/// The list of computer players offered by the website and the Arena.
/// To add your own player, add one line to <see cref="All"/>.
///
/// Your player classes can live in this project, or in their own project that this one references
/// (e.g. one with machine-learning packages). The engine itself stays free of players.
/// </summary>
public static class PlayerRegistry
{
    /// <param name="Id">Short name used by the Arena (<c>--p1 id</c> or <c>--p1 id:level</c>) and saved settings.</param>
    /// <param name="DisplayName">Shown on the website.</param>
    /// <param name="Description">One short line shown on the website.</param>
    /// <param name="Category">Which group it appears in.</param>
    /// <param name="Create">Makes a fresh player. Receives the chosen level (players without levels can ignore it).</param>
    /// <param name="Levels">The levels it supports, or null for none (then no slider is shown).</param>
    public sealed record Entry(
        string Id,
        string DisplayName,
        string Description,
        PlayerCategory Category,
        Func<int, IPlayer> Create,
        LevelRange? Levels = null)
    {
        /// <summary>Creates the player at <paramref name="level"/> (clamped to its range; default if null).</summary>
        public IPlayer CreatePlayer(int? level = null) =>
            Create(Levels is null ? 0 : Levels.Clamp(level ?? Levels.Default));

        /// <summary>"Minimax" or "Minimax (level 4)".</summary>
        public string Label(int? level) =>
            Levels is null ? DisplayName : $"{DisplayName} (level {Levels.Clamp(level ?? Levels.Default)})";
    }

    public static readonly IReadOnlyList<Entry> All =
    [
        new("random", "Random", "Plays a random legal move.", PlayerCategory.Random, _ => new RandomPlayer()),

        // Examples of what your own players could look like:
        // new("minimax", "Minimax", "Alpha-beta search.", PlayerCategory.Computer,
        //     level => new MinimaxPlayer(depth: level), new LevelRange(1, 10, 4)),
        // new("rl", "RL network", "Trained by self-play.", PlayerCategory.AI,
        //     level => new RlPlayer($"models/generation-{level}.onnx"), new LevelRange(1, 20, 20)),
    ];

    public static Entry? Find(string id) =>
        All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Players in one category, in registry order.</summary>
    public static IEnumerable<Entry> InCategory(PlayerCategory category) => All.Where(e => e.Category == category);

    /// <summary>
    /// Parses "id" or "id:level" (e.g. "minimax:4"). The level is optional and is clamped to
    /// the player's range; giving a level to a player without levels is an error.
    /// </summary>
    public static bool TryParse(string spec, out Entry entry, out int? level, out string error)
    {
        entry = null!;
        level = null;
        var parts = spec.Split(':', 2);
        var found = Find(parts[0].Trim());
        if (found is null)
        {
            error = $"Unknown player '{parts[0]}'. Known: {string.Join(", ", All.Select(e => e.Id))}";
            return false;
        }
        entry = found;
        if (parts.Length == 2)
        {
            if (found.Levels is null)
            {
                error = $"'{found.Id}' doesn't have levels.";
                return false;
            }
            if (!int.TryParse(parts[1], out var l))
            {
                error = $"'{parts[1]}' isn't a level number.";
                return false;
            }
            level = found.Levels.Clamp(l);
        }
        error = "";
        return true;
    }
}
