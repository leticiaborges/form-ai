using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;

namespace FormAI.Application.Users.Auth;

public class ResendVerificationEmailHandler
{
    private readonly IUserRepository _users;
    private readonly IUserTokenConfirmationRepository _userTokens;
    private readonly IEmailService _emailService;
    private readonly IConfirmationTokenGenerator _confirmationTokenGenerator;

    public ResendVerificationEmailHandler(IUserRepository users, IUserTokenConfirmationRepository userTokens,
        IEmailService emailService, IConfirmationTokenGenerator confirmationTokenGenerator)
    {
        _users = users;
        _userTokens = userTokens;
        _emailService = emailService;
        _confirmationTokenGenerator = confirmationTokenGenerator;
    }

    // Returns the same way for an unknown email, an already-verified one and a fresh resend, so the
    // endpoint cannot be used to find out which emails are registered.
    public async Task HandleAsync(ResendVerificationEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["email"] = ["Email is required."]
            });

        var user = await _users.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null || user.IsEmailVerified)
            return;

        // The user is tracked, so this change is saved by the SaveChangesAsync inside AddAsync below.
        user.SetConfirmationSent();

        var resultToken = _confirmationTokenGenerator.Generate();

        var userToken = UserConfirmationToken.Create(user.Id, resultToken.TokenHash,
            Domain.Enums.TokenPurpose.EmailConfirmation,
            user.PendingRegistrationExpiresAt.GetValueOrDefault());

        await _userTokens.AddAsync(userToken, cancellationToken);

        await _emailService.SendVerificationEmailAsync(user.Email, user.Name, resultToken.RawToken, cancellationToken);
    }
}
