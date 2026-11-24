using System.Globalization;
using System.Text;

namespace RlChess.Engine;

/// <summary>
/// Running score of a match between two players, "P1" and "P2", from P1's point of view.
/// Used by the Arena (console and website) for the results, the rating estimate and the chart.
/// </summary>
public sealed class MatchStats
{
    private readonly List<double> _scores = new();

    public int Wins { get; private set; }
    public int Draws { get; private set; }
    public int Losses { get; private set; }
    public int Games => _scores.Count;

    /// <summary>P1's score in each game so far, in the order they finished: 1 win, 0.5 draw, 0 loss.</summary>
    public IReadOnlyList<double> Scores => _scores;

    public void Add(double p1Score)
    {
        if (p1Score >= 1) Wins++;
        else if (p1Score <= 0) Losses++;
        else Draws++;
        _scores.Add(Math.Clamp(p1Score, 0, 1));
    }

    /// <summary>Adds a finished game. <paramref name="p1White"/> says which colour P1 had.</summary>
    public void Add(GameResult result, bool p1White) => Add(ScoreFor(result, p1White));

    public static double ScoreFor(GameResult result, bool p1White) => result.Outcome switch
    {
        GameOutcome.WhiteWins => p1White ? 1 : 0,
        GameOutcome.BlackWins => p1White ? 0 : 1,
        _ => 0.5,
    };

    /// <summary>P1's average score, 0 to 1 (0.5 = level).</summary>
    public double Score => Games == 0 ? 0.5 : (Wins + 0.5 * Draws) / Games;

    /// <summary>Rating difference for P1 implied by the score, with a rough 95% margin. Null when it can't be estimated.</summary>
    public (double Diff, double Margin)? Elo => EloEstimate(Wins, Draws, Losses);

    /// <summary>
    /// Rating difference implied by a score, with a rough 95% margin (normal approximation).
    /// Null if there are no games or one side won every game (the difference is "infinite").
    /// </summary>
    public static (double Diff, double Margin)? EloEstimate(int wins, int draws, int losses)
    {
        double n = wins + draws + losses;
        if (n == 0) return null;
        double score = (wins + 0.5 * draws) / n;
        if (score <= 0 || score >= 1) return null;
        var (lo, hi) = ScoreInterval(wins, draws, losses);
        return (ToElo(score), (ToElo(Math.Clamp(hi, 1e-6, 1 - 1e-6)) - ToElo(Math.Clamp(lo, 1e-6, 1 - 1e-6))) / 2);
    }

    /// <summary>Rating difference that gives this expected score (0.5 → 0, 0.76 → about +200).</summary>
    public static double ToElo(double score) => -400 * Math.Log10(1 / score - 1);

    /// <summary>Rough 95% range for the true score, given the results so far.</summary>
    public static (double Low, double High) ScoreInterval(int wins, int draws, int losses)
    {
        double n = wins + draws + losses;
        if (n == 0) return (0, 1);
        double score = (wins + 0.5 * draws) / n;
        double variance = (wins * Math.Pow(1 - score, 2) + draws * Math.Pow(0.5 - score, 2) + losses * Math.Pow(score, 2)) / n;
        // With few games (or all one result) the variance estimate is useless; assume at least a coin-flip's worth.
        if (n < 30) variance = Math.Max(variance, 0.25 * (30 - n) / 30);
        double se = Math.Sqrt(variance / n);
        return (Math.Max(0, score - 1.96 * se), Math.Min(1, score + 1.96 * se));
    }

    /// <summary>"+120 ± 45" style text, or a short explanation when there's no estimate.</summary>
    public string EloText()
    {
        if (Games == 0) return "no games yet";
        if (Elo is not var (d, m)) return Wins > 0 ? "P1 won every game" : Losses > 0 ? "P2 won every game" : "—";
        return $"{d.ToString("+0;-0;0", CultureInfo.InvariantCulture)} ± {m.ToString("0", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// A self-contained SVG line chart of P1's running score (%), with the 95% range shaded and a
    /// dashed line at 50% (level). Above the line = P1 is doing better. Colours come from CSS
    /// variables with fallbacks, so it fits light and dark pages.
    /// </summary>
    public string ScoreChartSvg(int width = 640, int height = 220)
    {
        var inv = CultureInfo.InvariantCulture;
        const int left = 40, right = 12, top = 12, bottom = 26;
        double pw = width - left - right, ph = height - top - bottom;
        int n = Games;
        double X(int i) => left + (n <= 1 ? pw / 2 : pw * i / (n - 1));
        double Y(double s) => top + ph * (1 - s);
        string F(double v) => v.ToString("0.#", inv);

        var sb = new StringBuilder();
        sb.Append($"<svg class=\"score-chart\" viewBox=\"0 0 {width} {height}\" role=\"img\" aria-label=\"P1 running score\" xmlns=\"http://www.w3.org/2000/svg\" style=\"width:100%;height:auto;font:11px system-ui,sans-serif\">");
        // Grid: 0, 25, 50, 75, 100 %.
        foreach (var g in new[] { 0, 0.25, 0.5, 0.75, 1 })
        {
            string stroke = g == 0.5 ? "var(--chart-mid, #8a8783)" : "var(--chart-grid, rgba(128,128,128,.18))";
            string dash = g == 0.5 ? " stroke-dasharray=\"4 4\"" : "";
            sb.Append($"<line x1=\"{left}\" x2=\"{width - right}\" y1=\"{F(Y(g))}\" y2=\"{F(Y(g))}\" stroke=\"{stroke}\" stroke-width=\"1\"{dash}/>");
            sb.Append($"<text x=\"{left - 6}\" y=\"{F(Y(g) + 4)}\" text-anchor=\"end\" fill=\"var(--chart-text, #8a8783)\">{g * 100:0}%</text>");
        }
        sb.Append($"<text x=\"{left}\" y=\"{height - 6}\" fill=\"var(--chart-text, #8a8783)\">game 1</text>");
        if (n > 1) sb.Append($"<text x=\"{width - right}\" y=\"{height - 6}\" text-anchor=\"end\" fill=\"var(--chart-text, #8a8783)\">game {n}</text>");

        if (n > 0)
        {
            // Running score and band after each game.
            var mid = new double[n];
            var lo = new double[n];
            var hi = new double[n];
            int w = 0, d = 0, l = 0;
            for (int i = 0; i < n; i++)
            {
                double s = _scores[i];
                if (s >= 1) w++; else if (s <= 0) l++; else d++;
                mid[i] = (w + 0.5 * d) / (i + 1);
                (lo[i], hi[i]) = ScoreInterval(w, d, l);
            }
            // Thin the points for long matches so the SVG stays small.
            int step = Math.Max(1, n / 400);
            var idx = Enumerable.Range(0, n).Where(i => i % step == 0 || i == n - 1).ToList();

            var band = new StringBuilder();
            foreach (var i in idx) band.Append(band.Length == 0 ? 'M' : 'L').Append(F(X(i))).Append(',').Append(F(Y(hi[i])));
            for (int k = idx.Count - 1; k >= 0; k--) band.Append('L').Append(F(X(idx[k]))).Append(',').Append(F(Y(lo[idx[k]])));
            band.Append('Z');
            sb.Append($"<path d=\"{band}\" fill=\"var(--chart-line, #4b8fd6)\" opacity=\".16\"/>");

            var line = new StringBuilder();
            foreach (var i in idx) line.Append(line.Length == 0 ? 'M' : 'L').Append(F(X(i))).Append(',').Append(F(Y(mid[i])));
            sb.Append($"<path d=\"{line}\" fill=\"none\" stroke=\"var(--chart-line, #4b8fd6)\" stroke-width=\"2\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
            // Hover: an invisible column per point with a tooltip (bigger than the line, so easy to hit).
            for (int k = 0; k < idx.Count; k++)
            {
                int i = idx[k];
                double x0 = k == 0 ? left : (X(idx[k - 1]) + X(i)) / 2;
                double x1 = k == idx.Count - 1 ? width - right : (X(i) + X(idx[k + 1])) / 2;
                sb.Append($"<rect x=\"{F(x0)}\" y=\"{top}\" width=\"{F(Math.Max(1, x1 - x0))}\" height=\"{F(ph)}\" fill=\"transparent\">")
                  .Append($"<title>After game {i + 1}: P1 scored {mid[i] * 100:0}% (likely {lo[i] * 100:0}–{hi[i] * 100:0}%)</title></rect>");
            }
            // End marker with a surface ring, and its value.
            double ex = X(n - 1), ey = Y(mid[n - 1]);
            sb.Append($"<circle cx=\"{F(ex)}\" cy=\"{F(ey)}\" r=\"4\" fill=\"var(--chart-line, #4b8fd6)\" stroke=\"var(--chart-surface, #262421)\" stroke-width=\"2\"/>");
            sb.Append($"<text x=\"{F(Math.Min(ex, width - right - 2))}\" y=\"{F(ey < top + 16 ? ey + 16 : ey - 8)}\" text-anchor=\"end\" fill=\"var(--chart-text-strong, #e6e2dc)\" font-weight=\"600\">{mid[n - 1] * 100:0}%</text>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }
}
