

using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;

namespace FormAI.Application.Users.Auth;

public class RegisterHandler
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;

    public RegisterHandler(IUserRepository users, IPasswordHasher hasher)
    {
        _users = users;
        _hasher = hasher;
    }

    public async Task<RegisterUserResponse> HandleAsync(RegisterUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await _users.EmailExistsAsync(request.Email, cancellationToken))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["Email"] = ["This email is already registered."]
            });

        var hash = _hasher.Hash(request.Password);
        var user = User.Create(request.Name, request.Email, hash);

        await _users.AddAsync(user, cancellationToken);

        return new RegisterUserResponse(user.Id, user.Name, user.Email);
    }
}