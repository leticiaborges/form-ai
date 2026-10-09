namespace FormAI.API.RateLimiting;

public interface IRateLimitOptionsSlidingWindow
{
    int PermitLimit { get; set; }
    int WindowMinutes { get; set; }
    int SegmentsPerWindow { get; set; }
}
