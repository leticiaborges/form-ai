# Phase 37, slice 3: TDD walkthrough

Companion to [`phase-37-3.md`](./phase-37-3.md). That file says **what** to build and has the full final code. This one says **in what order**, test first, and **why**. Where it says "final code", copy it from the matching step of `phase-37-3.md`.

## How TDD works here (read once)

One loop, repeated for each behaviour:

1. **Red.** Write one test. Run it. Watch it fail, and read *why* it fails. A test you never saw fail proves nothing: it may pass for the wrong reason.
2. **Green.** Write the *least* code that makes it pass. Ugly is fine. Do not write code for a test you have not written yet.
3. **Refactor.** With the tests green, clean up. Run them again. (Most cycles in this slice need none.)

Two rules that keep it honest:

- **One failing test at a time.** If two are red you don't know which change fixed what.
- **A compile error counts as red.** When a test uses a method or parameter that doesn't exist yet, the build failing *is* the failing test. Fix it with the smallest stub, then see the assertion fail.

### Why so few tests

You asked for the minimum that proves the change. The question for each test is: *"if I broke this behaviour, would anything else catch it?"* If yes, skip it. This slice changes **where the request goes** (Anthropic → gateway) and **what the response looks like**. It does not change the prompt, the parsing of questions, grading, or the handler logic. So:

| Behaviour that changed | Proven by |
|---|---|
| The handler passes the user's guid to the service | 1 changed unit test (Cycle 1) |
| The request has the right URL, key, alias, `max_tokens`, `user` | 1 client test (Cycle 2) |
| The OpenAI response envelope is read correctly | 1 client test (Cycle 3) |
| No retry in the client; an error status surfaces | 1 client test (Cycle 4) |
| Missing key fails before any call | 1 client test (Cycle 5) |
| DI, config, the real HTTP path and the fake gateway work together | 1 e2e spec (Cycle 6) |

**Deliberately not tested** (and why):

- Markdown fence stripping, option/question parsing details: unchanged code, moved as-is. Cycle 3's draft already goes through it.
- Cancellation reaches the HTTP call: `SendAsync(message, cancellationToken)` is one argument, and `HttpClient` honours it. Low value.
- The 401/400 status codes separately: same code path as 500.
- An ungraded e2e variant: `ClearGradingIfUngraded` is already covered by handler unit tests.
- The gateway itself (aliases, retries): slices 1 and 2, verified by `smoke.sh`.

**Where tests live.** The service is in Infrastructure, and `FormAI.UnitTests` references only Domain and Application. So the client tests go in `FormAI.IntegrationTests`. They are plain xUnit with a fake `HttpMessageHandler`: no Docker, fast. The project name is misleading; don't worry about it.

---

## Step 0: Branch, baseline, local settings

```bash
git switch -c feature/litellm-client-text
dotnet build FormAI.sln
dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj
```

**Why a baseline first:** TDD only works if you start green. If something is already red, you can't tell your failures from the old ones. Fix or note it before continuing.

Local values (untracked, never commit): in `src/FormAI.API/appsettings.Development.json` replace the `Claude` section with:

```json
"Ai": {
  "GatewayUrl": "http://127.0.0.1:4000",
  "ApiKey": "<LITELLM_APP_KEY from .env>"
}
```

You only need this for the manual check at the end. Tests never touch it.

---

## Cycle 1: the handler passes the user id on

**Behaviour:** `GenerateFormHandler` gives the service the requesting user's guid, because the gateway contract says `user` = that guid.

### 1.1 Red: change the existing test first

`tests/FormAI.UnitTests/Forms/GenerateFormTests.cs`. We reuse the test that already asserts what the handler sends to the generator, instead of adding a new one.

1. In `GradedRequest_AsksTheGeneratorForTheRequestedQuestions` (~line 153) change the destructuring to `var (request, userId, _) = await GenerateFormAsync();` and add `userId,` after the `GenerationParameters` matcher, so the call reads:

```csharp
await _generationService.Received(1).GenerateAsync(
    "SourceTextTest",
    Arg.Is<GenerationParameters>(/* keep the existing matcher */),
    userId,
    Arg.Any<CancellationToken>());
```

2. The other three places that mention `GenerateAsync` (line 32 and the two near lines 205 and 228) also need the extra argument or they will not compile. They are not new tests, just keeping the old ones compiling. Add `Arg.Any<Guid>(),` before `Arg.Any<CancellationToken>()` in each.

```bash
dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj
```

**Expected red:** a compile error, `No overload for method 'GenerateAsync' takes 4 arguments`. Good: the test describes an interface that doesn't exist yet.

### 1.2 Green: the smallest change

`src/FormAI.Application/AI/IFormGenerationService.cs`: add the parameter.

```csharp
Task<IReadOnlyList<GeneratedQuestion>> GenerateAsync(
    string sourceText,
    GenerationParameters parameters,
    Guid userId,
    CancellationToken cancellationToken = default);
```

`GenerateFormHandler.cs` (~line 87): pass it.

```csharp
var generatedQuestions = await _generationService.GenerateAsync(
    request.SourceText, parameters, requestingUserId, cancellationToken);
```

The old `ClaudeFormGenerationService` now fails to compile too. Give its `GenerateAsync` the extra `Guid userId` parameter and ignore it. It is temporary: it is deleted in Cycle 6.

```bash
dotnet build FormAI.sln
dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj
```

**Expected green:** all unit tests pass, including the one that checks `userId`.

**Check it can fail (30 seconds, worth doing once):** in the handler, temporarily pass `Guid.NewGuid()` instead of `requestingUserId`. The test must go red. Undo it. This is how you learn to trust a test.

### 1.3 Commit

```bash
git add -A && git commit -m "refactor(ai): pass the user id to IFormGenerationService"
```

---

## Cycle 2: the client sends the right request

**Behaviour:** `POST {GatewayUrl}/v1/chat/completions` with `Authorization: Bearer <key>`, and a body with `model` (the alias), `max_tokens`, `user` (the guid), and a system plus a user message.

### 2.1 Test scaffolding and the first test (red)

Create `tests/FormAI.IntegrationTests/GatewayFormGenerationServiceTests.cs`.

**What the helpers do:** `StubHandler` replaces the network. It records the request the service sent and returns whatever response you give it. This is a **fake at the boundary**: the only thing we replace is the HTTP transport, so everything above it (prompt building, JSON, headers) is the real code under test.

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

    private static GatewayFormGenerationService CreateService(StubHandler handler, string apiKey = "sk-test")
    {
        const string baseUrl = "http://gateway.test/";
        var client = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        var settings = Options.Create(new AiSettings
        {
            ApiKey = apiKey, GatewayUrl = baseUrl, TextAlias = "form-generator", MaxTokens = 1234
        });
        return new GatewayFormGenerationService(client, settings);
    }

    private static readonly GenerationParameters Parameters = new(QuestionCount: 2, IncludeCorrectAnswers: true);

    [Fact]
    public async Task SendsAnOpenAiRequestToTheAliasWithTheUserGuidAndTheAppKey()
    {
        var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, Envelope(Draft))));
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
        Assert.Contains("exactly 2 questions", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Contains("some source text", messages[1].GetProperty("content").GetString());
    }
}
```

(`"exactly 2 questions"` comes from line 1 of `PromptGenerateForm.txt`: "generate exactly {questionCount} questions".)

```bash
dotnet test tests/FormAI.IntegrationTests --filter GatewayFormGenerationServiceTests
```

**Expected red #1:** compile errors: `AiSettings` and `GatewayFormGenerationService` don't exist.

### 2.2 Make it compile (still red)

Create `src/FormAI.Infrastructure/AI/AiSettings.cs` (final code in `phase-37-3.md`, Step 1). Add the `Ai` block to `src/FormAI.API/appsettings.json` and delete `ClaudeSettings.cs` **later**, in Cycle 6 (the old service still uses it, and deleting now would break the build).

Create the class with only a stub body:

```csharp
public class GatewayFormGenerationService : IFormGenerationService
{
    public const int PromptVersion = 1;

    public GatewayFormGenerationService(HttpClient client, IOptions<AiSettings> settings) { }

    public Task<IReadOnlyList<GeneratedQuestion>> GenerateAsync(
        string sourceText, GenerationParameters parameters, Guid userId,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
```

Run the test again.

**Expected red #2:** `NotImplementedException`. Now it compiles and fails for the right reason: the behaviour is missing.

### 2.3 Green

Fill in the class: the fields, the real constructor, `GenerateAsync` building and sending the request, `BuildSystemPrompt` and `BuildUserPrompt` (final code in `phase-37-3.md`, Step 3). **Stop before parsing.** For now end `GenerateAsync` with:

```csharp
using var response = await _client.SendAsync(message, cancellationToken);
return [];
```

Why `return []`: nothing yet asks for the parsed questions, so the least code that passes is to not parse. The next test forces the parser.

```bash
dotnet test tests/FormAI.IntegrationTests --filter GatewayFormGenerationServiceTests
```

**Expected green.** If you get `FileNotFoundException` for `PromptGenerateForm.txt`, the prompt wasn't copied to the test output: check the `.csproj` has it as `CopyToOutputDirectory` in Infrastructure.

---

## Cycle 3: the client reads the OpenAI response envelope

**Behaviour:** the questions come from `choices[0].message.content` (Anthropic's was `content[0].text`). This is *the* change in the parsing code, so it gets its own test.

### 3.1 Red

Add to the test class:

```csharp
[Fact]
public async Task ParsesTheDraftFromTheChoicesEnvelope()
{
    var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, Envelope(Draft))));

    var questions = await CreateService(handler).GenerateAsync("text", Parameters, Guid.NewGuid());

    Assert.Equal(["Capital of France?", "Why?"], questions.Select(q => q.Text));
    Assert.Equal([QuestionType.Single, QuestionType.Text], questions.Select(q => q.Type));
    Assert.Equal([true, false], questions[0].Options.Select(o => o.IsCorrect));
    Assert.Equal("Because", questions[1].CorrectAnswer);
}
```

**Expected red:** `Assert.Equal() Failure` (empty collection vs two texts), or index out of range. Right reason: `GenerateAsync` returns `[]`.

### 3.2 Green

Copy `ParseResponse` and the `using`s it needs from `ClaudeFormGenerationService.cs`, **changing only the envelope lines**:

```csharp
using var apiDoc = JsonDocument.Parse(responseJSON);
var aiReplyText = apiDoc.RootElement.GetProperty("choices")[0]
    .GetProperty("message").GetProperty("content").GetString()
 ?? throw new InvalidOperationException("The gateway returned empty content.");
```

Everything after that (fence stripping, questions, options) is untouched. In `GenerateAsync` replace the `return []` ending with:

```csharp
using var response = await _client.SendAsync(message, cancellationToken);
var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
return ParseResponse(responseJson);
```

Run the whole filter: both tests green.

---

## Cycle 4: an error status is not retried

**Behaviour (AI-7):** if the gateway answers with an error, the client throws and does **not** call again. Only the gateway retries; a retry here would multiply the cost and blow the timeout budget.

### 4.1 Red

```csharp
[Fact]
public async Task AnErrorStatusIsNotRetried()
{
    var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.InternalServerError, "{}")));

    await Assert.ThrowsAsync<HttpRequestException>(
        () => CreateService(handler).GenerateAsync("text", Parameters, Guid.NewGuid()));

    Assert.Equal(1, handler.Calls);
}
```

**Expected red:** a different exception type (the parser fails on `{}`, a `KeyNotFoundException`). The call count is fine; the *throw* is what's missing.

### 4.2 Green

In `GenerateAsync`, right after `SendAsync`:

```csharp
response.EnsureSuccessStatusCode();
```

**Mutation check:** the "not retried" half of this test passes by default, because the client has no retry code. To see it protect something, temporarily register `AddStandardResilienceHandler()` on the client in Cycle 6 and run it: it should go red with `Calls` > 1. This test is the guard that stops someone adding a resilience handler later. Don't leave it in.

---

## Cycle 5: a missing key fails before any call

**Behaviour:** with no `Ai:ApiKey`, `GenerateAsync` throws `InvalidOperationException` and calls nothing. This is the decision that lets the API boot in an environment with no gateway (production today); the failure happens at first use instead.

### 5.1 Red

```csharp
[Theory]
[InlineData("")]
[InlineData("   ")]
public async Task AMissingApiKeyFailsBeforeAnyCall(string apiKey)
{
    var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, Envelope(Draft))));

    await Assert.ThrowsAsync<InvalidOperationException>(
        () => CreateService(handler, apiKey).GenerateAsync("text", Parameters, Guid.NewGuid()));

    Assert.Equal(0, handler.Calls);
}
```

**Expected red:** no exception thrown (the call goes out with `Bearer `), and `Calls` is 1.

### 5.2 Green

First lines of `GenerateAsync`:

```csharp
if (_client.BaseAddress is null || string.IsNullOrWhiteSpace(_settings.ApiKey))
    throw new InvalidOperationException("Ai:GatewayUrl and Ai:ApiKey must be set to generate a form.");
```

Run all client tests: 5 green (the theory counts as 2).

### Refactor and commit

Read the class once top to bottom. Nothing should be left over (no `return []`, no unused usings). Run:

```bash
dotnet build FormAI.sln
dotnet test tests/FormAI.IntegrationTests --filter GatewayFormGenerationServiceTests
```

```bash
git add -A && git commit -m "feat(ai): add the gateway client behind IFormGenerationService"
```

---

## Cycle 6: wire it up and prove it end to end

The unit tests prove the client in isolation. They cannot prove that DI builds it, that `Ai:*` settings bind, or that the API reaches a real HTTP server. That is what the e2e spec is for: **one** test, because everything it covers is wiring.

### 6.1 Red: write the spec before the fake gateway

`frontend/e2e/support/api.ts`: next to `registerVerifiedUser` add (final code in `phase-37-3.md`, Step 8):

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

Create `frontend/e2e/generate-form.spec.ts`:

```ts
import { expect, test } from "@playwright/test";
import { API_URL } from "./env";
import { registerVerifiedUser, signIn } from "./support/api";

test("generating a form goes through the gateway and saves the draft it returns", async ({ request }) => {
  const auth = await signIn(request, await registerVerifiedUser(request));

  const res = await request.post(`${API_URL}/api/forms/generate/text`, {
    headers: auth,
    data: {
      title: "Generated through the fake gateway",
      description: "",
      sourceText: "Paris is the capital of France. The sky is blue because of Rayleigh scattering.",
      sourceType: "Text",
      sourceUrl: "",
      questionCount: 2,
      allowedTypes: ["Single", "Text"],
      difficultyLevel: "Medium",
      isGraded: true,
      showResultsAfterSubmit: false,
      expiresAt: new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString(),
    },
  });
  expect(res.status(), await res.text()).toBe(201);
  const form = await res.json();

  expect(form.questions.map((q: { text: string }) => q.text)).toEqual([
    "What is the capital of France?",
    "Why is the sky blue?",
  ]);
  expect(form.questions[0].options.map((o: { isCorrect: boolean }) => o.isCorrect)).toEqual([true, false, false]);
  expect(form.questions[1].correctAnswer).toBe("Rayleigh scattering");
});
```

Why those assertions: the question texts exist **only** in the fake gateway's canned draft. If they come back, the request really went API → gateway → parser → database. That is the proof; no `__last-request` hook is needed (the unit test already proves what the request contains).

```bash
cd frontend && npm run test:e2e -- generate-form
```

**Expected red:** the API still uses the old Anthropic client, so you get a 500 (no key / no network), not 201.

### 6.2 Green, part 1: register the client

`src/FormAI.Infrastructure/DependencyInjection.cs`: add `using Microsoft.Extensions.Options;` and replace the `Configure<ClaudeSettings>`, `AddHttpClient("claude"…)` and `AddScoped<IFormGenerationService…>` lines with the block in `phase-37-3.md`, Step 4. Then delete `ClaudeFormGenerationService.cs` and `ClaudeSettings.cs`.

```bash
dotnet build FormAI.sln
git grep -n "ClaudeFormGenerationService\|ClaudeSettings" -- src tests frontend
```

The grep must find nothing.

### 6.3 Green, part 2: the fake gateway

Rerun the spec now: it should still fail, because nothing listens on the gateway URL yet. That is a useful red: it shows the API is now calling the *gateway* address.

Create `frontend/e2e/support/fake-gateway.mjs`. It only needs to check the key and return the canned draft (**simpler than the version in `phase-37-3.md`**: no `__last-request` hook):

```js
// Fake AI gateway for CI and e2e: CI never calls a real model (plan T-1).
// Contract: docker/litellm/README.md. Grown slice by slice; today it only answers with a canned draft.
import http from "node:http";

const port = Number(process.env.FAKE_GATEWAY_PORT ?? 4055);
const appKey = process.env.FAKE_GATEWAY_KEY ?? "sk-fake-gateway-key";

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
    { text: "Why is the sky blue?", type: "Text", correctAnswer: "Rayleigh scattering", options: [] },
  ],
};

function send(res, status, body) {
  res.writeHead(status, { "Content-Type": "application/json" });
  res.end(JSON.stringify(body));
}

const server = http.createServer((req, res) => {
  if (req.method === "GET" && req.url === "/health") return send(res, 200, { status: "ok" });

  if (req.method === "POST" && req.url === "/v1/chat/completions") {
    // Rejecting a wrong key makes the e2e prove the API really sends its app key.
    if (req.headers.authorization !== `Bearer ${appKey}`) {
      return send(res, 401, { error: { message: "Invalid key" } });
    }
    req.resume(); // drain the body; its content is covered by the unit tests
    req.on("end", () =>
      send(res, 200, {
        id: "chatcmpl-fake",
        object: "chat.completion",
        choices: [{ index: 0, finish_reason: "stop", message: { role: "assistant", content: JSON.stringify(draft) } }],
        usage: { prompt_tokens: 10, completion_tokens: 10, total_tokens: 20 },
      }),
    );
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

1. Import `GATEWAY_KEY, GATEWAY_PORT, GATEWAY_URL` from `./e2e/env`.
2. In the API's `env`, replace `Claude__ApiKey: "unused-in-e2e",` with `Ai__GatewayUrl: GATEWAY_URL,` and `Ai__ApiKey: GATEWAY_KEY,`.
3. Add as the **first** `webServer` entry:

```ts
{
  command: "node e2e/support/fake-gateway.mjs",
  url: `${GATEWAY_URL}/health`,
  reuseExistingServer: false,
  env: { FAKE_GATEWAY_PORT: String(GATEWAY_PORT), FAKE_GATEWAY_KEY: GATEWAY_KEY },
},
```

```bash
cd frontend
npx prettier --write e2e playwright.config.ts
npm run test:e2e -- generate-form
```

**Expected green.** (Needs Docker Compose Postgres, Redis and Mailpit up, as for any e2e.)

### 6.4 Commit

```bash
git add -A && git commit -m "feat(ai): register the gateway client and add a fake gateway e2e"
```

---

## Cycle 7: docs (no test, but not optional)

`CLAUDE.md` says docs change in the same change. Do the items from `phase-37-3.md`, Step 9: `CLAUDE.md` AI section plus the Playwright line, the LiteLLM README timeout table, the root `README.md` settings, `docker-compose.app.yml`, and the `docs/known-gaps.md` row (gateway failures are a 500 until slice 4; production still injects `Claude__ApiKey` until slice 8).

```bash
git add -A && git commit -m "docs(ai): describe the gateway client"
```

---

## Final verification

Everything below must be green before you open the PR.

```bash
dotnet build FormAI.sln
dotnet test FormAI.sln --filter "FullyQualifiedName!~UserRepositoryTests"   # skips the Docker-only tests; drop the filter if Docker is up
cd frontend
npm run lint && npm run format:check && npm test
npm run test:e2e                                                            # whole suite: the API must still boot
```

Then once, by hand, against the real gateway (costs a fraction of a cent): `docker compose up -d`, `bash docker/litellm/provision-app-key.sh`, run the API with `Ai:ApiKey` set, sign in, call `POST /api/forms/generate/text` from Swagger. The draft should come back. Then `docker compose stop litellm` and repeat: expect a fast 500 (until slice 4), not an 80 s hang.

**Reminder:** don't merge and deploy this slice alone. Production has no gateway yet and still injects `Claude__ApiKey` (risk 1 in `phase-37-3.md`).

## What you ended up with

6 behaviours, 7 tests (1 changed unit test, 5 client tests counting the 2-case theory as 1 cycle, 1 e2e), each of which you saw fail first. Everything else is unchanged code that already has tests.

If you want to practise further: take any one of the "deliberately not tested" items, write that test, and see whether it ever fails. If it passes immediately and no change you could imagine would break it, you've just seen why it was skipped.
