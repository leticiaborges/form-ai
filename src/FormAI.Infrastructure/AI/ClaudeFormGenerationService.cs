using System.ComponentModel;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FormAI.Application.AI;
using FormAI.Domain.Enums;
using Humanizer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Options;

namespace FormAI.Infrastructure.AI;

public class ClaudeFormGenerationService : IFormGenerationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ClaudeSettings _settings;

    public ClaudeFormGenerationService(IHttpClientFactory httpClientFactory,
    IOptions<ClaudeSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<GeneratedQuestion>> GenerateAsync(
        string sourceText,
        GenerationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            model = _settings.Model,
            max_tokens = _settings.MaxTokens,
            system = new[] { new { text = await BuildSystemPrompt(parameters, cancellationToken), type = "text" } },
            messages = new[]
            {
                new { role = "user", content = BuildUserPrompt(sourceText, parameters) }
            }
        };


        var client = _httpClientFactory.CreateClient("claude");
        client.DefaultRequestHeaders.Add("x-api-key", _settings.ApiKey);


        var json = JsonSerializer.Serialize(request);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("v1/messages", httpContent, cancellationToken);
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
        prompt = prompt.Replace("{difficultyLevel}", parameters.DifficultyLevel);
        prompt = prompt.Replace("{markCorrect}", parameters.IncludeCorrectAnswers.ToString());

        return prompt;
    }

    private static string BuildUserPrompt(string sourceText, GenerationParameters parameters)
    {
        return $"Generate {parameters.QuestionCount} questions based on this content:\n\n{sourceText}";
    }

    private static IReadOnlyList<GeneratedQuestion> ParseResponse(string responseJSON)
    {
        using var apiDoc = JsonDocument.Parse(responseJSON);
        var aiReplyText = apiDoc.RootElement.GetProperty("content")[0].GetProperty("text").GetString()
         ?? throw new InvalidOperationException("Claude returned empty content.");

        string cleaned = Regex.Replace(
            aiReplyText,
            @"\A```json\s*|\s*```\z",
            ""
        ).Trim();

        using var questionsDoc = JsonDocument.Parse(cleaned);
        var questionsArray = questionsDoc.RootElement.GetProperty("questions");

        var results = new List<GeneratedQuestion>();
        foreach (var question in questionsArray.EnumerateArray())
        {
            var questionType = Enum.Parse<QuestionType>(question.GetProperty("type").GetString() ?? "");

            var options = question.GetProperty("options").EnumerateArray().Select(q =>
            new GeneratedOption(q.GetProperty("text").GetString()!.Truncate(1024),
             q.GetProperty("isCorrect").GetString() == null ? null : q.GetProperty("isCorrect").GetBoolean())).ToList();

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
