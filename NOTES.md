# Notes for future sessions

Read this first. It's the hand-over log between work sessions.

## Status (end of session 1)

Done and working:
- **Engine** (`src/RlChess.Engine`): 64-square board ("mailbox") with pre-computed move tables,
  pseudo-legal generation + make/check/undo legality filter, Zobrist hashing, FEN in/out,
  SAN + UCI formatting/parsing, `Game` (move list, SAN, result, PGN export), all end-of-game rules.
  Perft matches every standard position (start, Kiwipete, positions 3-6) at the depths in
  `PerftTests.cs`. Roughly 20M positions/sec in perft on the dev container.
- **Players**: `IPlayer`, optional `IAsyncPlayer` (for slow players on the website),
  `RandomPlayer` (optional seed), `PlayerRegistry` (one line per player → shows on website + Arena),
  `MatchRunner.PlayGame`.
- **Arena** (`src/RlChess.Arena`): `--games --p1 --p2 --pgn --fen --max-plies --seed`.
  ~280 random-vs-random games/sec including SAN/PGN bookkeeping.
- **Website** (`src/RlChess.Web`): play vs Random (choose colour) or two players on one device;
  drag-and-drop and click-click, legal-move dots, last move, check highlight; promotion picker;
  SAN move list with history browsing (click a move, arrow keys, Home/End); undo (vs computer it
  takes back to your turn); flip (button or `f`); new game dialog; rematch; game-over result;
  sounds + mute (remembered); FEN load/copy, PGN copy; phone layout; light/dark follows the device.
- **CI/CD**: `.github/workflows/deploy.yml` builds, runs fast tests, publishes, rewrites
  `<base href>` to `/rlChess/`, adds `.nojekyll` and `404.html`, deploys to Pages.

## Decisions / gotchas

- The brief says "work on main": we push straight to `main`.
- Licence: MIT. Pieces are Cburnett's from Wikimedia Commons, used under their **BSD** option
  (verified on each file's Commons page: `{{self|GFDL|migration=relicense|BSD|GPL}}`). Lichess's
  copies are listed as GPL, so we deliberately took the Commons originals.
- Sounds are synthesised by `tools/make-sounds.sh` (ffmpeg) instead of downloaded: lichess's sounds
  are AGPL, and the CC0 sources (Kenney) block scripted downloads. They have not been listened
  to by a human yet; tweak the script and re-run if they sound off.
- En passant square is only stored (and written to FEN) when a pawn could actually capture.
  This keeps repetition detection correct. FEN round-trip tests expect this.
- Threefold and fifty-move draws are automatic, not claimed.
- `Game.Play` computes SAN for every move (needed for display/PGN). For RL self-play at max speed,
  use `Position` + `GenerateLegalMoves(Span<Move>)` directly and skip `Game`.
- The website runs single-threaded WebAssembly. A slow `IPlayer` freezes the page while it thinks;
  `IAsyncPlayer` + `await Task.Yield()` is the escape hatch. Real multi-threading would need
  cross-origin-isolation headers that GitHub Pages doesn't send.
- Local test of the published site: publish, copy `wwwroot` into a folder named `rlChess`, fix the
  base href, serve the parent folder (see the workflow's "Prepare for GitHub Pages" step).
- The SDK isn't preinstalled in the cloud container: install with `dotnet-install.sh --channel 10.0`.

## Ideas / next steps

- Owner's own work: minimax/alpha-beta player, then RL player (not ours to write).
- Possible polish: move animation for undo/back-navigation, premoves, board/piece theme picker,
  arrows/right-click annotations, "download PGN" button, clocks.
- Arena: parallel games across CPU cores; an Elo-difference estimate in the summary.
- A faster (bitboard) move generator later if RL training needs more speed; perft tests make
  swapping the internals safe.
