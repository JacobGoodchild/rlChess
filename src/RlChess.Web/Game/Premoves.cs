using RlChess.Engine;

namespace RlChess.Web.Game;

/// <summary>
/// Where a piece could be premoved to. Like lichess, this is just the piece's movement pattern on an
/// empty board (the position will have changed by the time the premove is played); the premove is
/// only played if it's legal at that point.
/// </summary>
public static class Premoves
{
    private static readonly (int df, int dr)[] Knight = [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];
    private static readonly (int df, int dr)[] Straight = [(0, 1), (0, -1), (1, 0), (-1, 0)];
    private static readonly (int df, int dr)[] Diagonal = [(1, 1), (1, -1), (-1, 1), (-1, -1)];

    public static HashSet<int> Destinations(Position position, int from)
    {
        var result = new HashSet<int>();
        var piece = position[from];
        if (piece == Piece.None) return result;
        var color = piece.Color();
        int f = Square.File(from), r = Square.Rank(from);

        void Add(int ff, int rr) { if (ff is >= 0 and < 8 && rr is >= 0 and < 8) result.Add(Square.At(ff, rr)); }
        void Slide((int df, int dr)[] dirs)
        {
            foreach (var (df, dr) in dirs)
                for (int i = 1; i < 8; i++) Add(f + df * i, r + dr * i);
        }

        switch (piece.Type())
        {
            case PieceType.Pawn:
                int dir = color == Color.White ? 1 : -1;
                Add(f, r + dir);
                if (r == (color == Color.White ? 1 : 6)) Add(f, r + 2 * dir);
                Add(f - 1, r + dir);
                Add(f + 1, r + dir);
                break;
            case PieceType.Knight:
                foreach (var (df, dr) in Knight) Add(f + df, r + dr);
                break;
            case PieceType.Bishop: Slide(Diagonal); break;
            case PieceType.Rook: Slide(Straight); break;
            case PieceType.Queen: Slide(Straight); Slide(Diagonal); break;
            case PieceType.King:
                foreach (var (df, dr) in Straight.Concat(Diagonal)) Add(f + df, r + dr);
                int home = color == Color.White ? Square.E1 : Square.E8;
                if (from == home)
                {
                    var (k, q) = color == Color.White
                        ? (CastlingRights.WhiteKingside, CastlingRights.WhiteQueenside)
                        : (CastlingRights.BlackKingside, CastlingRights.BlackQueenside);
                    if ((position.Castling & k) != 0) result.Add(home + 2);
                    if ((position.Castling & q) != 0) result.Add(home - 2);
                }
                break;
        }
        return result;
    }
}
