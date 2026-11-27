namespace RlChess.Engine;

/// <summary>
/// Works out Elo-style ratings from a set of game results between several players (e.g. a round
/// robin). It finds the ratings that best explain all the results at once (maximum likelihood), then
/// shifts them so one "anchor" player has a fixed rating, because ratings only mean something
/// relative to each other.
/// </summary>
public static class Ratings
{
    /// <summary>The rating the Arena gives the Random player, so other ratings have a fixed reference.</summary>
    public const double RandomAnchor = 400;

    public sealed record Rated(int Index, double Rating, double Margin, int Games, double Score);

    /// <param name="playerCount">Players are numbered 0 to playerCount - 1.</param>
    /// <param name="games">Each game: player A, player B, and A's score (1, 0.5 or 0).</param>
    /// <param name="anchor">The player whose rating is fixed at <paramref name="anchorRating"/>.</param>
    public static IReadOnlyList<Rated> Compute(int playerCount, IEnumerable<(int A, int B, double ScoreA)> games,
        int anchor, double anchorRating = RandomAnchor) =>
        Compute(playerCount, games, new Dictionary<int, double> { [anchor] = anchorRating });

    /// <summary>
    /// Same, with several players' ratings fixed (e.g. Stockfish at calibrated levels). Everyone else
    /// is rated relative to them. With one fixed player this is the same as an anchor.
    /// </summary>
    public static IReadOnlyList<Rated> Compute(int playerCount, IEnumerable<(int A, int B, double ScoreA)> games,
        IReadOnlyDictionary<int, double> fixedRatings)
    {
        if (fixedRatings.Count == 0) throw new ArgumentException("At least one player's rating must be fixed.", nameof(fixedRatings));
        // Results per pair: games played and points scored by the lower index.
        var played = new double[playerCount, playerCount];
        var points = new double[playerCount, playerCount];
        var gamesOf = new int[playerCount];
        var scoreOf = new double[playerCount];
        foreach (var (a, b, s) in games)
        {
            if (a == b) continue;
            played[a, b]++; played[b, a]++;
            points[a, b] += s; points[b, a] += 1 - s;
            gamesOf[a]++; gamesOf[b]++;
            scoreOf[a] += s; scoreOf[b] += 1 - s;
        }

        // A small prior (one virtual draw per pair that met) keeps ratings finite when someone
        // wins every game, and barely matters once there are many games.
        const double prior = 1;
        for (int i = 0; i < playerCount; i++)
            for (int j = 0; j < playerCount; j++)
                if (i != j && played[i, j] > 0) { played[i, j] += prior; points[i, j] += prior / 2; }

        const double scale = 400 / 2.302585092994046; // 400 / ln(10)
        var r = new double[playerCount];
        double start = fixedRatings.Values.Average();
        for (int i = 0; i < playerCount; i++) r[i] = fixedRatings.TryGetValue(i, out var f) ? f : start;
        for (int iter = 0; iter < 2000; iter++)
        {
            double maxStep = 0;
            for (int i = 0; i < playerCount; i++)
            {
                if (fixedRatings.ContainsKey(i)) continue;
                double gradient = 0, curvature = 0;
                for (int j = 0; j < playerCount; j++)
                {
                    if (i == j || played[i, j] == 0) continue;
                    double e = Expected(r[i] - r[j]);
                    gradient += points[i, j] - played[i, j] * e;
                    curvature += played[i, j] * e * (1 - e);
                }
                if (curvature <= 0) continue;
                double step = Math.Clamp(scale * gradient / curvature, -400, 400);
                r[i] += step;
                maxStep = Math.Max(maxStep, Math.Abs(step));
            }
            if (maxStep < 1e-6) break;
        }

        var result = new List<Rated>();
        for (int i = 0; i < playerCount; i++)
        {
            double info = 0;
            for (int j = 0; j < playerCount; j++)
            {
                if (i == j || played[i, j] == 0) continue;
                double e = Expected(r[i] - r[j]);
                info += played[i, j] * e * (1 - e);
            }
            double margin = fixedRatings.ContainsKey(i) ? 0 : info > 0 ? 1.96 * scale / Math.Sqrt(info) : double.PositiveInfinity;
            result.Add(new Rated(i, r[i], margin, gamesOf[i], gamesOf[i] == 0 ? 0 : scoreOf[i] / gamesOf[i]));
        }
        return result;
    }

    /// <summary>Expected score for a player rated <paramref name="diff"/> points above the opponent.</summary>
    public static double Expected(double diff) => 1 / (1 + Math.Pow(10, -diff / 400));
}
