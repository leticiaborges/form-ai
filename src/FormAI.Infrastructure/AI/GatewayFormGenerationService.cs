using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FormAI.Application.AI;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Common.Files;
using FormAI.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FormAI.Infrastructure.AI;

public class GatewayFormGenerationService : IFormGenerationService
{
    public const int PromptVersion = 1;

    private readonly HttpClient _client;
    private readonly AiSettings _settings;
    private readonly ILogger<GatewayFormGenerationService> _logger;

    public GatewayFormGenerationService(HttpClient client,
    IOptions<AiSettings> settings,
    ILogger<GatewayFormGenerationService>? logger = null)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger ?? NullLogger<GatewayFormGenerationService>.Instance;
    }

    public async Task<IReadOnlyList<GeneratedQuestion>> GenerateAsync(IReadOnlyList<GenerationSource> sources, GenerationParameters parameters,
     Guid userId, CancellationToken cancellationToken = default)
    {
        if (_client.BaseAddress is null || string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new InvalidOperationException("Ai:GatewayUrl and Ai:ApiKey must be set to generate a form.");

        var pdfs = sources.OfType<PdfSource>().ToList();
        if (pdfs.Count > 1)
            throw new ArgumentException("At most one PDF can be sent.", nameof(sources));

        var pdf = pdfs.SingleOrDefault();
        using HttpRequestMessage message = await GetHttpRequestMessage(sources, parameters, userId,
            pdfs, cancellationToken);

        string responseJson;
        try
        {
            using var response = await _client.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (IsBudgetExceeded(errorBody))
                    throw BudgetReached(new HttpRequestException($"The gateway answered {(int)response.StatusCode}: budget exceeded."));

                // The provider refused the file itself (damaged, encrypted, too many pages): the
                // user's file, not an outage. Only a 400 or 422;
                if (pdf is not null && response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
                {
                    _logger.LogWarning("The gateway rejected a PDF with {Status}: {Body}",
                        (int)response.StatusCode, Truncate(errorBody, 300));
                    throw FileHelper.PdfUnreadable();
                }

                throw Unavailable(new HttpRequestException($"The gateway answered {(int)response.StatusCode}."));
            }

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

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private async Task<HttpRequestMessage> GetHttpRequestMessage(IReadOnlyList<GenerationSource> sources, GenerationParameters parameters, Guid userId, List<PdfSource> pdfs, CancellationToken cancellationToken)
    {
        var pdf = pdfs.SingleOrDefault();
        var texts = sources.OfType<TextSource>().Select(t => t.Content).ToList();

        var marker = UntrustedSource.NewMarker();

        var request = new
        {
            model = pdf is null ? _settings.TextAlias : _settings.VisionAlias,
            max_tokens = _settings.MaxTokens,
            // The user's guid only, never an email or a name (docker/litellm/README.md, Metadata).
            user = userId.ToString(),
            response_format = FormAISchema.ResponseFormat(parameters),
            messages = new object[]
            {
                new { role = "system", content = await BuildSystemPrompt(parameters, marker, cancellationToken) },
                new { role = "user", content = UserContent(texts, pdf, marker) }
            }
        };
        var message = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        return message;
    }

    private const string PdfFileName = "source.pdf";

    private static object UserContent(List<string> texts, PdfSource? pdf, string marker)
    {
        if (pdf is null)
            return UntrustedSource.Wrap(string.Join("\n\n", texts), marker);

        var parts = new List<object>();

        if (texts.Count > 0)
            parts.Add(new { type = "text", text = UntrustedSource.Wrap(string.Join("\n\n", texts), marker) });

        parts.Add(new
        {
            type = "file",
            file = new
            {
                filename = PdfFileName,
                file_data = "data:application/pdf;base64," + Convert.ToBase64String(pdf.Content)
            }
        });

        return parts;
    }

    private static GenerationException BudgetReached(HttpRequestException inner) =>
        new(ValidationErrorCode.GenerationBudgetReached,
            "Form generation is unavailable right now. Please try again later.", inner);

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

    private static bool IsBudgetExceeded(string errorBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(errorBody);
            return doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("type", out var type)
                && type.GetString() == "budget_exceeded";
        }
        catch
        {
            return false;
        }
    }
}
