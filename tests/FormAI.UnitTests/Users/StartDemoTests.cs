using System.Text.RegularExpressions;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Application.Users.Auth;
using FormAI.Domain.Entities;
using NSubstitute;
using static FormAI.UnitTests.Users.AuthTestData;

namespace FormAI.UnitTests.Users;

public class StartDemoTests
{
    private const string DemoPassword = "DemoUser852*";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtService _jwt = Substitute.For<IJwtService>();

    public StartDemoTests()
    {
        _hasher.Hash(DemoPassword).Returns(HashedPassword);
        _jwt.GenerateAccessToken(Arg.Any<User>()).Returns("access-token");
        _jwt.GenerateRefreshToken().Returns("refresh-token");
        _jwt.ConfigureRefreshTokens();
    }

    private StartDemoHandler HandlerWith(string? password) =>
        new(_users, _refreshTokens, _hasher, _jwt, new DemoAccountSettings(password));

    private List<User> CaptureAddedUsers()
    {
        var added = new List<User>();
        _ = _users.AddAsync(Arg.Do<User>(added.Add), Arg.Any<CancellationToken>());
        return added;
    }

    [Fact]
    public async Task CreatesAVerifiedDemoUser()
    {
        var added = CaptureAddedUsers();

        await HandlerWith(DemoPassword).HandleAsync();

        var user = Assert.Single(added);
        Assert.True(user.IsDemo);
        Assert.True(user.IsEmailVerified);
        Assert.NotNull(user.VerifiedAt);
        Assert.Equal("Demo user", user.Name);
        Assert.Matches(new Regex(@"^demo-[0-9a-f-]{36}@demo\.invalid$"), user.Email);
    }

    [Fact]
    public async Task HashesTheConfiguredPassword()
    {
        var added = CaptureAddedUsers();

        await HandlerWith(DemoPassword).HandleAsync();

        Assert.Equal(HashedPassword, Assert.Single(added).PasswordHash);
        _hasher.Received(1).Hash(DemoPassword);
    }

    [Fact]
    public async Task IssuesTokensLikeLogin()
    {
        var added = CaptureAddedUsers();
        RefreshToken? stored = null;
        _ = _refreshTokens.AddAsync(Arg.Do<RefreshToken>(t => stored = t), Arg.Any<CancellationToken>());

        var before = DateTime.UtcNow;
        var tokens = await HandlerWith(DemoPassword).HandleAsync();
        var after = DateTime.UtcNow;

        var user = Assert.Single(added);
        _jwt.Received(1).GenerateAccessToken(user);
        Assert.Equal("access-token", tokens.AccessToken);
        Assert.Equal("refresh-token", tokens.RefreshToken);
        Assert.NotNull(stored);
        Assert.Equal(user.Id, stored.UserId);
        Assert.Equal(HashOf("refresh-token"), stored.TokenHash);
        Assert.InRange(stored.ExpiresAt, before + RefreshTokenLifetime, after + RefreshTokenLifetime);
        Assert.Equal(stored.ExpiresAt, tokens.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task EveryCallCreatesADistinctUser()
    {
        var added = CaptureAddedUsers();
        var handler = HandlerWith(DemoPassword);

        await handler.HandleAsync();
        await handler.HandleAsync();

        Assert.Equal(2, added.Count);
        Assert.NotEqual(added[0].Id, added[1].Id);
        Assert.NotEqual(added[0].Email, added[1].Email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankPasswordIsNotFoundAndCreatesNothing(string? password)
    {
        await Assert.ThrowsAsync<NotFoundException>(() => HandlerWith(password).HandleAsync());

        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }
}
