namespace RlChess.Engine.Players;

/// <summary>Picks a uniformly random legal move. Pass a seed to get the same games every time.</summary>
public sealed class RandomPlayer : IPlayer
{
    private readonly Random _random;

    public RandomPlayer(int? seed = null) => _random = seed is int s ? new Random(s) : new Random();

    public string Name => "Random";

    public Move ChooseMove(Position position)
    {
        Span<Move> moves = stackalloc Move[Position.MaxMoves];
        int n = position.GenerateLegalMoves(moves);
        if (n == 0) throw new InvalidOperationException("No legal moves: the game is already over.");
        return moves[_random.Next(n)];
    }
}
