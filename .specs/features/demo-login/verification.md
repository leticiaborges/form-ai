# Demo login verification

**Verdict**: PASS
**Profile**: light
**Diff range**: 4c52b5d..ae56c3e (fix: 39dd584..ae56c3e)
**Round**: 2 - scoped
**Verifier**: independent sub-agent (author != verifier)

All 29 checks are proven at HEAD `ae56c3e` with located evidence. Both round-1 FAIL findings are
closed. The hero "Try demo" button now uses the same size classes as the other hero buttons, and
a demo click now clears the stale login error. The remaining findings are notes. None of them
contradicts a criterion, and each is either an accepted limitation in checks.md or a clause that
the code meets, with the file:line evidence given below.

No faults injected - profile light.

## Scope of this round

- The fix diff `39dd584..ae56c3e` touches only `frontend/src/pages/LandingPage.tsx` (+1/-1),
  `LandingPage.test.tsx` (+2), `LoginPage.tsx` (+4/-1), `LoginPage.test.tsx` (+20) and
  `.specs/.../checks.md` (C17 claim widened, C29 added). No backend, e2e, `BasePage` or
  `tokenStore` file changed (`git diff --stat 39dd584..ae56c3e -- src tests frontend/e2e` is empty).
- **Proofs:** re-run in full at `ae56c3e` for all of C1-C29 (see Gate).
- **Citations:** refreshed at `ae56c3e` for C17, C22, C29 (touched), and for C18-C21, C23 (their lines
  moved because tests were inserted above them). All other evidence is marked `carried from 39dd584`.
  Those files are byte-identical between the two commits, so the round-1 line numbers still hold.
- **Round-1 gaps:** each one is re-judged below, under "Round-1 gaps re-judged".

## Binding sources

Carried from 39dd584. The plan marks no source as binding, and profile `light` does not run step 1. n/a.

## Checks

| Check | Claim | Proof run | Evidence | Result |
| --- | --- | --- | --- | --- |
| C1 | handler adds one verified demo user, name `Demo user`, email `^demo-[0-9a-f-]{36}@demo\.invalid$` | unit batch @ae56c3e, `Passed StartDemoTests.CreatesAVerifiedDemoUser` | carried from 39dd584: `tests/FormAI.UnitTests/Users/StartDemoTests.cs:45` `Assert.Single(added)`; `:46` `Assert.True(user.IsDemo)`; `:47` `Assert.True(user.IsEmailVerified)`; `:48` `Assert.NotNull(user.VerifiedAt)`; `:49` `Assert.Equal("Demo user", user.Name)`; `:50` `Assert.Matches(new Regex(@"^demo-[0-9a-f-]{36}@demo\.invalid$"), user.Email)` | PASS |
| C2 | stored hash is `IPasswordHasher.Hash(configured password)` | unit batch @ae56c3e, `Passed StartDemoTests.HashesTheConfiguredPassword` | carried from 39dd584: `StartDemoTests.cs:60` `Assert.Equal(HashedPassword, Assert.Single(added).PasswordHash)`; `:61` `_hasher.Received(1).Hash(DemoPassword)` | PASS |
| C3 | issues tokens and stores hashed refresh token as `LoginHandler` does | unit batch @ae56c3e, `Passed StartDemoTests.IssuesTokensLikeLogin` | carried from 39dd584: `StartDemoTests.cs:76` `_jwt.Received(1).GenerateAccessToken(user)`; `:81` `Assert.Equal(HashOf("refresh-token"), stored.TokenHash)`; `:82` `Assert.InRange(stored.ExpiresAt, before + RefreshTokenLifetime, after + RefreshTokenLifetime)`; `:83` `Assert.Equal(stored.ExpiresAt, tokens.RefreshTokenExpiresAt)` | PASS |
| C4 | two calls, two users, different ids and emails | unit batch @ae56c3e, `Passed StartDemoTests.EveryCallCreatesADistinctUser` | carried from 39dd584: `StartDemoTests.cs:95` `Assert.Equal(2, added.Count)`; `:96` `Assert.NotEqual(added[0].Id, added[1].Id)`; `:97` `Assert.NotEqual(added[0].Email, added[1].Email)` | PASS |
| C5 | `null`/`""`/whitespace password -> `NotFoundException`, no user, no refresh token | unit batch @ae56c3e, 3 cases passed: `(password: null)`, `(password: "")`, `(password: "   ")` | carried from 39dd584: `StartDemoTests.cs:101-103` three `InlineData`; `:106` `Assert.ThrowsAsync<NotFoundException>(...)`; `:108` `_users.DidNotReceive().AddAsync(...)`; `:109` `_refreshTokens.DidNotReceive().AddAsync(...)` | PASS |
| C6 | demo user's token carries `is_demo` = `"true"` | integration batch @ae56c3e, `Passed JwtServiceDemoClaimTests.DemoUserTokenCarriesIsDemoTrue` | carried from 39dd584: `tests/FormAI.IntegrationTests/JwtServiceDemoClaimTests.cs:24` `Assert.Equal("true", token.Claims.Single(c => c.Type == "is_demo").Value)` | PASS |
| C7 | `User.Create` user's token has no `is_demo` claim | integration batch @ae56c3e, `Passed JwtServiceDemoClaimTests.RegularUserTokenHasNoIsDemoClaim` | carried from 39dd584: `JwtServiceDemoClaimTests.cs:32` `Assert.DoesNotContain(token.Claims, c => c.Type == "is_demo")` | PASS |
| C8 | `POST /api/auth/demo` -> 200, `accessToken`, refresh cookie as login, `is_demo` "true", name `Demo user` | playwright @ae56c3e, `✓ [chromium] › e2e\demo.spec.ts:21:1 › starts, refreshes and rate limits a demo session` | carried from 39dd584: `frontend/e2e/demo.spec.ts:23` `expect(first.status()).toBe(200)`; `:28` `expect(claims.is_demo).toBe("true")`; `:29` `expect(claims.name).toBe("Demo user")`; `:37-40` `/httponly/i`, `/secure/i`, `/samesite=strict/i`, `/path=\/api\/auth/i` | PASS |
| C9 | `IsDemo` round-trips true/false against a migrated DB | integration batch @ae56c3e, `Passed UserRepositoryTests.PersistsTheDemoFlag` | carried from 39dd584: `tests/FormAI.IntegrationTests/UserRepositoryTests.cs:49` `MigrateAsync()`; `:59` `Assert.True((await freshRepo.GetByIdAsync(demo.Id))!.IsDemo)`; `:60` `Assert.False((await freshRepo.GetByIdAsync(regular.Id))!.IsDemo)` | PASS |
| C10 | migration adds `is_demo` non-null boolean default false | `grep -c "defaultValue: false" .../*AddUserIsDemo.cs` @ae56c3e printed `1`, exit 0 | carried from 39dd584: `src/FormAI.Infrastructure/Migrations/20260929130710_AddUserIsDemo.cs:16` `type: "boolean"`; `:17` `nullable: false`; `:18` `defaultValue: false` | PASS |
| C11 | `appsettings.json` Demo limits 10/15/3, no `Demo:Password` | integration batch @ae56c3e, `Passed DemoConfigurationTests.ShippedDefaults` | carried from 39dd584: `tests/FormAI.IntegrationTests/DemoConfigurationTests.cs:24` `Assert.Equal(10, demo.PermitLimit)`; `:25` `Assert.Equal(15, demo.WindowMinutes)`; `:26` `Assert.Equal(3, demo.SegmentsPerWindow)`; `:27` `Assert.Null(configuration["Demo:Password"])` | PASS |
| C12 | second call's token differs in `sub` and `email` | same playwright test @ae56c3e, passed | carried from 39dd584: `demo.spec.ts:44` `expect(second.status()).toBe(200)`; `:46` `expect(other.sub).not.toBe(claims.sub)`; `:47` `expect(other.email).not.toBe(claims.email)` | PASS |
| C13 | refresh keeps `is_demo` "true" | same playwright test @ae56c3e, passed | carried from 39dd584: `demo.spec.ts:53` `expect(refreshed.status()).toBe(200)`; `:56` `expect(refreshedClaims.is_demo).toBe("true")` | PASS |
| C14 | login with demo email + configured password -> 200 | same playwright test @ae56c3e, passed | carried from 39dd584: `demo.spec.ts:60` `password: "e2e-demo-password"` (= `frontend/playwright.config.ts:64`); `:62` `expect(login.status()).toBe(200)` | PASS |
| C15 | no Mailpit message to the demo email | same playwright test @ae56c3e, passed | carried from 39dd584: `demo.spec.ts:66` `query: \`to:${claims.email}\``; `:68` `expect((await mail.json()).messages ?? []).toHaveLength(0)` (precision note unchanged, see below) | PASS |
| C16 | sixth call with limit 5 -> 429, `errors`/`code` null, message names limit and window | same playwright test @ae56c3e, passed | carried from 39dd584: `demo.spec.ts:73` `expect(limited.status()).toBe(429)`; `:75` `expect(body.errors).toBeNull()`; `:76` `expect(body.code).toBeNull()`; `:77` `toContain("5 demo sessions")`; `:78` `toContain("15 minutes")` | PASS |
| C17 | landing: Try demo first in hero row, primary, **same size classes as Start for free (`px-8 py-3 text-base`)**; Start for free outline | vitest @ae56c3e, `✓ LandingPage demo > leads the hero with a primary Try demo` | verified at ae56c3e: `frontend/src/pages/LandingPage.test.tsx:26` `expect(buttons[0]).toHaveTextContent("Try demo")`; `:27` `expect(buttons[0]).toHaveClass("bg-brand-600")`; `:29` `expect(startForFree).toHaveClass("border-brand-600")`; `:30` `expect(startForFree).not.toHaveClass("bg-brand-600")`; `:31` `expect(buttons[0]).toHaveClass("px-8", "py-3", "text-base")`; `:32` `expect(startForFree).toHaveClass("px-8", "py-3", "text-base")`. Code: `frontend/src/pages/LandingPage.tsx:36` `className="px-8 py-3 text-base shadow-lg ring-4 ring-brand-200"`, `:41` and `:46` `className="px-8 py-3 text-base"` | PASS |
| C18 | login: full-width primary Try demo before Email; Log in outline | vitest @ae56c3e, `✓ LoginPage demo > leads with a primary Try demo and an outline Log in` | verified at ae56c3e (lines moved +20): `frontend/src/pages/LoginPage.test.tsx:141` `expect(tryDemo).toHaveClass("bg-brand-600", "w-full")`; `:143` `expect(tryDemo.compareDocumentPosition(email) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()`; `:145` `expect(logIn).toHaveClass("border-brand-600")`; `:146` `expect(logIn).not.toHaveClass("bg-brand-600")` | PASS |
| C19 | login Try demo: POST with no body, token stored, lands on `/dashboard` | vitest @ae56c3e, `✓ LoginPage demo > signs in through Try demo and opens the dashboard` | verified at ae56c3e: `LoginPage.test.tsx:164` `findByText("Dashboard page")`; `:165` `expect(calls).toBe(1)`; `:166` `expect(body).toBe("")`; `:167` `expect(tokenStore.get()).toBe(accessToken)` | PASS |
| C20 | landing Try demo lands on `/dashboard` | vitest @ae56c3e, `✓ LandingPage demo > signs in through Try demo and opens the dashboard` | verified at ae56c3e (lines moved +2): `LandingPage.test.tsx:47` `expect(await screen.findByText("Dashboard page")).toBeInTheDocument()`; `:48` `expect(tokenStore.get()).toBe(accessToken)` | PASS |
| C21 | pending: disabled, loading, second click sends nothing | vitest @ae56c3e, `✓ LoginPage demo > disables Try demo while the request is pending` | verified at ae56c3e: `LoginPage.test.tsx:187` `expect(button).toBeDisabled()`; `:191` `expect(calls).toBe(1)` (precision note unchanged) | PASS |
| C22 | login: 429 `message` shown, stays on `/login`; no message -> fallback | vitest @ae56c3e, `✓ LoginPage demo > shows why the demo could not start` | verified at ae56c3e: `LoginPage.test.tsx:207` `expect(await screen.findByText("You have reached the limit.")).toBeInTheDocument()`; `:208` `expect(screen.queryByText("Dashboard page")).not.toBeInTheDocument()`; `:209` `expect(screen.getByLabelText("Email")).toBeInTheDocument()`; `:213-215` `findByText("Could not start the demo. Please try again.")`. Code: the error box is `frontend/src/pages/LoginPage.tsx:95-99` | PASS |
| C23 | landing: fallback text shown, stays on `/` | vitest @ae56c3e, `✓ LandingPage demo > shows why the demo could not start` | verified at ae56c3e (lines moved +2): `LandingPage.test.tsx:57` `expect(await screen.findByText("Could not start the demo. Please try again.")).toBeVisible()`; `:58` `queryByText("Dashboard page")).not.toBeInTheDocument()`; `:59` `getByRole("main")` | PASS |
| C24 | `userFromAccessToken` isDemo true/false | vitest @ae56c3e, `✓ userFromAccessToken > reads the is_demo claim` | carried from 39dd584: `frontend/src/auth/tokenStore.test.ts:12` `.isDemo).toBe(true)`; `:13` `.isDemo).toBe(false)` | PASS |
| C25 | demo user sees exact banner, "Create an account" -> `/register` | vitest @ae56c3e, `✓ BasePage demo banner > warns a demo user and links to register` | carried from 39dd584: `frontend/src/components/BasePage.test.tsx:31` `expect(banner).toHaveTextContent(BANNER)`; `:32-35` `toHaveAttribute("href", "/register")` | PASS |
| C26 | regular user sees no banner | vitest @ae56c3e, `✓ BasePage demo banner > shows no banner to a regular user` | carried from 39dd584: `BasePage.test.tsx:42` `queryByRole("status")).not.toBeInTheDocument()`; `:43` `queryByText(/demo account/i)).not.toBeInTheDocument()` | PASS |
| C27 | banner has no control but the link | vitest @ae56c3e, `✓ BasePage demo banner > offers no way to dismiss the banner` | carried from 39dd584: `BasePage.test.tsx:50` `queryAllByRole("button")).toHaveLength(0)`; `:51` `getAllByRole("link")).toHaveLength(1)` | PASS |
| C28 | CONTEXT.md, CLAUDE.md, known-gaps.md document demo accounts | grep chain @ae56c3e exit 0 | carried from 39dd584: `CONTEXT.md:13` `**Demo account**:`; `CLAUDE.md:41` "creates a **demo account**"; `docs/known-gaps.md:20` `**Demo accounts are never cleaned up**` | PASS |
| C29 | after a failed password login, a failed demo request replaces the login error with the demo failure text | vitest @ae56c3e, `✓ LoginPage demo > replaces an earlier login error with the demo failure` | verified at ae56c3e: precondition `LoginPage.test.tsx:124` `expect(await screen.findByText("Invalid user or password.")).toBeInTheDocument()`; `:127-129` `expect(await screen.findByText("Could not start the demo. Please try again.")).toBeInTheDocument()`; `:130` `expect(screen.queryByText("Invalid user or password.")).not.toBeInTheDocument()`. Code: `frontend/src/pages/LoginPage.tsx:67-70` `onClick={() => { setServerError(null); startDemo(); }}` | PASS |

Every named test appears individually as passed in the runner output at `ae56c3e` (Gate). C17 and
C29 are the fix's new assertions; both were added in `39dd584..ae56c3e`. The other tests were
confirmed in round 1 as added in `4c52b5d..39dd584`.

## Round-1 gaps re-judged

| # | Round-1 gap | Status at ae56c3e | Basis |
| --- | --- | --- | --- |
| 1 | AC 9a size clause uncovered and contradicted | **closed** | `LandingPage.tsx:36` now `px-8 py-3 text-base`, identical size utilities to `:41` and `:46`. C17 widened to assert them (`LandingPage.test.tsx:31-32`). The extra `shadow-lg ring-4 ring-brand-200` is a box-shadow ring, so it adds emphasis without changing the button's box size. The plan's claim "at the size of the existing hero buttons" now matches the file |
| 2 | AC 12 stale login error masks the demo failure | **closed** | `LoginPage.tsx:67-70` clears `serverError` before `startDemo()`. `useStartDemo` clears its own error at start (`frontend/src/hooks/useStartDemo.ts` `setError(null)`). New check C29 proves it (`LoginPage.test.tsx:124,127-130`) |
| 3 | Surface `404` has no HTTP-level proof | **note (accepted limitation)** | checks.md documents it explicitly (Coverage bullets): proving it over HTTP would need a second API instance with no password configured. The handler throws `NotFoundException` (C5, `StartDemoTests.cs:106`). The controller does not catch it (`src/FormAI.API/Controllers/AuthController.cs:51-58`). The existing middleware maps it (`src/FormAI.API/Middleware/ExceptionHandlingMiddleware.cs:37`). This is a level gap with located evidence at each hop, not an unproven check |
| 4a | AC 7 "SHALL NOT create a user" at 429 has no check | **note** | Met by construction: `[EnableRateLimiting(RateLimitPolicies.Demo)]` at `AuthController.cs:52`, and `UseRateLimiter()` at `src/FormAI.API/Program.cs:81` runs in the middleware pipeline, so a rejected request never reaches the action at `:53-58` or `StartDemoHandler`. To close it: after the sixth call (429) in `e2e/demo.spec.ts`, count `users` rows with `email like 'demo-%@demo.invalid'` in the e2e DB and assert the count is unchanged. Alternatively, write a `WebApplicationFactory` test with `RateLimiting:Demo:PermitLimit=1` and a substituted `IUserRepository`, asserting that `AddAsync` is received once across two calls |
| 4b | AC 2 "SHALL NOT create a `UserConfirmationToken`" has no check | **note** | Met by construction: the constructor of `src/FormAI.Application/Users/Auth/StartDemoHandler.cs:15-16` takes only `IUserRepository`, `IRefreshTokenRepository`, `IPasswordHasher`, `IJwtService` and `DemoAccountSettings`, with no confirmation-token repository. `User.CreateDemo` (`src/FormAI.Domain/Entities/User.cs:37-43`) never touches `ConfirmationTokens` (`:20`). To close it: in the e2e test, after the first demo call, assert `select count(*) from user_confirmation_tokens where user_id = <sub>` is 0. Alternatively, in `StartDemoTests.CreatesAVerifiedDemoUser`, assert `Assert.Empty(user.ConfirmationTokens)` |
| 5 | AC 11 loading state on `/` unproven (C21 is `/login` only) | **note** | The same hook and the same `Button` are used: `LandingPage.tsx:34` `isLoading={isStarting}`, and `Button` sets `disabled={disabled \|\| isLoading}`. To close it, run a copy of C21 against `LandingPage` |

**Why 4a and 4b are notes rather than FAILs.** Under verify.md, a FAIL comes from "any check
without a located `file:line`", a failing check, an unproven coverage member, or an element that a
*binding* source decides with no covering check. Clauses 4a and 4b are neither checks nor members
of a binding source. The plan marks no source as binding, and profile `light` does not run the
Coverage recompute, which is where an uncovered member named in prose would turn into a FAIL.
Each clause is a negative obligation that the code meets, and the evidence for it is located at
file:line above. Round 1 set the line consistently: an uncovered clause that the code
*contradicts* (AC 9a, AC 12) fails, and one it *satisfies* by construction is a note. Both remain
real residual risk: a future edit that inserts the handler before the limiter, or that injects a
confirmation-token repository, would pass every check. That is why the closing check is named for
each.

## Level and precision findings

- **Precision (carried from 39dd584, still open):** C15 does not assert that the Mailpit call
  succeeded (`?? []`). C21 asserts disabled, not the spinner. C22 does not assert the error box
  container. C23 does not assert placement next to the button. The code meets each of these as read.
- **Precision (new, C17):** the assertion is on class presence. jsdom has no layout, so the
  computed size is not measured. `Button`'s base (`frontend/src/components/Button.tsx`) also
  carries `px-3 py-1 text-sm`, and the override relies on Tailwind's utility ordering. All three
  hero buttons share the same base and override, so they render at the same size in any case.
- **Test finding (carried):** C3's expected hash and lifetime come from `AuthTestData.cs` helpers,
  not from the assertion site.

## Swept existing

Carried from 39dd584. The row is `authorization: existing`, "anonymous by design like `Register`
and `Login`". It still holds: `AuthController.cs` was not touched by the fix, `StartDemo`
(`:51-53`) has no `[Authorize]`, and the e2e anonymous call gets 200 (`demo.spec.ts:23`).

## Gate

All run at `ae56c3e` from the worktree root.

- `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~StartDemoTests" --logger "console;verbosity=normal"`: 7 passed, 0 failed. Rows: CreatesAVerifiedDemoUser, HashesTheConfiguredPassword, IssuesTokensLikeLogin, EveryCallCreatesADistinctUser, and BlankPasswordIsNotFoundAndCreatesNothing for `null`, `""` and `"   "` (C1-C5).
- `dotnet test tests/FormAI.IntegrationTests/FormAI.IntegrationTests.csproj --filter "FullyQualifiedName~JwtServiceDemoClaimTests|FullyQualifiedName~UserRepositoryTests.PersistsTheDemoFlag|FullyQualifiedName~DemoConfigurationTests.ShippedDefaults" --logger "console;verbosity=normal"`: 4 passed, 0 failed (C6, C7, C9, C11).
- `cd frontend && npx vitest run src/pages/LandingPage.test.tsx src/pages/LoginPage.test.tsx src/auth/tokenStore.test.ts src/components/BasePage.test.tsx --reporter=verbose`: 16 passed, 0 failed across 4 files. 12 are the named demo tests (C17-C27, C29) and 4 are pre-existing LoginPage tests.
- `cd frontend && npx playwright test e2e/demo.spec.ts --reporter=list`: 1 passed, 0 failed (`demo.spec.ts:21:1`, which carries C8 and C12-C16).
- `grep -c "defaultValue: false" src/FormAI.Infrastructure/Migrations/*AddUserIsDemo.cs`: printed `1`, exit 0 (C10).
- `grep -q "Demo account" CONTEXT.md && grep -qi "demo account" CLAUDE.md && grep -qi "demo account" docs/known-gaps.md`: exit 0 (C28).

Faults: no faults injected - profile light.

## Ranked gaps

None fail the feature. Remaining notes, most significant first:

1. AC 7 "no user created on 429" has no check. It holds by construction (`AuthController.cs:52`, `Program.cs:81`). Close it with a user-count assertion after the 429 in `e2e/demo.spec.ts`.
2. AC 2 "no `UserConfirmationToken`" has no check. It holds by construction (`StartDemoHandler.cs:15-16`, `User.cs:37-43`). Close it with `Assert.Empty(user.ConfirmationTokens)` or a DB count in e2e.
3. Surface `404` is proven at the handler only. This is an accepted limitation in checks.md (C5, `StartDemoTests.cs:106`, `ExceptionHandlingMiddleware.cs:37`).
4. The AC 11 loading state on `/` is unproven. It uses the same hook and `Button` as `/login` (`LandingPage.tsx:34`).
5. Precision notes on C15, C17, C21, C22 and C23 (above).
