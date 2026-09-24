using FormAI.Application.Common.Exceptions;
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

        _defaultUser = User.Create("TestUser", Email, Password);
        _users.GetByIdAsync(_defaultUser.Id, Arg.Any<CancellationToken>()).Returns(_defaultUser);

        _originalRefreshToken = RefreshToken.Create(_defaultUser.Id, RefreshTokenTest, DateTime.UtcNow.AddDays(RefreshToken.ExpiryDays));
        _refreshTokens.GetByTokenAsync(RefreshTokenTest, Arg.Any<CancellationToken>()).Returns(_originalRefreshToken);

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

        Assert.True(_originalRefreshToken.IsRevoked);
        Assert.NotNull(newRefreshToken);
        Assert.Equal(newRefreshToken.UserId, _defaultUser.Id);
        Assert.Equal(newRefreshToken.Token, _originalRefreshToken.ReplacedByToken);
        Assert.InRange(newRefreshToken.ExpiresAt, beforeCall.AddDays(RefreshToken.ExpiryDays), afterCall.AddDays(RefreshToken.ExpiryDays));

        await _refreshTokens.Received(1).AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExpiredToken_ThrowsNotFoundException()
    {
        var expiredRefreshToken = "expired-refresh-token";
        var refreshTokenExpired = RefreshToken.Create(_defaultUser.Id, expiredRefreshToken,
            DateTime.UtcNow.AddDays(-1));

        _refreshTokens.GetByTokenAsync(expiredRefreshToken, Arg.Any<CancellationToken>()).Returns(refreshTokenExpired);

        var request = new RefreshTokenRequest(expiredRefreshToken);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(request));

        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokedToken_ThrowsNotFoundException()
    {
        var revokedTokenStr = "revoked-refresh-token";
        var revokedToken = RefreshToken.Create(_defaultUser.Id, revokedTokenStr, DateTime.UtcNow.AddDays(RefreshToken.ExpiryDays));
        revokedToken.Revoke("some-other-token");
        _refreshTokens.GetByTokenAsync(revokedTokenStr, Arg.Any<CancellationToken>()).Returns(revokedToken);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new RefreshTokenRequest(revokedTokenStr)));

        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }
}
