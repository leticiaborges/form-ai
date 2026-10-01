using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FormAI.Application.AI;
using FormAI.Domain.Enums;
using Humanizer;
using Microsoft.Extensions.Options;

namespace FormAI.Infrastructure.AI;

public class GatewayFormGenerationService : IFormGenerationService
{
    public const int PromptVersion = 1;

    private readonly HttpClient _client;
    private readonly AiSettings _settings;

    public GatewayFormGenerationService(HttpClient client, IOptions<AiSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<GeneratedQuestion>> GenerateAsync(string sourceText, GenerationParameters parameters,
     Guid userId, CancellationToken cancellationToken = default)
    {
        if (_client.BaseAddress is null || string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new InvalidOperationException("Ai:GatewayUrl and Ai:ApiKey must be set to generate a form.");

        var request = new
        {
            model = _settings.TextAlias,
            max_tokens = _settings.MaxTokens,
            // The user's guid only, never an email or a name (docker/litellm/README.md, Metadata).
            user = userId.ToString(),
            messages = new object[]
            {
                new { role = "system", content = await BuildSystemPrompt(parameters, cancellationToken) },
                new { role = "user", content = BuildUserPrompt(sourceText, parameters) }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        using var response = await _client.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseResponse(responseJson);
    }

    private static async Task<string> BuildSystemPrompt(GenerationParameters parameters, CancellationToken cancellationToken)
    {
        var allowedTypes = parameters.AllowedTypes is { Length: > 0 } ?
        string.Join(",", parameters.AllowedTypes) : string.Join(",", Enum.GetValues<QuestionType>());

        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
        "AI", "Prompt", "PromptGenerateForm.txt");
        var prompt = await File.ReadAllTextAsync(path, cancellationToken);

        prompt = prompt.Replace("{questionCount}", parameters.QuestionCount.ToString());
        prompt = prompt.Replace("{allowedTypes}", allowedTypes);
        prompt = prompt.Replace("{difficultyLevel}", parameters.DifficultyLevel.ToString());
        prompt = prompt.Replace("{markCorrect}", parameters.IncludeCorrectAnswers.ToString());

        return prompt;
    }

    private static string BuildUserPrompt(string sourceText, GenerationParameters parameters)
    {
        return $"User text:\n\n{sourceText}";
    }

    private static IReadOnlyList<GeneratedQuestion> ParseResponse(string responseJSON)
    {
        // OpenAI-compatible envelope: choices[0].message.content (Anthropic's was content[0].text).
        using var apiDoc = JsonDocument.Parse(responseJSON);
        var aiReplyText = apiDoc.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString()
         ?? throw new InvalidOperationException("The gateway returned empty content.");

        string cleaned = Regex.Replace(
            aiReplyText,
            @"\A```json\s*|\s*```\z",
            ""
        , RegexOptions.None, TimeSpan.FromMilliseconds(1000)).Trim();

        using var questionsDoc = JsonDocument.Parse(cleaned);
        var questionsArray = questionsDoc.RootElement.GetProperty("questions");

        var results = new List<GeneratedQuestion>();
        foreach (var question in questionsArray.EnumerateArray())
        {
            var questionType = Enum.Parse<QuestionType>(question.GetProperty("type").GetString() ?? "");

            var options = question.GetProperty("options").EnumerateArray().Select(q =>
            new GeneratedOption(q.GetProperty("text").GetString()!.Truncate(1024),
             q.GetProperty("isCorrect").ValueKind == JsonValueKind.Null ? null : q.GetProperty("isCorrect").GetBoolean())).ToList();

            results.Add(new GeneratedQuestion(
                Text: question.GetProperty("text").GetString()!.Truncate(1024),
                Type: questionType,
                IsRequired: true,
                CorrectAnswer: question.TryGetProperty("correctAnswer", out var correctAnswer) && correctAnswer.ValueKind
                != JsonValueKind.Null ? correctAnswer.GetString().Truncate(1024) : null,
                Options: options
            ));
        }

        return results;
    }
}
