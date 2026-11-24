using System.Text;
using RlChess.Engine;

/// <summary>
/// "openings a.tsv b.tsv ... --out openings.tsv": turns lichess's opening list
/// (https://github.com/lichess-org/chess-openings, public domain / CC0) into the small file the
/// website uses for opening names and "Book" moves: one line per named position,
/// <c>position (FEN without move counters) TAB eco TAB name</c>.
/// </summary>
static class OpeningsCommand
{
    public static int Run(string[] args)
    {
        var inputs = new List<string>();
        string outPath = "openings.tsv";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) outPath = args[++i];
            else inputs.Add(args[i]);
        }
        var byPosition = new Dictionary<string, (string Eco, string Name, int Plies)>();
        int lines = 0, failed = 0;
        foreach (var path in inputs)
        {
            foreach (var line in File.ReadLines(path).Skip(1)) // header: eco name pgn
            {
                var cols = line.Split('\t');
                if (cols.Length < 3) continue;
                lines++;
                try
                {
                    var game = Pgn.Parse(cols[2]).Game;
                    string key = OpeningBook.Key(game.Position);
                    // Several lines can reach one position: keep the shortest (it's the main name).
                    if (!byPosition.TryGetValue(key, out var old) || game.Moves.Count < old.Plies)
                        byPosition[key] = (cols[0], cols[1], game.Moves.Count);
                }
                catch (FormatException) { failed++; }
            }
        }
        var sb = new StringBuilder();
        foreach (var (key, v) in byPosition.OrderBy(kv => kv.Value.Eco).ThenBy(kv => kv.Value.Plies))
            sb.Append(key).Append('\t').Append(v.Eco).Append('\t').Append(v.Name).Append('\n');
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"{lines} lines read ({failed} unreadable), {byPosition.Count} positions written to {Path.GetFullPath(outPath)}");
        return failed == 0 ? 0 : 1;
    }

}
