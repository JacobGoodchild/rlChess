using System.Globalization;
using System.Text;

namespace RlChess.Engine.Players;

/// <summary>Formats a <see cref="SearchInfo"/> for people: "depth 6 · 120k positions · 450k/s · best Nf3 · +0.35 · Nf3 Nc6 Bb5".</summary>
public static class ThinkingText
{
    /// <param name="position">The position the player was thinking about (to write moves as Nf3 rather than g1f3).</param>
    public static string Format(SearchInfo info, Position position)
    {
        var parts = new List<string>();
        if (info.Depth is int d) parts.Add($"depth {d}");
        if (info.PositionsSearched is long n) parts.Add($"{Count(n)} positions");
        if (info.PositionsPerSecond is double nps) parts.Add($"{Count((long)nps)}/s");
        if (info.BestMove is Move best && position.IsLegal(best)) parts.Add($"best {Notation.ToSan(position, best)}");
        if (info.Score is EvalScore s) parts.Add($"{s.ForWhite(position.SideToMove)} (White's view)");
        if (Line(info, position) is { Length: > 0 } line) parts.Add($"line {line}");
        if (!string.IsNullOrWhiteSpace(info.Note)) parts.Add(info.Note!);
        return string.Join(" · ", parts);
    }

    /// <summary>The expected line in SAN ("Nf3 Nc6 Bb5"), stopping at the first move that isn't legal.</summary>
    public static string Line(SearchInfo info, Position position, int maxMoves = 8)
    {
        if (info.Line is not { Count: > 0 } line) return "";
        var p = position.Clone();
        var sb = new StringBuilder();
        foreach (var m in line.Take(maxMoves))
        {
            if (!p.TryFindLegalMove(m.From, m.To, m.Promotion, out var legal)) break;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(Notation.ToSan(p, legal));
            p.MakeMove(legal);
        }
        return sb.ToString();
    }

    /// <summary>1234 → "1.2k", 4500000 → "4.5M".</summary>
    public static string Count(long n) => n switch
    {
        >= 10_000_000 => (n / 1e6).ToString("0", CultureInfo.InvariantCulture) + "M",
        >= 1_000_000 => (n / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + "M",
        >= 10_000 => (n / 1e3).ToString("0", CultureInfo.InvariantCulture) + "k",
        >= 1_000 => (n / 1e3).ToString("0.0", CultureInfo.InvariantCulture) + "k",
        _ => n.ToString(CultureInfo.InvariantCulture),
    };
}
