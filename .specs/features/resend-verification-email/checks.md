# Resend verification email checks

Profile: light
Plan: `.specs/features/resend-verification-email/plan.md`

27 checks in 3 slices plus a cross-cutting group · 1 one-way door · 0 open, of which 0 block

Proof commands run from the repo root. `dotnet test` targets the unit project alone (no Docker);
`vitest` and `playwright` run from `frontend/`. The e2e proofs need the repo's local stack up
(Postgres, Redis, Mailpit) and `.env`, exactly as `npm run test:e2e` already does.

The e2e API runs with `RateLimiting__ResendVerification__PermitLimit=5` (set in
`frontend/playwright.config.ts`), so one test can walk every branch of the endpoint (five permits)
and still reach the 429 on the sixth call. The shipped default of 3 is proven by C20. The rate
limiter is in-memory and partitioned by remote IP for this anonymous endpoint, so everything that
calls the endpoint over HTTP lives in **one** Playwright test (`fullyParallel` would otherwise
split a file's tests across workers sharing the same partition).

## Checks

### S1 - The success page offers a resend · 5 files · 25 KB · ~7k

**C1** - After a successful registration the app lands on `/register/success` showing the "Resend verification email" control, i.e. the registered email travelled as router state (AC 1)
Proof: `cd frontend && npx vitest run src/pages/RegisterPage.test.tsx -t "carries the registered email to the success page"`

**C2** - `RegisterSuccessPage` rendered with `state.email` shows a "Resend verification email" button and copy telling the user to click it if nothing arrives in a few minutes (AC 2)
Proof: `cd frontend && npx vitest run src/pages/RegisterSuccessPage.test.tsx -t "offers a resend when the email is known"`

**C3** - `RegisterSuccessPage` rendered with no router state shows no "Resend verification email" control and still shows "Check your inbox" and "Go to login" (AC 3)
Proof: `cd frontend && npx vitest run src/pages/RegisterSuccessPage.test.tsx -t "hides the resend when the email is unknown"`

**C4** - Clicking resend sends `POST /api/auth/resend-verification` with body `{ "email": <state email> }` (AC 4, Flow 3)
Proof: `cd frontend && npx vitest run src/pages/RegisterSuccessPage.test.tsx -t "posts the known email to the resend endpoint"`

**C5** - While the resend request is in flight the control is disabled (loading state) (Observable: loading state)
Proof: `cd frontend && npx vitest run src/pages/RegisterSuccessPage.test.tsx -t "disables the control while the request is in flight"`

**C6** - After a successful resend a confirmation message is shown and the control is disabled with a countdown reading 60 (AC 4)
Proof: `cd frontend && npx vitest run src/pages/RegisterSuccessPage.test.tsx -t "confirms and starts a 60 second countdown after a successful resend"`

**C7** - The countdown reads 59 after one second, and after 60 seconds the control is enabled again (AC 4)
Proof: `cd frontend && npx vitest run src/pages/RegisterSuccessPage.test.tsx -t "counts down and re-enables after 60 seconds"`

**C8** - A resend answered 500, and one that fails with a network error, each show a generic retry message, leave the control enabled at once and show no countdown (AC 5)
Proof: `cd frontend && npx vitest run src/pages/RegisterSuccessPage.test.tsx -t "shows a generic retry message and stays enabled when the request fails"`

**C9** - A resend answered 429 shows the server's `message` and leaves the control enabled at once (AC 5 excludes 429 from the generic message; the plan is silent on what replaces it - settled here: the server's own message, no countdown)
Proof: `cd frontend && npx vitest run src/pages/RegisterSuccessPage.test.tsx -t "shows the server message when rate limited"`

### S2 - The endpoint issues a fresh token without leaking account state · 5 files · 20 KB · ~6k

**C10** - `ResendVerificationEmailHandler` for an existing unverified user stores exactly one new `UserConfirmationToken` with the user's id, purpose `EmailConfirmation`, the generator's hash, and `ExpiresAt` equal to the user's refreshed `PendingRegistrationExpiresAt` (about 15 minutes from now) (AC 6)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~ResendVerificationEmailTests.UnverifiedUser_StoresFreshTokenExpiringWithTheRefreshedWindow"`

**C11** - For an existing unverified user whose `ConfirmationSentAt` and `PendingRegistrationExpiresAt` are in the past, both are moved forward, and already are when the token is handed to `IUserTokenConfirmationRepository.AddAsync` (that call persists the user change in the same `SaveChangesAsync`) (AC 6, Flow 5)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~ResendVerificationEmailTests.UnverifiedUser_RefreshesTheWindowBeforeTheTokenIsPersisted"`

**C12** - For an existing unverified user one verification email is sent to that user's address and name carrying the generator's raw token (AC 6)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~ResendVerificationEmailTests.UnverifiedUser_EmailsTheRawTokenToTheirAddress"`

**C13** - For an email that belongs to no user the handler completes without throwing, stores no token and sends no email (AC 7)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~ResendVerificationEmailTests.UnknownEmail_DoesNothing"`

**C14** - For an already-verified user the handler completes without throwing, stores no token, sends no email and leaves `ConfirmationSentAt` and `PendingRegistrationExpiresAt` unchanged (AC 7)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~ResendVerificationEmailTests.VerifiedUser_DoesNothingAndChangesNoState"`

**C15** - A null, empty and whitespace-only `Email` each throw `ValidationException` whose `Errors["email"]` is `["Email is required."]`, before any lookup, token or email (AC 9)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~ResendVerificationEmailTests.MissingEmail_IsRejectedBeforeAnyLookup"`

**C16** - Over HTTP, `POST /api/auth/resend-verification` for a registered unverified email answers `200` with body `{ "message": "If an account exists for this email and isn't verified yet, we've sent a new link." }`, Mailpit then holds two messages to that address, and the token in the newer one verifies the account with `POST /api/auth/verify-email` answering `200` (AC 6, AC 8, door 1)
Proof: `cd frontend && npx playwright test e2e/resend-verification.spec.ts -g "resend endpoint answers alike and is rate limited"`

**C17** - Over HTTP, the same call for an unknown email and for an already-verified email each answer `200` with the identical body, and Mailpit gains no message for either (the unknown address has none; the verified address still has only its registration email) (AC 7, AC 8, door 1)
Proof: `cd frontend && npx playwright test e2e/resend-verification.spec.ts -g "resend endpoint answers alike and is rate limited"`

**C18** - Over HTTP, a body of `{ "email": "" }` and a body of `{}` each answer `400` with `errors.email` equal to `["Email is required."]` and `code` null (AC 9)
Proof: `cd frontend && npx playwright test e2e/resend-verification.spec.ts -g "resend endpoint answers alike and is rate limited"`

### S3 - Resend is rate limited per caller · 4 files · 15 KB · ~4k

**C19** - Over HTTP, the sixth call within the window (limit 5 in the e2e API) answers `429` with body `{ message, errors: null, code: null }` whose `message` names the limit `5` and the window `15 minutes`, and the earlier five calls (400, 400, 200, 200, 200) were not limited (AC 10, AC 11)
Proof: `cd frontend && npx playwright test e2e/resend-verification.spec.ts -g "resend endpoint answers alike and is rate limited"`

**C20** - `appsettings.json` ships `RateLimiting:ResendVerification` with `PermitLimit` 3, `WindowMinutes` 15 and `SegmentsPerWindow` 3 (AC 10)
Proof: `node -e "const s=require('./src/FormAI.API/appsettings.json').RateLimiting.ResendVerification; if(!(s.PermitLimit===3&&s.WindowMinutes===15&&s.SegmentsPerWindow===3)) process.exit(1)"`

**C21** - Sharing one `OnRejected` between two policies did not change the `generate` policy: over HTTP a signed-in user's eleventh `POST /api/forms/generate/text` within the window answers `429` with a `message` naming `10 form generations per 60 minutes`, the first ten answering `400` (unchanged behaviour, the code this feature edits)
Proof: `cd frontend && npx playwright test e2e/resend-verification.spec.ts -g "generate keeps its own rate limit message"`

### Cross-cutting

**C22** - The docs say what the code now does: `docs/known-gaps.md` no longer lists "No way to resend the confirmation email" but still lists the missing cleanup of expired unconfirmed users, and no longer says only the generation endpoint is limited; `CLAUDE.md` documents the resend endpoint and its `RateLimiting:ResendVerification` limit
Proof: `! grep -q "No way to resend the confirmation email" docs/known-gaps.md`
Proof: `grep -q "cleanup of expired unconfirmed" docs/known-gaps.md`
Proof: `! grep -q "Only this endpoint is limited" docs/known-gaps.md`
Proof: `grep -q "resend-verification" CLAUDE.md`
Proof: `grep -q "RateLimiting:ResendVerification" CLAUDE.md`

**C23** - The whole backend builds and the whole frontend type-checks and lints
Proof: `dotnet build FormAI.sln`
Proof: `cd frontend && npm run build`
Proof: `cd frontend && npm run lint`

**C24** - Unchanged by this feature: registering still stores the generator's hash and emails the raw token
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~RegisterTests.ValidEmail_StoresGeneratedHash_EmailsGeneratedRawToken"`

**C25** - Unchanged by this feature: verifying with a valid token still verifies the user and marks the token used (the only path that consumes what resend creates)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~VerifyEmailTests.ValidToken_VerifiesUserAndMarksTokenUsed"`

**C26** - Unchanged by this feature: the generate endpoint, still `[EnableRateLimiting(RateLimitPolicies.Generate)]`, keeps its handler rejection of an invalid request before generating or saving
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~GenerateFormTests.InvalidRequest_IsRejectedBeforeGeneratingOrSaving"`

**C27** - Unchanged by this feature: registering with an already-registered email is still rejected with `ValidationException` and sends no email (the "re-register" path stays closed, so resend is the only recovery)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~RegisterTests.ExistingEmail_ThrowsException"`

## Coverage

| Set (size) | Member -> proof | Unproven |
| --- | --- | --- |
| `POST /api/auth/resend-verification` statuses (3) | 200 C16 C17 · 400 C18 · 429 C19 | - |
| handler branches (4) | unverified user C10 C11 C12 · unknown email C13 · verified user C14 · blank email C15 | - |
| blank `Email` inputs (3) | null C15 · empty C15 · whitespace C15, table-driven over all 3 | - |
| identical-`200` cases (3) | unknown email C17 · verified email C17 · freshly resent C16 | - |
| resend page states (2) | email known C2 · email unknown C3 | - |
| resend request outcomes (4) | success C6 C7 · non-429 status C8 · network error C8 · 429 C9 | - |
| `Landing` door 1 cases (3) | unknown email C17 · already-verified C17 · freshly issued C16 | - |
| `generate` policy statuses (2) | 400 C21 · 429 C21 | - |
| startup config: `ResendVerification` policy, limits, handler DI (1 assembly) | `FormAI.API` host (`Program.cs`), the one e2e boots and the one production runs C16 C19 | - |

- Claims naming a status code, route or response shape: C16, C17, C18, C19, C21 - each has a Playwright proof
  that crosses the HTTP boundary. C10-C15 assert the handler's outcome and name no status
- C24-C27 are existing tests, unchanged by this feature; each claim is worded as the behaviour the
  test asserts today
- C16, C17, C18 and C19 share one Playwright test on purpose (see the note above); it is a single
  proof of four claims, and its steps assert each separately
- No other check claims more than the single case its proof exercises

## Swept

- validation: C15, C18 - blank or missing email is a 400 under `errors.email`, and the request record takes `string? Email` so a missing field reaches the handler instead of the framework's own 400 shape
- failure modes: C8, C9 - the page shows a generic retry message on failure and the server's message on 429; a failed SMTP send after the token is stored is left as `RegisterHandler` leaves it (error surfaces, user retries)
- idempotency: C16 - a second resend is not deduplicated, it issues another token; both stay valid until their own `ExpiresAt` (the plan's assumption), bounded by C19 and the 60 second cooldown in C7
- authorization: n/a - anonymous by design, as `Register` and `VerifyEmail` on the same controller; C17 proves the body does not reveal whether an account exists
- concurrency: n/a - two overlapping resends each mint their own token and the last write wins on `ConfirmationSentAt`; nothing needs ordering because every unexpired token is valid
- data lifecycle: n/a - only new rows of the existing `UserConfirmationToken` shape; expired token rows accumulate as they already do (the known gap on cleanup stays, C22)
- dependency failure: n/a - the one dependency is `IEmailService`, already used by `RegisterHandler` with no retry or fallback; nothing new is added
- state transitions: C10, C11, C14 - an unverified user's window is refreshed; a verified user never moves back
- observability: n/a - no logging requirement in this change

## Out of scope

None beyond the plan's `## Out of scope`, which is unchanged.

## Handoff

- S1 = ~7k (`RegisterPage`, `RegisterSuccessPage`, `api/auth.ts`, 2 new test files, `renderWithProviders` gains an optional router `state`), S2 = ~6k (new handler, request record, controller action, DI line, 1 new test file), S3 = ~4k (`RateLimitingExtensions`, new options class, `appsettings.json`, e2e config and spec), docs edits ~2k; about 20 KB of touched files plus ~25 KB of new tests, about 19k in total, under the 150k budget - one builder
- Mechanism: one builder (fits, no ask)

- **Boundary:** C1-C27 closed at the commit that adds this line
- **Settled mid-build:** the local `form_ai_e2e` database was empty and was migrated as `form_ai_migrator` (the documented prerequisite) to run the e2e proofs; a 429 on the page shows the server's own message with no countdown (C9); the countdown is a `setInterval` (the test advances a fake clock in one jump, which a chained `setTimeout` cannot follow); `renderWithProviders` gained an optional router `state`
- **Abandoned:** nothing
