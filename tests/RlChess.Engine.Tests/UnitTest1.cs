namespace RlChess.Engine.Tests;

public class SmokeTests
{
    [Fact]
    public void EngineLoads() => Assert.Equal("rlChess", EngineInfo.Name);
}
