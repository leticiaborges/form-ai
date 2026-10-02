using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FormAI.Application.AI;
using FormAI.Application.Common.Exceptions;
using FormAI.Domain.Enums;
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

        var marker = UntrustedSource.NewMarker();

        var request = new
        {
            model = _settings.TextAlias,
            max_tokens = _settings.MaxTokens,
            // The user's guid only, never an email or a name (docker/litellm/README.md, Metadata).
            user = userId.ToString(),
            response_format = FormAISchema.ResponseFormat(parameters),
            messages = new object[]
            {
                new { role = "system", content = await BuildSystemPrompt(parameters, marker, cancellationToken) },
                new { role = "user", content = UntrustedSource.Wrap(sourceText, marker) }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);


        string responseJson;
        try
        {
            using var response = await _client.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw Unavailable(new HttpRequestException($"The gateway answered {(int)response.StatusCode}."));

            responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable(ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable(ex);
        }

        return GeneratedFormAIJSONParser.Parse(responseJson);
    }

    private static GenerationException Unavailable(Exception inner) =>
       new(ValidationErrorCode.GenerationUnavailable,
           "Form generation is unavailable right now. Please try again later.", inner);

    private static async Task<string> BuildSystemPrompt(GenerationParameters parameters, string marker, CancellationToken cancellationToken)
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
        prompt = prompt.Replace("{marker}", marker);

        return prompt;
    }
}
