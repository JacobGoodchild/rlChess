using System.Text;

namespace RlChess.Engine;

/// <summary>
/// Turns finished games into rows a learning program can read: one row per position, saying
/// which move was played there and how the game ended for the side to move.
///
/// CSV columns: <c>game,ply,fen,move,result</c>
/// <list type="bullet">
/// <item><c>game</c>: game number (from 1), <c>ply</c>: half-moves played before this position (from 0)</item>
/// <item><c>fen</c>: the position (FEN has spaces but no commas, so it needs no quotes)</item>
/// <item><c>move</c>: the move played, in UCI form like <c>e2e4</c> or <c>e7e8q</c></item>
/// <item><c>result</c>: from the side to move's point of view: 1 = went on to win, 0 = draw, -1 = lost</item>
/// </list>
/// </summary>
public static class TrainingData
{
    public const string CsvHeader = "game,ply,fen,move,result";

    /// <summary>Writes the rows for one game (without the header).</summary>
    public static void AppendCsv(StringBuilder sb, Game game, int gameNumber) => WriteCsv(new StringWriter(sb), game, gameNumber);

    public static void WriteCsv(TextWriter w, Game game, int gameNumber)
    {
        var position = Position.FromFen(game.StartFen);
        var outcome = game.Result.Outcome;
        for (int ply = 0; ply < game.Moves.Count; ply++)
        {
            var mover = position.SideToMove;
            int result = outcome switch
            {
                GameOutcome.WhiteWins => mover == Color.White ? 1 : -1,
                GameOutcome.BlackWins => mover == Color.Black ? 1 : -1,
                _ => 0,
            };
            var move = game.Moves[ply];
            w.Write(gameNumber);
            w.Write(',');
            w.Write(ply);
            w.Write(',');
            w.Write(position.ToFen());
            w.Write(',');
            w.Write(move.ToUci());
            w.Write(',');
            w.Write(result);
            w.Write('\n');
            position.MakeMove(move);
        }
    }

    /// <summary>All games as one CSV text, header included.</summary>
    public static string ToCsv(IEnumerable<Game> games)
    {
        var sb = new StringBuilder(CsvHeader).Append('\n');
        int n = 1;
        foreach (var g in games) AppendCsv(sb, g, n++);
        return sb.ToString();
    }
}
