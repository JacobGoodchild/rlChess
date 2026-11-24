namespace RlChess.Engine;

/// <summary>
/// A time control such as "3+2": each side starts with <see cref="Initial"/> and gains
/// <see cref="Increment"/> after each of their moves.
/// </summary>
public sealed record TimeControl(TimeSpan Initial, TimeSpan Increment)
{
    public static TimeControl FromMinutes(double minutes, double incrementSeconds = 0) =>
        new(TimeSpan.FromMinutes(minutes), TimeSpan.FromSeconds(incrementSeconds));

    /// <summary>Short name, e.g. "3+2" or "0.5+0".</summary>
    public string Name => $"{Initial.TotalMinutes:0.##}+{Increment.TotalSeconds:0.##}";

    public override string ToString() => Name;
}

/// <summary>
/// A two-sided chess clock. Time is read from an injectable source, so it can be tested without
/// waiting and paused/resumed by a UI. The clock doesn't know the rules of chess: call
/// <see cref="Start"/> when a side should start thinking and <see cref="Press"/> when it has moved.
/// </summary>
public sealed class ChessClock
{
    private readonly Func<TimeSpan> _now;
    private readonly TimeSpan[] _remaining = new TimeSpan[2];
    private TimeSpan _turnStartedAt;

    /// <param name="now">The current time (defaults to a real stopwatch). Tests pass a fake.</param>
    public ChessClock(TimeControl control, Func<TimeSpan>? now = null)
    {
        Control = control;
        if (now is null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            now = () => sw.Elapsed;
        }
        _now = now;
        _remaining[0] = _remaining[1] = control.Initial;
    }

    public TimeControl Control { get; }

    /// <summary>The side whose clock is running, or null if stopped.</summary>
    public Color? Running { get; private set; }

    public bool IsRunning => Running is not null;

    /// <summary>Time left for <paramref name="side"/> right now (never below zero).</summary>
    public TimeSpan Remaining(Color side)
    {
        var t = _remaining[(int)side];
        if (Running == side) t -= _now() - _turnStartedAt;
        return t < TimeSpan.Zero ? TimeSpan.Zero : t;
    }

    /// <summary>Has <paramref name="side"/> run out of time?</summary>
    public bool IsFlagged(Color side) => Remaining(side) <= TimeSpan.Zero;

    /// <summary>Starts (or switches) the clock so that <paramref name="side"/>'s time runs.</summary>
    public void Start(Color side)
    {
        Stop();
        Running = side;
        _turnStartedAt = _now();
    }

    /// <summary>Stops the clock, keeping the time used so far. <see cref="Start"/> resumes it.</summary>
    public void Stop()
    {
        if (Running is Color c)
        {
            _remaining[(int)c] = Remaining(c);
            Running = null;
        }
    }

    /// <summary>
    /// <paramref name="mover"/> has just moved: stop their clock, add the increment, and start
    /// the opponent's. Returns false (and adds no increment) if they had already run out of time.
    /// </summary>
    public bool Press(Color mover)
    {
        bool hadTime = Running != mover || !IsFlagged(mover);
        if (Running == mover) Stop();
        if (hadTime) _remaining[(int)mover] += Control.Increment;
        Start(mover.Opponent());
        return hadTime;
    }
}

public static class TimeoutRules
{
    /// <summary>
    /// The result when <paramref name="flagged"/> runs out of time in <paramref name="position"/>:
    /// a loss, unless the opponent couldn't possibly checkmate, in which case it's a draw.
    /// </summary>
    public static GameResult Result(Position position, Color flagged)
    {
        var winner = flagged.Opponent();
        return CanEverCheckmate(position, winner)
            ? GameResult.Win(winner, GameEndReason.Timeout)
            : GameResult.Draw(GameEndReason.TimeoutVsInsufficientMaterial);
    }

    /// <summary>
    /// Could <paramref name="side"/> checkmate with the material on the board, by any series of moves
    /// (including the opponent "helping")? This is a simplified version of the rule lichess uses:
    /// <list type="bullet">
    /// <item>a lone king can never mate;</item>
    /// <item>king + one knight, or king + bishops all on one square colour, can't mate a bare king,
    /// nor (for bishops) a king whose only other pieces are bishops on that same colour;</item>
    /// <item>anything with a pawn, rook or queen, or more minor pieces, can.</item>
    /// </list>
    /// </summary>
    public static bool CanEverCheckmate(Position position, Color side)
    {
        int knights = 0, bishops = 0;
        bool lightB = false, darkB = false;
        int theirNonKing = 0;
        bool theirOnlyBishops = true, theirLightB = false, theirDarkB = false;

        for (int sq = 0; sq < 64; sq++)
        {
            var p = position[sq];
            if (p == Piece.None || p.Type() == PieceType.King) continue;
            if (p.Color() == side)
            {
                switch (p.Type())
                {
                    case PieceType.Knight: knights++; break;
                    case PieceType.Bishop:
                        bishops++;
                        if (Square.IsLight(sq)) lightB = true; else darkB = true;
                        break;
                    default: return true; // pawn, rook or queen
                }
            }
            else
            {
                theirNonKing++;
                if (p.Type() == PieceType.Bishop) { if (Square.IsLight(sq)) theirLightB = true; else theirDarkB = true; }
                else theirOnlyBishops = false;
            }
        }

        if (knights == 0 && bishops == 0) return false;          // lone king
        if (knights == 0 && !(lightB && darkB))                   // bishops all on one colour
        {
            // Mate needs blockers on the other colour; opposing bishops on the same colour can't provide them.
            bool sameColourBishopsOnly = theirOnlyBishops && !(lightB ? theirDarkB : theirLightB);
            return !(theirNonKing == 0 || sameColourBishopsOnly);
        }
        if (knights == 1 && bishops == 0) return theirNonKing > 0; // lone knight vs bare king can't mate
        return true;
    }
}
