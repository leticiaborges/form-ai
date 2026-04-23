

using System.Security.Cryptography;
using System.Text;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;

namespace FormAI.Application.Users.Auth;

public class VerifyEmailHandler
{
    private readonly IUserRepository _users;
    private readonly IUserTokenConfirmationRepository _userTokens;

    public VerifyEmailHandler(IUserRepository users, IUserTokenConfirmationRepository userTokens)
    {
        _users = users;
        _userTokens = userTokens;
    }
    public async Task HandleAsync(VerifyEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["Token"] = ["Token is required."]
            });

        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));
            
        var userToken = await _userTokens.FindActiveAsync(tokenHash, Domain.Enums.TokenPurpose.EmailConfirmation,
         cancellationToken);

         if (userToken is null)
            throw new NotFoundException("Invalid or expired verification token.");

        var user = await _users.GetByIdAsync(userToken.UserId, cancellationToken);
        if (user == null)
            throw new NotFoundException("User was not found.");

        if (user.IsEmailVerified)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["Email"] = ["This email is already verified."]
            });

        userToken.MarkUsed();
        user.MarkAsVerified();
        
        await _userTokens.SaveChangesAsync();
    }
}