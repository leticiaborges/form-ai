# Plan: refresh token cleanup worker

## Context

`docs/known-gaps.md` documents this gap explicitly: `RefreshTokenHandler` (rotation) and `LogoutHandler` only ever mark a `RefreshToken` row revoked (`RevokedAt`) or leave it to pass `ExpiresAt` — nothing deletes it. The `refresh_tokens` table only grows. This plan closes that gap with an in-process background worker (confirmed with the user, over a separate scheduled ECS task, to avoid new Terraform/CI infra) that periodically bulk-deletes inactive rows, keeping a short retention window for recent activity.

Confirmed policy (from user):
- **Hosting**: in-process `BackgroundService`, hosted by `FormAI.API` (not a separate ECS task).
- **Retention**: a row is deleted once it has been inactive (revoked, or expired) for more than **10 days**. Revocation and expiry are not both consulted for the same row — if a token was revoked, its `RevokedAt` is the moment it became inactive; only unrevoked rows fall back to `ExpiresAt`.
- **Interval**: runs **once a day**.

## Design

Follows the existing conventions: a plain handler (no MediatR) doing the business logic, a thin repository method doing the bulk SQL, and a small worker doing only scheduling. The worker itself lives in **`FormAI.API`, not `FormAI.Infrastructure`** — `BackgroundService`/`IHostedService` is a hosting-framework concern, and CLAUDE.md already draws that exact line for `FormResultsHub`: "`IHubContext<T>` needs the hosting framework only `FormAI.API` references," so its DI lives in `Program.cs`, not `DependencyInjection.cs`. `GenerateRateLimitOptions` is the same story one level down — it lives in `FormAI.API/RateLimiting/`, not Infrastructure, because it's config for an API-hosted concern. The cleanup worker and its options follow both precedents and live in a new `FormAI.API/Workers/`, registered in `Program.cs`. Infrastructure keeps only what it already owns: the repository implementing `IRefreshTokenRepository`.

### 1. Domain — no change

`RefreshToken.IsExpired` / `IsRevoked` stay as the semantic reference, but a bulk `ExecuteDeleteAsync` can't call instance members, so the equivalent predicate is inlined in the repository (same approach the rest of the repo already uses — no generic repository/specification abstraction exists here).

### 2. `IRefreshTokenRepository` + `RefreshTokenRepository`

`src/FormAI.Application/Interfaces/IRefreshTokenRepository.cs` — add:

```csharp
Task<int> DeleteInactiveTokensAsync(DateTime olderThan, CancellationToken cancellationToken = default);
```

`src/FormAI.Infrastructure/Repositories/RefreshTokenRepository.cs` — implement with EF Core's `ExecuteDeleteAsync` (EF Core 10 is already in use, no package changes needed) for a real bulk delete, no entity materialization:

```csharp
public async Task<int> DeleteInactiveTokensAsync(DateTime olderThan, CancellationToken cancellationToken = default)
{
    return await _context.RefreshTokens
        .Where(t => (t.RevokedAt != null && t.RevokedAt < olderThan)
                 || (t.RevokedAt == null && t.ExpiresAt < olderThan))
        .ExecuteDeleteAsync(cancellationToken);
}
```

### 3. Application handler

New file `src/FormAI.Application/Users/Auth/CleanupExpiredRefreshTokensHandler.cs`, placed flat in `Auth/` alongside `RefreshTokenHandler`/`LogoutHandler` (that folder is already the flat home for auth use cases, not one-subfolder-per-operation). Owns the retention business rule — computing the cutoff from a retention period is domain logic, not scheduling:

```csharp
public class CleanupExpiredRefreshTokensHandler
{
    private readonly IRefreshTokenRepository _refreshTokens;

    public CleanupExpiredRefreshTokensHandler(IRefreshTokenRepository refreshTokens) => _refreshTokens = refreshTokens;

    public Task<int> HandleAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        return _refreshTokens.DeleteInactiveTokensAsync(cutoff, cancellationToken);
    }
}
```

### 4. Worker + options (API)

New folder `src/FormAI.API/Workers/`:

- `RefreshTokenCleanupOptions.cs` — same shape as `GenerateRateLimitOptions`:
  ```csharp
  public class RefreshTokenCleanupOptions
  {
      public int RetentionDays { get; set; } = 10;
      public int IntervalHours { get; set; } = 24;
  }
  ```
- `RefreshTokenCleanupWorker.cs` — a `BackgroundService` that owns only scheduling. `BackgroundService` is a singleton, so it resolves the scoped handler via `IServiceScopeFactory` each tick, and swallows/logs exceptions per iteration so a transient DB error doesn't kill the loop permanently:
  ```csharp
  public class RefreshTokenCleanupWorker : BackgroundService
  {
      private readonly IServiceScopeFactory _scopeFactory;
      private readonly IOptions<RefreshTokenCleanupOptions> _options;
      private readonly ILogger<RefreshTokenCleanupWorker> _logger;
      // ctor assigns fields

      protected override async Task ExecuteAsync(CancellationToken stoppingToken)
      {
          using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.Value.IntervalHours));
          do
          {
              try
              {
                  using var scope = _scopeFactory.CreateScope();
                  var handler = scope.ServiceProvider.GetRequiredService<CleanupExpiredRefreshTokensHandler>();
                  var deleted = await handler.HandleAsync(_options.Value.RetentionDays, stoppingToken);
                  _logger.LogInformation("Deleted {Count} inactive refresh tokens older than {RetentionDays} days", deleted, _options.Value.RetentionDays);
              }
              catch (Exception ex) when (ex is not OperationCanceledException)
              {
                  _logger.LogError(ex, "Refresh token cleanup failed");
              }
          } while (await timer.WaitForNextTickAsync(stoppingToken));
      }
  }
  ```
  Runs once immediately on startup, then every `IntervalHours` — acceptable since the delete is idempotent and cheap.

No new package reference is needed: `FormAI.API` is already `Microsoft.NET.Sdk.Web`, which pulls in `Microsoft.Extensions.Hosting` (and `Options`/`Logging`) as part of the shared framework — the same reason the Hub-related types are already available there without an explicit package.

### 5. Wiring

`src/FormAI.Infrastructure/DependencyInjection.cs` — only the handler is registered here, alongside `RefreshTokenHandler`/`LogoutHandler` (unchanged Application-layer registration pattern):
```csharp
services.AddScoped<CleanupExpiredRefreshTokensHandler>();
```

`src/FormAI.API/Program.cs` — worker + its options are registered here, same file/pattern as the Hub/notifier wiring:
```csharp
builder.Services.Configure<RefreshTokenCleanupOptions>(builder.Configuration.GetSection("RefreshTokenCleanup"));
builder.Services.AddHostedService<RefreshTokenCleanupWorker>();
```

`src/FormAI.API/appsettings.json` — add section (mirrors `RateLimiting`/`Jwt` style):
```json
"RefreshTokenCleanup": {
  "RetentionDays": 10,
  "IntervalHours": 24
}
```

No new Terraform/ECS/EventBridge, no `deploy.yml` changes — the worker rides inside the existing `api` ECS service/task.

### 6. Tests

- **Unit** (`tests/FormAI.UnitTests/Users/CleanupExpiredRefreshTokensTests.cs`), following `RefreshTokenTests.cs`/`LogoutTests.cs` conventions (NSubstitute for `IRefreshTokenRepository`, handler constructed directly): assert `HandleAsync(retentionDays)` calls `DeleteInactiveTokensAsync` with a cutoff `~DateTime.UtcNow.AddDays(-retentionDays)` (tolerance window) and returns the repository's count.
- **Integration** (`tests/FormAI.IntegrationTests/`, new `RefreshTokenRepositoryTests.cs` alongside the existing `UserRepositoryTests.cs`, using the same Testcontainers Postgres setup): seed rows in each state (active, revoked >10 days ago, revoked <10 days ago, expired >10 days ago, expired <10 days ago but not revoked) and assert `DeleteInactiveTokensAsync` removes exactly the two "older than cutoff" rows and leaves the rest — this is the part worth verifying against real SQL rather than a mock, since the OR/precedence logic is easy to get subtly wrong.

### 7. Docs

- `docs/known-gaps.md` — remove the "Expired and revoked refresh tokens are never deleted" row (the gap is closed); update the "Last audited" line at the top with today's date and a short clause, matching the file's existing style.
- No new ADR (per user instruction) — this is a straightforward application of the layering rule CLAUDE.md already states via the Hub exception, not a new hard-to-reverse decision.

## Verification

1. `dotnet build FormAI.sln` — compiles with the new files (no new package references).
2. `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj` — new handler unit test passes.
3. `dotnet test tests/FormAI.IntegrationTests/FormAI.IntegrationTests.csproj` (requires Docker for Testcontainers) — new repository test passes against real PostgreSQL.
4. Manual smoke check: run `dotnet run --project src/FormAI.API`, confirm the app still starts (worker logs its first run on startup), and temporarily set `RefreshTokenCleanup:IntervalHours` low in `appsettings.Development.json` plus seed an old revoked/expired row via psql to watch it get deleted and logged.
