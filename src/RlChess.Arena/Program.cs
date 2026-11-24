using System.Diagnostics;
using System.Text;
using RlChess.Engine;
using RlChess.Engine.Players;
using RlChess.Players;
using ChessGame = RlChess.Engine.Game;

// rlChess Arena: plays N games between two players as fast as possible, alternating colours.
//
//   dotnet run --project src/RlChess.Arena -c Release -- --games 1000 --p1 random --p2 random --pgn games.pgn --threads all
//
// Players are looked up in PlayerRegistry (src/RlChess.Players/PlayerRegistry.cs): "id" or "id:level".

if (args.Length >= 2 && args[0] == "bench") return Bench.Run(args[1..]);

// "perft <depth> [fen]": count move sequences (checks move generation and measures speed).
if (args.Length >= 2 && args[0] == "perft")
{
    int depth = int.Parse(args[1]);
    var position = Position.FromFen(args.Length > 2 ? string.Join(' ', args[2..]) : Position.StartFen);
    var timer = Stopwatch.StartNew();
    long total = 0;
    foreach (var (move, nodes) in Perft.Divide(position, depth))
    {
        Console.WriteLine($"  {move.ToUci(),-6} {nodes,14:N0}");
        total += nodes;
    }
    timer.Stop();
    Console.WriteLine($"perft({depth}) = {total:N0} in {timer.Elapsed.TotalSeconds:F2} s ({total / Math.Max(timer.Elapsed.TotalSeconds, 1e-9) / 1e6:F1} M positions/sec)");
    return 0;
}

int games = 100;
string p1Id = "random", p2Id = "random";
string? pgnPath = null;
string? reportPath = null;
string? exportPath = null;
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
        case "--report": reportPath = Next(); break;
        case "--export": exportPath = Next(); break;
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

IPlayer Create(string spec, int offset)
{
    if (!PlayerRegistry.TryParse(spec, out var entry, out var level, out var error)) throw new ArgumentException(error);
    // Seeded random players get distinct, reproducible seeds (per game, so threads don't matter).
    if (seed is int s && entry.Category == PlayerCategory.Random && entry.Id == "random")
        return new RandomPlayer(unchecked(s * 1_000_003 + offset));
    return entry.CreatePlayer(level);
}

string Label(string spec) =>
    PlayerRegistry.TryParse(spec, out var e, out var l, out _) ? e.Label(l) : spec;

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
Console.WriteLine($"Arena: {Label(p1Id)} (P1) vs {Label(p2Id)} (P2), {games} games, alternating colours, {threads} thread(s).");

bool seededRandom(string spec) => seed is not null && PlayerRegistry.TryParse(spec, out var e, out _, out _) && e.Id == "random";
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

var stats = new MatchStats();
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
    stats.Add(r, p1White);
    var reasons = r.Outcome == GameOutcome.Draw ? drawReasons : winReasons;
    reasons[r.Reason] = reasons.GetValueOrDefault(r.Reason) + 1;

    pgn?.Append(game.ToPgn(p1White ? $"{Label(p1Id)} (P1)" : $"{Label(p2Id)} (P2)", p1White ? $"{Label(p2Id)} (P2)" : $"{Label(p1Id)} (P1)",
        eventName: "rlChess Arena")).Append('\n');
}

sw.Stop();
double secs = Math.Max(sw.Elapsed.TotalSeconds, 1e-9);

Console.WriteLine();
Console.WriteLine($"Results after {games} games:");
Console.WriteLine($"  {Label(p1Id)} (P1) wins: {stats.Wins,6}  ({100.0 * stats.Wins / games:F1}%)");
Console.WriteLine($"  {Label(p2Id)} (P2) wins: {stats.Losses,6}  ({100.0 * stats.Losses / games:F1}%)");
Console.WriteLine($"  Draws:            {stats.Draws,6}  ({100.0 * stats.Draws / games:F1}%)");
foreach (var (reason, count) in drawReasons.OrderByDescending(kv => kv.Value))
    Console.WriteLine($"    {GameResult.Draw(reason).ReasonText}: {count}");
foreach (var (reason, count) in winReasons.OrderByDescending(kv => kv.Value))
    Console.WriteLine($"  Decisive by {GameResult.Draw(reason).ReasonText.ToLowerInvariant()}: {count}");
Console.WriteLine(stats.Elo is var (diff, margin)
    ? $"  Elo difference:   {diff:+0;-0;0} ± {margin:0} for {Label(p1Id)} (P1), 95% confidence"
    : "  Elo difference:   can't estimate (one side won or lost every game)");
Console.WriteLine($"  Average length:   {(double)totalPlies / games / 2:F1} moves ({(double)totalPlies / games:F1} half-moves)");
Console.WriteLine($"  Speed:            {games / secs:F1} games/sec ({secs:F2} s total)");

if (pgnPath is not null)
{
    File.WriteAllText(pgnPath, pgn!.ToString());
    Console.WriteLine($"  Saved PGN to {Path.GetFullPath(pgnPath)}");
}
if (exportPath is not null)
{
    using (var w = new StreamWriter(exportPath))
    {
        w.Write(TrainingData.CsvHeader + "\n");
        for (int g = 0; g < games; g++) TrainingData.WriteCsv(w, results[g], g + 1);
    }
    Console.WriteLine($"  Saved training data ({totalPlies:N0} positions) to {Path.GetFullPath(exportPath)}");
}
if (reportPath is not null)
{
    File.WriteAllText(reportPath, Report.Html(Label(p1Id), Label(p2Id), stats, drawReasons, winReasons, results,
        g => g % 2 == 0, totalPlies, secs));
    Console.WriteLine($"  Saved report to {Path.GetFullPath(reportPath)} (open it in a web browser)");
}
return 0;

static void PrintHelp()
{
    Console.WriteLine("""
        rlChess Arena - play many games between two players.

        Also: perft <depth> [fen]   count all move sequences to <depth> (speed/correctness check)
              bench <id[:level]> [--positions N] [--seed N]
                                    time how long a player takes per move (and positions/sec)

        Options:
          -n, --games <N>     number of games (default 100)
          --p1 <id[:level]>   first player, e.g. random or minimax:4 (default random)
          --p2 <id[:level]>   second player (default random)
          --pgn <file>        save all games to a PGN file
          --report <file>     save a results page with a chart (open the .html file in a browser)
          --export <file>     save every position as training data (CSV: game,ply,fen,move,result)
          --fen <fen>         start every game from this position
          --max-plies <N>     declare a draw after this many half-moves (default 1000)
          --seed <N>          seed for random players, for reproducible runs
          -t, --threads <N>   play games in parallel on N CPU cores ("all" = every core)
        """);
    Console.WriteLine("Players:");
    foreach (var e in PlayerRegistry.All)
        Console.WriteLine($"  {e.Id,-12} {e.Category,-9} {(e.Levels is { } r ? $"levels {r.Min}-{r.Max} (default {r.Default})" : "no levels"),-28} {e.Description}");
}
