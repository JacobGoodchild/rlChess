using RlChess.Engine;

/// <summary>Small helpers shared by the Arena's commands.</summary>
static class Shared
{
    /// <summary>Stockfish's think time per move (--sf-time).</summary>
    public static TimeSpan StockfishMoveTime { get; private set; } = StockfishStrength.DefaultMoveTime;

    /// <summary>Reads and removes --stockfish-path and --sf-time, wherever they are.</summary>
    public static string[] TakeStockfishOptions(string[] args)
    {
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--stockfish-path" && i + 1 < args.Length) StockfishProcess.PathOverride = args[++i];
            else if (args[i] == "--sf-time" && i + 1 < args.Length) StockfishMoveTime = TimeSpan.FromMilliseconds(Math.Max(10, int.Parse(args[++i])));
            else rest.Add(args[i]);
        }
        return rest.ToArray();
    }

    public static void Dispose(object? player) => (player as IDisposable)?.Dispose();
}
