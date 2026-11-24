using System.Net;
using System.Text;
using RlChess.Engine;
using ChessGame = RlChess.Engine.Game;

/// <summary>Writes a single self-contained HTML page summarising an Arena run (no internet needed to view it).</summary>
static class Report
{
    public static string Html(string p1, string p2, MatchStats stats,
        Dictionary<GameEndReason, int> drawReasons, Dictionary<GameEndReason, int> winReasons,
        ChessGame[] games, Func<int, bool> p1White, long totalPlies, double seconds)
    {
        string E(string s) => WebUtility.HtmlEncode(s);
        int n = stats.Games;
        string Pct(int k) => n == 0 ? "0%" : $"{100.0 * k / n:0.#}%";
        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Arena results</title>
            <style>
            :root { --bg:#f7f6f5; --panel:#fff; --line:#e2dfdb; --text:#4d4d4d; --strong:#1f1f1f; --dim:#8a8783;
                    --chart-line:#1b78d0; --chart-grid:rgba(0,0,0,.08); --chart-mid:#8a8783; --chart-text:#8a8783;
                    --chart-text-strong:#1f1f1f; --chart-surface:#fff; --win:#4f8a2e; --loss:#c9523f; }
            @media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { --bg:#161512; --panel:#262421; --line:#3b3935; --text:#bababa;
                    --strong:#e6e2dc; --chart-line:#5aa0ea; --chart-grid:rgba(255,255,255,.08); --chart-text-strong:#e6e2dc; --chart-surface:#262421;
                    --win:#7aa64a; --loss:#d9705e; } }
            body { margin:0; background:var(--bg); color:var(--text); font:15px/1.45 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif; }
            main { max-width:860px; margin:0 auto; padding:24px 16px 48px; }
            h1 { color:var(--strong); font-weight:500; font-size:24px; margin:0 0 4px; }
            .sub { color:var(--dim); margin:0 0 20px; }
            .panel { background:var(--panel); border-radius:8px; padding:16px 18px; margin-bottom:14px; box-shadow:0 1px 4px rgba(0,0,0,.08); }
            .tiles { display:grid; grid-template-columns:repeat(auto-fit,minmax(150px,1fr)); gap:10px; }
            .tile .k { font-size:12px; text-transform:uppercase; letter-spacing:.06em; color:var(--dim); }
            .tile .v { font-size:26px; color:var(--strong); font-variant-numeric:tabular-nums; }
            .tile .s { font-size:13px; color:var(--dim); }
            h2 { font-size:14px; text-transform:uppercase; letter-spacing:.06em; color:var(--dim); font-weight:500; margin:0 0 10px; }
            .note { font-size:13px; color:var(--dim); margin:6px 0 0; }
            table { width:100%; border-collapse:collapse; font-variant-numeric:tabular-nums; font-size:14px; }
            th, td { text-align:left; padding:5px 8px; border-bottom:1px solid var(--line); }
            th { color:var(--dim); font-weight:500; font-size:12px; text-transform:uppercase; letter-spacing:.05em; }
            .w { color:var(--win); font-weight:600; } .l { color:var(--loss); font-weight:600; }
            details summary { cursor:pointer; color:var(--strong); }
            </style></head><body><main>
            <h1>{{E(p1)}} <span style="color:var(--dim)">vs</span> {{E(p2)}}</h1>
            <p class="sub">{{n}} games, colours alternating · {{DateTime.Now:d MMM yyyy, HH:mm}} · {{seconds:0.0}} s</p>
            <div class="panel tiles">
              <div class="tile"><div class="k">P1 wins</div><div class="v">{{stats.Wins}}</div><div class="s">{{Pct(stats.Wins)}} · {{E(p1)}}</div></div>
              <div class="tile"><div class="k">P2 wins</div><div class="v">{{stats.Losses}}</div><div class="s">{{Pct(stats.Losses)}} · {{E(p2)}}</div></div>
              <div class="tile"><div class="k">Draws</div><div class="v">{{stats.Draws}}</div><div class="s">{{Pct(stats.Draws)}}</div></div>
              <div class="tile"><div class="k">Rating difference</div><div class="v">{{E(stats.EloText())}}</div><div class="s">for P1, 95% range</div></div>
            </div>
            <div class="panel">
              <h2>P1's score as the games went on</h2>
              {{stats.ScoreChartSvg()}}
              <p class="note">The line is P1's average score so far (a win counts 1, a draw ½). The shaded band is where the true
              score probably lies; it narrows as more games are played. If the whole band is above the dashed 50% line,
              P1 is very likely the stronger player. Hover over the chart for exact numbers.</p>
            </div>
            """);

        sb.Append("<div class=\"panel\"><h2>How games ended</h2><table><tr><th>Ending</th><th>Games</th></tr>");
        foreach (var (reason, count) in winReasons.OrderByDescending(kv => kv.Value))
            sb.Append($"<tr><td>Won by {E(GameResult.Draw(reason).ReasonText.ToLowerInvariant())}</td><td>{count}</td></tr>");
        foreach (var (reason, count) in drawReasons.OrderByDescending(kv => kv.Value))
            sb.Append($"<tr><td>Draw: {E(GameResult.Draw(reason).ReasonText.ToLowerInvariant())}</td><td>{count}</td></tr>");
        sb.Append($"</table><p class=\"note\">Average length {(n == 0 ? 0 : (double)totalPlies / n / 2):0.0} moves.</p></div>");

        sb.Append("<details class=\"panel\"><summary>Every game</summary><table><tr><th>#</th><th>White</th><th>Black</th><th>Result</th><th>For P1</th><th>Ending</th><th>Moves</th></tr>");
        for (int g = 0; g < games.Length; g++)
        {
            var game = games[g];
            bool w = p1White(g);
            double sc = MatchStats.ScoreFor(game.Result, w);
            string cls = sc >= 1 ? "w" : sc <= 0 ? "l" : "";
            string forP1 = sc >= 1 ? "win" : sc <= 0 ? "loss" : "draw";
            sb.Append($"<tr><td>{g + 1}</td><td>{(w ? "P1" : "P2")}</td><td>{(w ? "P2" : "P1")}</td><td>{game.Result.ToPgnString()}</td>" +
                      $"<td class=\"{cls}\">{forP1}</td><td>{E(game.Result.ReasonText)}</td><td>{(game.Moves.Count + 1) / 2}</td></tr>");
        }
        sb.Append("</table></details></main></body></html>\n");
        return sb.ToString();
    }
}
