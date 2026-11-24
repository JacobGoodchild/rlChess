using System.Text;
using System.Text.RegularExpressions;

namespace RlChess.Engine;

/// <summary>A game read from PGN: its tags (White, Black, Event, ...) and the replayed game.</summary>
public sealed record PgnGame(IReadOnlyDictionary<string, string> Tags, Game Game);

/// <summary>Reading PGN (the standard text format for chess games). Writing is <see cref="Game.ToPgn"/>.</summary>
public static partial class Pgn
{
    /// <summary>Reads the first game in <paramref name="text"/>.</summary>
    public static PgnGame Parse(string text) =>
        ParseAll(text).FirstOrDefault() ?? throw new FormatException("No game found in the PGN.");

    /// <summary>
    /// Reads every game in <paramref name="text"/> (e.g. a file saved by the Arena).
    /// Comments, variations and annotation symbols are skipped. Throws <see cref="FormatException"/>
    /// if a move is illegal or can't be understood.
    /// </summary>
    public static List<PgnGame> ParseAll(string text)
    {
        var games = new List<PgnGame>();
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var moveText = new StringBuilder();
        bool inBraceComment = false;

        void Flush()
        {
            if (tags.Count == 0 && string.IsNullOrWhiteSpace(moveText.ToString())) return;
            games.Add(new PgnGame(new Dictionary<string, string>(tags), BuildGame(tags, moveText.ToString())));
            tags.Clear();
            moveText.Clear();
        }

        foreach (var rawLine in text.Replace("\r", "").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('%')) continue; // escape lines
            var tag = TagRegex().Match(line);
            if (tag.Success)
            {
                // A tag after some moves means a new game has started.
                if (moveText.ToString().Trim().Length > 0) Flush();
                tags[tag.Groups[1].Value] = tag.Groups[2].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                continue;
            }
            moveText.Append(StripLineComment(line, ref inBraceComment)).Append(' ');
        }
        Flush();
        return games;
    }

    private static Game BuildGame(Dictionary<string, string> tags, string moveText)
    {
        var game = new Game(tags.TryGetValue("FEN", out var fen) ? fen : null);
        string? resultToken = null;

        foreach (var token in Tokenise(moveText))
        {
            if (token is "1-0" or "0-1" or "1/2-1/2" or "*") { resultToken = token; break; }
            if (game.IsOver) throw new FormatException($"Move '{token}' comes after the game has already ended.");
            if (!Notation.TryParseSan(game.Position, token, out var move) && !Notation.TryParseUci(game.Position, token, out move))
                throw new FormatException($"Illegal or unreadable move '{token}' after {game.Moves.Count} half-moves.");
            game.Play(move);
        }

        // Respect a decisive/drawn result recorded in the PGN (e.g. resignation) if the rules didn't end it.
        resultToken ??= tags.GetValueOrDefault("Result");
        if (!game.IsOver && resultToken is "1-0" or "0-1" or "1/2-1/2")
        {
            var reason = tags.GetValueOrDefault("Termination", "")
                .Contains("move limit", StringComparison.OrdinalIgnoreCase) ? GameEndReason.MoveLimit : GameEndReason.Resignation;
            game.End(resultToken switch
            {
                "1-0" => GameResult.Win(Color.White, reason),
                "0-1" => GameResult.Win(Color.Black, reason),
                _ => GameResult.Draw(reason == GameEndReason.MoveLimit ? reason : GameEndReason.None),
            });
        }
        return game;
    }

    /// <summary>Cuts a ";" comment (runs to the end of the line), ignoring ";" inside {braces}.</summary>
    private static string StripLineComment(string line, ref bool inBrace)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (inBrace) { if (line[i] == '}') inBrace = false; }
            else if (line[i] == '{') inBrace = true;
            else if (line[i] == ';') return line[..i];
        }
        return line;
    }

    private static IEnumerable<string> Tokenise(string moveText)
    {
        // Drop {comments}, (variations, possibly nested) and $NAGs. ';' comments were removed per line.
        var sb = new StringBuilder();
        int depth = 0;
        for (int i = 0; i < moveText.Length; i++)
        {
            char c = moveText[i];
            if (c == '{')
            {
                int end = moveText.IndexOf('}', i + 1);
                i = end < 0 ? moveText.Length : end;
                sb.Append(' ');
            }
            else if (c == '(') depth++;
            else if (c == ')') { if (depth > 0) depth--; sb.Append(' '); }
            else if (depth == 0) sb.Append(c);
        }

        foreach (var part in sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith('$')) continue;
            // Strip leading move numbers like "12." or "12..." (possibly glued to the move: "12.Nf3").
            var token = MoveNumberRegex().Replace(part, "");
            if (token.Length == 0) continue;
            yield return token;
        }
    }

    [GeneratedRegex("^\\[\\s*(\\w+)\\s+\"(.*)\"\\s*\\]$")]
    private static partial Regex TagRegex();

    [GeneratedRegex("^\\d+\\.+")]
    private static partial Regex MoveNumberRegex();
}
