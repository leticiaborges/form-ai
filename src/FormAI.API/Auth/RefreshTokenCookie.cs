namespace FormAI.API.Auth;

public static class RefreshTokenCookie
{
    public const string Name = "refresh_token";

    // Sent only to /api/auth/* (refresh and logout), never to the rest of the API.
    private const string CookiePath = "/api/auth";

    public static void Set(HttpResponse response, string refreshToken, DateTime expiresAtUtc)
    {
        var options = BaseOptions();
        options.Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc));
        response.Cookies.Append(Name, refreshToken, options);
    }

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, BaseOptions());

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static CookieOptions BaseOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
    };
}
