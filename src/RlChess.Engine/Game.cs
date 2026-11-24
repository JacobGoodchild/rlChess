using System.Text;

namespace RlChess.Engine;

/// <summary>
/// A game from a starting position: the moves played (with their SAN), the current position,
/// and the result. This is what the website and the Arena use. For raw speed (e.g. search
/// inside a player) use <see cref="Position"/> directly instead.
/// </summary>
public sealed class Game
{
    private readonly List<Move> _moves = new();
    private readonly List<string> _san = new();

    public string StartFen { get; }
    public Position Position { get; }
    public IReadOnlyList<Move> Moves => _moves;
    public IReadOnlyList<string> SanMoves => _san;

    private readonly bool _recordSan;

    /// <param name="startFen">Starting position (default: the normal start).</param>
    /// <param name="recordSan">
    /// Keep the SAN of each move (needed for <see cref="SanMoves"/> and <see cref="ToPgn"/>).
    /// Turning it off makes playing moves noticeably faster, e.g. for self-play training.
    /// </param>
    public Game(string? startFen = null, bool recordSan = true)
    {
        _recordSan = recordSan;
        StartFen = startFen ?? Position.StartFen;
        Position = Position.FromFen(StartFen);
        StartFen = Position.ToFen(); // normalised
        Result = Position.GetResult();
    }

    public GameResult Result { get; private set; }
    public bool IsOver => Result.IsOver;
    public Color SideToMove => Position.SideToMove;

    /// <summary>Plays a move. Throws if the game is over or the move is illegal.</summary>
    public void Play(Move move)
    {
        if (IsOver) throw new InvalidOperationException("The game is over.");
        if (!Position.TryFindLegalMove(move.From, move.To, move.Promotion, out var legal))
            throw new ArgumentException($"Illegal move {move.ToUci()} in {Position.ToFen()}.", nameof(move));
        if (_recordSan) _san.Add(Notation.ToSan(Position, legal));
        _moves.Add(legal);
        Position.MakeMove(legal);
        Result = Position.GetResult();
    }

    /// <summary>Plays a move given in SAN ("Nf3") or UCI ("g1f3").</summary>
    public void Play(string moveText)
    {
        if (IsOver) throw new InvalidOperationException("The game is over.");
        if (Notation.TryParseUci(Position, moveText, out var m) || Notation.TryParseSan(Position, moveText, out m))
            Play(m);
        else
            throw new ArgumentException($"'{moveText}' is not a legal move here.", nameof(moveText));
    }

    /// <summary>Takes back the last move. Returns false if there is nothing to undo.</summary>
    public bool Undo()
    {
        if (_moves.Count == 0) return false;
        Position.UndoMove();
        _moves.RemoveAt(_moves.Count - 1);
        if (_recordSan) _san.RemoveAt(_san.Count - 1);
        Result = Position.GetResult();
        return true;
    }

    /// <summary>Ends the game early, e.g. by resignation or an Arena move limit.</summary>
    public void End(GameResult result)
    {
        Result = result;
    }

    /// <summary>The position after the first <paramref name="ply"/> half-moves (0 = start).</summary>
    public Position PositionAt(int ply)
    {
        var p = Position.FromFen(StartFen);
        for (int i = 0; i < ply && i < _moves.Count; i++) p.MakeMove(_moves[i]);
        return p;
    }

    /// <summary>Exports the game as PGN.</summary>
    /// <param name="extraTags">More tags to write after the standard ones, e.g. ("Mode", "vs computer").</param>
    public string ToPgn(string white = "?", string black = "?", string eventName = "rlChess game", DateTime? date = null,
        IEnumerable<(string Key, string Value)>? extraTags = null)
    {
        if (!_recordSan) throw new InvalidOperationException("This game was created with recordSan: false, so it can't be exported as PGN.");
        var sb = new StringBuilder();
        void Tag(string k, string v) => sb.Append('[').Append(k).Append(" \"").Append(v.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"]\n");
        Tag("Event", eventName);
        Tag("Site", "rlChess");
        Tag("Date", (date ?? DateTime.UtcNow).ToString("yyyy.MM.dd"));
        Tag("Round", "-");
        Tag("White", white);
        Tag("Black", black);
        Tag("Result", Result.ToPgnString());
        if (StartFen != Position.StartFen)
        {
            Tag("SetUp", "1");
            Tag("FEN", StartFen);
        }
        if (Result.IsOver) Tag("Termination", Result.ReasonText);
        if (extraTags is not null) foreach (var (k, v) in extraTags) Tag(k, v);
        sb.Append('\n');

        var start = Position.FromFen(StartFen);
        int moveNumber = start.FullmoveNumber;
        bool whiteToMove = start.SideToMove == Color.White;
        var line = new StringBuilder();
        void Word(string w)
        {
            if (line.Length + w.Length + 1 > 80) { sb.Append(line).Append('\n'); line.Clear(); }
            if (line.Length > 0) line.Append(' ');
            line.Append(w);
        }
        for (int i = 0; i < _san.Count; i++)
        {
            if (whiteToMove) Word($"{moveNumber}.");
            else if (i == 0) Word($"{moveNumber}...");
            Word(_san[i]);
            if (!whiteToMove) moveNumber++;
            whiteToMove = !whiteToMove;
        }
        Word(Result.ToPgnString());
        sb.Append(line).Append('\n');
        return sb.ToString();
    }
}
