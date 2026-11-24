using RlChess.Engine.Players;
using RlChess.Players;

namespace RlChess.Engine.Tests;

public class PlayerRegistryTests
{
    private sealed class LevelledPlayer(int level) : IPlayer
    {
        public int Level { get; } = level;
        public string Name => $"Levelled {Level}";
        public Move ChooseMove(Position position) => position.GetLegalMoves()[0];
    }

    private static readonly PlayerRegistry.Entry Levelled =
        new("lev", "Levelled", "test", PlayerCategory.Computer, l => new LevelledPlayer(l), new LevelRange(1, 10, 4));

    [Fact]
    public void RandomIsRegistered()
    {
        Assert.True(PlayerRegistry.TryParse("random", out var e, out var level, out _));
        Assert.Equal(PlayerCategory.Random, e.Category);
        Assert.Null(level);
        Assert.IsType<RandomPlayer>(e.CreatePlayer());
        Assert.Contains(e, PlayerRegistry.InCategory(PlayerCategory.Random));
    }

    [Theory]
    [InlineData("nobody", "Unknown player")]
    [InlineData("random:3", "doesn't have levels")]
    public void BadSpecsGiveClearErrors(string spec, string message)
    {
        Assert.False(PlayerRegistry.TryParse(spec, out _, out _, out var error));
        Assert.Contains(message, error);
    }

    [Theory]
    [InlineData(null, 4)]
    [InlineData(7, 7)]
    [InlineData(0, 1)]
    [InlineData(99, 10)]
    public void LevelsAreDefaultedAndClamped(int? requested, int expected)
    {
        var p = (LevelledPlayer)Levelled.CreatePlayer(requested);
        Assert.Equal(expected, p.Level);
    }

    [Fact]
    public void LabelShowsLevel() => Assert.Equal("Levelled (level 4)", Levelled.Label(null));
}
