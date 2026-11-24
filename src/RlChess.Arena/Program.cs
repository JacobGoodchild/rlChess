using System.Diagnostics;
using System.Text;
using RlChess.Engine;
using RlChess.Engine.Players;
using ChessGame = RlChess.Engine.Game;

// rlChess Arena: plays N games between two players as fast as possible, alternating colours.
//
//   dotnet run --project src/RlChess.Arena -c Release -- --games 1000 --p1 random --p2 random --pgn games.pgn --threads all
//
// Players are looked up by id in PlayerRegistry (see src/RlChess.Engine/Players/PlayerRegistry.cs).

int games = 100;
string p1Id = "random", p2Id = "random";
string? pgnPath = null;
string? startFen = null;
int maxPlies = 1000;
int? seed = null;
int threads = 1;

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
    switch (args[i])
    {
        case "-n" or "--games": games = int.Parse(Next()); break;
        case "--p1": p1Id = Next(); break;
        case "--p2": p2Id = Next(); break;
        case "--pgn": pgnPath = Next(); break;
        case "--fen": startFen = Next(); break;
        case "--max-plies": maxPlies = int.Parse(Next()); break;
        case "--seed": seed = int.Parse(Next()); break;
        case "-t" or "--threads":
            var t = Next();
            threads = t == "all" ? Environment.ProcessorCount : int.Parse(t);
            break;
        case "-h" or "--help":
            PrintHelp();
            return 0;
        default:
            Console.Error.WriteLine($"Unknown option '{args[i]}'. Use --help.");
            return 1;
    }
}

IPlayer Create(string id, int offset)
{
    // Seeded random players get distinct, reproducible seeds (per game, so threads don't matter).
    if (seed is int s && id.Equals("random", StringComparison.OrdinalIgnoreCase)) return new RandomPlayer(unchecked(s * 1_000_003 + offset));
    var entry = PlayerRegistry.Find(id);
    if (entry is null)
        throw new ArgumentException($"Unknown player '{id}'. Known: {string.Join(", ", PlayerRegistry.All.Select(e => e.Id))}");
    return entry.Create();
}

IPlayer p1, p2;
try
{
    p1 = Create(p1Id, 0);
    p2 = Create(p2Id, 1);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

threads = Math.Clamp(threads, 1, Math.Max(1, games));
Console.WriteLine($"Arena: {p1.Name} (P1) vs {p2.Name} (P2), {games} games, alternating colours, {threads} thread(s).");

bool seededRandom(string id) => seed is not null && id.Equals("random", StringComparison.OrdinalIgnoreCase);
var results = new ChessGame[games];
int finished = 0;
var sw = Stopwatch.StartNew();

// Each worker thread gets its own player objects (players don't have to be thread-safe).
Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = threads },
    () => (P1: Create(p1Id, 0), P2: Create(p2Id, 1)),
    (g, _, local) =>
    {
        var a = seededRandom(p1Id) ? Create(p1Id, 2 * g) : local.P1;
        var b = seededRandom(p2Id) ? Create(p2Id, 2 * g + 1) : local.P2;
        bool p1White = g % 2 == 0;
        results[g] = MatchRunner.PlayGame(p1White ? a : b, p1White ? b : a, startFen, maxPlies, recordSan: pgnPath is not null);
        int done = Interlocked.Increment(ref finished);
        if (games >= 20 && done % Math.Max(1, games / 10) == 0) Console.WriteLine($"  {done}/{games} games...");
        return local;
    },
    _ => { });

int p1Wins = 0, p2Wins = 0, draws = 0;
long totalPlies = 0;
var drawReasons = new Dictionary<GameEndReason, int>();
var winReasons = new Dictionary<GameEndReason, int>();
var pgn = pgnPath is null ? null : new StringBuilder();

for (int g = 0; g < games; g++)
{
    var game = results[g];
    bool p1White = g % 2 == 0;
    totalPlies += game.Moves.Count;

    var r = game.Result;
    if (r.Outcome == GameOutcome.Draw)
    {
        draws++;
        drawReasons[r.Reason] = drawReasons.GetValueOrDefault(r.Reason) + 1;
    }
    else
    {
        bool whiteWon = r.Outcome == GameOutcome.WhiteWins;
        if (whiteWon == p1White) p1Wins++; else p2Wins++;
        winReasons[r.Reason] = winReasons.GetValueOrDefault(r.Reason) + 1;
    }

    pgn?.Append(game.ToPgn(p1White ? $"{p1.Name} (P1)" : $"{p2.Name} (P2)", p1White ? $"{p2.Name} (P2)" : $"{p1.Name} (P1)",
        eventName: "rlChess Arena")).Append('\n');
}

sw.Stop();
double secs = Math.Max(sw.Elapsed.TotalSeconds, 1e-9);

Console.WriteLine();
Console.WriteLine($"Results after {games} games:");
Console.WriteLine($"  {p1.Name} (P1) wins: {p1Wins,6}  ({100.0 * p1Wins / games:F1}%)");
Console.WriteLine($"  {p2.Name} (P2) wins: {p2Wins,6}  ({100.0 * p2Wins / games:F1}%)");
Console.WriteLine($"  Draws:            {draws,6}  ({100.0 * draws / games:F1}%)");
foreach (var (reason, count) in drawReasons.OrderByDescending(kv => kv.Value))
    Console.WriteLine($"    {GameResult.Draw(reason).ReasonText}: {count}");
foreach (var (reason, count) in winReasons.OrderByDescending(kv => kv.Value))
    Console.WriteLine($"  Decisive by {GameResult.Draw(reason).ReasonText.ToLowerInvariant()}: {count}");
Console.WriteLine($"  Average length:   {(double)totalPlies / games / 2:F1} moves ({(double)totalPlies / games:F1} half-moves)");
Console.WriteLine($"  Speed:            {games / secs:F1} games/sec ({secs:F2} s total)");

if (pgnPath is not null)
{
    File.WriteAllText(pgnPath, pgn!.ToString());
    Console.WriteLine($"  Saved PGN to {Path.GetFullPath(pgnPath)}");
}
return 0;

static void PrintHelp()
{
    Console.WriteLine("""
        rlChess Arena - play many games between two players.

        Options:
          -n, --games <N>     number of games (default 100)
          --p1 <id>           first player (default random)
          --p2 <id>           second player (default random)
          --pgn <file>        save all games to a PGN file
          --fen <fen>         start every game from this position
          --max-plies <N>     declare a draw after this many half-moves (default 1000)
          --seed <N>          seed for random players, for reproducible runs
          -t, --threads <N>   play games in parallel on N CPU cores ("all" = every core)
        """);
    Console.WriteLine("Players: " + string.Join(", ", PlayerRegistry.All.Select(e => e.Id)));
}
