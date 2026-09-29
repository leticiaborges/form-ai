# Demo login verification

**Verdict**: FAIL
**Profile**: light
**Diff range**: 4c52b5d..39dd584
**Round**: 1 - full
**Verifier**: independent sub-agent (author != verifier)

All 28 checks are proven at HEAD with located evidence. The feature still fails: plan criterion
**9a** requires the landing "Try demo" button to be "at the size of the existing hero buttons", no
check covers that clause, and the code makes it larger (`frontend/src/pages/LandingPage.tsx:36`
`px-10 py-3 text-lg shadow-lg ring-4 ring-brand-200` against `px-8 py-3 text-base` at
`LandingPage.tsx:41` and `:46`). A second uncovered defect against criterion 12 sits in
`LoginPage.tsx:92-94`. See "Plan criteria against checks" and "Ranked gaps".

No faults injected - profile light.

## Binding sources

The plan marks no source binding (`Sources`: the user's task description, `CLAUDE.md` and
`docs/known-gaps.md`, none marked binding), and profile `light` does not run step 1. n/a.

## Checks

| Check | Claim | Proof run | Evidence | Result |
| --- | --- | --- | --- | --- |
| C1 | handler adds one verified demo user, name `Demo user`, email `^demo-[0-9a-f-]{36}@demo\.invalid$` | unit batch, `Passed StartDemoTests.CreatesAVerifiedDemoUser` | `tests/FormAI.UnitTests/Users/StartDemoTests.cs:45` `Assert.Single(added)`; `:46` `Assert.True(user.IsDemo)`; `:47` `Assert.True(user.IsEmailVerified)`; `:48` `Assert.NotNull(user.VerifiedAt)`; `:49` `Assert.Equal("Demo user", user.Name)`; `:50` `Assert.Matches(new Regex(@"^demo-[0-9a-f-]{36}@demo\.invalid$"), user.Email)` | PASS |
| C2 | stored hash is `IPasswordHasher.Hash(configured password)` | unit batch, `Passed StartDemoTests.HashesTheConfiguredPassword` | `StartDemoTests.cs:60` `Assert.Equal(HashedPassword, Assert.Single(added).PasswordHash)`; `:61` `_hasher.Received(1).Hash(DemoPassword)` (stub `:22` maps `DemoPassword` -> `HashedPassword`) | PASS |
| C3 | issues access token, stores refresh token hashed via `HashRefreshToken`, expiry `RefreshTokenLifetime`, returns `AuthTokens` | unit batch, `Passed StartDemoTests.IssuesTokensLikeLogin` | `StartDemoTests.cs:76` `_jwt.Received(1).GenerateAccessToken(user)`; `:78` `Assert.Equal("refresh-token", tokens.RefreshToken)`; `:81` `Assert.Equal(HashOf("refresh-token"), stored.TokenHash)`; `:82` `Assert.InRange(stored.ExpiresAt, before + RefreshTokenLifetime, after + RefreshTokenLifetime)`; `:83` `Assert.Equal(stored.ExpiresAt, tokens.RefreshTokenExpiresAt)`. Expected hash/lifetime live in `tests/FormAI.UnitTests/Users/AuthTestData.cs:13,15,20` (helper-defined, not readable at the assertion - minor test finding) | PASS |
| C4 | two calls, two users, different ids and emails | unit batch, `Passed StartDemoTests.EveryCallCreatesADistinctUser` | `StartDemoTests.cs:95` `Assert.Equal(2, added.Count)`; `:96` `Assert.NotEqual(added[0].Id, added[1].Id)`; `:97` `Assert.NotEqual(added[0].Email, added[1].Email)` | PASS |
| C5 | `null`/`""`/whitespace password throws `NotFoundException`, adds no user, no refresh token | unit batch, 3 cases passed: `(password: null)`, `(password: "")`, `(password: "   ")` | `StartDemoTests.cs:101-103` `[InlineData(null)] [InlineData("")] [InlineData("   ")]`; `:106` `Assert.ThrowsAsync<NotFoundException>(...)`; `:108` `_users.DidNotReceive().AddAsync(...)`; `:109` `_refreshTokens.DidNotReceive().AddAsync(...)`. Level gap on the route's 404, see below | PASS |
| C6 | demo user's token carries `is_demo` = `"true"` | integration batch, `Passed JwtServiceDemoClaimTests.DemoUserTokenCarriesIsDemoTrue` | `tests/FormAI.IntegrationTests/JwtServiceDemoClaimTests.cs:24` `Assert.Equal("true", token.Claims.Single(c => c.Type == "is_demo").Value)` | PASS |
| C7 | `User.Create` user's token has no `is_demo` claim | integration batch, `Passed JwtServiceDemoClaimTests.RegularUserTokenHasNoIsDemoClaim` | `JwtServiceDemoClaimTests.cs:32` `Assert.DoesNotContain(token.Claims, c => c.Type == "is_demo")` | PASS |
| C8 | `POST /api/auth/demo` -> 200, `accessToken`, refresh cookie as login, `is_demo` "true", name `Demo user` | `npx playwright test e2e/demo.spec.ts` - `✓ [chromium] › e2e\demo.spec.ts:21:1 › starts, refreshes and rate limits a demo session` | `frontend/e2e/demo.spec.ts:23` `expect(first.status()).toBe(200)`; `:28` `expect(claims.is_demo).toBe("true")`; `:29` `expect(claims.name).toBe("Demo user")`; `:37-40` `toMatch(/httponly/i)`, `/secure/i`, `/samesite=strict/i`, `/path=\/api\/auth/i` | PASS |
| C9 | demo user round-trips `IsDemo` true, regular false, migrated DB | integration batch, `Passed UserRepositoryTests.PersistsTheDemoFlag` | `tests/FormAI.IntegrationTests/UserRepositoryTests.cs:49` `await context.Database.MigrateAsync()`; `:59` `Assert.True((await freshRepo.GetByIdAsync(demo.Id))!.IsDemo)`; `:60` `Assert.False((await freshRepo.GetByIdAsync(regular.Id))!.IsDemo)` (fresh context `:57`) | PASS |
| C10 | migration adds `is_demo` non-null boolean default false | `grep -c "defaultValue: false" .../*AddUserIsDemo.cs` printed `1`, exit 0 | `src/FormAI.Infrastructure/Migrations/20260929130710_AddUserIsDemo.cs:16` `type: "boolean"`; `:17` `nullable: false`; `:18` `defaultValue: false`; model side `src/FormAI.Infrastructure/Data/Configurations/UserConfiguration.cs:44-46` `.IsRequired().HasDefaultValue(false)` | PASS |
| C11 | `appsettings.json` Demo limits 10/15/3, no `Demo:Password` | integration batch, `Passed DemoConfigurationTests.ShippedDefaults` | `tests/FormAI.IntegrationTests/DemoConfigurationTests.cs:24` `Assert.Equal(10, demo.PermitLimit)`; `:25` `Assert.Equal(15, demo.WindowMinutes)`; `:26` `Assert.Equal(3, demo.SegmentsPerWindow)`; `:27` `Assert.Null(configuration["Demo:Password"])`; source `src/FormAI.API/appsettings.json:28-31` | PASS |
| C12 | second call's token differs in `sub` and `email` | same Playwright run (single test, passed) | `demo.spec.ts:44` `expect(second.status()).toBe(200)`; `:46` `expect(other.sub).not.toBe(claims.sub)`; `:47` `expect(other.email).not.toBe(claims.email)` | PASS |
| C13 | refresh with the demo cookie keeps `is_demo` "true" | same Playwright run | `demo.spec.ts:53` `expect(refreshed.status()).toBe(200)`; `:55` `expect(refreshedClaims.sub).toBe(claims.sub)`; `:56` `expect(refreshedClaims.is_demo).toBe("true")` | PASS |
| C14 | login with demo email + configured password -> 200 | same Playwright run | `demo.spec.ts:60` `data: { email: claims.email, password: "e2e-demo-password" }` (matches `frontend/playwright.config.ts:64` `Demo__Password: "e2e-demo-password"`); `:62` `expect(login.status()).toBe(200)` | PASS |
| C15 | no Mailpit message to the demo email | same Playwright run | `demo.spec.ts:66` `query: \`to:${claims.email}\``; `:68` `expect((await mail.json()).messages ?? []).toHaveLength(0)`. Precision note: the Mailpit response status is not asserted and `?? []` would also pass on a body without `messages`; structurally backed by `StartDemoHandler` having no email dependency (`src/FormAI.Application/Users/Auth/StartDemoHandler.cs:15-16`) | PASS |
| C16 | sixth call with limit 5 -> 429, `errors`/`code` null, message names limit and window | same Playwright run | `demo.spec.ts:71` three more 200s; `:73` `expect(limited.status()).toBe(429)`; `:75` `expect(body.errors).toBeNull()`; `:76` `expect(body.code).toBeNull()`; `:77` `toContain("5 demo sessions")`; `:78` `toContain("15 minutes")`; harness `playwright.config.ts:65` `RateLimiting__Demo__PermitLimit: "5"` | PASS |
| C17 | landing: Try demo first in hero row, primary (`bg-brand-600`); Start for free outline | vitest batch, `✓ LandingPage demo > leads the hero with a primary Try demo` | `frontend/src/pages/LandingPage.test.tsx:26` `expect(buttons[0]).toHaveTextContent("Try demo")`; `:27` `expect(buttons[0]).toHaveClass("bg-brand-600")`; `:29` `expect(startForFree).toHaveClass("border-brand-600")`; `:30` `.not.toHaveClass("bg-brand-600")`. The claim as written holds; it omits AC 9a's size clause (gap 1) | PASS |
| C18 | login: full-width primary Try demo before Email; Log in outline | vitest batch, `✓ LoginPage demo > leads with a primary Try demo and an outline Log in` | `frontend/src/pages/LoginPage.test.tsx:121` `expect(tryDemo).toHaveClass("bg-brand-600", "w-full")`; `:123` `expect(tryDemo.compareDocumentPosition(email) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()`; `:125` `expect(logIn).toHaveClass("border-brand-600")`; `:126` `.not.toHaveClass("bg-brand-600")` | PASS |
| C19 | login Try demo: POST with no body, token stored, lands on `/dashboard` | vitest batch, `✓ LoginPage demo > signs in through Try demo and opens the dashboard` | `LoginPage.test.tsx:144` `findByText("Dashboard page")`; `:145` `expect(calls).toBe(1)`; `:146` `expect(body).toBe("")`; `:147` `expect(tokenStore.get()).toBe(accessToken)` | PASS |
| C20 | landing Try demo lands on `/dashboard` | vitest batch, `✓ LandingPage demo > signs in through Try demo and opens the dashboard` | `LandingPage.test.tsx:45` `expect(await screen.findByText("Dashboard page")).toBeInTheDocument()`; `:46` `expect(tokenStore.get()).toBe(accessToken)` | PASS |
| C21 | pending: disabled, loading state, second click sends nothing | vitest batch, `✓ LoginPage demo > disables Try demo while the request is pending` | `LoginPage.test.tsx:167` `expect(button).toBeDisabled()`; `:171` `expect(calls).toBe(1)`. Precision note: the loading indicator is not asserted directly; `frontend/src/components/Button.tsx` sets `disabled={disabled \|\| isLoading}` and the Try demo button passes no `disabled`, so disabled implies the `isLoading` branch that renders the spinner | PASS |
| C22 | login: 429 `message` shown, stays on `/login`; no message -> fallback | vitest batch, `✓ LoginPage demo > shows why the demo could not start` | `LoginPage.test.tsx:187` `findByText("You have reached the limit.")`; `:188` `queryByText("Dashboard page")).not.toBeInTheDocument()`; `:189` `getByLabelText("Email")).toBeInTheDocument()`; `:193-194` `findByText("Could not start the demo. Please try again.")`. Precision note: "in the login form's error box" is not asserted (code: `frontend/src/pages/LoginPage.tsx:92-94`) | PASS |
| C23 | landing: fallback text next to button, stays on `/` | vitest batch, `✓ LandingPage demo > shows why the demo could not start` | `LandingPage.test.tsx:55` `findByText("Could not start the demo. Please try again.")).toBeVisible()`; `:56` no `Dashboard page`; `:57` `getByRole("main")`. Precision note: "next to the button" not asserted (code `frontend/src/pages/LandingPage.tsx:51-53`) | PASS |
| C24 | `userFromAccessToken` isDemo true/false | vitest batch, `✓ userFromAccessToken > reads the is_demo claim` | `frontend/src/auth/tokenStore.test.ts:12` `expect(userFromAccessToken(fakeJwt({ ...base, is_demo: "true" })).isDemo).toBe(true)`; `:13` `expect(userFromAccessToken(fakeJwt(base)).isDemo).toBe(false)` | PASS |
| C25 | demo user sees exact banner, "Create an account" -> `/register` | vitest batch, `✓ BasePage demo banner > warns a demo user and links to register` | `frontend/src/components/BasePage.test.tsx:31` `expect(banner).toHaveTextContent(BANNER)` (BANNER `:7-8` is the exact AC 13 string); `:32-35` `getByRole("link", { name: "Create an account" })).toHaveAttribute("href", "/register")` | PASS |
| C26 | regular user sees no banner | vitest batch, `✓ BasePage demo banner > shows no banner to a regular user` | `BasePage.test.tsx:42` `expect(screen.queryByRole("status")).not.toBeInTheDocument()`; `:43` `expect(screen.queryByText(/demo account/i)).not.toBeInTheDocument()` | PASS |
| C27 | banner has no control but the link | vitest batch, `✓ BasePage demo banner > offers no way to dismiss the banner` | `BasePage.test.tsx:50` `expect(within(banner).queryAllByRole("button")).toHaveLength(0)`; `:51` `expect(within(banner).getAllByRole("link")).toHaveLength(1)` | PASS |
| C28 | CONTEXT.md, CLAUDE.md, known-gaps.md document demo accounts | grep chain exit 0 | `CONTEXT.md:13` `**Demo account**:`; `CLAUDE.md:41` "creates a **demo account**: `User.CreateDemo` ..."; `docs/known-gaps.md:20` `\| **Demo accounts are never cleaned up** \|` | PASS |

All named tests confirmed to exist by `rg -n` (line numbers above) and all were added in the diff range
(`git diff --stat 4c52b5d..HEAD` lists `StartDemoTests.cs`, `JwtServiceDemoClaimTests.cs`,
`DemoConfigurationTests.cs`, `UserRepositoryTests.cs` +21, `demo.spec.ts`, `LandingPage.test.tsx`,
`LoginPage.test.tsx` +88, `BasePage.test.tsx`, `tokenStore.test.ts`).

## Level and precision findings

- **Level gap, Surface `404`.** The plan's Surface lists `404` for `POST /api/auth/demo`; its only
  proof is at the handler (C5, `StartDemoTests.cs:106`, `NotFoundException`). The mapping is existing
  code (`src/FormAI.API/Middleware/ExceptionHandlingMiddleware.cs:37`
  `NotFoundException ex => (HttpStatusCode.NotFound, ...)`), and the controller action does not
  catch it (`src/FormAI.API/Controllers/AuthController.cs:51-58`), so the 404 is well-founded by
  reading, but no proof crosses the HTTP boundary for it. checks.md states this openly; it remains
  a level gap, not a proof.
- **Precision:** C15 does not assert the Mailpit call succeeded; C21 asserts disabled, not the
  loading indicator; C22 does not assert the error box; C23 does not assert placement. Each is
  satisfied by the code as read, so none is downgraded.
- **Test finding:** C3's expected hash and lifetime come from `AuthTestData.cs` helpers, not from
  the assertion site.

## Plan criteria against checks

| Criterion / surface | Covered by | Finding |
| --- | --- | --- |
| AC 1 | C1, C2, C3, C8 | - |
| AC 2 "no email" | C15 | - |
| AC 2 "SHALL NOT create a `UserConfirmationToken`" | none | uncovered clause; structurally satisfied - `StartDemoHandler.cs:15-16` takes no confirmation-token dependency and `User.CreateDemo` (`src/FormAI.Domain/Entities/User.cs:37-42`) creates none |
| AC 3 | C4, C12 | "each token carries its own user's `sub`" is proven as "subs differ" (C12) plus `sub` stable across refresh (C13); `sub == user.Id` is existing `JwtService` code |
| AC 4 | C6, C8, C13 | - |
| AC 5 | C7 | - |
| AC 6 | C5 | level gap on 404 (above) |
| AC 7 "429 with shape" | C16, C11 | - |
| AC 7 "SHALL NOT create a user" at 429 | none | uncovered clause; structurally satisfied - the limiter rejects before the action runs (`AuthController.cs:52` `[EnableRateLimiting(RateLimitPolicies.Demo)]`, `src/FormAI.API/Program.cs:81` `UseRateLimiter()` after auth) |
| AC 8 | C14 | - |
| AC 9 | C18 | - |
| **AC 9a** "at the size of the existing hero buttons" | none | **uncovered and contradicted**: `LandingPage.tsx:36` `className="px-10 py-3 text-lg shadow-lg ring-4 ring-brand-200"` vs the existing hero buttons `LandingPage.tsx:41,46` `className="px-8 py-3 text-base"`. C17 checks order and variant only |
| AC 10 | C19, C20 | - |
| AC 11 | C21 (login only) | `/` loading state has no proof (Observable row for `/` lists AC 11); same hook and `Button` (`LandingPage.tsx:34` `isLoading={isStarting}`), so low risk |
| AC 12 | C22, C23 | **defect outside the checks**: `LoginPage.tsx:92-94` renders `serverError ?? demoError`, and a demo click never clears `serverError` (only `onSubmit` does, `LoginPage.tsx:35`). After a failed login, a failed demo request shows the stale login error, not the demo's message, violating "SHALL show the server's `message`" |
| AC 13 | C25 | "on every page that uses `BasePage`" is structural (`frontend/src/components/BasePage.tsx:33` inside `BasePage`, before `children` at `:45`) |
| AC 14 | C26 | - |
| AC 15 | C27 | - |
| Landing door 1 | C9, C10 | `private set` and set only by `CreateDemo`: `User.cs:15` `public bool IsDemo { get; private set; }`, `User.cs:40` the only assignment |
| Landing door 2 | C6, C7, C24 | code `src/FormAI.Infrastructure/Security/JwtService.cs:36-37`, `frontend/src/auth/tokenStore.ts:52` |
| Surface 200 / 404 / 429 | C8 / C5 (handler only) / C16 | 404 level gap |

## Swept existing

- **authorization: existing** - "anonymous by design like `Register` and `Login`". Confirmed:
  `AuthController.cs:10-12` carries no `[Authorize]`; `Register` (`:36`) and `Login` (`:43`) have
  none either, and `StartDemo` (`:51-53`) matches them. The e2e call at `demo.spec.ts:16` sends no
  credentials and gets 200 (`:23`). The rate-limit partition for an anonymous caller is the remote
  IP (`src/FormAI.API/RateLimiting/RateLimitingExtensions.cs:90-94`), as checks.md states. Holds.

## Gate

- `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~StartDemoTests" --logger "console;verbosity=normal"` - 7 passed, 0 failed (5 facts + 3 theory cases counted as 7 rows: CreatesAVerifiedDemoUser, HashesTheConfiguredPassword, IssuesTokensLikeLogin, EveryCallCreatesADistinctUser, BlankPasswordIsNotFoundAndCreatesNothing x3)
- `dotnet test tests/FormAI.IntegrationTests/FormAI.IntegrationTests.csproj --filter "FullyQualifiedName~JwtServiceDemoClaimTests|FullyQualifiedName~UserRepositoryTests.PersistsTheDemoFlag|FullyQualifiedName~DemoConfigurationTests.ShippedDefaults" --logger "console;verbosity=normal"` - 4 passed, 0 failed
- `cd frontend && npx vitest run src/pages/LandingPage.test.tsx src/pages/LoginPage.test.tsx src/auth/tokenStore.test.ts src/components/BasePage.test.tsx --reporter=verbose` - 15 passed, 0 failed (4 files; 11 are the named demo tests, 4 pre-existing LoginPage tests)
- `cd frontend && npx playwright test e2e/demo.spec.ts --reporter=list` - 1 passed, 0 failed (carries C8, C12-C16)
- `grep -c "defaultValue: false" src/FormAI.Infrastructure/Migrations/*AddUserIsDemo.cs` - `1`, exit 0 (C10)
- `grep -q "Demo account" CONTEXT.md && grep -qi "demo account" CLAUDE.md && grep -qi "demo account" docs/known-gaps.md` - exit 0 (C28)

Faults: no faults injected - profile light.

## Ranked gaps

1. AC 9a size clause uncovered by any check and contradicted by the code - no check - `frontend/src/pages/LandingPage.tsx:36` vs `:41`, `:46`
2. AC 12 stale-error defect: a prior login `serverError` masks the demo failure message - no check - `frontend/src/pages/LoginPage.tsx:92-94`, `:35`
3. Surface `404` has no HTTP-level proof (handler level only) - C5 - `tests/FormAI.UnitTests/Users/StartDemoTests.cs:106`, mapping `src/FormAI.API/Middleware/ExceptionHandlingMiddleware.cs:37`
4. AC 7 "no user at 429" and AC 2 "no `UserConfirmationToken`" clauses have no check (structurally satisfied) - no check - `AuthController.cs:52`, `StartDemoHandler.cs:15-16`
5. AC 11 loading state on `/` unproven (login only) - C21 - `LandingPage.tsx:34`
