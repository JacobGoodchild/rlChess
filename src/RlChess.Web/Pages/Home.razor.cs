using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using RlChess.Engine;
using RlChess.Engine.Players;
using RlChess.Web.Game;
using ChessGame = RlChess.Engine.Game;

namespace RlChess.Web.Pages;

public partial class Home : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    // --- game setup
    private ChessGame _game = new();
    private PlayerOption _opponent = PlayerOption.Computers[0];
    private Color _humanColor = Color.White;          // only meaningful against a computer
    private string _colorChoice = "white";             // "white" | "black" | "random"
    private IPlayer? _computer;

    // --- view state
    private readonly PieceTracker _tracker = new();
    private int _viewPly;                              // which position is shown (== moves count when live)
    private Color _orientation = Color.White;
    private bool _animate = true;
    private int _selected = -1;
    private HashSet<int>? _dests;
    private bool _clickedSelected;                     // pressed on an already-selected piece
    private (int From, int To)? _pendingPromotion;
    private bool _thinking;
    private int _generation;                           // bumps on new game / undo, cancels pending computer moves
    private CancellationTokenSource _cts = new();

    // --- UI bits
    private bool _muted;
    private bool _showThemes;
    private string _boardTheme = "brown";

    private static readonly (string Name, string Light, string Dark)[] BoardThemes =
    [
        ("brown", "#ecd9b9", "#ae8968"),
        ("blue", "#dee3e6", "#8ca2ad"),
        ("green", "#eeeed2", "#769656"),
        ("grey", "#e2e2e0", "#a4a39f"),
    ];

    private async Task SetBoardTheme(string name)
    {
        _boardTheme = name;
        _showThemes = false;
        await SaveSetting("board", name);
    }
    private bool _showNewGame;
    private bool _showFlash;
    private string _flash = "";
    private string _fenInput = "";
    private string? _fenError;
    private ElementReference _moveListEl;
    private IJSObjectReference? _module, _keys;
    private DotNetObjectReference<Home>? _self;

    private bool IsLive => _viewPly == _game.Moves.Count;
    private bool VsComputer => !_opponent.IsHuman;
    private bool HumanToMove => !_game.IsOver && (!VsComputer || _game.SideToMove == _humanColor);

    private Position _shown = new();

    protected override void OnInitialized()
    {
        _tracker.Reset(_game.Position);
        _shown = _game.Position;
        _fenInput = _game.Position.ToFen();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/board.js");
            _self = DotNetObjectReference.Create(this);
            _keys = await _module.InvokeAsync<IJSObjectReference>("attachKeys", _self);
            await _module.InvokeVoidAsync("preloadSounds", (object)new[] { "move", "capture", "check", "gameover" });

            _muted = await _module.InvokeAsync<string?>("getSetting", "muted") == "1";
            var savedBoard = await _module.InvokeAsync<string?>("getSetting", "board");
            if (BoardThemes.Any(t => t.Name == savedBoard)) _boardTheme = savedBoard!;
            var savedOpponent = await _module.InvokeAsync<string?>("getSetting", "opponent");
            var savedColor = await _module.InvokeAsync<string?>("getSetting", "color");
            _opponent = PlayerOption.Find(savedOpponent);
            if (savedColor is "white" or "black" or "random") _colorChoice = savedColor;
            StartGame(null);
            StateHasChanged();
        }
        else if (_module is not null)
        {
            await _module.InvokeVoidAsync("scrollMoveIntoView", _moveListEl);
        }
    }

    // ================================================================== game flow

    private void StartGame(string? fen)
    {
        _generation++;
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _thinking = false;

        _game = new ChessGame(fen);
        _computer = _opponent.CreateComputer?.Invoke();
        _humanColor = _colorChoice switch
        {
            "black" => Color.Black,
            "random" => Random.Shared.Next(2) == 0 ? Color.White : Color.Black,
            _ => Color.White,
        };
        _orientation = VsComputer ? _humanColor : Color.White;
        ClearSelection();
        _pendingPromotion = null;
        _tracker.Reset(_game.Position);
        GoToPly(_game.Moves.Count, animate: false, sound: false);
        _ = MaybeComputerMove();
    }

    private void NewGameFromDialog()
    {
        _showNewGame = false;
        _ = SaveSetting("opponent", _opponent.Id);
        _ = SaveSetting("color", _colorChoice);
        StartGame(null);
    }

    private void Rematch()
    {
        if (VsComputer && _colorChoice != "random") _colorChoice = _humanColor == Color.White ? "black" : "white";
        StartGame(_game.StartFen);
    }

    private void PlayMove(Move move, bool animate)
    {
        if (!IsLive) GoToPly(_game.Moves.Count, animate: false, sound: false);
        var before = _game.Position.Clone();
        _game.Play(move);
        _tracker.Apply(before, move);
        _viewPly = _game.Moves.Count;
        _shown = _game.Position;
        _animate = animate;
        _fenInput = _shown.ToFen();
        ClearSelection();
        PlayMoveSound(move, _shown);
        if (_game.IsOver) _ = Sound("gameover", delayMs: 250);
    }

    private async Task MaybeComputerMove()
    {
        if (!VsComputer || _computer is null || _game.IsOver || _game.SideToMove == _humanColor) return;

        int generation = _generation;
        var token = _cts.Token;
        _thinking = true;
        StateHasChanged();
        try
        {
            // A short, slightly varied pause so the reply feels natural rather than instant.
            await Task.Delay(Random.Shared.Next(350, 750), token);
            var move = await _computer.ChooseMoveAsync(_game.Position.Clone(), token);
            if (generation != _generation || token.IsCancellationRequested) return;
            _thinking = false;
            PlayMove(move, animate: true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (generation == _generation) _thinking = false;
        }
        StateHasChanged();
    }

    private void Undo()
    {
        if (_game.Moves.Count == 0) return;
        _generation++;
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _thinking = false;

        // Against a computer, take back until it's the human's turn again.
        do { _game.Undo(); }
        while (VsComputer && _game.Moves.Count > 0 && _game.SideToMove != _humanColor);

        _pendingPromotion = null;
        GoToPly(_game.Moves.Count, animate: true, sound: false);
        _ = MaybeComputerMove(); // e.g. human plays black and undid everything
    }

    /// <summary>There's something to take back (against a computer: at least one of the human's own moves).</summary>
    private bool CanUndo
    {
        get
        {
            if (!VsComputer) return _game.Moves.Count > 0;
            // The human has moved if the first ply was theirs, or there are at least two plies.
            var firstMover = Position.FromFen(_game.StartFen).SideToMove;
            return _game.Moves.Count >= 2 || (_game.Moves.Count == 1 && firstMover == _humanColor);
        }
    }

    private void Flip() => _orientation = _orientation.Opponent();

    // ================================================================== history navigation

    private void GoToPly(int ply, bool animate = true, bool sound = true)
    {
        ply = Math.Clamp(ply, 0, _game.Moves.Count);
        var target = ply == _game.Moves.Count ? _game.Position : _game.PositionAt(ply);

        if (ply == _viewPly + 1 && _viewPly < _game.Moves.Count)
        {
            var move = _game.Moves[_viewPly];
            _tracker.Apply(_shown, move);
            _animate = animate;
            if (sound) PlayMoveSound(move, target);
        }
        else
        {
            // Slide pieces to where they belong (backwards steps, jumps, undo).
            _tracker.SyncTo(target);
            _animate = animate;
        }
        _viewPly = ply;
        _shown = target;
        _fenInput = target.ToFen();
        _fenError = null;
        ClearSelection();
    }

    [JSInvokable]
    public Task OnKey(string key)
    {
        switch (key)
        {
            case "ArrowLeft": GoToPly(_viewPly - 1); break;
            case "ArrowRight": GoToPly(_viewPly + 1); break;
            case "ArrowUp": case "Home": GoToPly(0); break;
            case "ArrowDown": case "End": GoToPly(_game.Moves.Count); break;
            case "f": Flip(); break;
            case "Escape": ClearSelection(); _pendingPromotion = null; _showNewGame = false; _showThemes = false; break;
        }
        StateHasChanged();
        return Task.CompletedTask;
    }

    // ================================================================== board input

    private Color? MovableColor => IsLive && HumanToMove && _pendingPromotion is null ? _game.SideToMove : null;

    private void ClearSelection()
    {
        _selected = -1;
        _dests = null;
    }

    private void Select(int sq)
    {
        _selected = sq;
        _dests = _game.Position.GetLegalMoves().Where(m => m.From == sq).Select(m => m.To).ToHashSet();
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
            TryMove(_selected, sq, animate: true);
            return;
        }
        if (_game.Position[sq].IsColor(mine))
        {
            _clickedSelected = _selected == sq;
            Select(sq);
        }
        else
        {
            ClearSelection();
        }
    }

    private void OnSquareUp((int From, int To, bool Dragged) e)
    {
        if (e.Dragged)
        {
            if (e.To >= 0 && e.To != e.From && _selected == e.From && _dests!.Contains(e.To))
                TryMove(e.From, e.To, animate: false);
            // Otherwise the piece snaps back and stays selected.
        }
        else if (_clickedSelected && e.From == _selected)
        {
            ClearSelection(); // clicking a selected piece again deselects it
        }
        _clickedSelected = false;
    }

    private void TryMove(int from, int to, bool animate)
    {
        var candidates = _game.Position.GetLegalMoves().Where(m => m.From == from && m.To == to).ToList();
        if (candidates.Count == 0) return;
        if (candidates.Any(m => m.IsPromotion))
        {
            _pendingPromotion = (from, to);
            ClearSelection();
            return;
        }
        PlayMove(candidates[0], animate);
        _ = MaybeComputerMove();
    }

    private void OnPromotion(PieceType type)
    {
        if (_pendingPromotion is not (int from, int to)) return;
        _pendingPromotion = null;
        if (type == PieceType.None) return; // cancelled
        if (_game.Position.TryFindLegalMove(from, to, type, out var move))
        {
            PlayMove(move, animate: false);
            _ = MaybeComputerMove();
        }
    }

    // ================================================================== FEN / PGN / settings

    private void LoadFen()
    {
        try
        {
            var fen = _fenInput.Trim();
            var test = Position.FromFen(fen);
            _fenError = null;
            StartGame(test.ToFen());
            Flash("Position loaded");
        }
        catch (FormatException e)
        {
            _fenError = e.Message;
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
        var (white, black) = PlayerNames();
        if (await _module.InvokeAsync<bool>("copyText", _game.ToPgn(white, black))) Flash("PGN copied");
    }

    private async void Flash(string text)
    {
        _flash = text;
        _showFlash = true;
        StateHasChanged();
        await Task.Delay(1600);
        if (_flash == text) { _showFlash = false; StateHasChanged(); }
    }

    private async Task ToggleMute()
    {
        _muted = !_muted;
        await SaveSetting("muted", _muted ? "1" : "0");
    }

    private async Task SaveSetting(string key, string value)
    {
        if (_module is not null) await _module.InvokeVoidAsync("setSetting", key, value);
    }

    // ================================================================== sounds

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

    private (string White, string Black) PlayerNames()
    {
        if (!VsComputer) return ("White", "Black");
        var comp = _computer?.Name ?? _opponent.Name;
        return _humanColor == Color.White ? ("You", comp) : (comp, "You");
    }

    private string NameOf(Color c) => c == Color.White ? PlayerNames().White : PlayerNames().Black;

    private bool IsComputer(Color c) => VsComputer && c != _humanColor;

    private int CheckSquare => _shown.IsInCheck ? _shown.KingSquare(_shown.SideToMove) : -1;

    private Move? ShownLastMove => _viewPly > 0 ? _game.Moves[_viewPly - 1] : null;

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
            // Show the opponent's pieces we've "captured" more of, like lichess.
            for (int i = 0; i < diff; i++) extra.Add(t.Of(side.Opponent()));
        }
        return (extra, score);
    }

    private string StatusText()
    {
        if (_game.IsOver)
        {
            var r = _game.Result;
            return r.Outcome switch
            {
                GameOutcome.WhiteWins => "White is victorious",
                GameOutcome.BlackWins => "Black is victorious",
                _ => "Draw",
            };
        }
        if (_thinking) return $"{_computer?.Name ?? "Computer"} is thinking…";
        if (VsComputer) return _game.SideToMove == _humanColor ? "Your turn" : "Waiting for computer";
        return _game.SideToMove == Color.White ? "White to play" : "Black to play";
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

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            if (_keys is not null) { await _keys.InvokeVoidAsync("dispose"); await _keys.DisposeAsync(); }
            if (_module is not null) await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        _self?.Dispose();
    }
}
