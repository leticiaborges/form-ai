# Phase 34: Rate limiting for `POST /api/forms/generate/text`

## Context

`GenerateFromText` (`FormsController.cs:107`) calls Claude through `GenerateFormHandler` → `ClaudeFormGenerationService`. Every call spends Anthropic credits, and any signed-in user can call it in a loop (listed in `docs/known-gaps.md`).

Two separate holes, two fixes:

1. **Too many calls** → ASP.NET Core's built-in rate limiter (no new package).
2. **Too expensive a call** → `SourceText` is only checked for empty (`GenerateFormHandler.cs:33`). A rate limit caps how _often_ you can call, not how _big_ each call is, so a user could paste a huge document into each allowed call. Add a length cap.

### Options considered

| Option                                            | Verdict                                                                                                           |
| ------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| **ASP.NET Core built-in rate limiter**            | **Chosen.** In the framework, per-endpoint policies, partitionable by user.                                       |
| Redis-backed limiter                              | Only needed with more than one API instance. Redis already exists (SignalR backplane), so it is the upgrade path. |
| DB quota (forms created by the user in 24h)       | Durable, no new infrastructure. Good complement later.                                                            |
| Reverse proxy / gateway (nginx, Cloudflare, YARP) | Can't see the user id, so no per-user quota. Good against anonymous floods.                                       |
| Anthropic-side spend limit                        | A backstop: it stops the bill, but also stops the app for everyone.                                               |

## Concepts you need first

Read this before the edits; the code below is small because these few ideas do the work.

**Rate limiting is middleware.** `app.UseRateLimiter()` adds a step to the request pipeline. For each request it asks a _limiter_ "may this one through?". If yes, the request continues to the controller. If no, the middleware answers immediately and the controller never runs.

**Policy.** A named rule, registered once (`AddPolicy("generate", ...)`) and attached to an endpoint with `[EnableRateLimiting("generate")]`. Endpoints without the attribute are not limited at all. An endpoint can have **one** policy, which is why this phase uses a single limit rather than "burst _and_ daily".

**Partition.** A policy does not count all requests together; it counts them **per partition key**. Our key is the user id, so each user gets their own counter and one user hitting the limit does not affect anyone else. The policy function runs on every request and returns "which partition is this request in, and what are that partition's limits?". The limiter object for a key is created the first time that key is seen and then reused.

**Algorithms.** The framework ships four; we use the first.

| Limiter        | How it counts                                                   | Use when                                                                                                                          |
| -------------- | --------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| Fixed window   | N permits per window (e.g. 10 per hour), counter resets at once | Simple and easy to reason about. **Chosen.** Weakness: up to 2N calls can straddle a window boundary.                             |
| Sliding window | Same, but the window is split into segments that slide          | Smoother. Same options plus `SegmentsPerWindow`; a drop-in swap for fixed window if the boundary weakness matters.                |
| Token bucket   | Bucket refills at a steady rate; each call takes a token        | You want to allow short bursts but a steady average rate.                                                                         |
| Concurrency    | Limits calls _in flight at once_, not per time                  | Protecting a slow resource from parallel calls. Doesn't fit "cost per hour".                                                      |

**Lease.** Asking a limiter for a permit returns a _lease_. If it was denied, the lease carries metadata such as `RetryAfter` (how long until a permit frees up). We read it in `OnRejected` to tell the user how long to wait.

**Queueing.** A limiter can hold excess requests in a queue instead of rejecting them. We set `QueueLimit = 0`: for an expensive AI call we want a fast "no", not a request hanging open.

**The default rejection is 503, not 429.** Without `RejectionStatusCode = 429` the middleware answers `503 Service Unavailable`. Easy to miss, and 503 tells clients the _server_ is at fault.

**Middleware order matters.** `UseRateLimiter()` must come **after** `UseAuthentication()` / `UseAuthorization()`, otherwise `HttpContext.User` is still empty when the policy builds the partition key and every request lands in the same bucket. It must also come after routing (the middleware reads the endpoint's `[EnableRateLimiting]` metadata); `WebApplication` inserts routing at the start of the pipeline automatically, so nothing to add.

**The limiter runs before the controller.** So every request that reaches the endpoint uses up a permit, including ones the handler later rejects with 400. That is a deliberate trade-off: the limiter can't know whether a request will succeed. It is also what lets you test the limiter without spending credits (see Verification).

**Memory, per instance.** The counters live in the API process's memory. A restart resets them; two API instances each keep their own, so the real limit is N × instances. Fine for one instance. See "Going further".

**Why the frontend needs no 429 handling.** `OnRejected` will write the same `{ message, errors, code }` JSON that `ExceptionHandlingMiddleware` writes. `getErrorMessage` (`frontend/src/utils/getErrorMessage.ts`) already reads `response.data.message`, and `CreateFormPage.onSubmit` already shows it with `showError`. The user sees "Try again in 12 minute(s)" for free. (The middleware _can't_ produce this itself: it only catches exceptions, and the limiter answers without throwing.)

## Changes

Do them in this order; the app builds after each step.

### 1. `src/FormAI.API/RateLimiting/GenerateRateLimitOptions.cs` (new)

The limits come from configuration, not from constants, so they can be tuned per environment without a rebuild. The defaults here apply when the config section is missing.

```csharp
namespace FormAI.API.RateLimiting;

public class GenerateRateLimitOptions
{
    public const string SectionName = "RateLimiting:Generate";

    public int PermitLimit { get; set; } = 10;
    public int WindowMinutes { get; set; } = 60;
}
```

10 per hour means at most ~240 Claude calls per user per day (up to 20 in a single hour if they straddle a window boundary). Adjust to taste.

### 2. `src/FormAI.API/RateLimiting/RateLimitingExtensions.cs` (new)

All the limiter wiring in one place, so `Program.cs` stays two lines.

```csharp
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FormAI.API.RateLimiting;

public static class RateLimitPolicies
{
    public const string Generate = "generate";
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services,
        IConfiguration configuration)
    {
        var generate = configuration.GetSection(GenerateRateLimitOptions.SectionName)
            .Get<GenerateRateLimitOptions>() ?? new GenerateRateLimitOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicies.Generate, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = generate.PermitLimit,
                        Window = TimeSpan.FromMinutes(generate.WindowMinutes),
                        QueueLimit = 0,
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;

                var message = "Too many requests. Please try again later.";
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                    message = $"You have reached the limit for generating forms. " +
                        $"Try again in {(int)Math.Ceiling(retryAfter.TotalMinutes)} minute(s).";
                }

                await response.WriteAsJsonAsync(
                    new { message, errors = (object?)null, code = (string?)null },
                    cancellationToken);
            };
        });

        return services;
    }

    // Same claims, same order, as FormsController.CurrentUserId. The endpoint is [Authorize], so the
    // IP fallback is only a safety net and should never be reached.
    private static string GetPartitionKey(HttpContext httpContext) =>
        httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? httpContext.User.FindFirstValue("sub")
        ?? httpContext.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}
```

What each piece does:

- `RateLimitPolicies.Generate` is a constant so the policy name is spelled once (registration and attribute both use it; a typo would only fail at runtime).
- `RateLimitPartition.GetFixedWindowLimiter(key, factory)`: "requests with this key share one fixed-window limiter, built by this factory the first time the key appears".
- `Retry-After` is the standard HTTP header (seconds) that well-behaved clients and proxies understand; the `message` is for humans.
- `WriteAsJsonAsync` serializes with camelCase, so the body is `{"message":"...","errors":null,"code":null}`, identical in shape to the errors the rest of the API returns.

### 3. `src/FormAI.API/Program.cs`

Add the using at the top:

```csharp
using FormAI.API.RateLimiting;
```

Register the service, after `AddCors`:

```csharp
builder.Services.AddApiRateLimiting(builder.Configuration);
```

Add the middleware **after `UseAuthorization()`** and before `MapControllers()`:

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
```

### 4. `src/FormAI.API/Controllers/FormsController.cs`

Add two usings:

```csharp
using FormAI.API.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
```

Add the attribute to the action (`FormsController.cs:108`):

```csharp
    // POST /api/forms/generate/text
    [HttpPost("generate/text")]
    [EnableRateLimiting(RateLimitPolicies.Generate)]
    public async Task<IActionResult> GenerateFromText(...)
```

Only this action is limited. `[EnableRateLimiting]` on the class would limit every action in `FormsController`, which is not what we want.

### 5. `src/FormAI.API/appsettings.json`

Add a section next to `Claude`:

```json
  "RateLimiting": {
    "Generate": {
      "PermitLimit": 10,
      "WindowMinutes": 60
    }
  }
```

Config keys map to environment variables with `__` as the separator: `RateLimiting__Generate__PermitLimit`. You will use that to lower the limit for the manual test.

### 6. `src/FormAI.Application/Forms/GenerateForm/GenerateFormHandler.cs`

Next to `PrefixTitle` (line 14):

```csharp
    public const int MaxSourceTextLength = 30_000;
```

In `ValidateForm`, directly after the `IsNullOrWhiteSpace(request.SourceText)` block (line 39):

```csharp
        if (request.SourceText.Length > MaxSourceTextLength)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["sourceText"] = [$"Source text must be at most {MaxSourceTextLength} characters."]
            });
        }
```

30,000 characters is roughly 5,000 words, about 7-8k input tokens. It sits in the Application layer (not the API) because it is a business rule about the request, like the title and description limits already there. It is a starting value; tune it.

### 7. `tests/FormAI.UnitTests/Forms/GenerateFormTests.cs`

Add a row to `InvalidRequests` (line 211). The existing theory already asserts nothing was generated or saved:

```csharp
        { "My form 1", new string('a', GenerateFormHandler.MaxSourceTextLength + 1), 7, "sourceText" },
```

And a boundary test, next to the other facts, proving the limit itself is accepted:

```csharp
    [Fact]
    public async Task SourceTextAtTheLimit_IsAccepted()
    {
        var request = CreateRequest(sourceText: new string('a', GenerateFormHandler.MaxSourceTextLength));

        await _handler.HandleAsync(request, Guid.NewGuid());

        await _forms.Received(1).AddAsync(Arg.Any<Form>(), Arg.Any<CancellationToken>());
    }
```

### 8. Frontend: `frontend/src/pages/CreateFormPage.tsx`

The server now rejects long text, so the form should say so before sending it. Above `createFormSchema`:

```ts
// Keep in sync with GenerateFormHandler.MaxSourceTextLength on the server.
const MAX_SOURCE_TEXT_LENGTH = 30_000;
```

Change the `sourceText` rule (line 16):

```ts
  sourceText: z
    .string()
    .min(50, "Source text must be at least 50 characters long")
    .max(
      MAX_SOURCE_TEXT_LENGTH,
      `Source text must be at most ${MAX_SOURCE_TEXT_LENGTH} characters long`,
    ),
```

### 9. Frontend test: `frontend/src/pages/CreateFormPage.test.tsx`

Add inside the `describe`. `user.paste` is used because typing 30,001 characters one by one would be very slow.

```tsx
  it("rejects source text longer than the server allows, without calling the API", async () => {
    let called = false;
    server.use(
      http.post(GENERATE_URL, () => {
        called = true;
        return HttpResponse.json({ formId: "form-1", title: "Generated" }, { status: 201 });
      }),
    );

    const user = userEvent.setup();
    renderCreate();

    await user.click(screen.getByLabelText("Source content"));
    await user.paste("a".repeat(30_001));
    await user.click(screen.getByRole("button", { name: "Generate form" }));

    expect(
      await screen.findByText("Source text must be at most 30000 characters long"),
    ).toBeInTheDocument();
    expect(called).toBe(false);
  });
```

There is deliberately **no** Vitest test for the 429 toast: `renderWithProviders` does not mount a toaster, and the path (`getErrorMessage` → `showError`) is the same one every other API error already takes.

### 10. Docs (`CLAUDE.md` says to update them in the same change)

- `docs/known-gaps.md`
  - Delete the **Rate limiting on generation** row from "Not built".
  - Add a row to "Wrong or incomplete on purpose": _Generation rate limit is per API instance_ — counters are in process memory, so a restart resets them and N instances allow N× the limit; the request body limit is still Kestrel's 30 MB default, so the 30,000-character cap is applied after the body has been parsed.
  - Update the "Last audited" line.
- `CLAUDE.md`, "Business rules" → "Forms and questions", add:
  > `POST /api/forms/generate/text` is rate limited per signed-in user (fixed window, `RateLimiting:Generate` settings, default 10 per hour) and answers 429 with `Retry-After`. The limiter runs before the handler, so requests that fail validation still count. `SourceText` is capped at `GenerateFormHandler.MaxSourceTextLength` (30,000 characters). The limiter is wired in `FormAI.API/RateLimiting/`, and its 429 body has the same `{ message, errors, code }` shape as `ExceptionHandlingMiddleware`.
- ADR: not required. Choosing per-instance memory over Redis is easy to reverse. Write `docs/adr/0006-...` only if you want to record why.
- `CONTEXT.md`: no new domain term.

## Verification

### Automated

```bash
dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj
cd frontend && npm test
```

### Manual: does the limiter answer 429?

This costs no Anthropic credits. An empty `sourceText` is rejected by the handler with 400 _before_ Claude is called, but the limiter (which runs first) still counts each request.

Terminal 1, with Docker Compose running, lower the limit so you don't need many requests:

```powershell
$env:RateLimiting__Generate__PermitLimit = "3"
dotnet run --project src/FormAI.API
```

Terminal 2 (use a verified account):

```powershell
$base = "http://localhost:5155/api"
$login = Invoke-RestMethod -Method Post -Uri "$base/auth/login" -ContentType "application/json" `
  -Body (@{ email = "you@example.com"; password = "your-password" } | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($login.accessToken)" }

$body = @{
  sourceText = ""; sourceType = "Text"; questionCount = 3; difficultyLevel = "Medium"
  isGraded = $false; showResultsAfterSubmit = $false
  expiresAt = (Get-Date).ToUniversalTime().AddDays(7).ToString("o")
} | ConvertTo-Json

1..5 | ForEach-Object {
  $i = $_
  try {
    Invoke-RestMethod -Method Post -Uri "$base/forms/generate/text" -Headers $headers `
      -ContentType "application/json" -Body $body | Out-Null
  } catch {
    $r = $_.Exception.Response
    "$i -> $([int]$r.StatusCode)  Retry-After=$($r.Headers['Retry-After'])"
  }
}
```

Expected: requests 1-3 print `400`, requests 4-5 print `429` with a `Retry-After` of up to 3600 seconds.

Also check:

- A **second user's** token is not affected while the first is blocked (per-user partitions).
- Restarting the API resets the counter (the per-instance memory limitation, seen for yourself).
- In the browser, with `PermitLimit` at 1, generate two forms: the second shows the "Try again in N minute(s)" toast.
- Remove the env var (or open a new terminal) so your normal runs use the default of 10.

## Going further (not in this phase)

- **A second, daily tier.** One endpoint gets one policy, so "3 per minute _and_ 20 per day" needs `PartitionedRateLimiter.CreateChained(...)` assigned to `options.GlobalLimiter`, with each limiter returning `RateLimitPartition.GetNoLimiter(...)` for every endpoint except this one. More moving parts, so add it only if the hourly cap proves too loose.
- **More than one API instance.** Move the counters to Redis (already running for SignalR) via a custom `PartitionedRateLimiter` or a package such as `RedisRateLimiting`.
- **A durable daily quota.** Count `Form` rows created by the user in the last 24h in `GenerateFormHandler`; survives restarts and needs no new infrastructure.
- **A smaller request body limit.** Kestrel accepts 30 MB by default and the length cap only runs after parsing. `[RequestSizeLimit]` on the action would stop it earlier, but Kestrel's `BadHttpRequestException` is not handled by `ExceptionHandlingMiddleware` (it would become a 500), so that middleware would need a case for it first.
- **Behind a reverse proxy**, `RemoteIpAddress` is the proxy's address unless `UseForwardedHeaders` is configured. Irrelevant today (the key is the user id), but it matters the moment you rate-limit anonymous endpoints such as `POST {id}/submit`.
