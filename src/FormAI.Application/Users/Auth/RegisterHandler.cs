
using System.Security.Cryptography;
using System.Text;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;

namespace FormAI.Application.Users.Auth;

public class RegisterHandler
{
    private readonly IUserRepository _users;
    private readonly IUserTokenConfirmationRepository _userTokens;
    private readonly IPasswordHasher _hasher;
    private readonly IEmailService _emailService;
    private readonly IConfirmationTokenGenerator _confirmationTokenGenerator;

    public RegisterHandler(IUserRepository users, IPasswordHasher hasher,
    IUserTokenConfirmationRepository userTokens, IEmailService emailService,
    IConfirmationTokenGenerator confirmationTokenGenerator)
    {
        _users = users;
        _hasher = hasher;
        _userTokens = userTokens;
        _emailService = emailService;
        _confirmationTokenGenerator = confirmationTokenGenerator;
    }

    public async Task<RegisterUserResponse> HandleAsync(RegisterUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await _users.EmailExistsAsync(request.Email, cancellationToken))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["email"] = ["This email is already registered."]
            });

        var hash = _hasher.Hash(request.Password);
        var user = User.Create(request.Name, request.Email, hash);
        user.SetConfirmationSent();

        await _users.AddAsync(user, cancellationToken);

        var resultToken = _confirmationTokenGenerator.Generate();

        var userToken = UserConfirmationToken.Create(user.Id, resultToken.TokenHash,
         Domain.Enums.TokenPurpose.EmailConfirmation,
        user.PendingRegistrationExpiresAt.GetValueOrDefault());

        await _userTokens.AddAsync(userToken, cancellationToken);

        await _emailService.SendVerificationEmailAsync(user.Email, user.Name, resultToken.RawToken, cancellationToken);

        return new RegisterUserResponse(user.Id, user.Name, user.Email);
    }
}