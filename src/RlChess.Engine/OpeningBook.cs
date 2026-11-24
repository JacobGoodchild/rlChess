namespace RlChess.Engine;

/// <summary>
/// Opening names (from lichess's public-domain list, see <c>tools/README</c>) looked up by position,
/// so transpositions find their name too. Used for "Book" moves in game review and the opening name
/// shown during games.
/// </summary>
public sealed class OpeningBook
{
    public sealed record Opening(string Eco, string Name);

    private readonly Dictionary<string, Opening> _byPosition = new();

    public int Count => _byPosition.Count;

    /// <summary>Reads the file made by the Arena's <c>openings</c> command (position TAB eco TAB name per line).</summary>
    public static OpeningBook Parse(string text)
    {
        var book = new OpeningBook();
        foreach (var line in text.Split('\n'))
        {
            var cols = line.TrimEnd('\r').Split('\t');
            if (cols.Length >= 3) book._byPosition[cols[0]] = new Opening(cols[1], cols[2]);
        }
        return book;
    }

    /// <summary>The first four FEN fields (pieces, side to move, castling, en passant): the lookup key.</summary>
    public static string Key(Position p) => string.Join(' ', p.ToFen().Split(' ').Take(4));

    public Opening? Find(Position p) => _byPosition.GetValueOrDefault(Key(p));

    /// <summary>
    /// The name of the opening reached by a game up to <paramref name="ply"/>: the last named position
    /// on the way (so it stays named after the game leaves the book).
    /// </summary>
    public Opening? NameAt(Game game, int ply)
    {
        var p = Position.FromFen(game.StartFen);
        Opening? last = Find(p);
        for (int i = 0; i < ply && i < game.Moves.Count; i++)
        {
            p.MakeMove(game.Moves[i]);
            if (Find(p) is { } o) last = o;
        }
        return last;
    }

    /// <summary>How many of the game's first moves are book moves (each reaching a named position, without a gap).</summary>
    public int BookPlies(Game game)
    {
        if (game.StartFen != Position.StartFen) return 0;
        var p = Position.FromFen(game.StartFen);
        int n = 0;
        foreach (var m in game.Moves)
        {
            p.MakeMove(m);
            if (Find(p) is null) break;
            n++;
        }
        return n;
    }
}
