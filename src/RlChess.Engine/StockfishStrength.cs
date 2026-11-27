namespace RlChess.Engine;

/// <summary>
/// How strong Stockfish should play when used as a reference opponent.
/// Either a calibrated rating (<see cref="Elo"/>, using Stockfish's UCI_LimitStrength + UCI_Elo,
/// valid 1320 to 3190), or an uncalibrated <see cref="SkillLevel"/> (0 to 20) for weaker play.
/// </summary>
public sealed record StockfishStrength
{
    public const int MinElo = 1320, MaxElo = 3190;
    public const int MinSkill = 0, MaxSkill = 20;

    /// <summary>Stockfish's own default think time for our games: see README ("How strong is my bot?").</summary>
    public static readonly TimeSpan DefaultMoveTime = TimeSpan.FromSeconds(1);

    public int? Elo { get; private init; }
    public int? SkillLevel { get; private init; }
    public TimeSpan MoveTime { get; init; } = DefaultMoveTime;

    /// <summary>A calibrated strength (clamped to 1320–3190).</summary>
    public static StockfishStrength AtElo(int elo) => new() { Elo = Math.Clamp(elo, MinElo, MaxElo) };

    /// <summary>An uncalibrated skill level (clamped to 0–20). No rating can be inferred from games against it.</summary>
    public static StockfishStrength AtSkill(int level) => new() { SkillLevel = Math.Clamp(level, MinSkill, MaxSkill) };

    public bool IsCalibrated => Elo is not null;

    /// <summary>"Stockfish 1500" or "Stockfish skill 5 (uncalibrated)".</summary>
    public override string ToString() => Elo is int e ? $"Stockfish {e}" : $"Stockfish skill {SkillLevel} (uncalibrated)";

    /// <summary>The UCI commands that set this strength (sent before each search, so one engine can switch levels).</summary>
    public IReadOnlyList<string> SetupCommands() => Elo is int e
        ?
        [
            "setoption name Skill Level value 20",
            "setoption name UCI_LimitStrength value true",
            $"setoption name UCI_Elo value {e}",
        ]
        :
        [
            "setoption name UCI_LimitStrength value false",
            $"setoption name Skill Level value {SkillLevel}",
        ];

    /// <summary>The UCI "go" command for one move.</summary>
    public string GoCommand() => $"go movetime {Math.Max(1, (int)MoveTime.TotalMilliseconds)}";

    /// <summary>Reads Stockfish's "bestmove e2e4 ponder e7e5" answer. False for "bestmove (none)" or anything unreadable.</summary>
    public static bool TryParseBestMove(string line, Position position, out Move move)
    {
        move = Move.None;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] == "bestmove" && Notation.TryParseUci(position, parts[1], out move);
    }
}
