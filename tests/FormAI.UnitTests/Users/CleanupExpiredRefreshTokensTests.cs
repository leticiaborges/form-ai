using FormAI.Application.Interfaces;
using FormAI.Application.Users.Auth;
using NSubstitute;

namespace FormAI.UnitTests.Users;

public class CleanupExpiredRefreshTokensTests
{
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly CleanupExpiredRefreshTokensHandler _handler;

    public CleanupExpiredRefreshTokensTests()
    {
        _handler = new CleanupExpiredRefreshTokensHandler(_refreshTokens);
    }

    [Fact]
    public async Task HandleAsync_DeletesTokensOlderThanRetentionWindow()
    {
        const int retentionDays = 10;
        _refreshTokens.DeleteInactiveTokensAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(3);

        var beforeCall = DateTime.UtcNow;
        var deleted = await _handler.HandleAsync(retentionDays);
        var afterCall = DateTime.UtcNow;

        Assert.Equal(3, deleted);

        await _refreshTokens.Received(1).DeleteInactiveTokensAsync(
            Arg.Is<DateTime>(cutoff =>
                cutoff >= beforeCall.AddDays(-retentionDays) &&
                cutoff <= afterCall.AddDays(-retentionDays)),
            Arg.Any<CancellationToken>());
    }
}
