namespace FormAI.Infrastructure.Email;

public class EmailSettings
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string? Username { get; set; }
    public required string Password { get; set; }
    // Used to build the verification link inserted into the email body
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
}