using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Application.Users.Auth;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using NSubstitute;
using static FormAI.UnitTests.Users.AuthTestData;

namespace FormAI.UnitTests.Users;

public class VerifyEmailTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserTokenConfirmationRepository _userTokens = Substitute.For<IUserTokenConfirmationRepository>();
    private readonly IConfirmationTokenGenerator _confirmationTokenGenerator = Substitute.For<IConfirmationTokenGenerator>();
    private readonly VerifyEmailHandler _handler;

    private const string Token = "token";
    private const string HashedToken = "hashed-value";

    private User _defaultUser;
    private UserConfirmationToken _defaultUserToken;


    public VerifyEmailTests()
    {
        _handler = new VerifyEmailHandler(_users, _userTokens, _confirmationTokenGenerator);

        _defaultUser = User.Create(Name, Email, HashedPassword);
        _users.GetByIdAsync(_defaultUser.Id, Arg.Any<CancellationToken>()).Returns(_defaultUser);

        _confirmationTokenGenerator.GenerateHash(Token).Returns(HashedToken);

        _defaultUserToken = UserConfirmationToken.Create(_defaultUser.Id, HashedToken, TokenPurpose.EmailConfirmation, DateTime.UtcNow.AddDays(7));
        _userTokens.FindActiveAsync(HashedToken, TokenPurpose.EmailConfirmation, Arg.Any<CancellationToken>()).Returns(_defaultUserToken);
    }

    [Fact]
    public async Task ValidToken_VerifiesUserAndMarksTokenUsed()
    {
        var request = new VerifyEmailRequest(Token);

        await _handler.HandleAsync(request);

        await _userTokens.Received(1).FindActiveAsync(HashedToken, TokenPurpose.EmailConfirmation, Arg.Any<CancellationToken>());
        await _users.Received(1).GetByIdAsync(_defaultUser.Id, Arg.Any<CancellationToken>());
        await _userTokens.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        Assert.True(_defaultUser.IsEmailVerified);
        Assert.NotNull(_defaultUserToken.UsedAt);
    }

    [Fact]
    public async Task InvalidToken_ThrowsException()
    {
        var request = new VerifyEmailRequest("invalid-token");

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(request));

        await _userTokens.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlreadyConfirmed_ThrowsException()
    {
        var user = User.Create(Name, Email, HashedPassword);
        user.MarkAsVerified();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var userToken = UserConfirmationToken.Create(user.Id, HashedToken, TokenPurpose.EmailConfirmation, DateTime.UtcNow.AddDays(7));
        userToken.MarkUsed();
        _userTokens.FindActiveAsync(HashedToken, TokenPurpose.EmailConfirmation, Arg.Any<CancellationToken>()).Returns(userToken);

        var request = new VerifyEmailRequest(Token);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(request));

        await _userTokens.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
