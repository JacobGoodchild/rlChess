using System.Diagnostics;
using RlChess.Engine;
using RlChess.Engine.Players;

/// <summary>
/// Stockfish installed on this computer, used as a reference opponent over UCI.
/// One program runs per player object (so parallel games each get their own).
/// Stockfish is GPL software that you install separately; nothing of it is in this repository.
/// </summary>
sealed class StockfishProcess : IPlayer, IReportsThinking, IDisposable
{
    private readonly Process _process;
    private readonly StockfishStrength _strength;
    private readonly object _lock = new();

    public string Name => _strength.ToString();
    public Action<SearchInfo>? Report { get; set; }

    /// <summary>Set by --stockfish-path; otherwise <see cref="Find"/> looks in the usual places.</summary>
    public static string? PathOverride { get; set; }

    public StockfishProcess(string path, StockfishStrength strength)
    {
        _strength = strength;
        _process = new Process
        {
            StartInfo = new ProcessStartInfo(path)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        _process.Start();
        Send("uci");
        WaitFor("uciok");
        Send("setoption name Threads value 1"); // one core per game; run more games in parallel instead
        Send("isready");
        WaitFor("readyok");
    }

    public Move ChooseMove(Position position)
    {
        lock (_lock)
        {
            foreach (var c in _strength.SetupCommands()) Send(c);
            Send($"position fen {position.ToFen()}");
            Send(_strength.GoCommand());
            var sw = Stopwatch.StartNew();
            while (true)
            {
                var line = _process.StandardOutput.ReadLine() ?? throw new InvalidOperationException("Stockfish stopped unexpectedly.");
                if (line.StartsWith("info ") && line.Contains(" pv ") && Report is not null) Report(Info(line, position, sw.Elapsed));
                if (!line.StartsWith("bestmove")) continue;
                if (StockfishStrength.TryParseBestMove(line, position, out var move)) return move;
                throw new InvalidOperationException($"Stockfish gave no move ({line}) in {position.ToFen()}");
            }
        }
    }

    private static SearchInfo Info(string line, Position position, TimeSpan elapsed)
    {
        var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int? depth = null; long? nodes = null; EvalScore? score = null; var pv = new List<Move>();
        var p = position.Clone();
        for (int i = 1; i < t.Length; i++)
        {
            switch (t[i])
            {
                case "depth" when i + 1 < t.Length: depth = int.Parse(t[++i]); break;
                case "nodes" when i + 1 < t.Length: nodes = long.Parse(t[++i]); break;
                case "score" when i + 2 < t.Length:
                    int v = int.Parse(t[i + 2]);
                    score = t[i + 1] == "mate" ? EvalScore.MateIn(v) : EvalScore.FromCentipawns(v);
                    i += 2;
                    break;
                case "pv":
                    for (int k = i + 1; k < t.Length && Notation.TryParseUci(p, t[k], out var m); k++) { pv.Add(m); p.MakeMove(m); }
                    i = t.Length;
                    break;
            }
        }
        return new SearchInfo { Depth = depth, PositionsSearched = nodes, Score = score, BestMove = pv.Count > 0 ? pv[0] : null, Line = pv, Elapsed = elapsed };
    }

    private void Send(string command)
    {
        _process.StandardInput.WriteLine(command);
        _process.StandardInput.Flush();
    }

    private void WaitFor(string token)
    {
        string? line;
        while ((line = _process.StandardOutput.ReadLine()) is not null)
            if (line.StartsWith(token)) return;
        throw new InvalidOperationException("Stockfish stopped unexpectedly.");
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited) { Send("quit"); if (!_process.WaitForExit(1000)) _process.Kill(); }
        }
        catch (Exception) { }
        _process.Dispose();
    }

    /// <summary>Where Stockfish is: --stockfish-path, the STOCKFISH_PATH variable, the PATH, or the usual install folders.</summary>
    public static string? Find()
    {
        if (!string.IsNullOrWhiteSpace(PathOverride)) return File.Exists(PathOverride) ? PathOverride : null;
        if (Environment.GetEnvironmentVariable("STOCKFISH_PATH") is { Length: > 0 } env && File.Exists(env)) return env;
        var names = OperatingSystem.IsWindows() ? new[] { "stockfish.exe" } : new[] { "stockfish" };
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Concat(["/usr/games", "/usr/local/bin", "/usr/bin", "/opt/homebrew/bin", "/snap/bin"]);
        foreach (var dir in dirs.Where(d => d.Length > 0))
            foreach (var name in names)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    public const string InstallHelp = """
        Stockfish isn't installed (or wasn't found). It's free; to install it on Linux Mint / Ubuntu:

            sudo apt install stockfish

        (it goes in /usr/games/stockfish, which the Arena checks). On other systems, download it from
        https://stockfishchess.org/download/ and tell the Arena where it is with
        --stockfish-path /path/to/stockfish   (or set the STOCKFISH_PATH environment variable).
        """;

    /// <summary>Lets the player list make Stockfish opponents (called once at start-up).</summary>
    public static void Register()
    {
        RlChess.Players.PlayerRegistry.StockfishFactory = strength =>
        {
            var path = Find() ?? throw new StockfishMissingException();
            return new StockfishProcess(path, strength with { MoveTime = Shared.StockfishMoveTime });
        };
    }
}

sealed class StockfishMissingException() : Exception(StockfishProcess.InstallHelp);
