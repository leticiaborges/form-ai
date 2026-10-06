using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Application.Users.Auth;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using NSubstitute;
using static FormAI.UnitTests.Users.AuthTestData;

namespace FormAI.UnitTests.Users;

public class RegisterTests
{
    private const string HashedPassword = "hashed-value";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserTokenConfirmationRepository _userTokens = Substitute.For<IUserTokenConfirmationRepository>();
    private readonly IConfirmationTokenGenerator _confirmationTokenGenerator = Substitute.For<IConfirmationTokenGenerator>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly RegisterHandler _handler;

    public RegisterTests()
    {
        _handler = new RegisterHandler(_users, _hasher, _userTokens, _emailService, _confirmationTokenGenerator);

        _hasher.Hash(Password).Returns(HashedPassword);
    }

    [Fact]
    public async Task ValidEmail_ReturnsUserInfo_AndPersistsHashedPasswordNotPlaintext()
    {
        User? createdUser = null;
        _ = _users.AddAsync(Arg.Do<User>(u => createdUser = u), Arg.Any<CancellationToken>());

        var response = await _handler.HandleAsync(new RegisterUserRequest(Name, Email, Password));

        Assert.Equal(Name, response.Name);
        Assert.Equal(Email, response.Email);

        Assert.NotNull(createdUser);
        Assert.Equal(HashedPassword, createdUser.PasswordHash);
        Assert.NotEqual(Password, createdUser.PasswordHash);

        await _users.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        _hasher.Received(1).Hash(Password);
        await _userTokens.Received(1).AddAsync(Arg.Any<UserConfirmationToken>(), Arg.Any<CancellationToken>());
        await _emailService.Received(1).SendVerificationEmailAsync(Email, Name, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidEmail_StoresGeneratedHash_EmailsGeneratedRawToken()
    {
        const string rawToken = "raw-token-value";
        const string tokenHash = "token-hash-value";
        _confirmationTokenGenerator.Generate().Returns((RawToken: rawToken, TokenHash: tokenHash));

        User? createdUser = null;
        UserConfirmationToken? storedToken = null;
        string? emailedRawToken = null;

        _ = _users.AddAsync(Arg.Do<User>(u => createdUser = u), Arg.Any<CancellationToken>());
        _ = _userTokens.AddAsync(Arg.Do<UserConfirmationToken>(t => storedToken = t), Arg.Any<CancellationToken>());
        _ = _emailService.SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Do<string>(t => emailedRawToken = t), Arg.Any<CancellationToken>());

        await _handler.HandleAsync(new RegisterUserRequest(Name, Email, Password));

        Assert.NotNull(createdUser);
        Assert.NotNull(storedToken);

        Assert.Equal(tokenHash, storedToken.TokenHash);
        Assert.Equal(rawToken, emailedRawToken);

        Assert.Equal(createdUser.Id, storedToken.UserId);
        Assert.Equal(TokenPurpose.EmailConfirmation, storedToken.Purpose);
    }

    [Fact]
    public async Task ExistingEmail_ThrowsException()
    {
        _users.EmailExistsAsync(Email, Arg.Any<CancellationToken>()).Returns(true);

        var ex = await Assert.ThrowsAsync<ValidationException>(
          () => _handler.HandleAsync(new RegisterUserRequest(Name, Email, Password)));

        Assert.Equal("This email is already registered.", ex.Errors["email"].FirstOrDefault());
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _userTokens.DidNotReceive().AddAsync(Arg.Any<UserConfirmationToken>(), Arg.Any<CancellationToken>());
        await _emailService.DidNotReceive().SendVerificationEmailAsync(Email, Name, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MixedCaseEmail_IsStoredAndCheckedLowercase()
    {
        User? createdUser = null;
        _ = _users.AddAsync(Arg.Do<User>(u => createdUser = u), Arg.Any<CancellationToken>());

        await _handler.HandleAsync(new RegisterUserRequest(Name, "  TestUser@Example.COM ", Password));

        await _users.Received(1).EmailExistsAsync(Email, Arg.Any<CancellationToken>());
        Assert.Equal(Email, createdUser!.Email);
    }

    [Theory]
    [InlineData("name", "A", "testuser@example.com", Password)]
    [InlineData("name", "   ", "testuser@example.com", Password)]
    [InlineData("email", Name, "not-an-email", Password)]
    [InlineData("email", Name, "user@localhost", Password)]
    [InlineData("password", Name, "testuser@example.com", "short1A")]
    [InlineData("password", Name, "testuser@example.com", "nouppercase1")]
    [InlineData("password", Name, "testuser@example.com", "NoNumberHere")]
    public async Task InvalidInput_IsRejectedBeforeAnythingIsSaved(string field, string name, string email, string password)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new RegisterUserRequest(name, email, password)));

        Assert.Contains(field, ex.Errors.Keys);
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _emailService.DidNotReceive().SendVerificationEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OverlongNameOrPassword_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            new RegisterUserRequest(new string('a', 101), Email, "A1" + new string('b', 71))));

        Assert.Contains("name", ex.Errors.Keys);
        Assert.Contains("password", ex.Errors.Keys);
    }
}
