# rlChess

A chess rules engine in C#, plus a website to play on, built so you can plug in your own
computer players (minimax, and later a reinforcement-learning player trained by self-play).

**Play it:** https://jacobgoodchild.github.io/rlChess/

![The rlChess website mid-game](docs/screenshot.png)

## What's in here

| Folder | What it is |
| --- | --- |
| `src/RlChess.Engine` | The rules library: board, legal moves, check/mate/draws, FEN, SAN/UCI, PGN (read and write). No UI code. |
| `src/RlChess.Players` | The list of computer players the website and Arena offer (`PlayerRegistry`). Your players go here. |
| `src/RlChess.Web` | The website (Blazor WebAssembly), running the same engine in the browser. |
| `src/RlChess.Arena` | Console app that plays many games between two players and prints statistics. |
| `tests/RlChess.Engine.Tests` | Unit tests and perft tests (move-generation proofs). |

## Build, run, test

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Open `RlChess.sln` in Rider, or:

```bash
dotnet build
```

```bash
dotnet test --filter "Category!=Slow"
```

```bash
dotnet test --filter "Category=Slow"
```

```bash
dotnet run --project src/RlChess.Web
```

```bash
dotnet run --project src/RlChess.Arena -c Release -- --games 1000 --p1 random --p2 random --pgn games.pgn --threads all
```

Speed/correctness check of the move generator: `dotnet run --project src/RlChess.Arena -c Release -- perft 5`
(optionally followed by a FEN).

Arena options: `--games`, `--p1`, `--p2`, `--pgn <file>`, `--fen <start position>`, `--max-plies`,
`--seed` (reproducible random players) and `--threads` (a number, or `all` for every CPU core). Also:

- `--report results.html`: saves a results page with a chart of the score as the games went on
  (open it in any browser; it works offline).
- `--thinking`: prints every move with what the player reported (depth, positions, score, line),
  for debugging. Plays one game at a time.
- `--export data.csv`: saves every position as training data for a learning player. One row per
  position: `game,ply,fen,move,result`, where `move` is the move played (UCI, e.g. `e2e4`) and
  `result` is how the game ended *for the side to move* (1 win, 0 draw, -1 loss).

**How fast is my player?** `dotnet run --project src/RlChess.Arena -c Release -- bench minimax:4`
times it on 50 varied positions (`--positions N`, `--seed N`; the same seed gives the same
positions, so you can compare versions of your code). It prints the time per move. If your player
also implements `ISearchStats` (one property: how many positions it looked at for the last
move), it prints positions per second too.

The fast tests take about a second. The slow ones are deep perft runs (~600 million positions, about
10 seconds). Every push to `main` builds, runs the fast tests and deploys the website.

## How to add your own player

1. Write a class that implements `IPlayer`, in `src/RlChess.Players` (or in a new project that
   `RlChess.Players` references, which is handy if it needs extra packages):

   ```csharp
   using RlChess.Engine;
   using RlChess.Engine.Players;

   public sealed class MyPlayer : IPlayer
   {
       public string Name => "My player";

       public Move ChooseMove(Position position)
       {
           // `position` is your own copy: MakeMove/UndoMove on it freely while you search.
           var moves = position.GetLegalMoves();
           Move best = moves[0];
           // ... your evaluation and search go here ...
           return best;
       }
   }
   ```

2. Register it with one line in `src/RlChess.Players/PlayerRegistry.cs`, saying which group it
   belongs to on the website (`Random`, `Computer` for search-based players, or `AI` for learned ones):

   ```csharp
   new("mine", "My player", "What it does, in a few words.", PlayerCategory.Computer, _ => new MyPlayer()),
   ```

That's it: it now appears in the website's **New game** dialog and in the Arena
(`--p1 mine --p2 random`).

### Difficulty levels (optional)

If your player can play at different strengths, give the entry a `LevelRange` (lowest, highest,
default). The website then shows a level slider for it, and the chosen level is passed to your
create function. What a level *means* is up to you: search depth for minimax, or which trained
version of an RL network to load.

```csharp
new("minimax", "Minimax", "Alpha-beta search.", PlayerCategory.Computer,
    level => new MinimaxPlayer(depth: level), new LevelRange(1, 10, 4)),

new("rl", "RL network", "Trained by self-play.", PlayerCategory.AI,
    level => new RlPlayer($"models/generation-{level}.onnx"), new LevelRange(1, 20, 20)),
```

In the Arena, add the level after a colon: `--p1 minimax:6 --p2 rl:12`.

### Showing what your bot is thinking (optional)

Let your player also implement `IReportsThinking` (one property). Whenever you like, for example
after each depth of your search, call `Report` with whatever you know. Every field is optional:

```csharp
public sealed class MyPlayer : IPlayer, IReportsThinking
{
    public Action<SearchInfo>? Report { get; set; }   // set by the website/Arena; may be null

    public Move ChooseMove(Position position)
    {
        // ... inside your search, e.g. when a depth is finished:
        Report?.Invoke(new SearchInfo { Depth = depth, PositionsSearched = positions, BestMove = best });
        // You can also pass Score = EvalScore.FromCentipawns(35) (or EvalScore.MateIn(3)) and
        // Line = the moves you expect, e.g. [best, reply, ...].
    }
}
```

The website shows it live in a small panel (Settings → Advanced → *Bot thinking*), and the Arena
prints it for every move with `--thinking`. Positions per second is worked out for you.

### Saving a version of your bot ("Minimax v1")

So an old version stays playable and testable forever while you keep improving the current one:

1. Keep each bot in its own folder: `src/RlChess.Players/Bots/Minimax/`, with the namespace
   `RlChess.Players.Bots.Minimax`, and everything it needs inside that folder.
2. When you're happy with it, run from the repository root:

   ```bash
   python3 tools/freeze-bot.py Minimax v1
   ```

   It copies the folder to `src/RlChess.Players/Frozen/Minimax_v1/` and renames the namespace in the
   copy, so the old and new versions can live side by side.
3. Add one line to `PlayerRegistry.cs`: a copy of your bot's line with a new id, the frozen class
   and a version label (the script prints an example):

   ```csharp
   new("minimax-v1", "Minimax", "My first minimax.", PlayerCategory.Computer,
       level => new Frozen.Minimax_v1.MinimaxPlayer(depth: level), new LevelRange(1, 10, 4), Version: "v1"),
   ```

It then shows on the website as *Minimax v1*, and you can play it in the Arena (`--p1 minimax-v1`).
Don't edit frozen copies; freeze again as `v2` when you want a new snapshot.

### Ratings

```bash
dotnet run --project src/RlChess.Arena -c Release -- ratings --games 20
```

Every registered bot of yours (at its default level) plays every other bot, half the games with each colour.
`--games` is per pairing; `--players random,minimax:2,minimax:4,minimax-v1` picks who plays (handy to
rate several levels). It prints a table and saves `src/RlChess.Web/wwwroot/ratings.json`; push it and
the website shows the ratings on the bot cards (bots not in the file show "unrated").

Ratings are Elo-style and worked out from all the results together. **Random is fixed at 400**, so
numbers stay comparable between runs; a bot that beats Random 3 games out of 4 comes out around 590.
The ± is a rough 95% range, which shrinks with more games.

Handy engine calls: `position.GetLegalMoves()`, `MakeMove`/`UndoMove`, `IsInCheck`, `GetResult()`,
`position[square]` (the piece on a square; squares are 0–63 with a1 = 0, h8 = 63), `Hash`
(a 64-bit fingerprint, good for transposition tables), and the allocation-free
`GenerateLegalMoves(Span<Move>)` for fast search.

**Slow players on the website:** the browser runs everything on one thread, so a player that thinks
for several seconds freezes the page. If yours is slow, implement `IAsyncPlayer` instead and put
`await Task.Yield();` in your search loop now and then; the page stays responsive, and the
website can stop it when it runs out of time (it cancels the `CancellationToken` it passes you;
return your best move so far when that happens). The think-time limit is in the website's settings;
in games with a clock, the clock is the limit.

## How strong is my bot? (Stockfish as a reference opponent)

Ratings from the `ratings` command are only relative to Random. To tie your bots to something
real, play them against **Stockfish at a limited, calibrated strength**. It appears as its own
**Reference** group (website) and as `stockfish:<Elo>` (Arena), separate from your bots:

- `stockfish:1500`: Stockfish's `UCI_LimitStrength` + `UCI_Elo` setting, calibrated from 1320 to 3190.
- `stockfish-skill:5`: Stockfish's `Skill Level` (0–20) for weaker play. **Uncalibrated**: no rating
  can be worked out from games against it.

**On the website** choose *Stockfish* in the bot gallery (vs computer, computer vs computer, or the
Arena page) and set its Elo with the slider. It runs in the browser like the analysis engine. In the
Arena page, a match between your bot and calibrated Stockfish shows an *Estimated rating*.

**In the console Arena** Stockfish must be installed on your computer (it isn't part of this repository).
On Linux Mint / Ubuntu:

```bash
sudo apt install stockfish
```

It installs to `/usr/games/stockfish`, which the Arena finds by itself (otherwise use
`--stockfish-path /path/to/stockfish` or the `STOCKFISH_PATH` variable). Then:

```bash
dotnet run --project src/RlChess.Arena -c Release -- --games 20 --p1 minimax:3 --p2 stockfish:1320
```

After the results it prints *Estimated rating for Minimax (level 3): 1180 ± 150*. To test against
several levels at once and combine everything into one estimate:

```bash
dotnet run --project src/RlChess.Arena -c Release -- strength minimax:3 --levels 1320,1500,1700 --games 20
```

And to anchor the bot cards to Stockfish instead of Random, add Stockfish levels to the round robin
(they keep their calibrated ratings fixed and everyone else is rated against them; Random then gets
its own rating instead of the fixed 400):

```bash
dotnet run --project src/RlChess.Arena -c Release -- ratings --games 20 --stockfish 1320,1500
```

Stockfish thinks **1 second per move** (`--sf-time <ms>` to change it in the console). Each game runs
its own Stockfish program, so `--threads` still works.

**What the number means:** a *performance rating*: the rating at which your bot's score against those
Stockfish levels would be expected, with a rough 95% range (it narrows with more games). It's on the
**engine scale** (like the CCRL computer rating lists), **not** a chess.com or lichess rating, which
are measured against people and tend to be different. Caveats: Stockfish's Elo levels were calibrated
at long time controls (120 s + 1 s per move), so at 1 s per move it may play somewhat *below* its
label, making your bot look a bit stronger than it is; the website uses a lighter build ("lite"
network) that can be weaker still. Scores of 0% or 100% say little: pick levels where your bot scores
between about 20% and 80%.

**Licence:** Stockfish is GPLv3 and is never copied into this MIT repository: the console uses the
copy you installed, and the website downloads the official build at runtime (see below).

## Analysis engine (evaluation bar and game review)

These need something that can judge positions, so the website uses **Stockfish** like the big
chess sites do: the official "lite, single-threaded" WebAssembly build of Stockfish 19
([stockfish.js](https://github.com/nmrugg/stockfish.js), npm package `stockfish@19.0.1`), running in a
background worker so the page never freezes. It's only downloaded (about 1.8 MB, from jsDelivr) when
you switch on the evaluation bar or game review.

**Licence:** Stockfish is GPLv3. It isn't copied into this repository and isn't changed: your
browser downloads the official build from the npm CDN, and the site talks to it with plain-text UCI
commands, like any chess program talks to a separate engine. That keeps this repository MIT-licensed.
The settings menu credits Stockfish and links to it. (If you'd rather host the files yourself,
that's allowed too, as long as the GPL licence text and a link to the source go with them.)

**Using your own bot instead:** any registered bot that reports a `Score` (see "Showing what your bot
is thinking") appears under Settings → Advanced → *Analysis by*. The bar and review then use your
bot (`PlayerAnalysis` in the engine). For full control you can implement `IAnalysisEngine` yourself.
If your bot is slow, make it an `IAsyncPlayer` so it can be stopped after the time limit.

### Game review rules

Each move is judged by how much it changes the mover's **winning chances**. Engine scores become a
win % with lichess's formula (`50 + 50 × (2 / (1 + e^(−0.00368208 × centipawns)) − 1)`; mate = 100%),
then "loss" = win % before the move (with best play) − win % after the move played:

| Label | Rule |
| --- | --- |
| **Book** | A known opening move: every move so far reaches a named position in lichess's opening list. |
| **Brilliant** | A sacrifice (leaves a knight, bishop, rook or queen where it can be won for at least 2 points more than the move captured, not counting material that was already attacked), that loses under 2% and leaves you not worse (≥ 50%), when you weren't already completely winning. |
| **Great** | The only good move: loses under 2%, and the second-best move would have lost at least 15% more (needs Stockfish's second line). |
| **Best** | The engine's own choice (or loses under 0.5%). A forced move (the only legal move) is always Best. |
| **Excellent** | Loses under 2%. |
| **Good** | Loses under 5%. |
| **Miss** | The opponent's last move was a Mistake or Blunder, you were clearly better (≥ 60%), and this move gave back at least 10% (but you're not losing). |
| **Inaccuracy** | Loses 5–10%. |
| **Mistake** | Loses 10–15%. |
| **Blunder** | Loses 15% or more. |

(The 5 / 10 / 15 steps are lichess's.) **Accuracy** per move is lichess's formula
`103.1668 × e^(−0.04354 × loss) − 3.1669`; a player's game accuracy is the average of a
volatility-weighted mean and a harmonic mean of their moves, also like lichess. Review uses
Stockfish at depth 14 (or 1.5 s) per position with two lines.

Opening names come from [lichess's chess-openings list](https://github.com/lichess-org/chess-openings)
(public domain / CC0), built into `wwwroot/openings.tsv` with the Arena's `openings` command.

## Using the website

- **New game:** play *vs computer* (pick the opponent, its level if it has levels, and your colour),
  *two players* on one device, or *computer vs computer* (watch two players, with pause and speed).
  Optional clocks: 1+0, 3+2, 5+3, 10+0 or custom. Computers' thinking time counts on their clock.
- **Moving:** drag, or click a piece then its square. While the computer thinks you can
  **premove**, even several moves in a row: the board shows your pieces where they'll be, and
  each move is played the moment it's your turn. If one isn't legal by then, it and the rest of
  the queue are dropped. Right-click or click an empty square to cancel the queue.
- **Analyse:** the button above the move list lets either side try other moves from any position;
  your line appears under the move list as a variation, and the real game is left untouched.
  (Paused during a game against the computer; not available mid-game with a clock.)
- **Arena page:** pick two computer players, how many games and how many boards to show at once
  (up to 16), and watch the whole match live with a running score, a rating-difference estimate
  and a chart. Colours swap every game. Click a board to watch it bigger, or a finished game to
  look through it. Download all the games (PGN) or the training data (CSV, as above). The match
  pauses if you go to another page.
- **Games page:** every game you play is saved in this browser (newest 200). Open one to look
  through it, copy its PGN, or download them all. A game you leave unfinished is saved as unfinished.
- **Editor page:** set up any position (drag pieces, drag them off to remove, pick pieces from the
  bars to place them), choose who moves and castling rights, then *Play from here* or *Analyse*.
- **Loading games:** paste a PGN (e.g. from lichess or chess.com) or a FEN under
  "Load a game · position · export" on the Play page.
- **Choosing a bot:** the new-game dialog shows a card per bot, grouped Random / Computer / AI,
  with its description, levels and rating (from the Arena's `ratings` command).
- **Advanced features** (Settings → Advanced, all off by default): *Bot thinking* (a small live
  panel), *Evaluation bar* (beside the board; tap or hover for the score, "M3" = mate in 3),
  *Game review* (after a game, or for any game opened from Games/Arena/PGN: badges on every move,
  accuracy, a graph you can click to jump to a move, progress and Cancel while it runs), and
  *Opening names*.
- **Other:** right-click/drag draws circles and arrows (Shift = red, Alt/Ctrl = blue). Keyboard:
  ← → step through moves, ↑ ↓ jump to the start/end, `f` flips the board, Esc cancels.
  The gear menu has board colours (including wood and marble), piece sets, how fast pieces move,
  move hints when hovering over a piece, volume, and the computer think-time limit.
  Two-player games have a small "Offer draw" button. Going to another page and back keeps your game.

## Rules notes

- Draws by threefold repetition and the fifty-move rule are applied automatically (no claiming).
- Running out of time loses, unless the opponent couldn't possibly checkmate (e.g. a lone king,
  or king and knight against a bare king): then it's a draw. See `TimeoutRules` in the engine.
- Insufficient material: K v K, K + one minor piece v K, and positions where all remaining
  minor pieces are bishops on the same colour. (K+N v K+N is not counted: a mate is possible.)

## Credits & licences

- Code: [MIT](LICENSE).
- Chess pieces (each folder in `src/RlChess.Web/wwwroot/pieces/` has its licence file):
  - **Classic**: by [Colin M.L. Burnett](https://commons.wikimedia.org/wiki/User:Cburnett), from
    Wikimedia Commons, used under the BSD licence (one of the licences the author offers).
  - **Fantasy** and **Spatial**: from [Chess Art](https://github.com/maurimo/chess-art) by
    Maurizio Monge, MIT licence.
  - **Chessnut**: by [Alexis Luengas](https://github.com/LexLuengas/chessnut-pieces), Apache
    License 2.0.
- Sounds: wooden knocks from [Impact Sounds](https://kenney.nl/assets/impact-sounds) by
  [Kenney](https://www.kenney.nl), public domain (CC0); trimmed and mixed by `tools/make-sounds.sh`.
- Perft reference numbers: [Chess Programming Wiki](https://www.chessprogramming.org/Perft_Results).
