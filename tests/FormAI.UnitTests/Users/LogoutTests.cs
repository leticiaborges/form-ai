using FormAI.Application.Interfaces;
using FormAI.Application.Users.Auth;
using FormAI.Domain.Entities;
using NSubstitute;
using static FormAI.UnitTests.Users.AuthTestData;

namespace FormAI.UnitTests.Users;

public class LogoutTests
{
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IJwtService _jwtService = Substitute.For<IJwtService>();
    private readonly LogoutHandler _handler;

    private const string RawToken = "refresh-token";

    public LogoutTests()
    {
        _handler = new LogoutHandler(_refreshTokens, _jwtService);

        _jwtService.ConfigureRefreshTokens();
    }

    private RefreshToken GivenStoredToken(DateTime expiresAt)
    {
        var token = RefreshToken.Create(Guid.NewGuid(), HashOf(RawToken), expiresAt);
        _refreshTokens.GetByTokenHashAsync(HashOf(RawToken), Arg.Any<CancellationToken>()).Returns(token);
        return token;
    }

    [Fact]
    public async Task ActiveToken_IsRevoked()
    {
        var token = GivenStoredToken(DateTime.UtcNow.Add(RefreshTokenLifetime));

        await _handler.HandleAsync(new LogoutRequest(RawToken));

        await _refreshTokens.Received(1).RevokeAsync(token.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownToken_IsANoOp()
    {
        await _handler.HandleAsync(new LogoutRequest("never-issued"));

        await _refreshTokens.DidNotReceive().RevokeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExpiredToken_IsANoOp()
    {
        GivenStoredToken(DateTime.UtcNow.AddDays(-1));

        await _handler.HandleAsync(new LogoutRequest(RawToken));

        await _refreshTokens.DidNotReceive().RevokeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
