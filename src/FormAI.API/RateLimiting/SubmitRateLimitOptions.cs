namespace FormAI.API.RateLimiting;

public class SubmitRateLimitOptions : IRateLimitOptionsSlidingWindow
{
    public const string SectionName = "RateLimiting:Submit";

    public int PermitLimit { get; set; } = 100;
    public int WindowMinutes { get; set; } = 10;
    public int SegmentsPerWindow { get; set; } = 5;
}
