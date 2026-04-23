using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Application.Interfaces;

public interface IEmailService
{
   Task SendVerificationEmailAsync(string toEmail, string toName, string token, CancellationToken cancellationToken = default);
}
