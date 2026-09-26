using FormAI.Application.Interfaces;
using FormAI.Application.Users.Auth;
using FormAI.Domain.Entities;
using NSubstitute;
using static FormAI.UnitTests.Users.AuthTestData;

namespace FormAI.UnitTests.Users;

public class RefreshTokenTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IJwtService _jwtService = Substitute.For<IJwtService>();
    private readonly RefreshTokenHandler _handler;
    private readonly RefreshToken _originalRefreshToken;
    private readonly User _defaultUser;

    private const string RefreshTokenTest = "refresh-token";
    private const string AccessToken = "access-token";
    private const string NewRefreshToken = "new-refresh-token";

    public RefreshTokenTests()
    {
        _handler = new RefreshTokenHandler(_users, _refreshTokens, _jwtService);

        _jwtService.ConfigureRefreshTokens();

        _defaultUser = User.Create("TestUser", Email, Password);
        _users.GetByIdAsync(_defaultUser.Id, Arg.Any<CancellationToken>()).Returns(_defaultUser);

        _originalRefreshToken = RefreshToken.Create(_defaultUser.Id, HashOf(RefreshTokenTest), DateTime.UtcNow.Add(RefreshTokenLifetime));
        _refreshTokens.GetByTokenHashAsync(HashOf(RefreshTokenTest), Arg.Any<CancellationToken>()).Returns(_originalRefreshToken);

        _jwtService.GenerateAccessToken(Arg.Any<User>()).Returns(AccessToken);
        _jwtService.GenerateRefreshToken().Returns(NewRefreshToken);
    }

    [Fact]
    public async Task ValidToken_ReturnsNewRefreshToken()
    {
        var request = new RefreshTokenRequest(RefreshTokenTest);
        RefreshToken? newRefreshToken = null;
        _ = _refreshTokens.AddAsync(Arg.Do<RefreshToken>(refresh => newRefreshToken = refresh), Arg.Any<CancellationToken>());

        var beforeCall = DateTime.UtcNow;
        var result = await _handler.HandleAsync(request);
        var afterCall = DateTime.UtcNow;

        Assert.Equal(AccessToken, result.AccessToken);
        Assert.Equal(NewRefreshToken, result.RefreshToken);
        Assert.Equal(newRefreshToken?.ExpiresAt, result.RefreshTokenExpiresAt);

        Assert.True(_originalRefreshToken.IsRevoked);
        Assert.NotNull(newRefreshToken);
        Assert.Equal(newRefreshToken.UserId, _defaultUser.Id);
        Assert.Equal(newRefreshToken.TokenHash, _originalRefreshToken.ReplacedByTokenHash);
        Assert.InRange(newRefreshToken.ExpiresAt, beforeCall.Add(RefreshTokenLifetime), afterCall.Add(RefreshTokenLifetime));

        await _refreshTokens.Received(1).AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidToken_StoresOnlyTheHashOfTheNewToken()
    {
        RefreshToken? newRefreshToken = null;
        _ = _refreshTokens.AddAsync(Arg.Do<RefreshToken>(refresh => newRefreshToken = refresh), Arg.Any<CancellationToken>());

        await _handler.HandleAsync(new RefreshTokenRequest(RefreshTokenTest));

        Assert.NotNull(newRefreshToken);
        Assert.Equal(HashOf(NewRefreshToken), newRefreshToken.TokenHash);
        Assert.NotEqual(NewRefreshToken, newRefreshToken.TokenHash);
    }

    [Fact]
    public async Task ExpiredToken_ThrowsUnauthorizedAccessException()
    {
        var expiredRefreshToken = "expired-refresh-token";
        var refreshTokenExpired = RefreshToken.Create(_defaultUser.Id, HashOf(expiredRefreshToken),
            DateTime.UtcNow.AddDays(-1));

        _refreshTokens.GetByTokenHashAsync(HashOf(expiredRefreshToken), Arg.Any<CancellationToken>()).Returns(refreshTokenExpired);

        var request = new RefreshTokenRequest(expiredRefreshToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _handler.HandleAsync(request));

        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokedToken_ThrowsUnauthorizedAccessException()
    {
        var revokedTokenStr = "revoked-refresh-token";
        var revokedToken = RefreshToken.Create(_defaultUser.Id, HashOf(revokedTokenStr), DateTime.UtcNow.Add(RefreshTokenLifetime));
        revokedToken.Revoke("some-other-token-hash");
        _refreshTokens.GetByTokenHashAsync(HashOf(revokedTokenStr), Arg.Any<CancellationToken>()).Returns(revokedToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _handler.HandleAsync(new RefreshTokenRequest(revokedTokenStr)));

        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownToken_ThrowsUnauthorizedAccessException()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _handler.HandleAsync(new RefreshTokenRequest("never-issued")));

        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }
}
