namespace FormAI.Application.Users.Auth;

/// <summary>The one password every demo account is created with. Blank turns the demo off.</summary>
public record DemoAccountSettings(string? Password);
