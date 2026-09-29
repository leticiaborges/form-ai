# Demo login verification

**Verdict**: PASS
**Profile**: light
**Diff range**: 4c52b5d..bf31c48 (since round 2: ae56c3e..bf31c48)
**Round**: 3 - scoped
**Verifier**: independent sub-agent (author != verifier)

All 29 checks are proven at HEAD `bf31c48` with located evidence. The two changes since round 2
match the rewritten plan. On `/login`, "Try demo" is now a text-link button in the header block and
the Log in submit is primary again (AC 9, C18). On `/`, the hero row reads Try demo, Log in, Get
started (AC 9a, C17). One new note appeared: on `/login` the demo control lost its spinner. It is
only dimmed and disabled while the request is pending, and C21 cannot see the difference (ranked
gap 1). It is recorded as a note, not a FAIL, because AC 11 does not define "loading state" and the
control does show a distinct pending state.

No faults injected - profile light.

## Scope of this round

- Diff `ae56c3e..bf31c48`: `frontend/src/pages/LoginPage.tsx` (+15/-16), `LoginPage.test.tsx`
  (C18 test renamed and re-asserted), `LandingPage.tsx` (hero Log in / Get started swapped,
  "Start for free" removed), `LandingPage.test.tsx` (C17 now names "Get started"), and the spec files
  (`plan.md` AC 9/9a, `checks.md` C17/C18). `git diff --stat 39dd584..HEAD -- src tests frontend/e2e
  frontend/src/components frontend/src/auth frontend/src/hooks frontend/playwright.config.ts` is
  empty, so the backend, e2e, `BasePage`, `tokenStore`, `useStartDemo` and `Button` are byte-identical
  to round 1/2.
- **Proofs:** re-run in full at `bf31c48` for C1-C29 (see Gate).
- **Citations:** refreshed at `bf31c48` for C17, C18, C19, C21, C22 and C29 (touched, or their lines
  moved), and re-checked for C20 and C23 (unchanged lines). All backend, e2e, `BasePage` and
  `tokenStore` evidence is `carried from 39dd584`, because those files did not change.
- **Plan wording:** AC 9 and AC 9a are compared against the code below, under "AC 9 / AC 9a against
  the code".

## Binding sources

Carried from 39dd584. The plan marks no source as binding, and profile `light` does not run step 1. n/a.

## Checks

| Check | Claim | Proof run | Evidence | Result |
| --- | --- | --- | --- | --- |
| C1 | handler adds one verified demo user, name `Demo user`, email `^demo-[0-9a-f-]{36}@demo\.invalid$` | unit batch @bf31c48, `Passed FormAI.UnitTests.Users.StartDemoTests.CreatesAVerifiedDemoUser` | carried from 39dd584: `tests/FormAI.UnitTests/Users/StartDemoTests.cs:45` `Assert.Single(added)`; `:46` `Assert.True(user.IsDemo)`; `:47` `Assert.True(user.IsEmailVerified)`; `:48` `Assert.NotNull(user.VerifiedAt)`; `:49` `Assert.Equal("Demo user", user.Name)`; `:50` `Assert.Matches(new Regex(@"^demo-[0-9a-f-]{36}@demo\.invalid$"), user.Email)` | PASS |
| C2 | stored hash is `IPasswordHasher.Hash(configured password)` | unit batch @bf31c48, `Passed StartDemoTests.HashesTheConfiguredPassword` | carried from 39dd584: `StartDemoTests.cs:60` `Assert.Equal(HashedPassword, Assert.Single(added).PasswordHash)`; `:61` `_hasher.Received(1).Hash(DemoPassword)` | PASS |
| C3 | issues tokens and stores hashed refresh token as `LoginHandler` does | unit batch @bf31c48, `Passed StartDemoTests.IssuesTokensLikeLogin` | carried from 39dd584: `StartDemoTests.cs:76` `_jwt.Received(1).GenerateAccessToken(user)`; `:81` `Assert.Equal(HashOf("refresh-token"), stored.TokenHash)`; `:82` `Assert.InRange(stored.ExpiresAt, before + RefreshTokenLifetime, after + RefreshTokenLifetime)`; `:83` `Assert.Equal(stored.ExpiresAt, tokens.RefreshTokenExpiresAt)` | PASS |
| C4 | two calls, two users, different ids and emails | unit batch @bf31c48, `Passed StartDemoTests.EveryCallCreatesADistinctUser` | carried from 39dd584: `StartDemoTests.cs:95` `Assert.Equal(2, added.Count)`; `:96` `Assert.NotEqual(added[0].Id, added[1].Id)`; `:97` `Assert.NotEqual(added[0].Email, added[1].Email)` | PASS |
| C5 | `null`/`""`/whitespace password -> `NotFoundException`, no user, no refresh token | unit batch @bf31c48, 3 cases passed: `(password: null)`, `(password: "")`, `(password: "   ")` | carried from 39dd584: `StartDemoTests.cs:101-103` three `InlineData`; `:106` `Assert.ThrowsAsync<NotFoundException>(...)`; `:108` `_users.DidNotReceive().AddAsync(...)`; `:109` `_refreshTokens.DidNotReceive().AddAsync(...)` | PASS |
| C6 | demo user's token carries `is_demo` = `"true"` | integration batch @bf31c48, `Passed JwtServiceDemoClaimTests.DemoUserTokenCarriesIsDemoTrue` | carried from 39dd584: `tests/FormAI.IntegrationTests/JwtServiceDemoClaimTests.cs:24` `Assert.Equal("true", token.Claims.Single(c => c.Type == "is_demo").Value)` | PASS |
| C7 | `User.Create` user's token has no `is_demo` claim | integration batch @bf31c48, `Passed JwtServiceDemoClaimTests.RegularUserTokenHasNoIsDemoClaim` | carried from 39dd584: `JwtServiceDemoClaimTests.cs:32` `Assert.DoesNotContain(token.Claims, c => c.Type == "is_demo")` | PASS |
| C8 | `POST /api/auth/demo` -> 200, `accessToken`, refresh cookie as login, `is_demo` "true", name `Demo user` | playwright @bf31c48, `✓ 1 [chromium] › e2e\demo.spec.ts:21:1 › starts, refreshes and rate limits a demo session` | carried from 39dd584: `frontend/e2e/demo.spec.ts:23` `expect(first.status()).toBe(200)`; `:28` `expect(claims.is_demo).toBe("true")`; `:29` `expect(claims.name).toBe("Demo user")`; `:37-40` `/httponly/i`, `/secure/i`, `/samesite=strict/i`, `/path=\/api\/auth/i` | PASS |
| C9 | `IsDemo` round-trips true/false against a migrated DB | integration batch @bf31c48, `Passed UserRepositoryTests.PersistsTheDemoFlag` | carried from 39dd584: `tests/FormAI.IntegrationTests/UserRepositoryTests.cs:49` `MigrateAsync()`; `:59` `Assert.True((await freshRepo.GetByIdAsync(demo.Id))!.IsDemo)`; `:60` `Assert.False((await freshRepo.GetByIdAsync(regular.Id))!.IsDemo)` | PASS |
| C10 | migration adds `is_demo` non-null boolean default false | `grep -c "defaultValue: false" .../*AddUserIsDemo.cs` @bf31c48 printed `1`, exit 0 | re-read @bf31c48: `src/FormAI.Infrastructure/Migrations/20260929130710_AddUserIsDemo.cs:16` `type: "boolean"`; `:17` `nullable: false`; `:18` `defaultValue: false` | PASS |
| C11 | `appsettings.json` Demo limits 10/15/3, no `Demo:Password` | integration batch @bf31c48, `Passed DemoConfigurationTests.ShippedDefaults` | carried from 39dd584: `tests/FormAI.IntegrationTests/DemoConfigurationTests.cs:24` `Assert.Equal(10, demo.PermitLimit)`; `:25` `Assert.Equal(15, demo.WindowMinutes)`; `:26` `Assert.Equal(3, demo.SegmentsPerWindow)`; `:27` `Assert.Null(configuration["Demo:Password"])` | PASS |
| C12 | second call's token differs in `sub` and `email` | same playwright test @bf31c48, passed | carried from 39dd584: `demo.spec.ts:44` `expect(second.status()).toBe(200)`; `:46` `expect(other.sub).not.toBe(claims.sub)`; `:47` `expect(other.email).not.toBe(claims.email)` | PASS |
| C13 | refresh keeps `is_demo` "true" | same playwright test @bf31c48, passed | carried from 39dd584: `demo.spec.ts:53` `expect(refreshed.status()).toBe(200)`; `:56` `expect(refreshedClaims.is_demo).toBe("true")` | PASS |
| C14 | login with demo email + configured password -> 200 | same playwright test @bf31c48, passed | carried from 39dd584: `demo.spec.ts:60` `password: "e2e-demo-password"` (= `frontend/playwright.config.ts:64`); `:62` `expect(login.status()).toBe(200)` | PASS |
| C15 | no Mailpit message to the demo email | same playwright test @bf31c48, passed | carried from 39dd584: `demo.spec.ts:66` `query: \`to:${claims.email}\``; `:68` `expect((await mail.json()).messages ?? []).toHaveLength(0)` (precision note unchanged) | PASS |
| C16 | sixth call with limit 5 -> 429, `errors`/`code` null, message names limit and window | same playwright test @bf31c48, passed | carried from 39dd584: `demo.spec.ts:73` `expect(limited.status()).toBe(429)`; `:75` `expect(body.errors).toBeNull()`; `:76` `expect(body.code).toBeNull()`; `:77` `toContain("5 demo sessions")`; `:78` `toContain("15 minutes")` | PASS |
| C17 | landing: Try demo first in hero row, primary, same size classes as Get started (`px-8 py-3 text-base`); Get started outline | vitest @bf31c48, `✓ LandingPage demo > leads the hero with a primary Try demo` | verified at bf31c48: `frontend/src/pages/LandingPage.test.tsx:26` `expect(buttons[0]).toHaveTextContent("Try demo")`; `:27` `expect(buttons[0]).toHaveClass("bg-brand-600")`; `:28` `getByRole("button", { name: "Get started" })`; `:29` `expect(startForFree).toHaveClass("border-brand-600")`; `:30` `.not.toHaveClass("bg-brand-600")`; `:31` `expect(buttons[0]).toHaveClass("px-8", "py-3", "text-base")`; `:32` `expect(startForFree).toHaveClass("px-8", "py-3", "text-base")`. Code: `frontend/src/pages/LandingPage.tsx:33-39` Try demo (default primary) `className="px-8 py-3 text-base shadow-lg ring-4 ring-brand-200"`; `:41` Log in outline; `:46-47` Get started `variant="outline" className="px-8 py-3 text-base"` | PASS |
| C18 | login: link-style Try demo (`text-brand-600`, no `bg-brand-600`, no `w-full`) in the header block, before Email; Log in primary | vitest @bf31c48, `✓ LoginPage demo > offers Try demo as a link above a primary Log in` | verified at bf31c48: `frontend/src/pages/LoginPage.test.tsx:141` `expect(tryDemo).toHaveClass("text-brand-600")`; `:142` `.not.toHaveClass("bg-brand-600")`; `:143` `.not.toHaveClass("w-full")`; `:145` `expect(tryDemo.compareDocumentPosition(email) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()`; `:147` `expect(logIn).toHaveClass("bg-brand-600")`; `:148` `.not.toHaveClass("border-brand-600")`. Code: `frontend/src/pages/LoginPage.tsx:62-75` inside header `:51-76`; `:100` submit has no `variant` (defaults to primary). Precision gap on "in the header block" and "styled like Sign up", see below | PASS |
| C19 | login Try demo: POST with no body, token stored, lands on `/dashboard` | vitest @bf31c48, `✓ LoginPage demo > signs in through Try demo and opens the dashboard` | verified at bf31c48 (lines moved +2): `LoginPage.test.tsx:166` `expect(await screen.findByText("Dashboard page")).toBeInTheDocument()`; `:167` `expect(calls).toBe(1)`; `:168` `expect(body).toBe("")`; `:169` `expect(tokenStore.get()).toBe(accessToken)` | PASS |
| C20 | landing Try demo lands on `/dashboard` | vitest @bf31c48, `✓ LandingPage demo > signs in through Try demo and opens the dashboard` | verified at bf31c48 (unchanged lines): `LandingPage.test.tsx:47` `expect(await screen.findByText("Dashboard page")).toBeInTheDocument()`; `:48` `expect(tokenStore.get()).toBe(accessToken)` | PASS |
| C21 | pending: disabled, loading, second click sends nothing | vitest @bf31c48, `✓ LoginPage demo > disables Try demo while the request is pending` | verified at bf31c48 (lines moved +2): `LoginPage.test.tsx:189` `expect(button).toBeDisabled()`; `:193` `expect(calls).toBe(1)`. Code: `LoginPage.tsx:66` `disabled={isStarting}`, `:71` `disabled:opacity-50 disabled:cursor-not-allowed`. The spinner is gone on `/login` (ranked gap 1) | PASS |
| C22 | login: 429 `message` shown, stays on `/login`; no message -> fallback | vitest @bf31c48, `✓ LoginPage demo > shows why the demo could not start` | verified at bf31c48 (lines moved +2): `LoginPage.test.tsx:209` `expect(await screen.findByText("You have reached the limit.")).toBeInTheDocument()`; `:210` `expect(screen.queryByText("Dashboard page")).not.toBeInTheDocument()`; `:211` `expect(screen.getByLabelText("Email")).toBeInTheDocument()`; `:215-217` `findByText("Could not start the demo. Please try again.")`. Code: error box `LoginPage.tsx:94-98` `{(serverError ?? demoError) && ...}`, still inside the `<form>` at `:78-103` | PASS |
| C23 | landing: fallback text shown, stays on `/` | vitest @bf31c48, `✓ LandingPage demo > shows why the demo could not start` | verified at bf31c48 (unchanged lines): `LandingPage.test.tsx:57` `expect(await screen.findByText("Could not start the demo. Please try again.")).toBeVisible()`; `:58` `queryByText("Dashboard page")).not.toBeInTheDocument()`; `:59` `getByRole("main")`. Code: `LandingPage.tsx:51-55` `role="alert"` directly under the hero row | PASS |
| C24 | `userFromAccessToken` isDemo true/false | vitest @bf31c48, `✓ userFromAccessToken > reads the is_demo claim` | carried from 39dd584: `frontend/src/auth/tokenStore.test.ts:12` `.isDemo).toBe(true)`; `:13` `.isDemo).toBe(false)` | PASS |
| C25 | demo user sees exact banner, "Create an account" -> `/register` | vitest @bf31c48, `✓ BasePage demo banner > warns a demo user and links to register` | carried from 39dd584: `frontend/src/components/BasePage.test.tsx:31` `expect(banner).toHaveTextContent(BANNER)`; `:32-35` `toHaveAttribute("href", "/register")` | PASS |
| C26 | regular user sees no banner | vitest @bf31c48, `✓ BasePage demo banner > shows no banner to a regular user` | carried from 39dd584: `BasePage.test.tsx:42` `queryByRole("status")).not.toBeInTheDocument()`; `:43` `queryByText(/demo account/i)).not.toBeInTheDocument()` | PASS |
| C27 | banner has no control but the link | vitest @bf31c48, `✓ BasePage demo banner > offers no way to dismiss the banner` | carried from 39dd584: `BasePage.test.tsx:50` `queryAllByRole("button")).toHaveLength(0)`; `:51` `getAllByRole("link")).toHaveLength(1)` | PASS |
| C28 | CONTEXT.md, CLAUDE.md, known-gaps.md document demo accounts | grep chain @bf31c48 exit 0 | re-read @bf31c48: `CONTEXT.md:13` `**Demo account**:`; `CLAUDE.md:41` "creates a **demo account**"; `docs/known-gaps.md:20` `**Demo accounts are never cleaned up**` | PASS |
| C29 | after a failed password login, a failed demo request replaces the login error with the demo failure text | vitest @bf31c48, `✓ LoginPage demo > replaces an earlier login error with the demo failure` | verified at bf31c48 (unchanged test lines): precondition `LoginPage.test.tsx:124` `expect(await screen.findByText("Invalid user or password.")).toBeInTheDocument()`; `:127-129` `expect(await screen.findByText("Could not start the demo. Please try again.")).toBeInTheDocument()`; `:130` `expect(screen.queryByText("Invalid user or password.")).not.toBeInTheDocument()`. Code (moved): `LoginPage.tsx:67-70` `onClick={() => { setServerError(null); startDemo(); }}` on the new link button | PASS |

Every named test appears individually as passed in the runner output at `bf31c48` (Gate). C18's
test was renamed and re-asserted in `ae56c3e..bf31c48`, and its new name ran. C17's test changed
its `name:` lookup in the same range. All other tests were confirmed in earlier rounds as added in
`4c52b5d..ae56c3e`.

## AC 9 / AC 9a against the code

| Plan claim | Code at bf31c48 | Match |
| --- | --- | --- |
| AC 9: "Try demo" is a text link | `LoginPage.tsx:64-74`: a `<button type="button">` with no `Button` component, no background, border or padding classes. Tailwind v4 preflight (`frontend/src/index.css:1` `@import "tailwindcss"`) resets the native button chrome | yes. Semantically it is a button, which is correct for an action |
| AC 9: styled like "Sign up" | Sign up `:58` `className="text-brand-600 hover:underline"`; Try demo `:71` `className="text-brand-600 hover:underline disabled:opacity-50 disabled:cursor-not-allowed"`. Both sit in a `<p className="text-sm text-gray-500 mt-1">` (`:56`, `:62`) | yes. Identical classes plus the `disabled:` states |
| AC 9: in the same header block, above the form | header `div.text-center.mb-6` `:51-76`; Try demo's `<p>` `:62-75` is its last child; `<form>` starts at `:78` | yes |
| C18: before Email in document order | Email `Input` `:79-85`, after `:76` | yes |
| AC 9: submit in the `primary` variant | `:100` `<Button type="submit" isLoading={isSubmitting} className="mt-2 w-full">`, no `variant`, so the default is `primary` (`frontend/src/components/Button.tsx` `variant = "primary"`) | yes |
| AC 9a: Try demo first in the hero row | hero row `LandingPage.tsx:32-50`; `:33-39` is its first child | yes |
| AC 9a: Try demo `primary` | `:33` no `variant` (default `primary`) | yes |
| AC 9a: at the size of the existing hero buttons (`px-8 py-3 text-base`) | `:36`, `:41`, `:46` all carry `px-8 py-3 text-base`; `:36` adds `shadow-lg ring-4 ring-brand-200` (box-shadow, no box-size change) | yes |
| AC 9a: "Get started" `outline` | `:46` `variant="outline"` | yes. The header nav "Get started" (`:18`) is primary, but it sits outside `<main>` and AC 9a names the hero row |
| "Start for free" removed (owner edit) | `rg "Start for free" frontend/src` finds nothing; the hero row is Try demo, Log in, Get started | consistent with the plan; AC 9a no longer names it |

**Does C18 prove "styled like Sign up"?** Only partly. It proves one shared class
(`text-brand-600`) and the absence of two button classes (`bg-brand-600`, `w-full`). It does not
compare Try demo's classes to Sign up's, so a regression that dropped `hover:underline`, or added
padding or a border, would still pass. It also does not prove "in the same header block". The
document-order assertion (`:145`) would still pass if the control moved between the header and the
form, or above the logo. Both are precision gaps in the check. The code meets both claims as read
(table above).

## Earlier gaps re-judged

| # | Earlier gap | Status at bf31c48 | Basis |
| --- | --- | --- | --- |
| 1 | AC 9a size clause (round 1) | **closed, still holds** | `LandingPage.tsx:36`, `:41` and `:46` share `px-8 py-3 text-base`; C17 `:31-32` |
| 2 | AC 12 stale login error (round 1) | **closed, still holds** | the clear-then-start `onClick` moved with the control to `LoginPage.tsx:67-70`; C29 passes |
| 3 | Surface `404` has no HTTP-level proof | **note, unchanged** | no backend change. Handler throws `NotFoundException` (C5, `StartDemoTests.cs:106`), the controller does not catch it (`src/FormAI.API/Controllers/AuthController.cs:51-58`), and the middleware maps it (`src/FormAI.API/Middleware/ExceptionHandlingMiddleware.cs:37`). This is an accepted limitation in checks.md |
| 4a | AC 7 "SHALL NOT create a user" at 429 has no check | **note, unchanged** | no backend change: `[EnableRateLimiting(RateLimitPolicies.Demo)]` at `AuthController.cs:52`, `UseRateLimiter()` at `src/FormAI.API/Program.cs:81`. To close it, count `demo-%@demo.invalid` users around the 429 in `e2e/demo.spec.ts` |
| 4b | AC 2 "SHALL NOT create a `UserConfirmationToken`" has no check | **note, unchanged** | `StartDemoHandler.cs:15-16` takes no confirmation-token repository, and `User.CreateDemo` (`User.cs:37-43`) never touches `ConfirmationTokens`. To close it, add `Assert.Empty(user.ConfirmationTokens)` in `StartDemoTests.CreatesAVerifiedDemoUser` |
| 5 | AC 11 loading state on `/` unproven | **note, unchanged** | `LandingPage.tsx:34` `isLoading={isStarting}` on `Button` (spinner plus `Loading…`, `disabled={disabled \|\| isLoading}`); no test |
| 6 | **new:** AC 11 loading state on `/login` weakened by the link change | **note (new)** | before `bf31c48`, `/login` used `<Button isLoading={isStarting}>`, which swaps the label for a spinner and `Loading…`. Now `LoginPage.tsx:64-74` renders `disabled={isStarting}` only. The label stays "Try demo", and the only visible change is `disabled:opacity-50 disabled:cursor-not-allowed`. AC 11 says "show 'Try demo' in its loading state" without defining the state, so a dimmed, non-clickable control is arguably that state for a text link, and "a second click SHALL NOT send a second request" still holds (C21 `:193`). C21 asserted only `toBeDisabled()` (`:189`), so the loss of the visible progress indicator went unnoticed: the round-2 precision note "C21 asserts disabled, not the spinner" is exactly what let it through. The owner should confirm that a dimmed link is acceptable feedback. If not, add a spinner or "Starting…" text and assert it in C21 |
| 7 | AC 12 failure message still visible on `/login` after the layout change | **checked, no gap** | the error box is `LoginPage.tsx:94-98`, inside the form, rendered on `serverError ?? demoError`. It is unaffected by moving the control, and C22 (`:209`, `:215-217`) and C29 (`:127-130`) find the text. It now sits below the password field rather than beside the control. The plan's parenthetical "(in the login form's error box on `/login`)" names that placement explicitly |

**Why 4a, 4b and 6 are notes, not FAILs.** This follows the line set in rounds 1 and 2. A clause
the code *contradicts* fails; a clause it *satisfies* as read is a note, with its evidence located.
Item 6 is the closest call. The code shows a distinct pending state and blocks the second request,
and the criterion names no indicator. A stricter reading ("loading state" = the `Button` spinner
used elsewhere in the app) would turn it into a contradiction of AC 11 on `/login`. That judgment
belongs to the owner, which is why it is ranked first.

## Level and precision findings

- **Precision (new, C18):** "styled like Sign up" is proven by one shared class, not by comparing
  the two controls, and "in the header block" is proven only by order relative to Email (see above).
- **Precision (carried, now material, C21):** the check asserts disabled, not the loading indicator.
  The indicator was removed on `/login` in `bf31c48` and the test stayed green (gap 6).
- **Precision (carried from 39dd584):** C15 does not assert that the Mailpit call succeeded
  (`?? []`). C22 does not assert the error-box container. C23 does not assert placement next to the
  button. C17 asserts class presence, not computed size (jsdom has no layout).
- **Test finding (carried):** C3's expected hash and lifetime come from `AuthTestData.cs` helpers,
  not from the assertion site.

## Swept existing

Carried from 39dd584. The row is `authorization: existing`, "anonymous by design like `Register`
and `Login`". It still holds: `AuthController.cs` is unchanged since round 1, `StartDemo`
(`:51-53`) has no `[Authorize]`, and the e2e anonymous call gets 200 (`demo.spec.ts:23`), which
passed at `bf31c48`.

## Gate

All run at `bf31c48` from the worktree root. Environment note: an unrelated dev API process
(`FormAI.API.exe`, PID 8200, listening on :5155, started before this round) held locks on
`src/FormAI.API/bin/Debug`. The Verifier did not stop it. The integration batch therefore ran with
`--no-build` against the in-tree binaries, which were built at 10:20:10, after the last backend
commit `653aff7` (10:09:52), with no uncommitted backend changes, so they correspond to `HEAD`.
Playwright's own `dotnet run` build was redirected with `BaseOutputPath=<scratchpad>/apibin/`. One
earlier integration attempt with a redirected `BaseOutputPath` failed `ShippedDefaults` with a
`NullReferenceException` in `ApiSettingsPath()` (`DemoConfigurationTests.cs:14`). The cause was the
test walking up from its output directory to find `src/FormAI.API/appsettings.json`, which does not
exist above the scratchpad. That was an artifact of the redirect, not a code fault, and the
`--no-build` run below passed it.

- `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~StartDemoTests" --logger "console;verbosity=normal"`: 7 passed, 0 failed. Rows: CreatesAVerifiedDemoUser, HashesTheConfiguredPassword, IssuesTokensLikeLogin, EveryCallCreatesADistinctUser, and BlankPasswordIsNotFoundAndCreatesNothing for `null`, `""` and `"   "` (C1-C5).
- `dotnet test tests/FormAI.IntegrationTests/FormAI.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~JwtServiceDemoClaimTests|FullyQualifiedName~UserRepositoryTests.PersistsTheDemoFlag|FullyQualifiedName~DemoConfigurationTests.ShippedDefaults" --logger "console;verbosity=normal"`: 4 passed, 0 failed (C6, C7, C9, C11).
- `cd frontend && npx vitest run src/pages/LandingPage.test.tsx src/pages/LoginPage.test.tsx src/auth/tokenStore.test.ts src/components/BasePage.test.tsx --reporter=verbose`: 16 passed, 0 failed across 4 files. 12 are the named demo tests (C17-C27, C29) and 4 are pre-existing LoginPage tests.
- `cd frontend && npx playwright test e2e/demo.spec.ts --reporter=list` (with `BaseOutputPath` redirected): 1 passed, 0 failed (`demo.spec.ts:21:1`, which carries C8 and C12-C16).
- `grep -c "defaultValue: false" src/FormAI.Infrastructure/Migrations/*AddUserIsDemo.cs`: printed `1`, exit 0 (C10).
- `grep -q "Demo account" CONTEXT.md && grep -qi "demo account" CLAUDE.md && grep -qi "demo account" docs/known-gaps.md`: exit 0 (C28).

Faults: no faults injected - profile light.

## Ranked gaps

None fail the feature. Remaining notes, most significant first:

1. **New:** on `/login` the demo control has no loading indicator. It only dims and disables (`LoginPage.tsx:66`, `:71`), where it used to show a spinner and `Loading…`. C21 (`LoginPage.test.tsx:189`) cannot tell the difference. The owner should confirm; otherwise add an indicator and assert it.
2. AC 7 "no user created on 429" has no check. It holds by construction (`AuthController.cs:52`, `Program.cs:81`).
3. AC 2 "no `UserConfirmationToken`" has no check. It holds by construction (`StartDemoHandler.cs:15-16`, `User.cs:37-43`).
4. Surface `404` is proven at the handler only. This is an accepted limitation in checks.md (C5, `ExceptionHandlingMiddleware.cs:37`).
5. The AC 11 loading state on `/` is unproven (`LandingPage.tsx:34`).
6. Precision notes: C18 (Sign up likeness and header containment), C15, C17, C22, C23.
