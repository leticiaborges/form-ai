namespace FormAI.API.RateLimiting;

public class RegisterRateLimitOptions : IRateLimitOptionsSlidingWindow
{
    public const string SectionName = "RateLimiting:Register";

    public int PermitLimit { get; set; } = 5;
    public int WindowMinutes { get; set; } = 60;
    public int SegmentsPerWindow { get; set; } = 6;
}
