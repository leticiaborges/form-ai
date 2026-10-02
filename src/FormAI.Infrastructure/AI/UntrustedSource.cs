using System.Security.Cryptography;

namespace FormAI.Infrastructure.AI;

/// <summary>
/// Fences source text the user supplied so the model can tell it apart from the instructions.
/// The marker is random per request, so a document cannot forge its own closing delimiter.
/// </summary>
public static class UntrustedSource
{
    public const string Label = "Source document";

    /// <summary>128 random bits as 32 lowercase hex characters.</summary>
    public static string NewMarker() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    /// <summary>The text is embedded as supplied: no escaping, no placeholder substitution.</summary>
    public static string Wrap(string sourceText, string marker) =>
        $"{Label}\n<<<SOURCE {marker}>>>\n{sourceText}\n<<<END SOURCE {marker}>>>";
}
