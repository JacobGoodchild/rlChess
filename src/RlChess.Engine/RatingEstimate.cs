namespace RlChess.Engine;

/// <summary>
/// "How strong is my bot?": a performance rating from results against opponents whose ratings are
/// known (e.g. Stockfish at a calibrated level). It finds the rating that best explains all the
/// results together, so games against several levels combine into one estimate.
/// </summary>
public static class RatingEstimate
{
    /// <summary>Results against one opponent of known rating.</summary>
    public sealed record Result(double OpponentRating, int Wins, int Draws, int Losses)
    {
        public int Games => Wins + Draws + Losses;
        public double Score => Games == 0 ? 0 : (Wins + 0.5 * Draws) / Games;
    }

    /// <param name="Rating">The best estimate.</param>
    /// <param name="Low">Low end of a rough 95% range.</param>
    /// <param name="High">High end of a rough 95% range.</param>
    /// <param name="OffTheScale">True if it won every game or lost every game: the real rating could be further out than shown.</param>
    public sealed record Estimate(double Rating, double Low, double High, int Games, bool OffTheScale)
    {
        public double Margin => (High - Low) / 2;
        public override string ToString() => $"{Rating:0} ± {Margin:0}";
    }

    /// <summary>Null if there are no games.</summary>
    public static Estimate? Performance(IEnumerable<Result> results)
    {
        var list = results.Where(r => r.Games > 0).ToList();
        if (list.Count == 0) return null;
        int games = list.Sum(r => r.Games);
        double points = list.Sum(r => r.Wins + 0.5 * r.Draws);
        bool offScale = points == 0 || points == games;

        // A small prior: half a draw against the average opponent keeps 0% and 100% finite
        // (otherwise one win from one game would mean "infinitely strong").
        double avgOpp = list.Sum(r => r.OpponentRating * r.Games) / games;
        var all = list.Select(r => (r.OpponentRating, (double)r.Games, r.Wins + 0.5 * r.Draws))
            .Append((avgOpp, 1.0, 0.5)).ToList();

        double rating = avgOpp;
        const double scale = 400 / 2.302585092994046; // 400 / ln(10)
        for (int i = 0; i < 200; i++)
        {
            double gradient = 0, curvature = 0;
            foreach (var (opp, n, s) in all)
            {
                double e = Ratings.Expected(rating - opp);
                gradient += s - n * e;
                curvature += n * e * (1 - e);
            }
            double step = Math.Clamp(scale * gradient / curvature, -400, 400);
            rating += step;
            if (Math.Abs(step) < 1e-7) break;
        }
        double info = all.Sum(x => x.Item2 * Ratings.Expected(rating - x.Item1) * (1 - Ratings.Expected(rating - x.Item1)));
        double margin = 1.96 * scale / Math.Sqrt(info);
        return new Estimate(rating, rating - margin, rating + margin, games, offScale);
    }

    public static Estimate? Performance(double opponentRating, int wins, int draws, int losses) =>
        Performance([new Result(opponentRating, wins, draws, losses)]);
}
