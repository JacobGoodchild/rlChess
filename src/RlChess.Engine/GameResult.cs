namespace RlChess.Engine;

public enum GameOutcome
{
    Ongoing,
    WhiteWins,
    BlackWins,
    Draw,
}

public enum GameEndReason
{
    None,
    Checkmate,
    Stalemate,
    FiftyMoveRule,
    ThreefoldRepetition,
    InsufficientMaterial,
    Resignation,
    MoveLimit,
    /// <summary>A player ran out of time on their clock.</summary>
    Timeout,
    /// <summary>A player ran out of time, but the opponent couldn't possibly have checkmated: a draw.</summary>
    TimeoutVsInsufficientMaterial,
    /// <summary>Both players agreed to a draw.</summary>
    Agreement,
    /// <summary>A computer player took longer than the allowed think time for one move.</summary>
    ThinkTimeExceeded,
}

/// <summary>Who won (if anyone) and why.</summary>
public readonly record struct GameResult(GameOutcome Outcome, GameEndReason Reason)
{
    public static readonly GameResult Ongoing = new(GameOutcome.Ongoing, GameEndReason.None);

    public bool IsOver => Outcome != GameOutcome.Ongoing;

    public static GameResult Win(Color winner, GameEndReason reason) =>
        new(winner == Color.White ? GameOutcome.WhiteWins : GameOutcome.BlackWins, reason);

    public static GameResult Draw(GameEndReason reason) => new(GameOutcome.Draw, reason);

    /// <summary>PGN result token: "1-0", "0-1", "1/2-1/2" or "*".</summary>
    public string ToPgnString() => Outcome switch
    {
        GameOutcome.WhiteWins => "1-0",
        GameOutcome.BlackWins => "0-1",
        GameOutcome.Draw => "1/2-1/2",
        _ => "*",
    };

    /// <summary>Human-readable reason, e.g. "Threefold repetition".</summary>
    public string ReasonText => Reason switch
    {
        GameEndReason.Checkmate => "Checkmate",
        GameEndReason.Stalemate => "Stalemate",
        GameEndReason.FiftyMoveRule => "Fifty-move rule",
        GameEndReason.ThreefoldRepetition => "Threefold repetition",
        GameEndReason.InsufficientMaterial => "Insufficient material",
        GameEndReason.Resignation => "Resignation",
        GameEndReason.MoveLimit => "Move limit reached",
        GameEndReason.Timeout => "Time out",
        GameEndReason.TimeoutVsInsufficientMaterial => "Time out vs insufficient material",
        GameEndReason.Agreement => "Draw agreed",
        GameEndReason.ThinkTimeExceeded => "Think-time limit exceeded",
        _ => "",
    };

    public override string ToString() => IsOver ? $"{ToPgnString()} ({ReasonText})" : "Ongoing";
}
