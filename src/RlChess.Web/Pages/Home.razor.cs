using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using RlChess.Engine;
using RlChess.Engine.Players;
using RlChess.Players;
using RlChess.Web.Game;
using ChessGame = RlChess.Engine.Game;

namespace RlChess.Web.Pages;

/// <summary>
/// The play page. Each colour has a "seat": a person, or a computer player (registry entry + level).
/// <see cref="ScheduleNext"/> decides what happens after every change: a computer moves, a queued
/// premove is played, or we wait for a person.
/// </summary>
public partial class Home : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ------------------------------------------------------------------ game & seats
    private GameSetup _setup = new();                 // last choices in the dialog (remembered)
    private GameMode _mode = GameMode.VsComputer;     // mode of the game on the board
    private ChessGame _game = new();
    private readonly IPlayer?[] _bots = new IPlayer?[2];
    private readonly string[] _botLabels = ["", ""];
    private ChessClock? _clock;

    private bool IsBot(Color c) => _bots[(int)c] is not null;
    private bool IsHuman(Color c) => _bots[(int)c] is null;
    private Color HumanColor => IsHuman(Color.White) ? Color.White : Color.Black; // meaningful vs computer

    // ------------------------------------------------------------------ turn control
    private int _generation;                          // bumps whenever pending computer moves must be abandoned
    private CancellationTokenSource _cts = new();
    private bool _thinking;
    private bool _paused;                             // computer vs computer
    private string _speed = "normal";                 // computer vs computer: slow | normal | fast
    private string? _computerError;
    private TimeSpan? _thinkLimit = TimeSpan.FromSeconds(10); // per move, when there's no clock

    // ------------------------------------------------------------------ view & board input
    private readonly PieceTracker _tracker = new();
    private Position _shown = new();
    private int _viewPly;                             // main-line position shown (== moves count when live)
    private Color _orientation = Color.White;
    private bool _animate = true;
    private int _selected = -1;
    private HashSet<int>? _dests;
    private bool _destsArePremove;
    private bool _clickedSelected;
    private (int From, int To)? _pendingPromotion;
    private readonly List<(int From, int To)> _premoves = new(); // queued premoves, played in order
    private Piece[]? _virtual;                        // the board with queued premoves applied (null = none)

    // ------------------------------------------------------------------ analysis (one variation)
    private bool _analysing;
    private int _varBase = -1;                        // main-line ply the variation branches from
    private readonly List<Move> _varMoves = new();
    private readonly List<string> _varSan = new();
    private bool _inVar;                              // viewing the variation (else the main line)
    private int _varPly;                              // moves into the variation being shown

    // ------------------------------------------------------------------ draw offers (two players)
    private Color? _drawOfferBy;

    // ------------------------------------------------------------------ UI bits
    private bool _muted;
    private bool _showSettings;
    private bool _showNewGame;
    private string _boardTheme = "brown";
    private string _pieceSet = "cburnett";
    private bool _showFlash;
    private string _flash = "";
    private string _fenInput = "";
    private string? _fenError;
    private string _pgnInput = "";
    private string? _pgnError;
    private ElementReference _moveListEl;
    private IJSObjectReference? _module, _keys;
    private DotNetObjectReference<Home>? _self;
    private PeriodicTimer? _flagTimer;
    private bool _disposed;

    private bool IsLive => !_inVar && _viewPly == _game.Moves.Count;
    private bool Clocked => _clock is not null;

    protected override void OnInitialized()
    {
        _tracker.Reset(_game.Position);
        _shown = _game.Position;
        _fenInput = _shown.ToFen();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/board.js");
            _self = DotNetObjectReference.Create(this);
            _keys = await _module.InvokeAsync<IJSObjectReference>("attachKeys", _self);
            await _module.InvokeVoidAsync("preloadSounds", (object)new[] { "move", "capture", "check", "gameover" });

            _muted = await GetSetting("muted") == "1";
            var board = await GetSetting("board");
            if (Settings.BoardThemes.Any(t => t.Name == board)) _boardTheme = board!;
            var pieces = await GetSetting("pieces");
            if (Settings.PieceSets.Any(p => p.Id == pieces)) _pieceSet = pieces!;
            _thinkLimit = Settings.ParseThinkLimit(await GetSetting("thinklimit"));
            _setup = GameSetup.Deserialize(await GetSetting("setup"));
            StartGame(_setup);
            StateHasChanged();
            _ = WatchFlags();
        }
        else if (_module is not null)
        {
            await _module.InvokeVoidAsync("scrollMoveIntoView", _moveListEl);
        }
    }

    // ================================================================== starting games

    private void StartGame(GameSetup setup, string? fen = null, ChessGame? existing = null)
    {
        AbandonPendingWork();
        _mode = setup.Mode;
        _game = existing ?? new ChessGame(fen);
        _computerError = null;
        _paused = false;
        _premoves.Clear();
        _virtual = null;
        _drawOfferBy = null;
        ClearVariation();
        _analysing = false;

        _bots[0] = _bots[1] = null;
        switch (_mode)
        {
            case GameMode.VsComputer:
                var human = setup.Color switch
                {
                    "black" => Color.Black,
                    "random" => Random.Shared.Next(2) == 0 ? Color.White : Color.Black,
                    _ => Color.White,
                };
                SetBot(human.Opponent(), setup.Opponent);
                _orientation = human;
                break;
            case GameMode.ComputerVsComputer:
                SetBot(Color.White, setup.WhiteBot);
                SetBot(Color.Black, setup.BlackBot);
                _orientation = Color.White;
                break;
            default:
                _orientation = Color.White;
                break;
        }

        _clock = existing is null && setup.TimeControl is { } tc ? new ChessClock(tc) : null;

        ClearSelection();
        _pendingPromotion = null;
        _tracker.Reset(_game.Position);
        ShowMain(_game.Moves.Count, animate: false, sound: false);
        ScheduleNext();
    }

    private void SetBot(Color side, BotChoice choice)
    {
        _bots[(int)side] = choice.Entry.CreatePlayer(choice.EffectiveLevel);
        _botLabels[(int)side] = choice.Label;
    }

    private async Task OnDialogPlay(GameSetup setup)
    {
        _showNewGame = false;
        _setup = setup;
        await SaveSetting("setup", setup.Serialize());
        StartGame(setup);
    }

    private void Rematch()
    {
        // Swap sides, keep everything else (including a custom start position).
        var s = _setup.Clone();
        s.Mode = _mode;
        if (_mode == GameMode.VsComputer && s.Color != "random") s.Color = HumanColor == Color.White ? "black" : "white";
        if (_mode == GameMode.ComputerVsComputer) (s.WhiteBot, s.BlackBot) = (s.BlackBot, s.WhiteBot);
        if (_clock is null) s.Clock = "none";
        _setup = s;
        StartGame(s, _game.StartFen);
    }

    /// <summary>Cancels any computer thinking or waiting, so stale moves are never played.</summary>
    private void AbandonPendingWork()
    {
        _generation++;
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _thinking = false;
    }

    // ================================================================== what happens next

    private void ScheduleNext()
    {
        if (_game.IsOver || _analysing || _paused) return;
        var side = _game.SideToMove;
        if (IsBot(side)) _ = RunBotTurn(_generation);
        else TryPlayPremove();
    }

    private int CosmeticDelayMs() => _mode == GameMode.ComputerVsComputer
        ? _speed switch { "slow" => 1200, "fast" => 60, _ => 450 }
        : Random.Shared.Next(350, 750); // feels natural rather than instant

    private async Task RunBotTurn(int generation)
    {
        var side = _game.SideToMove;
        var bot = _bots[(int)side]!;
        var token = _cts.Token;
        _thinking = true;
        StateHasChanged();
        try
        {
            // The little pause before replying isn't "thinking", so it doesn't use the bot's clock.
            bool clockWasRunning = _clock?.Running == side;
            if (clockWasRunning) _clock!.Stop();
            await Task.Delay(CosmeticDelayMs(), token);
            if (generation != _generation) return;
            if (clockWasRunning) _clock!.Start(side);

            // Limit: the clock if there is one, else the think-time setting.
            TimeSpan? limit = _clock is { IsRunning: true } ? _clock.Remaining(side) : _clock is null ? _thinkLimit : null;
            using var thinkCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var sw = Stopwatch.StartNew();
            Task<Move> task;
            try { task = bot.ChooseMoveAsync(_game.Position.Clone(), thinkCts.Token); }
            catch (Exception e) when (e is not OperationCanceledException) { task = Task.FromException<Move>(e); }

            if (limit is TimeSpan lim && !task.IsCompleted)
            {
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var finished = await Task.WhenAny(task, Task.Delay(lim + TimeSpan.FromMilliseconds(30), delayCts.Token));
                delayCts.Cancel();
                if (generation != _generation) return;
                if (finished != task)
                {
                    thinkCts.Cancel(); // ask it to stop and return its best move so far
                    if (_clock is null && await Task.WhenAny(task, Task.Delay(1000, token)) == task && task.IsCompletedSuccessfully)
                    {
                        // It stopped politely in time: accept that move.
                    }
                    else
                    {
                        if (generation != _generation) return;
                        OutOfTime(side, thinkLimit: _clock is null);
                        return;
                    }
                }
            }

            var move = await task;
            if (generation != _generation) return;
            // A synchronous player can't be interrupted; check afterwards.
            if (_clock is null && _thinkLimit is TimeSpan tl && sw.Elapsed > tl + TimeSpan.FromSeconds(1)) { OutOfTime(side, thinkLimit: true); return; }
            if (_clock is not null && _clock.IsFlagged(side)) { OutOfTime(side, thinkLimit: false); return; }
            if (!_game.Position.IsLegal(move))
                throw new InvalidOperationException($"it chose an illegal move ({move.ToUci()}) in {_game.Position.ToFen()}");

            _thinking = false;
            PlayMove(move, animate: true, keepSelection: true);
            ScheduleNext();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (generation == _generation)
        {
            // A bug in a computer player shouldn't break the page: say what happened instead.
            // Full details go to the browser's developer console.
            Console.WriteLine($"[rlChess] Computer player '{bot.Name}' failed: {e}");
            _computerError = $"{bot.Name} failed: {e.Message}";
            _clock?.Stop();
        }
        finally
        {
            if (generation == _generation) _thinking = false;
        }
        StateHasChanged();
    }

    private void OutOfTime(Color side, bool thinkLimit)
    {
        AbandonPendingWork();
        _clock?.Stop();
        ClearPremoves();
        _game.End(thinkLimit
            ? GameResult.Win(side.Opponent(), GameEndReason.ThinkTimeExceeded)
            : TimeoutRules.Result(_game.Position, side));
        if (thinkLimit) _computerError = $"{_botLabels[(int)side]} took longer than {Settings.ThinkLimitLabel(_thinkLimit)} to move.";
        ShowMain(_game.Moves.Count, animate: false, sound: false);
        _ = Sound("gameover");
        StateHasChanged();
    }

    /// <summary>Watches the running clock for a flag fall (people can run out of time too).</summary>
    private async Task WatchFlags()
    {
        _flagTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (!_disposed && await _flagTimer.WaitForNextTickAsync())
            {
                if (_clock is { Running: Color side } c && c.IsFlagged(side) && !_game.IsOver)
                    OutOfTime(side, thinkLimit: false);
            }
        }
        catch (ObjectDisposedException) { }
    }

    /// <param name="keepSelection">
    /// For moves the person didn't just make (computer replies, premoves being played): keep any piece
    /// they're selecting or dragging, so a premove in progress isn't interrupted.
    /// </param>
    private void PlayMove(Move move, bool animate, bool keepSelection = false)
    {
        int keep = keepSelection ? _selected : -1;
        if (!IsLive) ShowMain(_game.Moves.Count, animate: false, sound: false);
        var mover = _game.SideToMove;
        var before = _game.Position.Clone();
        _game.Play(move);
        if (_premoves.Count > 0 || _virtual is not null) UpdateVirtual(animate); // board shows queued premoves
        else _tracker.Apply(before, move);
        _viewPly = _game.Moves.Count;
        _shown = _game.Position;
        _animate = animate;
        _fenInput = _shown.ToFen();
        ClearSelection();
        // Re-select the piece (with fresh destinations: real moves if it's now their turn, else premoves).
        if (keep >= 0 && MovableColor is Color mine && At(keep).IsColor(mine)) Select(keep);

        if (_clock is not null)
        {
            // The clock starts after the first move of the game (so nobody loses time before starting).
            if (_game.Moves.Count == 1) _clock.Start(mover.Opponent());
            else _clock.Press(mover);
            if (_game.IsOver) _clock.Stop();
        }
        if (_drawOfferBy is Color offerer && offerer != mover) _drawOfferBy = null; // moving instead of answering declines

        PlayMoveSound(move, _shown);
        if (_game.IsOver) _ = Sound("gameover", delayMs: 250);
    }

    private void TryPlayPremove()
    {
        if (_premoves.Count == 0) return;
        var (from, to) = _premoves[0];
        _premoves.RemoveAt(0);
        var piece = _game.Position[from];
        var promo = piece.Type() == PieceType.Pawn && Square.Rank(to) is 0 or 7 ? PieceType.Queen : PieceType.None;
        if (piece.IsColor(_game.SideToMove) && _game.Position.TryFindLegalMove(from, to, promo, out var move))
        {
            PlayMove(move, animate: true, keepSelection: true);
            ScheduleNext();
        }
        else
        {
            // Not legal any more: drop it and the rest of the queue (they were planned after it).
            _premoves.Clear();
            UpdateVirtual(animate: true);
        }
    }

    /// <summary>Shows the board with the queued premoves applied (or the real board when there are none).</summary>
    private void UpdateVirtual(bool animate)
    {
        if (_premoves.Count == 0)
        {
            if (_virtual is null) return;
            _virtual = null;
            _tracker.SyncTo(_shown);
        }
        else
        {
            var (board, applied) = VirtualBoard.Apply(_game.Position, _premoves, HumanColor);
            if (applied < _premoves.Count) _premoves.RemoveRange(applied, _premoves.Count - applied); // a premoved piece was captured
            _virtual = board;
            _tracker.SyncTo(sq => board[sq]);
        }
        _animate = animate;
    }

    private void ClearPremoves()
    {
        if (_premoves.Count == 0 && _virtual is null) return;
        _premoves.Clear();
        UpdateVirtual(animate: true);
    }

    /// <summary>The piece on a square as the player sees it (with queued premoves applied).</summary>
    private Piece At(int sq) => _virtual is not null ? _virtual[sq] : _shown[sq];

    // ================================================================== controls

    private bool CanUndo => !Clocked && !_analysing && _mode != GameMode.ComputerVsComputer && HumanMovesPlayed > 0;

    /// <summary>Plies played by people (all of them in a two-player game).</summary>
    private int HumanMovesPlayed
    {
        get
        {
            var side = Position.FromFen(_game.StartFen).SideToMove;
            int n = 0;
            for (int i = 0; i < _game.Moves.Count; i++, side = side.Opponent())
                if (IsHuman(side)) n++;
            return n;
        }
    }

    private void Undo()
    {
        if (!CanUndo) return;
        AbandonPendingWork();
        _computerError = null;
        ClearPremoves();
        _drawOfferBy = null;
        // Take back until it's a person's turn again (against a computer: your move and its reply).
        do { _game.Undo(); }
        while (_game.Moves.Count > 0 && IsBot(_game.SideToMove));
        _pendingPromotion = null;
        ShowMain(_game.Moves.Count, animate: true, sound: false);
        ScheduleNext();
    }

    private void Flip() => _orientation = _orientation.Opponent();

    private void Resign()
    {
        if (_game.IsOver || _mode == GameMode.ComputerVsComputer) return;
        AbandonPendingWork();
        _clock?.Stop();
        ClearPremoves();
        var loser = _mode == GameMode.VsComputer ? HumanColor : _game.SideToMove;
        _game.End(GameResult.Win(loser.Opponent(), GameEndReason.Resignation));
        ShowMain(_game.Moves.Count, animate: false, sound: false);
        _ = Sound("gameover");
    }

    private void TogglePause()
    {
        if (_mode != GameMode.ComputerVsComputer || _game.IsOver) return;
        if (_paused)
        {
            _paused = false;
            if (_clock is not null && _game.Moves.Count > 0) _clock.Start(_game.SideToMove);
            ScheduleNext();
        }
        else
        {
            _paused = true;
            AbandonPendingWork();
            _clock?.Stop();
        }
    }

    private void OfferDraw()
    {
        if (_mode != GameMode.TwoPlayers || _game.IsOver) return;
        _drawOfferBy = _game.SideToMove;
    }

    private void AnswerDraw(bool accept)
    {
        if (_drawOfferBy is null) return;
        _drawOfferBy = null;
        if (!accept) return;
        _clock?.Stop();
        _game.End(GameResult.Draw(GameEndReason.Agreement));
        ShowMain(_game.Moves.Count, animate: false, sound: false);
        _ = Sound("gameover");
    }

    // ================================================================== analysis

    /// <summary>Analysis pauses the game; it's not available mid-game when there's a clock.</summary>
    private bool CanAnalyse => _game.IsOver || !Clocked;

    private void ToggleAnalysis()
    {
        if (_analysing)
        {
            _analysing = false;
            ShowMain(_game.Moves.Count, animate: true, sound: false);
            ScheduleNext();
        }
        else if (CanAnalyse)
        {
            AbandonPendingWork();
            _analysing = true;
            ClearPremoves();
            _pendingPromotion = null;
            ClearSelection();
        }
    }

    private void ClearVariation()
    {
        _varBase = -1;
        _varMoves.Clear();
        _varSan.Clear();
        _inVar = false;
        _varPly = 0;
    }

    /// <summary>A move made while analysing: follows the main line or variation if it matches, else branches.</summary>
    private void AnalysisMove(Move move)
    {
        var before = _shown.Clone();
        if (!_inVar)
        {
            if (_viewPly < _game.Moves.Count && _game.Moves[_viewPly] == move)
            {
                ShowMain(_viewPly + 1, animate: true, sound: true);
                return;
            }
            ClearVariation();
            _varBase = _viewPly;
        }
        else
        {
            if (_varPly < _varMoves.Count && _varMoves[_varPly] == move)
            {
                ShowVar(_varPly + 1, animate: true, sound: true);
                return;
            }
            _varMoves.RemoveRange(_varPly, _varMoves.Count - _varPly);
            _varSan.RemoveRange(_varPly, _varSan.Count - _varPly);
        }
        _varSan.Add(Notation.ToSan(before, move));
        _varMoves.Add(move);
        ShowVar(_varMoves.Count, animate: !_lastInputWasDrag, sound: true);
    }

    // ================================================================== showing positions

    private Position VarPosition(int ply)
    {
        var p = _game.PositionAt(_varBase);
        for (int i = 0; i < ply; i++) p.MakeMove(_varMoves[i]);
        return p;
    }

    private void ShowMain(int ply, bool animate = true, bool sound = true)
    {
        ply = Math.Clamp(ply, 0, _game.Moves.Count);
        var target = ply == _game.Moves.Count ? _game.Position : _game.PositionAt(ply);
        Move? step = !_inVar && ply == _viewPly + 1 && _viewPly < _game.Moves.Count ? _game.Moves[_viewPly] : null;
        Show(target, step, animate, sound);
        _inVar = false;
        _viewPly = ply;
    }

    private void ShowVar(int ply, bool animate = true, bool sound = true)
    {
        if (_varBase < 0) return;
        ply = Math.Clamp(ply, 0, _varMoves.Count);
        if (ply == 0) { ShowMain(_varBase, animate, sound); return; }
        Move? step = null;
        if (_inVar && ply == _varPly + 1) step = _varMoves[_varPly];
        else if (!_inVar && _viewPly == _varBase && ply == 1) step = _varMoves[0];
        Show(VarPosition(ply), step, animate, sound);
        _inVar = true;
        _varPly = ply;
    }

    private void Show(Position target, Move? step, bool animate, bool sound)
    {
        _premoves.Clear(); // looking at another position cancels premoves
        _virtual = null;
        if (step is Move m)
        {
            _tracker.Apply(_shown, m);
            if (sound) PlayMoveSound(m, target);
        }
        else
        {
            _tracker.SyncTo(target); // slide pieces to where they belong (back, jumps, undo)
        }
        _animate = animate;
        _shown = target;
        _fenInput = target.ToFen();
        _fenError = null;
        ClearSelection();
    }

    private void StepBack()
    {
        if (_inVar) ShowVar(_varPly - 1);
        else ShowMain(_viewPly - 1);
    }

    private void StepForward()
    {
        if (_inVar) ShowVar(_varPly + 1);
        else ShowMain(_viewPly + 1);
    }

    private bool CanStepBack => _inVar || _viewPly > 0;
    private bool CanStepForward => _inVar ? _varPly < _varMoves.Count : _viewPly < _game.Moves.Count;

    [JSInvokable]
    public Task OnKey(string key)
    {
        switch (key)
        {
            case "ArrowLeft": StepBack(); break;
            case "ArrowRight": StepForward(); break;
            case "ArrowUp": case "Home": ShowMain(0); break;
            case "ArrowDown": case "End": ShowMain(_game.Moves.Count); break;
            case "f": Flip(); break;
            case "Escape":
                ClearSelection(); _pendingPromotion = null; ClearPremoves();
                _showNewGame = false; _showSettings = false;
                break;
        }
        StateHasChanged();
        return Task.CompletedTask;
    }

    // ================================================================== board input

    /// <summary>Who can be moved on the board right now (null = nobody).</summary>
    private Color? MovableColor
    {
        get
        {
            if (_pendingPromotion is not null) return null;
            if (_analysing) return _shown.HasLegalMoves() ? _shown.SideToMove : null;
            if (!IsLive || _game.IsOver || _paused) return null;
            if (IsHuman(_game.SideToMove)) return _game.SideToMove;
            if (_mode == GameMode.VsComputer) return HumanColor; // premove
            return null;
        }
    }

    private bool PremoveMode => !_analysing && IsLive && !_game.IsOver && _mode == GameMode.VsComputer && IsBot(_game.SideToMove);

    private bool _lastInputWasDrag;

    private void ClearSelection()
    {
        _selected = -1;
        _dests = null;
        _destsArePremove = false;
    }

    private void Select(int sq)
    {
        _selected = sq;
        _destsArePremove = PremoveMode;
        _dests = _destsArePremove
            ? Premoves.Destinations(At, _game.Position.Castling, sq)
            : _shown.GetLegalMoves().Where(m => m.From == sq).Select(m => m.To).ToHashSet();
    }

    private void OnSquareDown(int sq)
    {
        _clickedSelected = false;
        if (MovableColor is not Color mine)
        {
            ClearSelection();
            return;
        }
        if (_selected >= 0 && _dests!.Contains(sq))
        {
            _lastInputWasDrag = false;
            TryMove(_selected, sq, animate: true);
            return;
        }
        if (At(sq).IsColor(mine))
        {
            _clickedSelected = _selected == sq;
            Select(sq);
        }
        else
        {
            ClearSelection();
            ClearPremoves(); // clicking an empty (or enemy) square cancels the premoves
        }
    }

    private void OnSquareUp((int From, int To, bool Dragged) e)
    {
        if (e.Dragged)
        {
            if (e.To >= 0 && e.To != e.From && _selected == e.From && _dests!.Contains(e.To))
            {
                _lastInputWasDrag = true;
                TryMove(e.From, e.To, animate: false);
            }
            // Otherwise the piece snaps back and stays selected.
        }
        else if (_clickedSelected && e.From == _selected)
        {
            ClearSelection(); // clicking a selected piece again deselects it
        }
        _clickedSelected = false;
    }

    private void OnRightClick()
    {
        if (_premoves.Count == 0 && _selected < 0) return;
        ClearPremoves();
        ClearSelection();
    }

    private void TryMove(int from, int to, bool animate)
    {
        if (_destsArePremove)
        {
            _premoves.Add((from, to));
            ClearSelection();
            UpdateVirtual(animate);
            return;
        }
        var candidates = _shown.GetLegalMoves().Where(m => m.From == from && m.To == to).ToList();
        if (candidates.Count == 0) return;
        if (candidates.Any(m => m.IsPromotion))
        {
            _pendingPromotion = (from, to);
            ClearSelection();
            return;
        }
        Commit(candidates[0], animate);
    }

    private void Commit(Move move, bool animate)
    {
        if (_analysing)
        {
            AnalysisMove(move);
            return;
        }
        PlayMove(move, animate);
        ScheduleNext();
    }

    private void OnPromotion(PieceType type)
    {
        if (_pendingPromotion is not (int from, int to)) return;
        _pendingPromotion = null;
        if (type == PieceType.None) return; // cancelled
        if (_shown.TryFindLegalMove(from, to, type, out var move)) Commit(move, animate: false);
    }

    // ================================================================== FEN / PGN

    private void LoadFen()
    {
        try
        {
            var position = Position.FromFen(_fenInput.Trim());
            _fenError = null;
            var s = _setup.Clone();
            s.Mode = _mode;
            StartGame(s, position.ToFen());
            Flash("Position loaded");
        }
        catch (FormatException e)
        {
            _fenError = e.Message;
        }
    }

    /// <summary>Loads a pasted PGN as a two-player game, showing its final position.</summary>
    private void LoadPgn()
    {
        try
        {
            var parsed = Pgn.Parse(_pgnInput);
            _pgnError = null;
            StartGame(new GameSetup { Mode = GameMode.TwoPlayers }, existing: parsed.Game);
            _pgnInput = "";
            Flash($"Game loaded · {parsed.Game.Moves.Count} moves");
        }
        catch (FormatException e)
        {
            _pgnError = e.Message;
        }
    }

    private async Task CopyFen()
    {
        if (_module is null) return;
        if (await _module.InvokeAsync<bool>("copyText", _shown.ToFen())) Flash("FEN copied");
    }

    private async Task CopyPgn()
    {
        if (_module is null) return;
        if (await _module.InvokeAsync<bool>("copyText", _game.ToPgn(NameOf(Color.White), NameOf(Color.Black)))) Flash("PGN copied");
    }

    private async Task DownloadPgn()
    {
        if (_module is null) return;
        await _module.InvokeVoidAsync("downloadText", $"rlchess-{DateTime.Now:yyyy-MM-dd-HHmm}.pgn",
            _game.ToPgn(NameOf(Color.White), NameOf(Color.Black)));
    }

    // ================================================================== settings & feedback

    private async Task SetBoardTheme(string name)
    {
        _boardTheme = name;
        await SaveSetting("board", name);
    }

    private async Task SetPieceSet(string id)
    {
        _pieceSet = id;
        await SaveSetting("pieces", id);
    }

    private async Task SetThinkLimit(TimeSpan? limit)
    {
        _thinkLimit = limit;
        await SaveSetting("thinklimit", Settings.FormatThinkLimit(limit));
    }

    private async Task ToggleMute()
    {
        _muted = !_muted;
        await SaveSetting("muted", _muted ? "1" : "0");
    }

    private async void Flash(string text)
    {
        _flash = text;
        _showFlash = true;
        StateHasChanged();
        await Task.Delay(1600);
        if (_flash == text) { _showFlash = false; StateHasChanged(); }
    }

    private async Task<string?> GetSetting(string key) =>
        _module is null ? null : await _module.InvokeAsync<string?>("getSetting", key);

    private async Task SaveSetting(string key, string value)
    {
        if (_module is not null) await _module.InvokeVoidAsync("setSetting", key, value);
    }

    private void PlayMoveSound(Move move, Position after)
    {
        if (after.IsInCheck) _ = Sound("check");
        else if (move.IsCapture) _ = Sound("capture");
        else _ = Sound("move");
    }

    private async Task Sound(string name, int delayMs = 0)
    {
        if (_muted || _module is null) return;
        if (delayMs > 0) await Task.Delay(delayMs);
        await _module.InvokeVoidAsync("playSound", name);
    }

    // ================================================================== display helpers

    private string NameOf(Color c) => _mode switch
    {
        GameMode.TwoPlayers => c == Color.White ? "White" : "Black",
        _ => IsBot(c) ? _botLabels[(int)c] : "You",
    };

    private string ModeText => _mode switch
    {
        GameMode.VsComputer => $"vs {_botLabels[(int)HumanColor.Opponent()]}",
        GameMode.TwoPlayers => "Two players · same device",
        _ => "Computer vs computer",
    } + (_clock is null ? "" : $" · {_clock.Control.Name}");

    private int CheckSquare => _shown.IsInCheck ? _shown.KingSquare(_shown.SideToMove) : -1;

    private Move? ShownLastMove => _inVar
        ? (_varPly > 0 ? _varMoves[_varPly - 1] : null)
        : (_viewPly > 0 ? _game.Moves[_viewPly - 1] : null);

    private static readonly PieceType[] MaterialOrder = [PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight, PieceType.Pawn];

    private static int Value(PieceType t) => t switch
    {
        PieceType.Pawn => 1, PieceType.Knight => 3, PieceType.Bishop => 3, PieceType.Rook => 5, PieceType.Queen => 9, _ => 0,
    };

    /// <summary>The extra pieces <paramref name="side"/> has over the opponent, and the points difference.</summary>
    private (List<Piece> Extra, int Score) MaterialFor(Color side)
    {
        var counts = new int[2, 7];
        for (int sq = 0; sq < 64; sq++)
        {
            var p = _shown[sq];
            if (p != Piece.None) counts[(int)p.Color(), (int)p.Type()]++;
        }
        var extra = new List<Piece>();
        int score = 0;
        foreach (var t in MaterialOrder)
        {
            int diff = counts[(int)side, (int)t] - counts[(int)side.Opponent(), (int)t];
            score += diff * Value(t);
            for (int i = 0; i < diff; i++) extra.Add(t.Of(side.Opponent()));
        }
        return (extra, score);
    }

    private string StatusText()
    {
        if (_game.IsOver)
        {
            var r = _game.Result;
            if (r.Outcome == GameOutcome.Draw) return $"{r.ReasonText} · draw";
            var winner = r.Outcome == GameOutcome.WhiteWins ? Color.White : Color.Black;
            return _mode switch
            {
                GameMode.VsComputer => winner == HumanColor ? $"{r.ReasonText} · you won!" : $"{r.ReasonText} · {NameOf(winner)} wins",
                GameMode.ComputerVsComputer => $"{r.ReasonText} · {(winner == Color.White ? "White" : "Black")} ({NameOf(winner)}) wins",
                _ => $"{r.ReasonText} · {(winner == Color.White ? "White" : "Black")} wins",
            };
        }
        if (_computerError is not null) return _computerError;
        if (_paused) return "Paused";
        var side = _game.SideToMove;
        if (_thinking) return $"{NameOf(side)} is thinking…";
        var check = _game.Position.IsInCheck ? " · check!" : "";
        return _mode switch
        {
            GameMode.VsComputer => (IsHuman(side) ? "Your turn" : "Waiting for the computer") + check,
            _ => (side == Color.White ? "White to play" : "Black to play") + check,
        };
    }

    private IEnumerable<(int Number, int WhitePly, int BlackPly)> MoveRows()
    {
        var start = Position.FromFen(_game.StartFen);
        int n = start.FullmoveNumber;
        int ply = 0;
        int count = _game.Moves.Count;
        if (start.SideToMove == Color.Black && count > 0)
        {
            yield return (n++, -2, 0);
            ply = 1;
        }
        for (; ply < count; ply += 2)
            yield return (n++, ply, ply + 1 < count ? ply + 1 : -1);
    }

    /// <summary>The variation's moves with their numbers, e.g. "12… Nf6", "13. Bg5".</summary>
    private IEnumerable<(int Index, string Prefix, string San)> VariationMoves()
    {
        if (_varBase < 0) yield break;
        var start = _game.PositionAt(_varBase);
        int number = start.FullmoveNumber;
        bool white = start.SideToMove == Color.White;
        for (int i = 0; i < _varMoves.Count; i++)
        {
            string prefix = white ? $"{number}." : i == 0 ? $"{number}…" : "";
            yield return (i, prefix, _varSan[i]);
            if (!white) number++;
            white = !white;
        }
    }

    /// <summary>The variation is shown after the move-list row containing the main move it replaces.</summary>
    private int VariationRowPly => Math.Min(_varBase, Math.Max(0, _game.Moves.Count - 1));

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _cts.Cancel();
        _flagTimer?.Dispose();
        try
        {
            if (_keys is not null) { await _keys.InvokeVoidAsync("dispose"); await _keys.DisposeAsync(); }
            if (_module is not null) await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        _self?.Dispose();
    }
}
