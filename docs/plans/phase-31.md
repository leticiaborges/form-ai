# Phase 31: Realtime Results via SignalR + Redis Backplane

## The problem

Today, when a respondent submits a form, the owner's Results tab shows stale data until they manually reload the page. The only existing "refresh" mechanism — `resultsReloadKey` in `FormEditorPage.tsx` — only bumps after the owner saves the form editor; it has nothing to do with new submissions. This phase makes the Results tab auto-refresh when a submission comes in while the owner has it open.

**Horizontal scaling is a hard requirement, not a nice-to-have.** This project is intended to eventually run on AWS behind a load balancer with multiple API instances (ECS/ASG). A naive single-process SignalR setup breaks the moment there's more than one instance: `Clients.Group(...)` on instance A only reaches WebSocket connections instance A itself is holding, so a submission handled by instance A would never reach an owner connected to instance B. A Redis backplane is what makes cross-instance delivery work, and every design decision below is made with that in mind.

Approaches considered and rejected for this feature: short polling (would work but no real push), SSE+Redis (same fan-out problem as SignalR, more hand-rolled code for no real benefit here), and a fully AWS-native pub/sub stack via API Gateway WebSocket + Lambda + DynamoDB + EventBridge (architecturally the most "cloud-native" but wildly oversized for "notify one owner's open tab" — see [ADR 0005](../adr/0005-realtime-results-via-signalr-redis.md) once written).

## Decisions already made

- **The Hub lives in `FormAI.API`**, not `FormAI.Infrastructure`. `IHubContext<T>` needs the ASP.NET Core hosting shared framework, which only the `Sdk.Web`-style `FormAI.API` project has. Its DI registration therefore goes in `Program.cs`, not `Infrastructure/DependencyInjection.cs` where every other handler is registered — a deliberate, one-off exception.
- **The Hub pushes a bare "refetch" signal** (`ResultsUpdated(Guid formId)`), never the computed `FormResults` payload. This avoids running the unbounded `GetFormResultsHandler`/`FormResultsCalculator` aggregation (see `docs/known-gaps.md` — "Reading every submission at once is unbounded") on every single submission regardless of whether an owner is even watching, avoids a second delivery path for the `FormResults` DTO shape, and fits the frontend's existing `reloadKey`-triggers-refetch pattern with **zero changes** to `SummaryResultsTab.tsx`/`IndividualResultsTab.tsx`.
- **`SubmitFormHandler` gets a new Application-layer interface**, `IFormResultsNotifier`, following the existing `IFormRepository`/`IFormGenerationService` pattern: the interface lives in Application (framework-free), the SignalR-aware implementation lives in API.
- **Auth reuses the existing JWT bearer scheme** via the standard `?access_token=` query-string pattern — browsers' native WebSocket API can't set custom headers, so the token has to travel some other way. Wired through `OnMessageReceived` in the *existing* `AddJwtBearer` block.
- **The notifier must never throw.** A Redis/SignalR hiccup can't be allowed to fail a respondent's submission. Resilience (try/catch + log) lives inside the implementation, not in `SubmitFormHandler`.
- **Local Redis runs without auth**, same as `mailpit` today — it's only reachable on `localhost` in dev. Production (AWS ElastiCache) will use AUTH/TLS via a connection string that never lives in this repo at all (see "Secrets" below).

## Secrets / connection strings — do not put real values in this file

This file is committed to git. Any real connection string, password, or endpoint belongs in **`docs/plans/phase-31.local.md`** instead — a new gitignored worksheet (added to `.gitignore` in step 0 below) where you fill in your actual local values as you go. Nothing in this document should ever contain a literal secret.

### Step 0 — one-time setup

- Add this line to `.gitignore` under the "Secrets / config overrides" section: `docs/plans/*.local.md`
- Create `docs/plans/phase-31.local.md` from this skeleton and fill it in as you complete the steps below:
  ```markdown
  # Phase 31 — local values (never commit)

  ## appsettings.Development.json addition
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }

  ## Notes
  (anything else you needed locally — e.g. a non-default Redis port if 6379 was taken)
  ```

## 1. Application-layer seam (no SignalR yet)

**Create** `src/FormAI.Application/Interfaces/IFormResultsNotifier.cs`:

```csharp
namespace FormAI.Application.Interfaces;

public interface IFormResultsNotifier
{
    Task NotifyResultsChangedAsync(Guid formId, CancellationToken cancellationToken = default);
}
```

**Modify** `src/FormAI.Application/Submissions/SubmitForm/SubmitFormHandler.cs`:
- Add `private readonly IFormResultsNotifier _notifier;` and take it as a constructor parameter.
- Right after the existing `await _submissions.AddAsync(submission, cancellationToken);` (line 61 today — this is the genuine post-commit point since `SubmissionRepository.AddAsync` calls `SaveChangesAsync` internally), add:
  ```csharp
  await _notifier.NotifyResultsChangedAsync(form!.Id, cancellationToken);
  ```

No other Application file changes at this stage — Application stays free of any ASP.NET Core/SignalR reference.

## 2. Hub and API wiring

**Add package** to `src/FormAI.API/FormAI.API.csproj`:
```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" Version="<latest 8.x/9.x matching your target framework>" />
```
(Base `Hub`/`IHubContext` types ship in the ASP.NET Core shared framework `FormAI.API` already references — no separate package for those.)

**Create** `src/FormAI.API/Hubs/IFormResultsClient.cs`:
```csharp
namespace FormAI.API.Hubs;

public interface IFormResultsClient
{
    Task ResultsUpdated(Guid formId);
}
```

**Create** `src/FormAI.API/Hubs/FormResultsHub.cs`:
```csharp
using System.Security.Claims;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.Validation;
using FormAI.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FormAI.API.Hubs;

[Authorize]
public class FormResultsHub : Hub<IFormResultsClient>
{
    private readonly IFormRepository _forms;

    public FormResultsHub(IFormRepository forms)
    {
        _forms = forms;
    }

    public static string GroupName(Guid formId) => $"form-results:{formId}";

    public async Task JoinFormResults(Guid formId)
    {
        var userId = GetUserId();
        var form = await _forms.GetByIdAsync(formId);

        try
        {
            FormAccessValidator.CheckOwnerAccess(form, userId);
        }
        catch (NotFoundException)
        {
            // Same generic message as every other owner-only endpoint — a non-owner
            // can't tell "doesn't exist" from "not yours" here either.
            throw new HubException("Form not found.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(formId));
    }

    public Task LeaveFormResults(Guid formId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(formId));

    private Guid GetUserId()
    {
        // Mirror FormsController.CurrentUserId's claim lookup exactly.
        var idClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? Context.User?.FindFirst("sub")?.Value;
        return Guid.Parse(idClaim!);
    }
}
```
> Check `FormsController.CurrentUserId` (`src/FormAI.API/Controllers/FormsController.cs:66-69`) before writing this and copy its exact claim-lookup logic — don't reimplement it slightly differently.

**Create** `src/FormAI.API/Hubs/SignalRFormResultsNotifier.cs`:
```csharp
using FormAI.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace FormAI.API.Hubs;

public class SignalRFormResultsNotifier : IFormResultsNotifier
{
    private readonly IHubContext<FormResultsHub, IFormResultsClient> _hub;
    private readonly ILogger<SignalRFormResultsNotifier> _logger;

    public SignalRFormResultsNotifier(
        IHubContext<FormResultsHub, IFormResultsClient> hub,
        ILogger<SignalRFormResultsNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyResultsChangedAsync(Guid formId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients.Group(FormResultsHub.GroupName(formId)).ResultsUpdated(formId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify results change for form {FormId}", formId);
        }
    }
}
```

**Modify** `src/FormAI.API/Program.cs`:
```csharp
builder.Services.AddSignalR()
    .AddStackExchangeRedis(
        builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing."));

// Registered here, not in Infrastructure/DependencyInjection.cs, because
// IHubContext<FormResultsHub> is only visible from FormAI.API.
builder.Services.AddSingleton<IFormResultsNotifier, SignalRFormResultsNotifier>();
```
and, after `app.MapControllers();`:
```csharp
app.MapHub<FormResultsHub>("/hubs/form-results");
```

CORS: the existing `"FrontendDev"` policy (`WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()`) should already be sufficient for the SignalR negotiate/WebSocket upgrade since auth travels via query string, not cookies — verify this in step 8 of the test checklist rather than pre-emptively widening CORS.

## 3. Auth over the Hub connection

**Modify** `src/FormAI.Infrastructure/DependencyInjection.cs`, inside the existing `.AddJwtBearer(options => { ... })` block (currently lines 87–101), add an `Events` assignment:
```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(accessToken) &&
            context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
        {
            context.Token = accessToken;
        }
        return Task.CompletedTask;
    }
};
```
Use the broad `/hubs` prefix (not the specific `/hubs/form-results` path) so a future second hub doesn't require touching this file again. This needs `using Microsoft.AspNetCore.Authentication.JwtBearer;` (already present in this file) — no new package.

## 4. Redis for local dev

**Modify** `docker-compose.yml` — add a `redis` service alongside `postgres`/`mailpit`:
```yaml
  redis:
    image: redis:8-alpine
    container_name: form-ai-redis
    ports:
      - "6379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 5s
      retries: 5
```
No volume — this Redis instance is a pure pub/sub backplane, not durable storage; losing it on restart is fine.

Then, per Step 0: add `"ConnectionStrings": { "Redis": "localhost:6379" }` to your local (untracked) `appsettings.Development.json`, and record it in `docs/plans/phase-31.local.md`.

**Modify `CLAUDE.md`**:
- Configuration table: add a row `ConnectionStrings__Redis | Redis connection string for the SignalR backplane (local dev: docker-compose `redis` service)`.
- "Local infrastructure is Docker Compose" line: extend to mention Redis alongside PostgreSQL and Mailpit.

## 5. Frontend

**Modify** `frontend/package.json`: add `@microsoft/signalr` (latest).

**Modify** `frontend/vite.config.ts`: add a second proxy entry mirroring the existing `/api` one:
```ts
'/hubs': {
  target: 'http://localhost:5155',
  changeOrigin: true,
  ws: true,
},
```

**Create** `frontend/src/hooks/useFormResultsHub.ts` (first file in a new `hooks/` directory — this codebase's `src` is currently flat: `api/`, `components/`, `pages/`, `types/`, `utils/`):
```ts
import { useEffect, useRef } from 'react';
import * as signalR from '@microsoft/signalr';

export function useFormResultsHub(formId: string, onResultsChanged: () => void) {
  const callbackRef = useRef(onResultsChanged);
  callbackRef.current = onResultsChanged;

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/form-results', {
        accessTokenFactory: () => localStorage.getItem('accessToken') ?? '',
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .build();

    connection.on('ResultsUpdated', (changedFormId: string) => {
      if (changedFormId === formId) callbackRef.current();
    });

    const join = () =>
      connection.invoke('JoinFormResults', formId).catch((err) =>
        console.warn('Failed to join form results group', err)
      );

    connection.start().then(join).catch((err) =>
      console.warn('SignalR connection failed to start', err)
    );

    // A reconnect gets a new ConnectionId, so Redis-backed group membership
    // from before the drop is gone — must rejoin explicitly.
    connection.onreconnected(join);

    return () => {
      connection.off('ResultsUpdated');
      connection.stop();
    };
  }, [formId]);
}
```
Connection/invoke failures are only logged, never thrown into the component — the existing REST fetch plus the save-triggered `reloadKey` remain a working fallback if the realtime channel never connects (e.g. blocked by a restrictive network).

**Modify** `frontend/src/components/results/ResultsTab.tsx`:
- Add `const [liveReloadKey, setLiveReloadKey] = useState(0);`
- `useFormResultsHub(formId, () => setLiveReloadKey((k) => k + 1));`
- Pass `reloadKey={reloadKey + liveReloadKey}` down to the existing sub-tab components instead of the raw `reloadKey` prop.
- **`SummaryResultsTab.tsx` and `IndividualResultsTab.tsx` need zero changes** — they already key their fetch `useEffect` on the `reloadKey` prop.

**Modify `frontend/src/api/axios.ts`** only if the access-token key isn't already exactly `"accessToken"` in `localStorage` — confirm before writing `useFormResultsHub`, don't assume.

## 6. Testing

**Create** `tests/FormAI.UnitTests/Submissions/SubmitForm/RecordingFormResultsNotifier.cs` — a hand-rolled fake (this repo's stated convention is "no mocking library"; this is the one narrow, justified exception since it's a side-effecting infrastructure collaborator on a use-case orchestrator, not a stub replacing pure Domain logic):
```csharp
using FormAI.Application.Interfaces;

namespace FormAI.UnitTests.Submissions.SubmitForm;

public class RecordingFormResultsNotifier : IFormResultsNotifier
{
    public List<Guid> NotifiedFormIds { get; } = new();

    public Task NotifyResultsChangedAsync(Guid formId, CancellationToken cancellationToken = default)
    {
        NotifiedFormIds.Add(formId);
        return Task.CompletedTask;
    }
}
```
Add a `SubmitFormHandler` test (none exist today, per `docs/known-gaps.md`'s "Test coverage" row — this is a net-new addition) asserting `RecordingFormResultsNotifier.NotifiedFormIds` contains the submitted form's id after a successful submission.

**Do not** try to unit-test `FormResultsHub`'s authorization logic or the Redis fan-out itself in `FormAI.UnitTests`/`FormAI.IntegrationTests` — that needs real multi-instance verification (see the manual checklist below). Call this out as a conscious scope decision if anyone asks why there's no `FormResultsHubTests`.

## 7. Docs

**Modify `docs/known-gaps.md`**: the "Realtime updates (SignalR)" row currently claims `FormHub` is "an empty class containing two comments" — this is stale, no such file exists in tracked source today. Once this phase ships, remove the row entirely (or rewrite it if anything is left undone).

**Create** `docs/adr/0005-realtime-results-via-signalr-redis.md`, following the frontmatter/Consequences/Status format of `docs/adr/0003-respondent-token-identity.md`. Cover two decisions:
1. SignalR+Redis chosen over polling / SSE+Redis / an AWS-native API Gateway WebSocket+Lambda+DynamoDB+EventBridge stack, for this specific feature — and why (right-sized for "one owner, one open tab", not a case that needs the connection-count-independent scaling an AWS-native stack buys you).
2. Authorizing the Hub via query-string JWT rather than a header — forced by the browser WebSocket API's inability to set custom headers on the handshake request.

`CONTEXT.md`: no change needed — this phase adds an implementation mechanism (a SignalR group per form), not a new domain concept.

## Horizontal scaling — what changes going from 1 instance to N behind a load balancer

This is the part to re-verify carefully once you actually deploy, since it can't be fully proven locally:

1. **Fan-out.** With 1 instance, `AddStackExchangeRedis(...)` is inert — plain in-memory SignalR would already work. With N instances, `Clients.Group(...)` on instance A only reaches connections A itself holds; the Redis backplane makes every instance publish group/send calls to a shared pub/sub channel every instance subscribes to. This is the entire reason Redis exists in this design.
2. **No sticky sessions needed for the WebSocket transport itself** — once upgraded, a WS connection is pinned to one instance for its whole lifetime, so there's no repeated per-request routing decision for the load balancer to get right. Sticky sessions would only matter for the long-polling/SSE fallback transports. Recommendation: force WebSocket-only on the client (`skipNegotiation: true`, explicit `HttpTransportType.WebSockets`) to sidestep that case — accepting no fallback on networks that block WebSocket outright.
3. **Load balancer idle timeout vs. SignalR keep-alive.** SignalR server defaults: 15s keep-alive, 30s client timeout. An AWS ALB's default target idle timeout is 60s — raise it to ~120s for real margin. Rule to configure by: **the load balancer's idle timeout must exceed SignalR's `KeepAliveInterval`, with margin**, not the other way around.
4. **Deploys / scale-in.** Set ECS/ASG deregistration delay to ~30–60s (not the 300s default). The client's `withAutomaticReconnect` + `onreconnected → JoinFormResults(formId)` rejoin (already in `useFormResultsHub.ts` above) is what makes a forced disconnect during a deploy transparent to the owner — they reconnect to a healthy instance and automatically rejoin their form's group, no page reload needed.
5. **Health checks.** The load balancer's health check must hit a plain HTTP endpoint, not the hub path — the hub route expects the SignalR handshake protocol, not a bare GET-200 probe. This repo has no dedicated `/health` endpoint yet; that's a separate, pre-existing gap you'll hit when actually deploying to ECS, out of scope for this phase.

## Manual test checklist

1. `git add .gitignore` shows the new `docs/plans/*.local.md` line; confirm `docs/plans/phase-31.local.md` itself does **not** show up in `git status`.
2. `dotnet build FormAI.sln` — confirms the new `Microsoft.AspNetCore.SignalR.StackExchangeRedis` package and all new files compile.
3. `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj` — confirms the new `SubmitFormHandler` notifier test passes (no Docker needed for this).
4. `docker compose up` (now including `redis`), `dotnet run --project src/FormAI.API`, `cd frontend && npm run dev`.
5. Open a form's editor → Results tab in one browser tab (as the owner). Submit that form as a different/anonymous respondent in a second tab. Confirm the first tab's Results updates without a manual reload.
6. Confirm a **non-owner** cannot join another owner's form-results group: manually invoke `JoinFormResults` with a form id you don't own (e.g. via browser devtools against the hub connection) and confirm it fails the same way a non-owner `GET /api/forms/{id}/results` would (can't distinguish "doesn't exist" from "not yours").
7. **Multi-instance simulation** (the closest local proxy for the ALB requirement without deploying to AWS): run two instances of the API locally on different ports (e.g. `--urls http://localhost:5155` and `--urls http://localhost:5156`) against the same Postgres + Redis. Point two browser tabs at different ports' hub connections, submit a form so the request lands on one instance, and confirm the Results tab connected to the *other* instance still updates — this proves the Redis backplane is doing real cross-instance fan-out, not just working by accident because everything's on one process.
8. Kill one of the two local instances mid-connection and confirm the client's `withAutomaticReconnect` + `onreconnected` rejoin logic recovers without a page reload — the local proxy for the ALB deregistration-delay/deploy scenario.
9. Confirm CORS: with the frontend dev server on `:5173` and the API on `:5155`, verify the SignalR negotiate/WebSocket handshake succeeds without adjusting the existing `"FrontendDev"` CORS policy; only widen it if step 4/5 actually fails because of it.
