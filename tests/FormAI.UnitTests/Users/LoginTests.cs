using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Application.Users.Auth;
using FormAI.Domain.Entities;
using NSubstitute;
using static FormAI.UnitTests.Users.AuthTestData;

namespace FormAI.UnitTests.Users;

public class LoginTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtService _jwt = Substitute.For<IJwtService>();
    private readonly LoginHandler _handler;

    public LoginTests()
    {
        _handler = new LoginHandler(_users, _refreshTokens, _hasher, _jwt);

        _hasher.Verify(Password, Arg.Any<string>()).Returns(true);
        _jwt.GenerateAccessToken(Arg.Any<User>()).Returns("access-token");
        _jwt.GenerateRefreshToken().Returns("refresh-token");
    }

    private User GivenUser(bool verified)
    {
        var user = User.Create(Name, Email, "hash");
        if (verified)
            user.MarkAsVerified();

        _users.GetByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    [Fact]
    public async Task VerifiedUser_WithCorrectPassword_ReceivesTokens()
    {
        GivenUser(verified: true);

        var response = await _handler.HandleAsync(new LoginRequest(Email, Password));

        Assert.Equal("access-token", response.AccessToken);
        Assert.Equal("refresh-token", response.RefreshToken);
        await _refreshTokens.Received(1).AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnverifiedUser_WithCorrectPassword_IsRejectedWithEmailNotVerified_AndGetsNoTokens()
    {
        GivenUser(verified: false);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new LoginRequest(Email, Password)));

        Assert.Equal(ValidationErrorCode.EmailNotVerified, ex.Code);
        _jwt.DidNotReceive().GenerateAccessToken(Arg.Any<User>());
        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnverifiedUser_WithWrongPassword_GetsTheGenericError_NotTheVerificationOne()
    {
        GivenUser(verified: false);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new LoginRequest(Email, "wrong-password")));
    }

    [Fact]
    public async Task UnknownEmail_GetsTheGenericError()
    {
        _users.GetByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns((User?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new LoginRequest(Email, Password)));
    }
}
