using FormAI.Application.Users.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FormAI.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly RegisterHandler _registerHandler;
    private readonly LoginHandler _loginHandler;
    private readonly RefreshTokenHandler _refreshTokenHandler;

    public AuthController(RegisterHandler registerHandler, LoginHandler loginHandler,
        RefreshTokenHandler refreshTokenHandler)
    {
        _registerHandler = registerHandler;
        _loginHandler = loginHandler;
        _refreshTokenHandler = refreshTokenHandler;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody]RegisterUserRequest request, CancellationToken cancellationToken)
    {
        var response = await _registerHandler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Register), response);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody]LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await _loginHandler.HandleAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody]RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var response = await _refreshTokenHandler.HandleAsync(request, cancellationToken);
        return Ok(response);
    }
}
