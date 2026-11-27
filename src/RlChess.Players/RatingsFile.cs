using System.Text;
using System.Text.Json;

namespace RlChess.Players;

/// <summary>
/// The ratings the Arena's <c>ratings</c> command works out, saved as a small JSON file that the
/// website reads (<c>src/RlChess.Web/wwwroot/ratings.json</c>) to show ratings on the bot cards.
/// Players are identified by their spec: <c>"random"</c> or <c>"minimax:4"</c> (id and level).
/// </summary>
public sealed class RatingsFile
{
    public sealed record Row(string Spec, string Name, double Rating, double Margin, int Games, double Score);

    public string Anchor { get; init; } = "random";
    public double AnchorRating { get; init; } = Engine.Ratings.RandomAnchor;
    public DateTime Updated { get; init; } = DateTime.UtcNow;
    public int GamesPerPairing { get; init; }
    /// <summary>True if anchored to calibrated Stockfish levels (engine-scale ratings), false if Random = 400.</summary>
    public bool EngineScale { get; init; }
    public List<Row> Players { get; init; } = new();

    /// <summary>The row for a player at a level (falls back to the player's default-level row), or null if unrated.</summary>
    public Row? Find(PlayerRegistry.Entry entry, int? level)
    {
        var spec = entry.Spec(level);
        return Players.FirstOrDefault(r => string.Equals(r.Spec, spec, StringComparison.OrdinalIgnoreCase));
    }

    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("anchor", Anchor);
            w.WriteNumber("anchorRating", AnchorRating);
            w.WriteString("updated", Updated.ToString("yyyy-MM-dd"));
            w.WriteNumber("gamesPerPairing", GamesPerPairing);
            w.WriteBoolean("engineScale", EngineScale);
            w.WriteStartArray("players");
            foreach (var r in Players)
            {
                w.WriteStartObject();
                w.WriteString("spec", r.Spec);
                w.WriteString("name", r.Name);
                w.WriteNumber("rating", Math.Round(r.Rating));
                w.WriteNumber("margin", double.IsFinite(r.Margin) ? Math.Round(r.Margin) : -1);
                w.WriteNumber("games", r.Games);
                w.WriteNumber("score", Math.Round(r.Score, 3));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray()) + "\n";
    }

    /// <summary>Reads the file; returns an empty set of ratings if the text can't be read.</summary>
    public static RatingsFile Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new RatingsFile();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var rows = new List<Row>();
            if (root.TryGetProperty("players", out var players))
                foreach (var p in players.EnumerateArray())
                    rows.Add(new Row(
                        p.GetProperty("spec").GetString() ?? "",
                        p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        p.GetProperty("rating").GetDouble(),
                        p.TryGetProperty("margin", out var m) ? m.GetDouble() : -1,
                        p.TryGetProperty("games", out var g) ? g.GetInt32() : 0,
                        p.TryGetProperty("score", out var s) ? s.GetDouble() : 0));
            return new RatingsFile
            {
                Anchor = root.TryGetProperty("anchor", out var a) ? a.GetString() ?? "random" : "random",
                AnchorRating = root.TryGetProperty("anchorRating", out var ar) ? ar.GetDouble() : Engine.Ratings.RandomAnchor,
                Updated = root.TryGetProperty("updated", out var u) && DateTime.TryParse(u.GetString(), out var d) ? d : DateTime.MinValue,
                GamesPerPairing = root.TryGetProperty("gamesPerPairing", out var gp) ? gp.GetInt32() : 0,
                EngineScale = root.TryGetProperty("engineScale", out var es) && es.ValueKind == JsonValueKind.True,
                Players = rows,
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return new RatingsFile();
        }
    }
}
