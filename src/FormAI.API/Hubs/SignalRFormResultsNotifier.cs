using FormAI.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace FormAI.API.Hubs;

public class SignalRFormResultsNotifier : IFormResultsNotifier
{
    private readonly IHubContext<FormResultsHub, IFormResultsClient> _hub;
    private readonly ILogger<SignalRFormResultsNotifier> _logger;

    public SignalRFormResultsNotifier(IHubContext<FormResultsHub, IFormResultsClient> hub,
        ILogger<SignalRFormResultsNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyResultsChangedAsync(Guid formId,
         CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients.Group(FormResultsHub.GroupName(formId)).ResultsUpdated(formId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify results change for form: {formId}", formId);
        }
    }
}