public class ClaudeSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-opus-4-5";
    public int MaxTokens { get; set; } = 4096;
}