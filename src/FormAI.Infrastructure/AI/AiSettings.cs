namespace FormAI.Infrastructure.AI;

public class AiSettings
{
    public const string SectionName = "Ai";

    public string GatewayUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string TextAlias { get; set; } = "form-generator";
    public string VisionAlias { get; set; } = "form-generator-vision";
    public int MaxTokens { get; set; } = 4096;

    // Gateway worst case is 2 x 35 s = 70 s; this stays below the 90 s overall limit.
    public int TimeoutSeconds { get; set; } = 80;
}
