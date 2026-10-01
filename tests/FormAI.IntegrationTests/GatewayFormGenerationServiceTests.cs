using System.Net;
using System.Text;
using System.Text.Json;
using FormAI.Application.AI;
using FormAI.Domain.Enums;
using FormAI.Infrastructure.AI;
using Microsoft.Extensions.Options;

namespace FormAI.IntegrationTests;

public class GatewayFormGenerationServiceTests
{
    private const string Draft = """
        {"questions":[
          {"text":"Capital of France?","type":"Single","correctAnswer":null,
           "options":[{"text":"Paris","isCorrect":true},{"text":"Lyon","isCorrect":false}]},
          {"text":"Why?","type":"Text","correctAnswer":"Because","options":[]}
        ]}
        """;

    private sealed class StubHandler(Func<Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? Request { get; private set; }
        public JsonDocument? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Request = request;
            Body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            return await respond();
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    // The OpenAI-compatible envelope: the model's text is at choices[0].message.content.
    private static string Envelope(string content) =>
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });

    private static GatewayFormGenerationService CreateService(StubHandler handler,
        string apiKey = "sk-test")
    {
        const string baseUrl = "http://gateway.test/";

        var client = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };

        var settings = Options.Create(new AiSettings
        {
            ApiKey = apiKey,
            GatewayUrl = baseUrl,
            TextAlias = "form-generator",
            MaxTokens = 4096
        });

        return new GatewayFormGenerationService(client, settings);
    }


    private static readonly GenerationParameters Parameters = new(QuestionCount: 2, IncludeCorrectAnswers: true);

    [Fact]
    public async Task SendsRequestWithTheUserGuidAndAppKey_Successfully()
    {
        var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK,
            Envelope(Draft))));

        var userId = Guid.NewGuid();

        await CreateService(handler).GenerateAsync("some source text", Parameters, userId);

        Assert.Equal("http://gateway.test/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", handler.Request.Headers.Authorization!.ToString());

        var body = handler.Body!.RootElement;
        Assert.Equal("form-generator", body.GetProperty("model").GetString());
        Assert.Equal(4096, body.GetProperty("max_tokens").GetInt32());
        Assert.Equal(userId.ToString(), body.GetProperty("user").GetString());

        var messages = body.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Contains("exactly 2 questions", messages[0].GetProperty("content").ToString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Contains("some source text", messages[1].GetProperty("content").ToString());
    }

    [Fact]
    public async Task ParsesTheDraftFromTheChoicesEnvelope_Successfully()
    {
        var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, Envelope(Draft))));

        var questions = await CreateService(handler).GenerateAsync("text",
        Parameters, Guid.NewGuid());

        Assert.Equal(["Capital of France?", "Why?"], questions.Select(q => q.Text));
        Assert.Equal([QuestionType.Single, QuestionType.Text], questions.Select(q => q.Type));
        Assert.Equal([true, false], questions[0].Options.Select(o => o.IsCorrect));
        Assert.Equal("Because", questions[1].CorrectAnswer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AMissingApiKey_ThrowsInvalidOperationException(string apiKey)
    {
        var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, Envelope(Draft))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(handler, apiKey: apiKey)
             .GenerateAsync("text", Parameters, Guid.NewGuid()));

        Assert.Equal(0, handler.Calls);
    }
}
