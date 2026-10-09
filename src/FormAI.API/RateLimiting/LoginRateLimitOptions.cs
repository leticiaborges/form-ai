namespace FormAI.API.RateLimiting;

public class LoginRateLimitOptions : IRateLimitOptionsSlidingWindow
{
    public const string SectionName = "RateLimiting:Login";

    public int PermitLimit { get; set; } = 10;
    public int WindowMinutes { get; set; } = 15;
    public int SegmentsPerWindow { get; set; } = 3;
}
