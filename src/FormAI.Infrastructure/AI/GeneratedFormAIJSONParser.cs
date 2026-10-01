using System.Text.Json;
using System.Text.Json.Serialization;
using FormAI.Application.AI;
using FormAI.Application.Common.Exceptions;
using FormAI.Domain.Enums;

namespace FormAI.Infrastructure.AI;

public static class GeneratedFormAIJSONParser
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private sealed record FormAIJSON(List<FormAIJSONQuestion>? Questions);
    private sealed record FormAIJSONQuestion(string? Text, QuestionType? Type, string? CorrectAnswer, List<FormAIJSONOption>? Options);
    private sealed record FormAIJSONOption(string? Text, bool? IsCorrect);

    public static IReadOnlyList<GeneratedQuestion> Parse(string responseJson)
    {
        try
        {
            return ParseCore(responseJson);
        }
        catch (JsonException ex)
        {
            throw Invalid("The AI reply was not valid JSON.", ex);
        }
    }

    private static IReadOnlyList<GeneratedQuestion> ParseCore(string responseJson)
    {
        using var envelope = JsonDocument.Parse(responseJson);

        if (!envelope.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
            throw Invalid("The AI reply did not contain any choices.");

        var choice = choices[0];
        if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String
            && (finish.GetString() is "length" or "content_filter"))
            throw Invalid($"The AI reply stopped early ({finish.GetString()}).");

        if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
                  || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            throw Invalid("The gateway reply had no content.");

        var draft = JsonSerializer.Deserialize<FormAIJSON>(content.GetString()!, Json);

        if (draft?.Questions is not { Count: > 0 } questions)
            throw Invalid("The form had no questions.");

        return questions.Select(ToQuestion).ToList();
    }

    private static GeneratedQuestion ToQuestion(FormAIJSONQuestion q)
    {
        return new(Text: q.Text?.Trim() ?? string.Empty,
        Type: q.Type.GetValueOrDefault(),
        IsRequired: true,
        CorrectAnswer: string.IsNullOrWhiteSpace(q.CorrectAnswer) ? null : q.CorrectAnswer.Trim(),
        Options: (q.Options ?? [])
                .Select(o => new GeneratedOption(o.Text?.Trim() ?? string.Empty, o.IsCorrect)).ToList());
    }

    private static GenerationException Invalid(string detail, Exception? inner = null) =>
        new(ValidationErrorCode.GenerationOutputInvalid,
            "The AI returned a form we could not use. Please try again.",
            inner ?? new InvalidOperationException(detail));
}
