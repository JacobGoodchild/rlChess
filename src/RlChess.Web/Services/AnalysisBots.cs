using RlChess.Engine;
using RlChess.Players;

namespace RlChess.Web.Services;

/// <summary>Your registered bots that can judge positions (they report a score while thinking).</summary>
public static class AnalysisBots
{
    private static IReadOnlyList<PlayerRegistry.Entry>? _all;

    public static IReadOnlyList<PlayerRegistry.Entry> All => _all ??= PlayerRegistry.OwnBots
        .Where(e =>
        {
            try { return PlayerAnalysis.CanAnalyse(e.CreatePlayer()); }
            catch { return false; }
        })
        .ToList();
}
