namespace FormAI.API.RateLimiting;

public class DemoRateLimitOptions
{
    public const string SectionName = "RateLimiting:Demo";

    public int PermitLimit { get; set; } = 3;
    public int WindowMinutes { get; set; } = 15;
    public int SegmentsPerWindow { get; set; } = 3;
}
