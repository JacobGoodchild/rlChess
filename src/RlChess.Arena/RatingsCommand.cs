using System.Diagnostics;
using RlChess.Engine;
using RlChess.Engine.Players;
using RlChess.Players;

/// <summary>
/// "ratings": every bot plays every other bot (both colours), then each gets a rating, with
/// Random fixed at <see cref="Ratings.RandomAnchor"/>. Prints a table and writes the ratings file the
/// website shows on its bot cards.
/// </summary>
static class RatingsCommand
{
    public static int Run(string[] args)
    {
        int gamesPerPair = 20, threads = Environment.ProcessorCount, maxPlies = 600;
        string? playersArg = null, outPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            switch (args[i])
            {
                case "--games": gamesPerPair = Math.Max(2, int.Parse(Next())); break;
                case "--players": playersArg = Next(); break;
                case "--out": outPath = Next(); break;
                case "--threads": var t = Next(); threads = t == "all" ? Environment.ProcessorCount : Math.Max(1, int.Parse(t)); break;
                case "--max-plies": maxPlies = int.Parse(Next()); break;
                default: Console.Error.WriteLine($"Unknown ratings option '{args[i]}'."); return 1;
            }
        }
        if (gamesPerPair % 2 == 1) gamesPerPair++; // even, so both colours are played equally

        // Who plays: every registered bot at its default level, or the list given.
        var specs = playersArg is null
            ? PlayerRegistry.All.Select(e => e.Spec(null)).ToList()
            : playersArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var players = new List<(string Spec, PlayerRegistry.Entry Entry, int? Level, string Name)>();
        foreach (var spec in specs)
        {
            if (!PlayerRegistry.TryParse(spec, out var e, out var lvl, out var err)) { Console.Error.WriteLine(err); return 1; }
            string key = e.Spec(lvl);
            string name = e.Label(lvl);
            int copies = players.Count(p => p.Spec == key);
            if (copies > 0) name += $" #{copies + 1}"; // the same bot twice: a sanity check, both should get the same rating
            players.Add((key, e, lvl, name));
        }
        int anchor = players.FindIndex(p => p.Spec == "random");
        if (anchor < 0)
        {
            var random = PlayerRegistry.Find("random")!;
            players.Insert(0, ("random", random, null, random.Label(null)));
            anchor = 0;
        }
        if (players.Count < 2)
        {
            Console.Error.WriteLine("Only one bot to rate. Register your own bot first (see README), or try --players random,random as a check.");
            return 1;
        }

        // Every pairing, half the games with each colour.
        var schedule = new List<(int A, int B, bool AWhite)>();
        for (int a = 0; a < players.Count; a++)
            for (int b = a + 1; b < players.Count; b++)
                for (int g = 0; g < gamesPerPair; g++)
                    schedule.Add((a, b, g % 2 == 0));

        Console.WriteLine($"Ratings: {players.Count} players, {gamesPerPair} games per pairing, {schedule.Count} games in all, {threads} thread(s).");
        var results = new (int A, int B, double ScoreA)[schedule.Count];
        int done = 0;
        var sw = Stopwatch.StartNew();
        Parallel.For(0, schedule.Count, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
        {
            var (a, b, aWhite) = schedule[i];
            IPlayer pa = players[a].Entry.CreatePlayer(players[a].Level), pb = players[b].Entry.CreatePlayer(players[b].Level);
            var game = MatchRunner.PlayGame(aWhite ? pa : pb, aWhite ? pb : pa, maxPlies: maxPlies, recordSan: false);
            results[i] = (a, b, MatchStats.ScoreFor(game.Result, aWhite));
            int n = Interlocked.Increment(ref done);
            if (schedule.Count >= 20 && n % Math.Max(1, schedule.Count / 10) == 0) Console.WriteLine($"  {n}/{schedule.Count} games...");
        });

        var rated = Ratings.Compute(players.Count, results, anchor);
        Console.WriteLine();
        Console.WriteLine($"  {"#",-3} {"Player",-32} {"Rating",7} {"±",5} {"Games",6} {"Score",6}");
        int rank = 1;
        foreach (var r in rated.OrderByDescending(r => r.Rating))
        {
            string margin = r.Index == anchor ? "fixed" : double.IsFinite(r.Margin) ? r.Margin.ToString("0") : "?";
            Console.WriteLine($"  {rank++,-3} {players[r.Index].Name,-32} {r.Rating,7:0} {margin,5} {r.Games,6} {r.Score * 100,5:0}%");
        }
        Console.WriteLine($"  Random is fixed at {Ratings.RandomAnchor:0} so ratings stay comparable between runs. ({sw.Elapsed.TotalSeconds:0.0} s)");

        var file = new RatingsFile
        {
            GamesPerPairing = gamesPerPair,
            Players = rated.Where(r => players.FindIndex(p => p.Spec == players[r.Index].Spec) == r.Index) // first copy only
                .OrderByDescending(r => r.Rating)
                .Select(r => new RatingsFile.Row(players[r.Index].Spec, players[r.Index].Name, r.Rating, r.Margin, r.Games, r.Score))
                .ToList(),
        };
        outPath ??= Directory.Exists(Path.Combine("src", "RlChess.Web", "wwwroot"))
            ? Path.Combine("src", "RlChess.Web", "wwwroot", "ratings.json")
            : "ratings.json";
        File.WriteAllText(outPath, file.ToJson());
        Console.WriteLine($"  Saved to {Path.GetFullPath(outPath)}" +
            (outPath.Contains("wwwroot") ? " (the website shows these after you push)" : ""));
        return 0;
    }
}
