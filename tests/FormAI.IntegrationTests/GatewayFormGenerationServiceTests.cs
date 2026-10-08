using System.Net;
using System.Text;
using System.Text.Json;
using FormAI.Application.AI;
using FormAI.Application.Common.Exceptions;
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

    private static string Envelope(string content, string finishReason = "stop") =>
       JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = finishReason, message = new { content } } } });

    private static string DraftOf(string question) => $$"""{"questions":[{{question}}]}""";

    private static IReadOnlyList<IGenerationSource> Text(string text) => [new TextSource(text)];

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
            VisionAlias = "form-generator-vision",
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

        await CreateService(handler).GenerateAsync(Text("some source text"), Parameters, userId);

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

        var questions = await CreateService(handler).GenerateAsync(Text("text"),
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
             .GenerateAsync(Text("text"), Parameters, Guid.NewGuid()));

        Assert.Equal(0, handler.Calls);
    }

    public static TheoryData<string> UnreadableDrafts => new()
    {
        "not json at all",
        """{}""",
        DraftOf("""{"text":"Q","type":"Bogus","correctAnswer":null,"options":[]}"""),
    };

    private async Task<GenerationException> Fails(StubHandler handler) =>
            await Assert.ThrowsAsync<GenerationException>(() =>
                CreateService(handler).GenerateAsync(Text("text"), Parameters, Guid.NewGuid()));

    [Fact]
    public async Task SendsAStrictJsonSchemaLimitedToTheAllowedTypes()
    {
        var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, Envelope(Draft))));
        var parameters = new GenerationParameters(2, [QuestionType.Single, QuestionType.Text]);

        await CreateService(handler).GenerateAsync(Text("text"), parameters, Guid.NewGuid());

        var format = handler.Body!.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());

        var jsonSchema = format.GetProperty("json_schema");
        Assert.True(jsonSchema.GetProperty("strict").GetBoolean());

        var type = jsonSchema.GetProperty("schema").GetProperty("properties").GetProperty("questions")
            .GetProperty("items").GetProperty("properties").GetProperty("type");
        Assert.Equal(["Single", "Text"], type.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [MemberData(nameof(UnreadableDrafts))]
    public async Task AnUnreadableDraft_ThrowsGenerationOutputInvalid(string draft)
    {
        var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, Envelope(draft))));

        var ex = await Fails(handler);

        Assert.Equal(ValidationErrorCode.GenerationOutputInvalid, ex.Code);
        Assert.Equal(1, handler.Calls); // never retried
    }

    [Fact]
    public async Task ARefusalWithNoContent_ThrowsGenerationOutputInvalid()
    {
        var refusal = JsonSerializer.Serialize(new
        {
            choices = new[] { new { finish_reason = "stop", message = new { content = (string?)null, refusal = "No." } } }
        });
        var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, refusal)));

        Assert.Equal(ValidationErrorCode.GenerationOutputInvalid, (await Fails(handler)).Code);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("content_filter")]
    public async Task ACutOffReply_ThrowsGenerationOutputInvalid_AndIsNotRetried(string finishReason)
    {
        var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, Envelope(Draft, finishReason))));

        var ex = await Fails(handler);

        Assert.Equal(ValidationErrorCode.GenerationOutputInvalid, ex.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ANonSuccessGatewayReply_ThrowsGenerationUnavailable(HttpStatusCode status)
    {
        var handler = new StubHandler(() => Task.FromResult(Json(status, """{"error":"x"}""")));

        var ex = await Fails(handler);

        Assert.Equal(ValidationErrorCode.GenerationUnavailable, ex.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AConnectionFailure_ThrowsGenerationUnavailable()
    {
        var handler = new StubHandler(() => Task.FromException<HttpResponseMessage>(new HttpRequestException("refused")));

        Assert.Equal(ValidationErrorCode.GenerationUnavailable, (await Fails(handler)).Code);
    }

    [Fact]
    public async Task AnHttpClientTimeout_ThrowsGenerationUnavailable()
    {
        // HttpClient surfaces its own timeout as a TaskCanceledException while the caller's token is untouched.
        var handler = new StubHandler(() => Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout")));

        Assert.Equal(ValidationErrorCode.GenerationUnavailable, (await Fails(handler)).Code);
    }

    [Fact]
    public async Task ACallerCancellation_StillPropagatesAsCancellation()
    {
        var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, Envelope(Draft))));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(handler).GenerateAsync(Text("text"), Parameters, Guid.NewGuid(), cts.Token));
    }
}
