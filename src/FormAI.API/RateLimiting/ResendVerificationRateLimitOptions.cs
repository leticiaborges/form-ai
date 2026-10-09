namespace FormAI.API.RateLimiting;

public class ResendVerificationRateLimitOptions : IRateLimitOptionsSlidingWindow
{
    public const string SectionName = "RateLimiting:ResendVerification";

    public int PermitLimit { get; set; } = 3;
    public int WindowMinutes { get; set; } = 15;
    public int SegmentsPerWindow { get; set; } = 3;
}
