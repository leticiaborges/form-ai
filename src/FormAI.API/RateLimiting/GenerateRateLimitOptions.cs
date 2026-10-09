namespace FormAI.API.RateLimiting;

public class GenerateRateLimitOptions : IRateLimitOptionsSlidingWindow
{
    public const string SectionName = "RateLimiting:Generate";

    public int PermitLimit { get; set; } = 10;
    public int WindowMinutes { get; set; } = 60;
    public int SegmentsPerWindow { get; set; } = 6;
}
