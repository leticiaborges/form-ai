namespace FormAI.API.Workers;

public class RefreshTokenCleanupOptions
{
    public int RetentionDays { get; set; } = 10;
    public int IntervalHours { get; set; } = 24;
}
