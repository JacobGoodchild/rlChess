// Frozen copy of Greedy (v2). Don't edit: improve the live bot in Bots/Greedy instead.
using RlChess.Engine;
using RlChess.Engine.Players;

namespace RlChess.Players.Frozen.Greedy_v2;

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

        List<Move> bestMoves = new List<Move>();
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
            int score = 0;
            
            position.MakeMove(move);
            
            if (position.IsCheckmate) //check for checkmate
            {
                if (sideToMove == Color.White)
                {
                    score = 100000;
                }
                else
                {
                    score = -100000;
                }
            }
            else if (position.IsStalemate) //check for stalemate
            {
                score = 0;
            }
            else //otherwise just evaluate normally
            {
                score = Evaluate(position);
            }
            
            //now add equal move to list or clear the list and add the better move
            
            if (sideToMove == Color.White)
            {
                if (score == bestScore)
                {
                    bestMoves.Add(move);
                }
                else if (score > bestScore)
                {
                    bestMoves.Clear();
                    bestMoves.Add(move);

                    bestScore = score; // for future move analysis
                }
            }
            else
            {
                if (score == bestScore)
                {
                    bestMoves.Add(move);
                }
                else if (score < bestScore)
                {
                    bestMoves.Clear();
                    bestMoves.Add(move);

                    bestScore = score;
                }
            }

            position.UndoMove();
        }
        
        return bestMoves[_random.Next(0, bestMoves.Count)];
        
    }
}