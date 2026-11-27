// Frozen copy of Minimax (v1). Don't edit: improve the live bot in Bots/Minimax instead.
using RlChess.Engine;
using RlChess.Engine.Players;

namespace RlChess.Players.Frozen.Minimax_v1;

public class MinimaxPlayer : IPlayer
{
    private readonly Random _random;

    
    public int Depth { get; set; }
    public string Name { get; set; } = "Minimax";
    
    public MinimaxPlayer(int depth)
    {
        Depth = depth;
        _random = new Random();
    }
    
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

    public int Minimax(Position position, int depth)
    {
        var moves = position.GetLegalMoves();
        Color sideToMove = position.SideToMove;
        int score;
        int bestScore;

        if (sideToMove == Color.White)
        {
            bestScore = int.MinValue;
        }
        else
        {
            bestScore = int.MaxValue;
        }

        //first check or checkmate/stalemate
        
        if (moves.Count == 0)
        {
            if (position.IsCheckmate)
            {
                if (sideToMove == Color.White)
                {
                    return -100_000;
                }
                else
                {
                    return 100_000;
                }
            }
            if (position.IsStalemate)
            {
                return 0;
            }
        }
        
        //base case

        if (depth == 0)
        {
            return Evaluate(position);
        }

        foreach (Move move in moves)
        {
            position.MakeMove(move);

            score = Minimax(position, depth - 1);

            position.UndoMove();

            if (sideToMove == Color.White)
            {
                if (score > bestScore)
                {
                    bestScore = score; // for future move analysis - new benchmark
                }
            }
            else
            {
                if (score < bestScore)
                {
                    bestScore = score;
                }
            }
        }

        return bestScore;
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
                score = Minimax(position, Depth - 1);
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

                    bestScore = score; // for future move analysis - new benchmark
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