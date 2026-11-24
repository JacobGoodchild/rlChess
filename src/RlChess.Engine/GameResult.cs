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
        _ => "",
    };

    public override string ToString() => IsOver ? $"{ToPgnString()} ({ReasonText})" : "Ongoing";
}
