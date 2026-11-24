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
`--seed` (reproducible random players) and `--threads` (a number, or `all` for every CPU core).

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

## Using the website

- **New game:** play *vs computer* (pick the opponent, its level if it has levels, and your colour),
  *two players* on one device, or *computer vs computer* (watch two players, with pause and speed).
  Optional clocks: 1+0, 3+2, 5+3, 10+0 or custom. Computers' thinking time counts on their clock.
- **Moving:** drag, or click a piece then its square. While the computer thinks you can
  **premove**: your move is shown in blue and played the moment it's your turn, if still legal.
  Right-click or click an empty square to cancel it.
- **Analyse:** the button above the move list lets either side try other moves from any position;
  your line appears under the move list as a variation, and the real game is left untouched.
  (Paused during a game against the computer; not available mid-game with a clock.)
- **Other:** right-click/drag draws circles and arrows (Shift = red, Alt/Ctrl = blue). Keyboard:
  ← → step through moves, ↑ ↓ jump to the start/end, `f` flips the board, Esc cancels.
  The gear menu has board colours, piece sets and the computer think-time limit.
  Two-player games have a small "Offer draw" button.

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
