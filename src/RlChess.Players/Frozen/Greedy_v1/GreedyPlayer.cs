// Frozen copy of Greedy (v1). Don't edit: improve the live bot in Bots/Greedy instead.
using RlChess.Engine;
using RlChess.Engine.Players;

namespace RlChess.Players.Frozen.Greedy_v1;

public class GreedyPlayer : IPlayer
{
    private readonly Random _random;

    public GreedyPlayer(int? seed = null) => _random = seed is int s ? new Random(s) : new Random();

    public string Name => "Greedy";

    public int Evaluate(Position position)
    {
        int score = 0;
        
        for (int i = 0; i < 64; i++)
        {
            Piece piece = position[i]; 

            int pieceScore = piece.Type() switch
            {
                PieceType.Pawn => 100,
                PieceType.Knight => 300,
                PieceType.Bishop => 300,
                PieceType.Rook => 500,
                PieceType.Queen => 900,
                _ => 0
            };

            if (piece.Color() == Color.Black)
            {
                pieceScore *= -1;
            }
            
            score += pieceScore;
        }

        return score;
    }

    public Move ChooseMove(Position position)
    {
        var moves = position.GetLegalMoves();
        var sideToMove = position.SideToMove;

        Move bestMove = new Move();
        int bestScore;
        
        if (sideToMove == Color.White)
        {
            bestScore = int.MinValue;
        }
        else
        {
            bestScore = int.MaxValue;
        }

        foreach (Move move in moves)
        {
            position.MakeMove(move);
            int score = Evaluate(position);

            if (sideToMove == Color.White)
            {
                if (score > bestScore)
                {
                    bestMove = move;
                    bestScore = score;
                }
            }
            else
            {
                if (score < bestScore)
                {
                    bestMove = move;
                    bestScore = score;
                }
            }
            position.UndoMove();
        }

        return bestMove;
    }
}