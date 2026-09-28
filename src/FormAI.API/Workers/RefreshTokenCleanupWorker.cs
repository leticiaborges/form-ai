using FormAI.Application.Users.Auth;
using Microsoft.Extensions.Options;

namespace FormAI.API.Workers;

public class RefreshTokenCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<RefreshTokenCleanupOptions> _options;
    private readonly ILogger<RefreshTokenCleanupWorker> _logger;

    public RefreshTokenCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<RefreshTokenCleanupOptions> options,
        ILogger<RefreshTokenCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.Value.IntervalHours));

        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<CleanupExpiredRefreshTokensHandler>();
                var deleted = await handler.HandleAsync(_options.Value.RetentionDays, stoppingToken);

                _logger.LogInformation(
                    "Deleted {Count} inactive refresh tokens older than {RetentionDays} days",
                    deleted, _options.Value.RetentionDays);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Refresh token cleanup failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
