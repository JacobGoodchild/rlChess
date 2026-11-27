using RlChess.Engine;
using RlChess.Players;
using ChessGame = RlChess.Engine.Game;

/// <summary>
/// "strength minimax:3 --levels 1320,1500,1700 --games 20": how strong is my bot? Plays it against
/// Stockfish at each calibrated level (both colours), then combines all the results into one
/// estimated rating on the engine scale.
/// </summary>
static class StrengthCommand
{
    public static int Run(string[] args)
    {
        string spec = args[0];
        var levels = new List<int> { 1320, 1500, 1700 };
        int games = 20, threads = Environment.ProcessorCount, maxPlies = 600;
        for (int i = 1; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            switch (args[i])
            {
                case "--levels": levels = Next().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(); break;
                case "--games": games = Math.Max(2, int.Parse(Next())); break;
                case "--threads": var t = Next(); threads = t == "all" ? Environment.ProcessorCount : Math.Max(1, int.Parse(t)); break;
                case "--max-plies": maxPlies = int.Parse(Next()); break;
                default: Console.Error.WriteLine($"Unknown strength option '{args[i]}'."); return 1;
            }
        }
        if (!PlayerRegistry.TryParse(spec, out var bot, out var botLevel, out var error)) { Console.Error.WriteLine(error); return 1; }
        var sf = PlayerRegistry.Find(PlayerRegistry.StockfishId)!;
        levels = levels.Select(l => sf.Levels!.Clamp(l)).Distinct().ToList();
        Shared.Dispose(sf.CreatePlayer(levels[0])); // finds Stockfish now (friendly message if it's missing)

        Console.WriteLine($"How strong is {bot.Label(botLevel)}? {games} games against each of: {string.Join(", ", levels.Select(l => $"Stockfish {l}"))}.");
        Console.WriteLine($"Stockfish thinks {Shared.StockfishMoveTime.TotalSeconds:0.##} s per move; {threads} game(s) at a time.");
        var perLevel = new List<RatingEstimate.Result>();
        foreach (int elo in levels)
        {
            var scores = new double[games];
            Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = threads }, g =>
            {
                var me = bot.CreatePlayer(botLevel);
                var them = sf.CreatePlayer(elo);
                bool meWhite = g % 2 == 0;
                try
                {
                    ChessGame game = MatchRunner.PlayGame(meWhite ? me : them, meWhite ? them : me, maxPlies: maxPlies, recordSan: false);
                    scores[g] = MatchStats.ScoreFor(game.Result, meWhite);
                }
                finally { Shared.Dispose(me); Shared.Dispose(them); }
            });
            var r = new RatingEstimate.Result(elo, scores.Count(s => s >= 1), scores.Count(s => s is > 0 and < 1), scores.Count(s => s <= 0));
            perLevel.Add(r);
            var one = RatingEstimate.Performance([r])!;
            Console.WriteLine($"  vs Stockfish {elo}: +{r.Wins} ={r.Draws} -{r.Losses} ({r.Score * 100:0}%)  → performance {one}");
        }
        var all = RatingEstimate.Performance(perLevel)!;
        Console.WriteLine();
        Console.WriteLine($"  Estimated rating: {all}  (95% range {all.Low:0}–{all.High:0}, from {all.Games} games)");
        if (all.OffTheScale)
            Console.WriteLine("  It won or lost every game, so the real rating could be further out: try other levels.");
        Console.WriteLine("  This is an engine-scale rating (like the CCRL lists), measured against Stockfish's UCI_Elo levels.");
        Console.WriteLine("  It is not a chess.com or lichess rating. Short think times can make Stockfish play below its label.");
        return 0;
    }
}
