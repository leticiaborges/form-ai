using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Application.Users.Auth;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using NSubstitute;
using static FormAI.UnitTests.Users.AuthTestData;

namespace FormAI.UnitTests.Users;

public class ResendVerificationEmailTests
{
    private const string RawToken = "raw-token-value";
    private const string TokenHash = "token-hash-value";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserTokenConfirmationRepository _userTokens = Substitute.For<IUserTokenConfirmationRepository>();
    private readonly IConfirmationTokenGenerator _confirmationTokenGenerator = Substitute.For<IConfirmationTokenGenerator>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly ResendVerificationEmailHandler _handler;

    public ResendVerificationEmailTests()
    {
        _handler = new ResendVerificationEmailHandler(_users, _userTokens, _emailService, _confirmationTokenGenerator);
        _confirmationTokenGenerator.Generate().Returns((RawToken: RawToken, TokenHash: TokenHash));
    }

    // A user whose first confirmation window ended long ago, i.e. the one this feature exists for.
    private User UnverifiedUserWithExpiredWindow()
    {
        var user = User.Create(Name, Email, HashedPassword);
        typeof(User).GetProperty(nameof(User.ConfirmationSentAt))!.SetValue(user, DateTime.UtcNow.AddDays(-2));
        typeof(User).GetProperty(nameof(User.PendingRegistrationExpiresAt))!.SetValue(user, DateTime.UtcNow.AddDays(-2).AddMinutes(15));
        _users.GetByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    [Fact]
    public async Task UnverifiedUser_StoresFreshTokenExpiringWithTheRefreshedWindow()
    {
        var user = UnverifiedUserWithExpiredWindow();
        var stored = new List<UserConfirmationToken>();
        _ = _userTokens.AddAsync(Arg.Do<UserConfirmationToken>(stored.Add), Arg.Any<CancellationToken>());

        var before = DateTime.UtcNow;
        await _handler.HandleAsync(new ResendVerificationEmailRequest(Email));

        var token = Assert.Single(stored);
        Assert.Equal(user.Id, token.UserId);
        Assert.Equal(TokenPurpose.EmailConfirmation, token.Purpose);
        Assert.Equal(TokenHash, token.TokenHash);
        Assert.Equal(user.PendingRegistrationExpiresAt, token.ExpiresAt);
        Assert.InRange(token.ExpiresAt, before.AddMinutes(15), DateTime.UtcNow.AddMinutes(15));
    }

    [Fact]
    public async Task UnverifiedUser_RefreshesTheWindowBeforeTheTokenIsPersisted()
    {
        var user = UnverifiedUserWithExpiredWindow();
        DateTime? sentAtWhenPersisted = null;
        DateTime? expiresAtWhenPersisted = null;
        _ = _userTokens.AddAsync(Arg.Do<UserConfirmationToken>(_ =>
        {
            sentAtWhenPersisted = user.ConfirmationSentAt;
            expiresAtWhenPersisted = user.PendingRegistrationExpiresAt;
        }), Arg.Any<CancellationToken>());

        var before = DateTime.UtcNow;
        await _handler.HandleAsync(new ResendVerificationEmailRequest(Email));

        Assert.NotNull(sentAtWhenPersisted);
        Assert.True(sentAtWhenPersisted >= before, "ConfirmationSentAt was not moved forward before the token was persisted");
        Assert.True(expiresAtWhenPersisted >= before.AddMinutes(15), "PendingRegistrationExpiresAt was not moved forward before the token was persisted");
    }

    [Fact]
    public async Task UnverifiedUser_EmailsTheRawTokenToTheirAddress()
    {
        UnverifiedUserWithExpiredWindow();

        await _handler.HandleAsync(new ResendVerificationEmailRequest(Email));

        await _emailService.Received(1).SendVerificationEmailAsync(Email, Name, RawToken, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownEmail_DoesNothing()
    {
        _users.GetByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns((User?)null);

        await _handler.HandleAsync(new ResendVerificationEmailRequest(Email));

        await _userTokens.DidNotReceive().AddAsync(Arg.Any<UserConfirmationToken>(), Arg.Any<CancellationToken>());
        await _emailService.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifiedUser_DoesNothingAndChangesNoState()
    {
        var user = UnverifiedUserWithExpiredWindow();
        user.MarkAsVerified();
        var sentAt = user.ConfirmationSentAt;
        var expiresAt = user.PendingRegistrationExpiresAt;

        await _handler.HandleAsync(new ResendVerificationEmailRequest(Email));

        Assert.Equal(sentAt, user.ConfirmationSentAt);
        Assert.Equal(expiresAt, user.PendingRegistrationExpiresAt);
        await _userTokens.DidNotReceive().AddAsync(Arg.Any<UserConfirmationToken>(), Arg.Any<CancellationToken>());
        await _emailService.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingEmail_IsRejectedBeforeAnyLookup(string? email)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new ResendVerificationEmailRequest(email)));

        Assert.Equal(["Email is required."], ex.Errors["email"]);
        await _users.DidNotReceive().GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _userTokens.DidNotReceive().AddAsync(Arg.Any<UserConfirmationToken>(), Arg.Any<CancellationToken>());
        await _emailService.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
