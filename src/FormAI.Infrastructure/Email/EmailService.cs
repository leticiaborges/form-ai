using FormAI.Application.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FormAI.Infrastructure.Email;

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;

    public EmailService(IOptions<EmailSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task SendVerificationEmailAsync(string toEmail, string toName, string token, CancellationToken cancellationToken = default)
    {
        var verifyUrl = $"{_settings.FrontendBaseUrl}/verify-email?token={Uri.EscapeDataString(token)}";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = "Confirm your FormAI account";

        var bodyBuilder = new BodyBuilder
        {
            TextBody = $"Hi {toName},\n\nPlease verify your email by visiting:\n{verifyUrl}\n\nThis link expires in 24 hours.",
            HtmlBody = $"""
                <!DOCTYPE html>
                <html>
                <body style="font-family: sans-serif; color: #333;">
                  <h2>Welcome to FormAI, {toName}!</h2>
                  <p>Click the button below to confirm your email address.</p>
                  <p>
                    <a href="{verifyUrl}"
                       style="display:inline-block;padding:12px 24px;background:#6366f1;
                              color:#fff;border-radius:6px;text-decoration:none;font-weight:bold;">
                      Verify Email
                    </a>
                  </p>
                  <p style="color:#888;font-size:12px;">
                    If you did not create an account, you can safely ignore this email.<br>
                    This link expires in 15 minutes.
                  </p>
                </body>
                </html>
                """
        };

        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort,
            SecureSocketOptions.Auto, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_settings.Username))
            await client.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);


        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}