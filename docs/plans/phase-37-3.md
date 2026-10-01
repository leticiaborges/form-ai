# Phase 37, slice 3: swap the client behind `IFormGenerationService`, text only, plus the first fake gateway

Covers: **AI-1, AI-3, AI-7, AI-10, T-1** of [`phase-37.md`](./phase-37.md). Docs: update `CLAUDE.md` (AI section), the LiteLLM README timeout table, the root `README.md` settings table.

## Context

Slices 1 and 2 shipped the gateway (aliases, app key, retries, timeout budget). The API still calls Anthropic directly through `ClaudeFormGenerationService`. This slice makes the API call the gateway instead, using an OpenAI-compatible request, and adds a fake gateway so CI and e2e never call a real model.

**In scope:** text input only; same `GenerateAsync` result (questions list); same response parsing as today.
**Out of scope (later slices):** strict schema and truncation handling and the 502/503 codes (4), prompt hardening and delimiters (5), usage ledger, model used, tokens, cost (6), PDF and source list (15). Until slice 4 a gateway failure still surfaces as a 500, exactly like an Anthropic failure does today.

## Decisions made in this plan

| Decision | Why |
|---|---|
| New `Ai` settings section (`GatewayUrl`, `ApiKey`, `TextAlias`, `MaxTokens`, `TimeoutSeconds`). The old `Claude` section is deleted. | AI-1: alias and URL come from configuration. The vision alias is added in slice 15, not now. |
| `GenerateAsync` gains a `Guid userId` parameter. | The gateway contract (README, GW-9) says `user` = the user's guid. Slice 6 needs it too. Cheaper to add now than to change the interface twice. |
| No retries in the client; no `AddStandardResilienceHandler`. `HttpClient.Timeout` = 80 s. | AI-7 and the README budget: gateway 2 x 35 s = 70 s < client 80 s < 90 s < load balancer. |
| Missing `Ai:ApiKey` / `Ai:GatewayUrl` fails **on the first generation call**, not at startup. | `GenerateFormHandler` is resolved by `FormsController` on every forms request. Failing at resolve time, or with `ValidateOnStart`, would take down the whole API in any environment that has no gateway yet (production, see risks). |
| The fake gateway is a ~50-line Node script started by Playwright's `webServer`. | The e2e suite is Node and already starts two servers. No new .NET test host, no Docker. |
| The service's unit tests live in `FormAI.IntegrationTests`. | `FormAI.UnitTests` references only Domain and Application; the service is in Infrastructure. These tests use a stub `HttpMessageHandler` and need no Docker. |

## Risks to know before you start

1. **Production has no gateway yet** (`deploy-litellm.yml` is slice 8). `infra/modules/container-platform/main.tf:92` and `docs/deployment/aws-deployment-guide.md:302` still inject `Claude__ApiKey`. If this branch is merged to `main` and deployed, generation in production breaks until the gateway and `Ai__*` secrets exist. **Do not merge and deploy this slice on its own**, or deploy it together with the infra change. This plan does not touch `infra/`.
2. `PromptGenerateForm.txt` is read from `AppDomain.CurrentDomain.BaseDirectory`. Step 7's test depends on the file being copied to the test output. It should be (the `.csproj` marks it `CopyToOutputDirectory` and the flag flows through project references). If the test throws `FileNotFoundException`, that is why.
3. The prompt template has doubled braces (`{{`, `}}`) that are sent literally to the model because the code uses `Replace`, not `string.Format`. Pre-existing and not changed here. Slice 5 rewrites the prompt.

---

## Step 0: Branch and local setup

```bash
git switch -c feature/litellm-client-text   # from the slice 2 branch, or from main once slice 2 is merged
```

Local values (untracked, do not commit): in `src/FormAI.API/appsettings.Development.json` replace the `Claude` section with:

```json
"Ai": {
  "GatewayUrl": "http://127.0.0.1:4000",
  "ApiKey": "<the value of LITELLM_APP_KEY from .env>"
}
```

(or `dotnet user-secrets set "Ai:ApiKey" "<LITELLM_APP_KEY>" --project src/FormAI.API`). Make sure the stack is up and the key exists: `docker compose up -d` then `bash docker/litellm/provision-app-key.sh`.

---

## Step 1: Settings class (replaces `ClaudeSettings`)

Create `src/FormAI.Infrastructure/AI/AiSettings.cs`:

```csharp
namespace FormAI.Infrastructure.AI;

public class AiSettings
{
    public const string SectionName = "Ai";

    public string GatewayUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string TextAlias { get; set; } = "form-generator";
    public int MaxTokens { get; set; } = 4096;

    // Gateway worst case is 2 x 35 s = 70 s; this stays below the 90 s overall limit.
    public int TimeoutSeconds { get; set; } = 80;
}
```

Edit `src/FormAI.API/appsettings.json`: replace the `Claude` block with

```json
  "Ai": {
    "GatewayUrl": "http://127.0.0.1:4000",
    "TextAlias": "form-generator",
    "MaxTokens": 4096,
    "TimeoutSeconds": 80
  },
```

(`ApiKey` is deliberately absent: it is a secret.)

Delete `src/FormAI.Infrastructure/AI/ClaudeSettings.cs`.

## Step 2: Change the interface (add `userId`)

`src/FormAI.Application/AI/IFormGenerationService.cs`, replace the interface:

```csharp
public interface IFormGenerationService
{
    Task<IReadOnlyList<GeneratedQuestion>> GenerateAsync(
        string sourceText,
        GenerationParameters parameters,
        Guid userId,
        CancellationToken cancellationToken = default);
}
```

`src/FormAI.Application/Forms/GenerateForm/GenerateFormHandler.cs`, the call (line ~87):

```csharp
var generatedQuestions = await _generationService.GenerateAsync(
    request.SourceText, parameters, requestingUserId, cancellationToken);
```

## Step 3: The gateway client

Create `src/FormAI.Infrastructure/AI/GatewayFormGenerationService.cs`. It is `ClaudeFormGenerationService` with a new transport and a new response envelope; the prompt building and question parsing are unchanged.

```csharp
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
    // Stored on each usage row from slice 6. Bump it whenever PromptGenerateForm.txt changes.
    public const int PromptVersion = 1;

    private readonly HttpClient _client;
    private readonly AiSettings _settings;

    public GatewayFormGenerationService(HttpClient client, IOptions<AiSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<GeneratedQuestion>> GenerateAsync(
        string sourceText,
        GenerationParameters parameters,
        Guid userId,
        CancellationToken cancellationToken = default)
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

        // No retry here (AI-7): the gateway is the only place that retries.
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
        return $"Generate {parameters.QuestionCount} questions based on this content:\n\n{sourceText}";
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
```

Delete `src/FormAI.Infrastructure/AI/ClaudeFormGenerationService.cs` (AI-10).

## Step 4: Registration

`src/FormAI.Infrastructure/DependencyInjection.cs`: add `using Microsoft.Extensions.Options;` and replace the block at lines 46 to 52 (`Configure<ClaudeSettings>`, `AddHttpClient("claude"…)`, `AddScoped<IFormGenerationService…>`) with:

```csharp
services.Configure<AiSettings>(configuration.GetSection(AiSettings.SectionName));
// A typed client registers the service too. No resilience or retry handler on purpose (AI-7):
// the gateway retries; this client only enforces its own timeout.
services.AddHttpClient<IFormGenerationService, GatewayFormGenerationService>((sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<AiSettings>>().Value;
    // The trailing slash makes the relative "v1/chat/completions" resolve under the base address.
    if (Uri.TryCreate(settings.GatewayUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress))
        client.BaseAddress = baseAddress;
    client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
});
```

`dotnet build FormAI.sln` will now fail only in `GenerateFormTests` (Step 5).

## Step 5: Update the existing handler tests

`tests/FormAI.UnitTests/Forms/GenerateFormTests.cs`: the interface has a new `Guid` argument.

1. Line 32, the stub:
```csharp
_generationService.GenerateAsync(Arg.Any<string>(), Arg.Any<GenerationParameters>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(questions);
```
2. `GradedRequest_AsksTheGeneratorForTheRequestedQuestions` (line ~153): `GenerateFormAsync()` already returns the user id. Change the destructuring to `var (request, userId, _) = await GenerateFormAsync();` and add the argument after the `GenerationParameters` matcher: `userId,` (so the call is `GenerateAsync("SourceTextTest", Arg.Is<GenerationParameters>(...), userId, Arg.Any<CancellationToken>())`). This is the test that proves the handler passes the user guid on.
3. `UngradedRequest_KeepsScoreAsNullAndIgnoreAnswerKey` (line ~205): add `Arg.Any<Guid>(),` after the parameters matcher.
4. `InvalidRequest_IsRejectedBeforeGeneratingOrSaving` (line ~228): add `Arg.Any<Guid>(),` before `Arg.Any<CancellationToken>()`.

Run: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj` (no Docker). All green.

## Step 6: Tests for the client (new file)

Create `tests/FormAI.IntegrationTests/GatewayFormGenerationServiceTests.cs`. No Docker needed, despite the project.

```csharp
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

    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? Request { get; private set; }
        public JsonDocument? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Request = request;
            Body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            return await respond(ct);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private static string Envelope(string content) =>
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });

    private static GatewayFormGenerationService CreateService(StubHandler handler, string apiKey = "sk-test",
        string baseUrl = "http://gateway.test/")
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        var settings = Options.Create(new AiSettings { ApiKey = apiKey, GatewayUrl = baseUrl, TextAlias = "form-generator", MaxTokens = 1234 });
        return new GatewayFormGenerationService(client, settings);
    }

    private static readonly GenerationParameters Parameters = new(QuestionCount: 2, IncludeCorrectAnswers: true);

    [Fact]
    public async Task SendsAnOpenAiRequestToTheAliasWithTheUserGuidAndTheAppKey()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, Envelope(Draft))));
        var userId = Guid.NewGuid();

        await CreateService(handler).GenerateAsync("some source text", Parameters, userId);

        Assert.Equal("http://gateway.test/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", handler.Request.Headers.Authorization!.ToString());

        var body = handler.Body!.RootElement;
        Assert.Equal("form-generator", body.GetProperty("model").GetString());
        Assert.Equal(1234, body.GetProperty("max_tokens").GetInt32());
        Assert.Equal(userId.ToString(), body.GetProperty("user").GetString());

        var messages = body.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Contains("generate exactly 2 questions", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Contains("some source text", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ParsesTheDraftFromTheChoicesEnvelope()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, Envelope(Draft))));

        var questions = await CreateService(handler).GenerateAsync("text", Parameters, Guid.NewGuid());

        Assert.Equal(["Capital of France?", "Why?"], questions.Select(q => q.Text));
        Assert.Equal([QuestionType.Single, QuestionType.Text], questions.Select(q => q.Type));
        Assert.Equal([true, false], questions[0].Options.Select(o => o.IsCorrect));
        Assert.Equal("Because", questions[1].CorrectAnswer);
    }

    [Fact]
    public async Task ParsesADraftWrappedInAMarkdownFence()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, Envelope("```json\n" + Draft + "\n```"))));

        var questions = await CreateService(handler).GenerateAsync("text", Parameters, Guid.NewGuid());

        Assert.Equal(2, questions.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task AnErrorStatusIsNotRetried(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(status, "{}")));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateService(handler).GenerateAsync("text", Parameters, Guid.NewGuid()));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TheCallersCancellationReachesTheHttpCall()
    {
        var handler = new StubHandler(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json(HttpStatusCode.OK, "{}");
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService(handler).GenerateAsync("text", Parameters, Guid.NewGuid(), cts.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AMissingApiKeyFailsBeforeAnyCall(string apiKey)
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, Envelope(Draft))));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService(handler, apiKey).GenerateAsync("text", Parameters, Guid.NewGuid()));

        Assert.Equal(0, handler.Calls);
    }
}
```

Run: `dotnet test tests/FormAI.IntegrationTests --filter GatewayFormGenerationServiceTests`. If you see `FileNotFoundException` for `PromptGenerateForm.txt`, see risk 2.

(`generate exactly 2 questions` comes from the prompt's first line, "generate exactly {questionCount} questions". Check the casing against `PromptGenerateForm.txt` if the assertion fails.)

## Step 7: The fake gateway (T-1, first version)

Create `frontend/e2e/support/fake-gateway.mjs`. Plain Node, no dependencies. It speaks just enough of the gateway contract: bearer auth, `POST /v1/chat/completions`, a canned draft, and a way for tests to see the last request. Later slices add one canned failure per error code here.

```js
// Fake AI gateway for CI and e2e: CI never calls a real model (plan T-1).
// Contract: docker/litellm/README.md. Grown slice by slice; today it only answers with a canned draft.
import http from "node:http";

const port = Number(process.env.FAKE_GATEWAY_PORT ?? 4055);
const appKey = process.env.FAKE_GATEWAY_KEY ?? "sk-fake-gateway-key";

let lastRequest = null;

const draft = {
  questions: [
    {
      text: "What is the capital of France?",
      type: "Single",
      correctAnswer: null,
      options: [
        { text: "Paris", isCorrect: true },
        { text: "Lyon", isCorrect: false },
        { text: "Marseille", isCorrect: false },
      ],
    },
    {
      text: "Why is the sky blue?",
      type: "Text",
      correctAnswer: "Rayleigh scattering",
      options: [],
    },
  ],
};

function send(res, status, body) {
  res.writeHead(status, { "Content-Type": "application/json" });
  res.end(JSON.stringify(body));
}

const server = http.createServer((req, res) => {
  if (req.method === "GET" && req.url === "/health") return send(res, 200, { status: "ok" });

  // Test hook, not part of the real gateway: what the API sent last.
  if (req.method === "GET" && req.url === "/__last-request") {
    return send(res, lastRequest ? 200 : 404, lastRequest ?? {});
  }

  if (req.method === "POST" && req.url === "/v1/chat/completions") {
    if (req.headers.authorization !== `Bearer ${appKey}`) {
      return send(res, 401, { error: { message: "Invalid key" } });
    }
    let raw = "";
    req.on("data", (chunk) => (raw += chunk));
    req.on("end", () => {
      let body;
      try {
        body = JSON.parse(raw);
      } catch {
        return send(res, 400, { error: { message: "Invalid JSON" } });
      }
      lastRequest = body;
      send(res, 200, {
        id: "chatcmpl-fake",
        object: "chat.completion",
        model: body.model,
        choices: [
          {
            index: 0,
            finish_reason: "stop",
            message: { role: "assistant", content: JSON.stringify(draft) },
          },
        ],
        usage: { prompt_tokens: 10, completion_tokens: 10, total_tokens: 20 },
      });
    });
    return;
  }

  send(res, 404, { error: { message: "Not found" } });
});

// 127.0.0.1, not "localhost": Node may resolve localhost to ::1 and the API would not connect.
server.listen(port, "127.0.0.1", () => console.log(`fake gateway on http://127.0.0.1:${port}`));
```

`frontend/e2e/env.ts`, append:

```ts
export const GATEWAY_PORT = Number(process.env.E2E_GATEWAY_PORT ?? 4055);
export const GATEWAY_URL = `http://127.0.0.1:${GATEWAY_PORT}`;
// Not a secret: the key the fake gateway accepts.
export const GATEWAY_KEY = "sk-fake-gateway-key";
```

`frontend/playwright.config.ts`:

1. Import: `import { API_URL, GATEWAY_KEY, GATEWAY_PORT, GATEWAY_URL, WEB_PORT, WEB_URL } from "./e2e/env";`
2. In the API's `env`, replace the `Claude__ApiKey: "unused-in-e2e",` line with:
```ts
        Ai__GatewayUrl: GATEWAY_URL,
        Ai__ApiKey: GATEWAY_KEY,
```
3. Add a first entry to the `webServer` array (before the API):
```ts
    {
      command: "node e2e/support/fake-gateway.mjs",
      url: `${GATEWAY_URL}/health`,
      reuseExistingServer: false,
      env: { FAKE_GATEWAY_PORT: String(GATEWAY_PORT), FAKE_GATEWAY_KEY: GATEWAY_KEY },
    },
```

ESLint only lints `*.ts, *.tsx`, so the `.mjs` is not linted. Run `npx prettier --write e2e playwright.config.ts` from `frontend/` so `format:check` passes in CI.

## Step 8: The e2e spec

`frontend/e2e/support/api.ts`: add an exported helper, next to `registerVerifiedUser` (the seed function keeps its own inline login; leave it alone):

```ts
export async function signIn(
  request: APIRequestContext,
  { email, password }: { email: string; password: string },
): Promise<{ Authorization: string }> {
  const login = await request.post(`${API_URL}/api/auth/login`, { data: { email, password } });
  await expectOk(login, "login");
  const { accessToken } = await login.json();
  return { Authorization: `Bearer ${accessToken}` };
}
```

Create `frontend/e2e/generate-form.spec.ts` (HTTP only, like the existing specs that need no browser):

```ts
import { expect, test } from "@playwright/test";
import { API_URL, GATEWAY_URL } from "./env";
import { registerVerifiedUser, signIn } from "./support/api";

function generateBody(isGraded: boolean) {
  return {
    title: "Generated through the fake gateway",
    description: "",
    sourceText: "Paris is the capital of France. The sky is blue because of Rayleigh scattering.",
    sourceType: "Text",
    sourceUrl: "",
    questionCount: 2,
    allowedTypes: ["Single", "Text"],
    difficultyLevel: "Medium",
    isGraded,
    showResultsAfterSubmit: false,
    expiresAt: new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString(),
  };
}

test("a graded generation reaches the gateway with the alias and a user guid, and keeps the answer key", async ({
  request,
}) => {
  const auth = await signIn(request, await registerVerifiedUser(request));

  const res = await request.post(`${API_URL}/api/forms/generate/text`, {
    headers: auth,
    data: generateBody(true),
  });
  expect(res.status(), await res.text()).toBe(201);
  const form = await res.json();

  expect(form.questions.map((q: { text: string }) => q.text)).toEqual([
    "What is the capital of France?",
    "Why is the sky blue?",
  ]);
  const single = form.questions[0];
  expect(single.options.map((o: { isCorrect: boolean }) => o.isCorrect)).toEqual([true, false, false]);
  expect(form.questions[1].correctAnswer).toBe("Rayleigh scattering");

  const sent = await (await request.get(`${GATEWAY_URL}/__last-request`)).json();
  expect(sent.model).toBe("form-generator");
  expect(sent.max_tokens).toBe(4096);
  expect(sent.user).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i);
  expect(sent.messages[1].content).toContain("Paris is the capital of France");
});

test("an ungraded generation drops the answer key the gateway returned", async ({ request }) => {
  const auth = await signIn(request, await registerVerifiedUser(request));

  const res = await request.post(`${API_URL}/api/forms/generate/text`, {
    headers: auth,
    data: generateBody(false),
  });
  expect(res.status(), await res.text()).toBe(201);
  const form = await res.json();

  expect(form.questions[0].options.map((o: { isCorrect: boolean | null }) => o.isCorrect)).toEqual([
    null,
    null,
    null,
  ]);
  expect(form.questions[1].correctAnswer).toBeNull();
});
```

Note: both tests share one fake gateway and `fullyParallel` is on, so `__last-request` can be overwritten by the other test between the generate call and the read. The first test only asserts values that are the same in both tests (alias, max tokens, guid shape, source text), so it still passes. If you later assert per-user values, make the hook keyed by `user`.

Run (needs Docker Compose Postgres, Redis, Mailpit, as before): `cd frontend && npm run test:e2e -- generate-form`.

## Step 9: Docs (in the same change, per `CLAUDE.md`)

1. **`CLAUDE.md`, "AI integration"** (line ~103). Replace the first sentence so it reads, in substance: `IFormGenerationService` (`Application/AI/`) is the boundary; `GatewayFormGenerationService` is the only class that calls a model, and it calls the LiteLLM gateway (OpenAI-compatible `POST /v1/chat/completions`), never a provider. It sends the `Ai:TextAlias` alias (`form-generator`), `max_tokens`, the user's guid as `user`, and the app key as a bearer token; it never retries (the gateway does) and times out at `Ai:TimeoutSeconds` (80). The `Ai` settings are `GatewayUrl`, `ApiKey` (secret: user-secrets or the untracked `appsettings.Development.json`), `TextAlias`, `MaxTokens`, `TimeoutSeconds`. Keep the existing sentences about `IncludeCorrectAnswers`, points and the prompt. Also add one line under "Tests" (Frontend/Playwright): `playwright.config.ts` also starts a fake gateway (`e2e/support/fake-gateway.mjs`) and points the API at it, so e2e never calls a model.
2. **`docker/litellm/README.md`, timeout table** (lines 38 to 42): "Client timeout (`HttpClient`, slice 3) 80 s" status becomes `Built: Ai:TimeoutSeconds, GatewayFormGenerationService client (slice 3). Not yet measured with a realistic PDF`. "Overall generation limit 90 s": `Enforced by the 80 s client timeout (below it)`. Leave the load balancer and Kestrel rows as they are.
3. **Root `README.md`** lines 74 and 135 to 136: replace `Claude:ApiKey` / `Claude__ApiKey` / `Claude__Model` / `Claude__MaxTokens` with `Ai:ApiKey` (the gateway app key, `LITELLM_APP_KEY`), `Ai:GatewayUrl` and the optional `Ai__TextAlias` / `Ai__MaxTokens` / `Ai__TimeoutSeconds`.
4. **`docker-compose.app.yml` line 20**: replace `Claude__ApiKey: ${CLAUDE_API_KEY}` with
```yaml
      Ai__GatewayUrl: http://litellm:4000
      Ai__ApiKey: ${LITELLM_APP_KEY}
```
(and make sure the `api` service can reach the `litellm` service's network, or point `Ai__GatewayUrl` at `http://host.docker.internal:4000`; check how the two compose files are combined before choosing).
5. **`docs/known-gaps.md`**: add one row under "Wrong or incomplete on purpose": gateway failures (down, timeout, 4xx, provider 5xx) and malformed model output surface as a 500 until slice 4 adds the 502/503 codes; and production still injects `Claude__ApiKey` until the gateway is deployed (slice 8). Remove both lines when those slices land.
6. `CONTEXT.md` needs no change: no new term is introduced (**Gateway** and **Model alias** exist).
7. No new ADR: ADR 0008 already records the decision.

## Verification

1. `dotnet build FormAI.sln` has no warnings about `ClaudeSettings` or a missing symbol. `git grep -n "ClaudeFormGenerationService\|ClaudeSettings"` finds nothing outside `docs/plans/` and `.specs/`.
2. `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj` is green (handler tests, with the new `userId` assertion).
3. `dotnet test tests/FormAI.IntegrationTests --filter GatewayFormGenerationServiceTests` is green: request shape, envelope parsing, fence stripping, no retry on 500, 400 and 401, cancellation, missing key.
4. `cd frontend && npm run test:e2e -- generate-form` is green with the fake gateway and **no** real key. Also run the whole e2e suite once: the API must still boot with the new settings.
5. `cd frontend && npm run lint && npm run format:check && npm test` stay green.
6. Real gateway, by hand (costs a fraction of a cent): `docker compose up -d`, `bash docker/litellm/provision-app-key.sh`, `dotnet run --project src/FormAI.API` with `Ai:ApiKey` set, sign in, call `POST /api/forms/generate/text` from Swagger. The draft comes back. Then confirm the spend is attributed to the app key and the user guid: `bash docker/litellm/smoke.sh` still passes, or query `LiteLLM_SpendLogs` for the `user` column.
7. Stop the gateway (`docker compose stop litellm`) and repeat the call. It returns 500 (expected until slice 4) and does so quickly, not after 80 s.

## Suggested commits (Conventional, atomic)

1. `refactor(ai): pass the user id to IFormGenerationService` (Step 2 and Step 5, with the old client temporarily taking and ignoring the argument, or do Steps 2, 3, 4, 5 as one commit if easier)
2. `feat(ai): call the gateway instead of Anthropic directly` (Steps 1, 3, 4, 6)
3. `test(e2e): add a fake gateway and a generation spec` (Steps 7 and 8)
4. `docs(ai): describe the gateway client` (Step 9)

## Done when

The API generates a form through the gateway, `Claude*` code and settings are gone from `src/`, and CI passes without any provider key. Mark slice 3 done in `phase-37.md` only if you track status there.
