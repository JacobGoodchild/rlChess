namespace RlChess.Engine.Players;

/// <summary>
/// Optional extra for players that think for a long time.
///
/// The website runs on a single thread in the browser, so a slow <see cref="IPlayer.ChooseMove"/>
/// freezes the page while it thinks. If your player implements this interface instead, the
/// website calls <see cref="ChooseMoveAsync"/>; put <c>await Task.Yield();</c> inside your search
/// every few thousand positions and the page stays responsive. The Arena just uses ChooseMove.
/// </summary>
public interface IAsyncPlayer : IPlayer
{
    Task<Move> ChooseMoveAsync(Position position, CancellationToken cancellationToken);
}

public static class PlayerExtensions
{
    /// <summary>Uses <see cref="IAsyncPlayer.ChooseMoveAsync"/> if the player supports it, else ChooseMove.</summary>
    public static Task<Move> ChooseMoveAsync(this IPlayer player, Position position, CancellationToken cancellationToken = default) =>
        player is IAsyncPlayer async
            ? async.ChooseMoveAsync(position, cancellationToken)
            : Task.FromResult(player.ChooseMove(position));
}
