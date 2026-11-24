using RlChess.Engine;

namespace RlChess.Web.Game;

/// <summary>
/// Gives each piece on the board a stable id, so the page can keep the same HTML element
/// for a piece as it moves and let CSS slide it smoothly to its new square.
/// </summary>
public sealed class PieceTracker
{
    public sealed record TrackedPiece(int Id, Piece Piece, int Square);

    private readonly List<TrackedPiece> _pieces = new();
    private int _nextId = 1;

    public IReadOnlyList<TrackedPiece> Pieces => _pieces;

    /// <summary>Forget everything and lay out the pieces of <paramref name="position"/> afresh.</summary>
    public void Reset(Position position)
    {
        _pieces.Clear();
        for (int sq = 0; sq < 64; sq++)
            if (position[sq] != Piece.None) _pieces.Add(new TrackedPiece(_nextId++, position[sq], sq));
    }

    /// <summary>Updates the pieces for <paramref name="move"/> played in <paramref name="before"/>.</summary>
    public void Apply(Position before, Move move)
    {
        int capturedSq = move.IsEnPassant
            ? (before.SideToMove == Color.White ? move.To - 8 : move.To + 8)
            : move.To;
        _pieces.RemoveAll(p => p.Square == capturedSq && p.Square != move.From && move.IsCapture);

        int i = _pieces.FindIndex(p => p.Square == move.From);
        if (i >= 0)
        {
            var p = _pieces[i];
            var piece = move.IsPromotion ? move.Promotion.Of(p.Piece.Color()) : p.Piece;
            _pieces[i] = p with { Piece = piece, Square = move.To };
        }

        if (move.IsCastling)
        {
            int rookFrom = move.To > move.From ? move.From + 3 : move.From - 4;
            int rookTo = move.To > move.From ? move.From + 1 : move.From - 1;
            int r = _pieces.FindIndex(p => p.Square == rookFrom);
            if (r >= 0) _pieces[r] = _pieces[r] with { Square = rookTo };
        }
    }
}
