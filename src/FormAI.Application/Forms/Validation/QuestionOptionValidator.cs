namespace FormAI.Application.Forms.Validation;

public static class QuestionOptionValidator
{
    /// <summary>
    /// Applies the option rules for one question: text is required, and text is
    /// unique within the question (trimmed, case-insensitive). Adds any problems
    /// to <paramref name="errors"/> under the key "questions[{questionIndex}].options".
    /// </summary>
    public static void Validate(
        int questionIndex,
        IReadOnlyList<string> optionTexts,
        Dictionary<string, string[]> errors)
    {
        var messages = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < optionTexts.Count; i++)
        {
            var text = optionTexts[i]?.Trim() ?? string.Empty;

            if (text.Length == 0)
                messages.Add($"Option {i + 1}: text is required.");
            else if (text.Length > 1024)
                messages.Add($"Option {i + 1}: text must be at most 1024 characters.");
            else if (!seen.Add(text))
                messages.Add($"Option {i + 1}: \"{text}\" is already used in this question.");
        }

        if (messages.Count > 0)
            errors[$"questions[{questionIndex}].options"] = messages.ToArray();
    }
}