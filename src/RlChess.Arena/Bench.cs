using System.Diagnostics;
using RlChess.Engine;
using RlChess.Engine.Players;
using RlChess.Players;

/// <summary>
/// "bench": how fast does a player choose its moves? Times it on a fixed, varied set of positions
/// (the same set every run with the same seed, so results are comparable between versions of your code).
/// </summary>
static class Bench
{
    public static int Run(string[] args)
    {
        string spec = args[0];
        int count = 50, seed = 1;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--positions" when i + 1 < args.Length: count = Math.Max(1, int.Parse(args[++i])); break;
                case "--seed" when i + 1 < args.Length: seed = int.Parse(args[++i]); break;
                default: Console.Error.WriteLine($"Unknown bench option '{args[i]}'."); return 1;
            }
        }
        if (!PlayerRegistry.TryParse(spec, out var entry, out var level, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        var positions = MakePositions(count, seed);
        var player = entry.CreatePlayer(level);
        Console.WriteLine($"Bench: {entry.Label(level)} on {positions.Count} positions (seed {seed}).");

        // One warm-up move so start-up costs (e.g. loading a model) don't count.
        player.ChooseMove(positions[0].Clone());

        var times = new List<double>();
        long searched = 0;
        var stats = player as ISearchStats;
        var total = Stopwatch.StartNew();
        foreach (var p in positions)
        {
            var sw = Stopwatch.StartNew();
            var move = player.ChooseMove(p.Clone());
            sw.Stop();
            if (!p.IsLegal(move))
            {
                Console.Error.WriteLine($"  It chose an illegal move ({move.ToUci()}) in {p.ToFen()}");
                return 1;
            }
            times.Add(sw.Elapsed.TotalMilliseconds);
            if (stats is not null) searched += stats.PositionsSearched;
        }
        total.Stop();

        times.Sort();
        double Median() => times.Count % 2 == 1 ? times[times.Count / 2] : (times[times.Count / 2 - 1] + times[times.Count / 2]) / 2;
        Console.WriteLine($"  Time per move:     average {times.Average():0.###} ms · median {Median():0.###} ms · slowest {times[^1]:0.###} ms");
        Console.WriteLine($"  Total:             {total.Elapsed.TotalSeconds:0.00} s");
        if (stats is not null)
        {
            double perSec = searched / Math.Max(total.Elapsed.TotalSeconds, 1e-9);
            Console.WriteLine($"  Positions searched: {searched:N0} ({searched / positions.Count:N0} per move)");
            Console.WriteLine($"  Search speed:      {perSec / 1000:N0} thousand positions/sec");
        }
        else
        {
            Console.WriteLine("  (To see positions/sec, let your player implement ISearchStats: see src/RlChess.Engine/Players/ISearchStats.cs.)");
        }

        // For comparison: how fast the engine itself can generate and play moves.
        var ps = Stopwatch.StartNew();
        long nodes = Perft.Count(Position.FromFen(Position.StartFen), 5);
        ps.Stop();
        Console.WriteLine($"  For comparison, the move generator alone does about {nodes / ps.Elapsed.TotalSeconds / 1e6:0.0} million positions/sec (perft).");
        return 0;
    }

    /// <summary>Positions from short random games: a mix of openings, middlegames and a few endgames.</summary>
    public static List<Position> MakePositions(int count, int seed)
    {
        var random = new Random(seed);
        var list = new List<Position>();
        while (list.Count < count)
        {
            var p = Position.FromFen(Position.StartFen);
            int plies = random.Next(4, 70);
            for (int i = 0; i < plies; i++)
            {
                var moves = p.GetLegalMoves();
                if (moves.Count == 0) break;
                p.MakeMove(moves[random.Next(moves.Count)]);
            }
            if (p.GetResult().IsOver) continue;
            list.Add(Position.FromFen(p.ToFen()));
        }
        return list;
    }
}
