# Demo login checks

Profile: light
Plan: `.specs/features/demo-login/plan.md`

29 checks in 4 slices · 2 one-way doors · 1 open, of which 1 blocks go-live

Proof commands run from the repo root. `dotnet test` targets one project and filters to one test
class; `vitest` and `playwright` run from `frontend/`. The Docker proofs (C9) and the e2e proof
need the local stack up (Postgres, Redis, Mailpit) and `.env`, exactly as `npm run test:e2e` already does.

The e2e API is given `Demo__Password` (a fixed test value, not a secret) and
`RateLimiting__Demo__PermitLimit=5` in `frontend/playwright.config.ts`, so one test can walk the
endpoint and still reach the 429 on the sixth call. The limiter is in-memory and partitioned by
remote IP for this anonymous endpoint, so **everything that calls `POST /api/auth/demo` over HTTP
lives in one Playwright test** (`fullyParallel` would otherwise split tests across workers sharing
the partition). The browser flows are proven with MSW in Vitest, never against the real endpoint,
for the same reason. The shipped defaults (10 / 15 / 3) are proven by C11.

## Checks

### S1 - One click starts a demo session · 7 files · 30 KB · ~9k

**C1** - `StartDemoHandler` adds exactly one user with `IsDemo` true, `IsEmailVerified` true, `VerifiedAt` set, name `Demo user`, and an email matching `^demo-[0-9a-f-]{36}@demo\.invalid$` (AC 1)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~StartDemoTests.CreatesAVerifiedDemoUser"`

**C2** - The stored password hash is the one `IPasswordHasher.Hash` returns for the configured demo password, and no plain text is stored (AC 1)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~StartDemoTests.HashesTheConfiguredPassword"`

**C3** - The handler issues an access token for that user, stores a refresh token hashed by `IJwtService.HashRefreshToken` with expiry `RefreshTokenLifetime` from now, and returns `AuthTokens` with the raw refresh token and that expiry, as `LoginHandler` does (AC 1)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~StartDemoTests.IssuesTokensLikeLogin"`

**C4** - Two calls add two users with different ids and different emails (AC 3)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~StartDemoTests.EveryCallCreatesADistinctUser"`

**C5** - A `null`, `""` or whitespace-only demo password throws `NotFoundException` and adds no user and no refresh token (AC 6)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~StartDemoTests.BlankPasswordIsNotFoundAndCreatesNothing"`

**C6** - An access token issued by `JwtService` for a demo user carries the claim `is_demo` with the value `"true"` (AC 4)
Proof: `dotnet test tests/FormAI.IntegrationTests/FormAI.IntegrationTests.csproj --filter "FullyQualifiedName~JwtServiceDemoClaimTests.DemoUserTokenCarriesIsDemoTrue"`

**C7** - An access token issued for a user made by `User.Create` carries no `is_demo` claim at all (AC 5)
Proof: `dotnet test tests/FormAI.IntegrationTests/FormAI.IntegrationTests.csproj --filter "FullyQualifiedName~JwtServiceDemoClaimTests.RegularUserTokenHasNoIsDemoClaim"`

**C8** - `POST /api/auth/demo` answers `200` with `{ accessToken }` and a `refresh_token` cookie set as login sets it (HttpOnly, Secure, SameSite Strict, path `/api/auth`), and the token decodes to `is_demo` `"true"`, name `Demo user` (AC 1, 4)
Proof: `cd frontend && npx playwright test e2e/demo.spec.ts -g "starts, refreshes and rate limits a demo session"` (assertion: first call)

**C9** - A demo user saved through `UserRepository` reads back with `IsDemo` true, and a user from `User.Create` reads back with `IsDemo` false, against a migrated database (Landing door 1)
Proof: `dotnet test tests/FormAI.IntegrationTests/FormAI.IntegrationTests.csproj --filter "FullyQualifiedName~UserRepositoryTests.PersistsTheDemoFlag"`

**C10** - The `AddUserIsDemo` migration adds `is_demo` as a non-null boolean with default `false`, so existing rows backfill to not-demo (Landing door 1, Impact: stored data)
Proof: `grep -c "defaultValue: false" src/FormAI.Infrastructure/Migrations/*AddUserIsDemo.cs` prints `1` (the assertion is the count; `grep -c` exits 0 on a match)

**C11** - `appsettings.json` binds `RateLimiting:Demo` to `PermitLimit` 10, `WindowMinutes` 15, `SegmentsPerWindow` 3, and holds no `Demo:Password` (AC 7, Assumptions)
Proof: `dotnet test tests/FormAI.IntegrationTests/FormAI.IntegrationTests.csproj --filter "FullyQualifiedName~DemoConfigurationTests.ShippedDefaults"`

**C12** - A second `POST /api/auth/demo` returns a token whose `sub` and `email` differ from the first (AC 3, over HTTP)
Proof: `cd frontend && npx playwright test e2e/demo.spec.ts -g "starts, refreshes and rate limits a demo session"` (assertion: second call)

**C13** - `POST /api/auth/refresh` with the demo session's refresh cookie returns a new access token that still decodes to `is_demo` `"true"` (AC 4, at refresh)
Proof: `cd frontend && npx playwright test e2e/demo.spec.ts -g "starts, refreshes and rate limits a demo session"` (assertion: refresh)

**C14** - `POST /api/auth/login` with the demo user's email and the configured demo password answers `200` (AC 8)
Proof: `cd frontend && npx playwright test e2e/demo.spec.ts -g "starts, refreshes and rate limits a demo session"` (assertion: login)

**C15** - No message addressed to the demo user's email reaches Mailpit after the demo call (AC 2)
Proof: `cd frontend && npx playwright test e2e/demo.spec.ts -g "starts, refreshes and rate limits a demo session"` (assertion: mailpit search `to:<email>` returns 0 messages)

**C16** - With `PermitLimit` 5, the sixth `POST /api/auth/demo` from one client answers `429` with `errors` and `code` null and a `message` naming the limit and its window (AC 7)
Proof: `cd frontend && npx playwright test e2e/demo.spec.ts -g "starts, refreshes and rate limits a demo session"` (assertion: sixth call)

### S2 - "Try demo" leads on the landing and login pages · 6 files · 22 KB · ~8k

**C17** - `LandingPage` renders "Try demo" first in the hero button row in the primary style (`bg-brand-600`) at the same size classes as "Start for free" (`px-8 py-3 text-base`), and "Start for free" in the outline style (AC 9a)
Proof: `cd frontend && npx vitest run src/pages/LandingPage.test.tsx -t "leads the hero with a primary Try demo"`

**C18** - `LoginPage` renders a full-width primary "Try demo" before the Email field in document order, and the form's "Log in" submit button in the outline style (AC 9)
Proof: `cd frontend && npx vitest run src/pages/LoginPage.test.tsx -t "leads with a primary Try demo and an outline Log in"`

**C19** - Clicking "Try demo" on `/login` sends `POST /api/auth/demo` with no body, stores the returned token in `tokenStore`, and lands on `/dashboard` (AC 10)
Proof: `cd frontend && npx vitest run src/pages/LoginPage.test.tsx -t "signs in through Try demo and opens the dashboard"`

**C20** - Clicking "Try demo" on `/` does the same and lands on `/dashboard` (AC 10)
Proof: `cd frontend && npx vitest run src/pages/LandingPage.test.tsx -t "signs in through Try demo and opens the dashboard"`

**C21** - While the demo request is pending the button is disabled and shows its loading state, and a second click sends no second request (AC 11)
Proof: `cd frontend && npx vitest run src/pages/LoginPage.test.tsx -t "disables Try demo while the request is pending"`

**C22** - A demo request answered `429 { message }` shows that `message` in the login form's error box and stays on `/login`; one that fails with no `message` shows `Could not start the demo. Please try again.` (AC 12, on `/login`)
Proof: `cd frontend && npx vitest run src/pages/LoginPage.test.tsx -t "shows why the demo could not start"`

**C23** - On `/` a failed demo request shows the same fallback text next to the button and stays on `/` (AC 12, on `/`)
Proof: `cd frontend && npx vitest run src/pages/LandingPage.test.tsx -t "shows why the demo could not start"`

**C29** - After a failed password login, a failed demo request replaces the login error with the demo failure text on `/login`; the old login error is no longer shown (AC 12)
Proof: `cd frontend && npx vitest run src/pages/LoginPage.test.tsx -t "replaces an earlier login error with the demo failure"`

### S3 - A demo user is told the account is temporary · 4 files · 12 KB · ~4k

**C24** - `userFromAccessToken` returns `isDemo: true` for a token with `is_demo: "true"` and `isDemo: false` for one without the claim (Landing door 2)
Proof: `cd frontend && npx vitest run src/auth/tokenStore.test.ts -t "reads the is_demo claim"`

**C25** - `BasePage` for a demo user renders `You're using a demo account. Your forms are only available for 1 day. Create an account to keep them.` with "Create an account" as a link to `/register` (AC 13)
Proof: `cd frontend && npx vitest run src/components/BasePage.test.tsx -t "warns a demo user and links to register"`

**C26** - `BasePage` for a user who is not a demo user renders no such banner (AC 14)
Proof: `cd frontend && npx vitest run src/components/BasePage.test.tsx -t "shows no banner to a regular user"`

**C27** - The demo banner contains no button or control other than the "Create an account" link, so it cannot be dismissed (AC 15)
Proof: `cd frontend && npx vitest run src/components/BasePage.test.tsx -t "offers no way to dismiss the banner"`

### S4 - The docs stay true · 3 files · 20 KB · ~5k

**C28** - `CONTEXT.md` defines **Demo account**, `CLAUDE.md` states the demo rules, and `docs/known-gaps.md` has a row for demo accounts never being cleaned up (Impact: docs)
Proof: `grep -q "Demo account" CONTEXT.md && grep -qi "demo account" CLAUDE.md && grep -qi "demo account" docs/known-gaps.md`

## Coverage

| Set (size) | Member -> proof | Unproven |
| --- | --- | --- |
| `POST /api/auth/demo` statuses (3) | 200 C8 · 404 C5 · 429 C16 | - |
| blank demo password (3) | `null` C5 · `""` C5 · whitespace C5, table-driven over all 3 | - |
| access token by user kind (2) | demo C6 · regular C7 | - |
| the shape of the `is_demo` claim: at demo start, at refresh (2) | start C8 · refresh C13 | - |
| pages carrying "Try demo" (2) | `/` C17 C20 C23 · `/login` C18 C19 C21 C22 | - |
| hero button styles (2) | Try demo primary C17 · Start for free outline C17 | - |
| demo failure text sources (2) | server `message` C22 · fallback C22, C23 | - |
| banner states (2) | demo user C25 · regular user C26 | - |
| door 1: `users.is_demo` (2) | round trip C9 · migration default C10 | - |
| door 2: `is_demo` claim (2) | issued C6, C7 · decoded by the frontend C24 | - |
| startup config: `Demo:Password` (1 assembly) | e2e harness C8 | - |
| startup config: `RateLimiting:Demo` (2 places) | shipped `appsettings.json` C11 · e2e harness override C16 | - |

- `Demo:Password` in production (ECS secret) is not a member: it is the plan's open question 1 and this build's Out of scope; locally it is set by hand in the untracked `appsettings.Development.json`
- Claims naming a status code, route or response shape: C8, C16 cross the HTTP boundary; **C5's `404` is proven at the handler** (it throws `NotFoundException`) and the mapping to 404 is the existing `ExceptionHandlingMiddleware`, not re-proven here - a `404` with `Demo:Password` unset over HTTP has no proof, since it would need a second API instance without the password
- C8, C12-C16 share one Playwright test; each names the assertion it stands for
- No other check claims more than the single case its proof exercises

## Swept

- validation: C5
- failure modes: C22, C23
- idempotency: n/a - every call creates a distinct user on purpose (C4, C12); nothing can be retried into a duplicate because the email carries a fresh guid
- authorization: existing - anonymous by design like `Register` and `Login` on `AuthController`; the demo user has no more rights than any verified user
- concurrency: n/a - each call inserts one row with a fresh guid email, so two simultaneous calls share no state and cannot hit the unique email index
- data lifecycle: n/a - deleting demo users is the separate cleanup service; C9 and C10 prove the flag it will query
- dependency failure: C22
- state transitions: n/a - a demo user is created verified and never changes state
- observability: n/a - no logging requirement in this change

## Out of scope

- Wiring `Demo:Password` into Terraform, `infra.yml` and the ECS task definition - the value goes into ECS secrets like `Jwt__Secret`, but that touches production infrastructure and needs a `DEMO_PASSWORD` GitHub secret first; it is the plan's open question 1 and needs its own go-ahead

## Handoff

- S1 = 9k (Application, Domain, Infrastructure, API), S2 = 8k and S3 = 4k (frontend), S4 = 5k (docs); total ~26k of file reads and edits, under the 150k budget - one builder
- Mechanism: one builder
