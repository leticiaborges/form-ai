using FormAI.API.Auth;
using FormAI.API.RateLimiting;
using FormAI.Application.Users.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FormAI.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly RegisterHandler _registerHandler;
    private readonly LoginHandler _loginHandler;
    private readonly RefreshTokenHandler _refreshTokenHandler;
    private readonly LogoutHandler _logoutHandler;
    private readonly VerifyEmailHandler _verifyEmailHandler;
    private readonly ResendVerificationEmailHandler _resendVerificationEmailHandler;
    private readonly StartDemoHandler _startDemoHandler;

    public AuthController(RegisterHandler registerHandler, LoginHandler loginHandler,
        RefreshTokenHandler refreshTokenHandler, LogoutHandler logoutHandler,
        VerifyEmailHandler verifyEmailHandler, ResendVerificationEmailHandler resendVerificationEmailHandler,
        StartDemoHandler startDemoHandler)
    {
        _registerHandler = registerHandler;
        _loginHandler = loginHandler;
        _refreshTokenHandler = refreshTokenHandler;
        _logoutHandler = logoutHandler;
        _verifyEmailHandler = verifyEmailHandler;
        _resendVerificationEmailHandler = resendVerificationEmailHandler;
        _startDemoHandler = startDemoHandler;
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
        var tokens = await _loginHandler.HandleAsync(request, cancellationToken);
        RefreshTokenCookie.Set(Response, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
        return Ok(new LoginResponse(tokens.AccessToken));
    }

    [HttpPost("demo")]
    [EnableRateLimiting(RateLimitPolicies.Demo)]
    public async Task<IActionResult> StartDemo(CancellationToken cancellationToken)
    {
        var tokens = await _startDemoHandler.HandleAsync(cancellationToken);
        RefreshTokenCookie.Set(Response, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
        return Ok(new LoginResponse(tokens.AccessToken));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var refreshToken = RefreshTokenCookie.Read(Request)
            ?? throw new UnauthorizedAccessException("Invalid refresh token");

        var tokens = await _refreshTokenHandler.HandleAsync(new RefreshTokenRequest(refreshToken), cancellationToken);
        RefreshTokenCookie.Set(Response, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
        return Ok(new LoginResponse(tokens.AccessToken));
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshToken = RefreshTokenCookie.Read(Request);
        if (refreshToken != null)
            await _logoutHandler.HandleAsync(new LogoutRequest(refreshToken), cancellationToken);

        RefreshTokenCookie.Clear(Response);
        return NoContent();
    }


    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody]VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        await _verifyEmailHandler.HandleAsync(request, cancellationToken);
        return Ok(new { message = "Email verified successfully." });
    }

    [HttpPost("resend-verification")]
    [EnableRateLimiting(RateLimitPolicies.ResendVerification)]
    public async Task<IActionResult> ResendVerification([FromBody]ResendVerificationEmailRequest request, CancellationToken cancellationToken)
    {
        await _resendVerificationEmailHandler.HandleAsync(request, cancellationToken);
        return Ok(new { message = "If an account exists for this email and isn't verified yet, we've sent a new link." });
    }
}
