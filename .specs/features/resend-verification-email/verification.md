# Resend verification email verification

**Verdict**: PASS
**Profile**: light
**Diff range**: e511564..ffc539e
**Round**: 2 - scoped
**Verifier**: independent sub-agent (author != verifier)

Scope of this round: proofs re-run in full at ffc539e (all 27 checks). Citations refreshed for the files
da2c06b, 0afc428 and ffc539e touched (`RegisterSuccessPage.tsx`, `RegisterSuccessPage.test.tsx`,
`VerifyEmailPage.tsx`, `VerifyEmailPage.test.tsx`, `plan.md`, `checks.md`). Everything else is marked
`carried from d9104e2`; `git diff d9104e2..HEAD -- src tests/FormAI.UnitTests frontend/e2e` is empty, so
no backend, unit-test or e2e file changed since Round 1.

## Checks

Proof commands, one per target, at HEAD ffc539e (each named test appeared individually as passed):

- Frontend: `cd frontend && npx vitest run src/pages/RegisterPage.test.tsx src/pages/RegisterSuccessPage.test.tsx src/pages/VerifyEmailPage.test.tsx --reporter=verbose` - 10 passed, 0 failed (3 files)
- Backend: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~ResendVerificationEmailTests|...~RegisterTests.ValidEmail_...|...~VerifyEmailTests.ValidToken_...|...~GenerateFormTests.InvalidRequest_...|...~RegisterTests.ExistingEmail_ThrowsException"` - 14 passed, 0 failed
- E2E: `cd frontend && npx playwright test e2e/resend-verification.spec.ts` - 2 passed (both tests named in the list output). See the environment note under Gate.

| Check | Claim | Proof run | Evidence | Result |
| --- | --- | --- | --- | --- |
| C1 | email travels as router state to success page | vitest, "carries the registered email to the success page" passed at ffc539e | `frontend/src/pages/RegisterPage.test.tsx:37` - `expect(screen.getByRole("button", { name: /resend verification email/i })).toBeInTheDocument()` (carried from d9104e2; file untouched) | PASS |
| C2 | resend button and "few minutes" copy when email known | vitest, "offers a resend when the email is known" passed at ffc539e | `RegisterSuccessPage.test.tsx:33` - `expect(screen.getByRole("button", RESEND)).toBeEnabled()`; `:34` - `expect(screen.getByText(/in a few minutes/i)).toBeInTheDocument()` (verified at ffc539e; copy at `RegisterSuccessPage.tsx:81`) | PASS |
| C3 | no resend when email unknown, rest intact | vitest, "hides the resend when the email is unknown" passed at ffc539e | `RegisterSuccessPage.test.tsx:40` - `expect(screen.queryByRole("button", RESEND)).not.toBeInTheDocument()`; `:41` heading "Check your inbox"; `:42` `getByRole("button", { name: "Go to login" })` (verified at ffc539e) | PASS |
| C4 | click POSTs `{ email: <state email> }` | vitest, "posts the known email to the resend endpoint" passed at ffc539e | `RegisterSuccessPage.test.tsx:58` - `await waitFor(() => expect(body).toEqual({ email: EMAIL }))`. The wait is on the captured request body, exactly what the claim names, and no longer on the success text: it is not weaker than the claim. Handler `:48-51` reads `request.json()` into `body`. | PASS |
| C5 | control disabled while request in flight | vitest, "disables the control while the request is in flight" passed at ffc539e | `RegisterSuccessPage.test.tsx:73` - `expect(screen.getByRole("button", { name: /loading/i })).toBeDisabled()` asserted right after the click while MSW holds the response for `delay(200)` (`:64`). Line `:74` (`await screen.findByRole("button", { name: /\(60s\)/ })`) only settles the test after the assertion; it does not carry the claim. The claim is settled by `:73` in the pending state, so it is not weaker. | PASS |
| C6 | after a successful resend: control disabled, countdown reads 60, and no success message | vitest, "starts a 60 second countdown without a success message after a successful resend" passed at ffc539e | Half 1 (countdown): `RegisterSuccessPage.test.tsx:84` - `const button = await screen.findByRole("button", { name: /\(60s\)/ })`; `:85` - `expect(button).toBeDisabled()`; `:86` - `expect(button).toHaveTextContent("60")`. Half 2 (no success message): `:87` - `expect(screen.queryByText(/we've sent a new link/i)).not.toBeInTheDocument()`; `:88` - `expect(screen.queryByRole("status")).not.toBeInTheDocument()`. Both halves are asserted after the button reached `(60s)`, which the code only sets after the awaited request resolves (`RegisterSuccessPage.tsx:27-32`), so the absence checks run in the post-success state rather than before it. The endpoint stub returns `{ message: "ok" }` (`:21-22`), so the text absence check uses the plan's door-1 message text and would fail if the page rendered any text matching it. | PASS |
| C7 | countdown reads 59 after 1s, enabled again after 60s | vitest, "counts down and re-enables after 60 seconds" passed at ffc539e | `RegisterSuccessPage.test.tsx:98` - `await screen.findByRole("button", { name: /\(60s\)/ })` (waits on the countdown start, the state the claim begins from, not on a message); `:100` `advanceTimersByTimeAsync(1000)`; `:101` - `expect(screen.getByRole("button", RESEND)).toHaveTextContent("59")`; `:102` `toBeDisabled()`; `:104` `advanceTimersByTimeAsync(59_000)`; `:105` - `expect(screen.getByRole("button", RESEND)).toBeEnabled()`. Matches the claim; the wait is the countdown, not weaker. | PASS |
| C8 | 500 and network error: generic message, enabled, no countdown | vitest, "shows a generic retry message and stays enabled when the request fails" passed at ffc539e | `RegisterSuccessPage.test.tsx:114` - `expect(await screen.findByText(/something went wrong/i)).toBeInTheDocument()`; `:116` - `toBeEnabled()`; `:117` - `not.toHaveTextContent("60")`; network-error repeat at `:121-123` (verified at ffc539e; refreshed lines) | PASS |
| C9 | 429 shows server message, enabled at once | vitest, "shows the server message when rate limited" passed at ffc539e | `RegisterSuccessPage.test.tsx:137` - `expect(await screen.findByText(/limit of 3 verification emails/i)).toBeInTheDocument()`; `:138` - `expect(screen.getByRole("button", RESEND)).toBeEnabled()` (verified at ffc539e) | PASS |
| C10 | one new token: user id, purpose, hash, ExpiresAt = refreshed window | dotnet, `UnverifiedUser_StoresFreshTokenExpiringWithTheRefreshedWindow` passed at ffc539e | `tests/FormAI.UnitTests/Users/ResendVerificationEmailTests.cs:48-53` - `Assert.Single(stored)`, `Assert.Equal(TokenPurpose.EmailConfirmation, token.Purpose)`, `Assert.Equal(TokenHash, token.TokenHash)`, `Assert.Equal(user.PendingRegistrationExpiresAt, token.ExpiresAt)`, `Assert.InRange(...)` (carried from d9104e2) | PASS |
| C11 | window moved forward before AddAsync | dotnet, `UnverifiedUser_RefreshesTheWindowBeforeTheTokenIsPersisted` passed at ffc539e | `ResendVerificationEmailTests.cs:72-73` - `Assert.True(sentAtWhenPersisted >= before, ...)`, `Assert.True(expiresAtWhenPersisted >= before.AddMinutes(15), ...)` (carried from d9104e2) | PASS |
| C12 | one email to user address/name with raw token | dotnet, `UnverifiedUser_EmailsTheRawTokenToTheirAddress` passed at ffc539e | `ResendVerificationEmailTests.cs:83` - `await _emailService.Received(1).SendVerificationEmailAsync(Email, Name, RawToken, Arg.Any<CancellationToken>())` (carried from d9104e2) | PASS |
| C13 | unknown email: no throw, no token, no email | dotnet, `UnknownEmail_DoesNothing` passed at ffc539e | `ResendVerificationEmailTests.cs:93` - `_userTokens.DidNotReceive().AddAsync(Arg.Any<UserConfirmationToken>(), ...)`; `:94-95` - `_emailService.DidNotReceive().SendVerificationEmailAsync(...)` (carried from d9104e2) | PASS |
| C14 | verified user: no state change, no token, no email | dotnet, `VerifiedUser_DoesNothingAndChangesNoState` passed at ffc539e | `ResendVerificationEmailTests.cs:108-109` - `Assert.Equal(sentAt, user.ConfirmationSentAt)`, `Assert.Equal(expiresAt, user.PendingRegistrationExpiresAt)`; `:110-112` `DidNotReceive()` on AddAsync and SendVerificationEmailAsync (carried from d9104e2) | PASS |
| C15 | null/empty/whitespace -> ValidationException `["Email is required."]` | dotnet, `MissingEmail_IsRejectedBeforeAnyLookup` passed for `null`, `""`, `"   "` (three rows listed) at ffc539e | `ResendVerificationEmailTests.cs:121` - `Assert.ThrowsAsync<ValidationException>(`; `:124` - `Assert.Equal(["Email is required."], ex.Errors["email"])` (carried from d9104e2) | PASS |
| C16 | HTTP: 200 identical body, 2nd Mailpit message, token verifies (200) | playwright, "resend endpoint answers alike and is rate limited" passed at ffc539e | `frontend/e2e/resend-verification.spec.ts:65-66` - `expect(res.status()).toBe(200)`, `expect(await res.json()).toEqual(SAME_MESSAGE)`; `:69` - `expect(messages).toHaveLength(2)`; `:76` - `expect(verify.status(), ...).toBe(200)` (carried from d9104e2) | PASS |
| C17 | HTTP: unknown and verified email: same 200, no new mail | same Playwright test at ffc539e | `e2e/resend-verification.spec.ts:56-57` - `expect(res.status(), email).toBe(200)`, `expect(await res.json()).toEqual(SAME_MESSAGE)`; `:59` `toHaveLength(0)`; `:60` `toHaveLength(verifiedMailBefore)` (carried from d9104e2) | PASS |
| C18 | HTTP: `{email:""}` and `{}` -> 400, errors.email, code null | same Playwright test at ffc539e | `e2e/resend-verification.spec.ts:46` - `.toBe(400)`; `:48` - `expect(body.errors.email).toEqual(["Email is required."])`; `:49` - `expect(body.code).toBeNull()` (carried from d9104e2) | PASS |
| C19 | HTTP: 6th call 429 naming limit 5 / 15 minutes; earlier 400,400,200,200,200 unlimited | same Playwright test at ffc539e | `e2e/resend-verification.spec.ts:81` - `.toBe(429)`; `:83` - `expect(body.message).toMatch(/limit of 5 .*per 15 minutes/)`; `:84-85` `errors`/`code` `toBeNull()` (carried from d9104e2) | PASS |
| C20 | appsettings ships 3 / 15 / 3 | `node -e ...` exit 0 at ffc539e | `src/FormAI.API/appsettings.json` `RateLimiting.ResendVerification`; predicate `s.PermitLimit===3&&s.WindowMinutes===15&&s.SegmentsPerWindow===3` exited 0 | PASS |
| C21 | generate policy unchanged: 10x 400, 11th 429 naming "10 form generations per 60 minutes" | playwright, "generate keeps its own rate limit message" passed at ffc539e | `e2e/resend-verification.spec.ts:99` - `.toBe(400)` (x10); `:103` - `.toBe(429)`; `:104` - `.message).toMatch(/limit of 10 form generations per 60 minutes/)` (carried from d9104e2) | PASS |
| C22 | docs updated | 5 greps, all exit 0 at ffc539e | `! grep "No way to resend the confirmation email" docs/known-gaps.md` (0); `grep "cleanup of expired unconfirmed" docs/known-gaps.md` (0); `! grep "Only this endpoint is limited" docs/known-gaps.md` (0); `grep "resend-verification" CLAUDE.md` (0); `grep "RateLimiting:ResendVerification" CLAUDE.md` (0) | PASS |
| C23 | backend builds, frontend builds and lints | `npm run build` succeeded (chunk-size advisory only); `npm run lint` exit 0; backend build see Gate note | `npm run build` OK, `lint=0` at ffc539e; `dotnet build src/FormAI.API` and `dotnet build tests/FormAI.IntegrationTests` each `0 Warning(s) 0 Error(s)`, and the unit project built inside the `dotnet test` run, so all 4 solution projects plus the unit project compiled | PASS |
| C24 | register still stores hash and emails raw token | dotnet, `RegisterTests.ValidEmail_StoresGeneratedHash_EmailsGeneratedRawToken` passed at ffc539e | `RegisterTests.cs:71-72` - `Assert.Equal(tokenHash, storedToken.TokenHash)`, `Assert.Equal(rawToken, emailedRawToken)` (carried from d9104e2) | PASS |
| C25 | verify with valid token verifies user, marks token used | dotnet, `VerifyEmailTests.ValidToken_VerifiesUserAndMarksTokenUsed` passed at ffc539e | `VerifyEmailTests.cs:49-50` - `Assert.True(_defaultUser.IsEmailVerified)`, `Assert.NotNull(_defaultUserToken.UsedAt)` (carried from d9104e2) | PASS |
| C26 | generate rejects invalid request before generating/saving | dotnet, `GenerateFormTests.InvalidRequest_IsRejectedBeforeGeneratingOrSaving` passed (3 theory rows listed) at ffc539e | `GenerateFormTests.cs:224-226` - `Assert.ThrowsAsync<ValidationException>(...)`, `Assert.Equal([expectedErrorKey], exception.Errors.Keys)` (carried from d9104e2) | PASS |
| C27 | re-register with taken email still rejected, no email sent | dotnet, `RegisterTests.ExistingEmail_ThrowsException` passed at ffc539e | `RegisterTests.cs:86` - `Assert.Equal("This email is already registered.", ex.Errors["email"].FirstOrDefault())`; `:89` - `_emailService.DidNotReceive().SendVerificationEmailAsync(...)` (carried from d9104e2) | PASS |

## Coverage

Carried from d9104e2, not recomputed (profile `light`). The fix touched no member of any set in `checks.md`
Coverage; the `resend request outcomes (4)` row (success C6 C7, non-429 C8, network C8, 429 C9) still has a
proof for every member at ffc539e.

## Test policy rows

Carried from d9104e2: `checks.md` carries no `Test policy` rows. Repo convention applies (Vitest for page
behaviour, xUnit for handlers, Playwright for HTTP status and shape) and is still met.

## Faults injected

Not run (profile `light`). This step is not required at this profile.

## Judgment notes

**(a) C6 as now worded** - both halves are asserted at `RegisterSuccessPage.test.tsx:84-88`: the countdown
half (`findByRole(... /\(60s\)/)`, `toBeDisabled()`, `toHaveTextContent("60")`) and the absence half
(`queryByText(/we've sent a new link/i)` and `queryByRole("status")` both `not.toBeInTheDocument()`). The
absence checks run after the `(60s)` state appears, which only happens after the request resolved, so they
test the post-success screen. The test name and the claim in `checks.md` agree word for word.

**(b) C4, C5, C7 after dropping the wait on the success text** - each still asserts what its claim says.
C4 waits on the captured request body (`:58`). C5 asserts the loading button in the pending state (`:73`);
the trailing `findByRole(... (60s))` on `:74` is only a settle step and carries no part of the claim. C7 waits
on the countdown start (`:98`) and then asserts 59 and enabled (`:101`, `:105`). No proof waits on something
weaker than its claim. No finding.

**(c) Plan against code** -
- AC 4 (`plan.md:91-92`): "disable the resend control for 60 seconds with a visible countdown, and SHALL NOT show a success message." Code: `setSecondsLeft(COOLDOWN_SECONDS)` with `COOLDOWN_SECONDS = 60` (`RegisterSuccessPage.tsx:7,32`); the button reads `Resend verification email (${secondsLeft}s)` and is `disabled={isCoolingDown}` (`:87,90-92`); only `notice.isError` renders (`:94`), so no success text. Matches.
- Flow hop 6 (`plan.md:43-45`): "starts a client-side cooldown ... No success message is shown" - matches the same lines.
- S1 independent test (`plan.md:96-97`): "a disabled, counting-down button and no success message" - matches `RegisterSuccessPage.test.tsx:84-88`.
- Landing door 1 is still honoured. `git diff d9104e2..HEAD -- src` is empty, so `AuthController` still returns the identical `200 { message }` for all three cases (Round 1 evidence, re-proven at HTTP by C16 and C17 at ffc539e). The page simply does not render it; the door is about the API body, not the UI, so the UI change does not touch it. Not a contradiction.
- The plan's Observable row (loading state, AC 4) and the `resend page states` and `resend request outcomes` coverage rows still hold.

**(d) Observation only, outside the checks** - da2c06b changed `VerifyEmailPage.tsx` (+/- 9 lines) so the token is
verified once under StrictMode, with a new `VerifyEmailPage.test.tsx` ("verifies the token once even when
StrictMode runs the effect twice", passed at ffc539e). The fix is relevant because the resend flow sends
users to that page and a double request would burn a single-use token, but no check C1-C27 covers it and it is
not counted as one.

**Other observations (not failures)**
1. `RegisterSuccessPage.tsx:28-31` still sets a non-error `notice` (the door-1 success text) that is never
   rendered, since only `notice.isError` renders at `:94`. It is dead state left over from the removed
   message and could be deleted. It does not affect any claim.
2. `RegisterSuccessPage.tsx:95` has `role={"alert"}` and a template literal with no interpolation
   (`` `mt-2 text-xs text-red-600` ``); style only.
3. Round 1 observations 1-5 stand, carried from d9104e2 (429 handling settled mid-build, no-body request
   binding not exercised, e2e limit 5 versus shipped 3, implicit "no throw", cosmetic table padding).

**Swept existing** - carried from d9104e2; no handler, request record or controller file changed in the range.

## Gate

`npx vitest run` (3 files) - 10 passed, 0 failed; `dotnet test` (filtered) - 14 passed, 0 failed;
`npx playwright test e2e/resend-verification.spec.ts` - 2 passed, 0 failed; `npm run build` OK; `npm run lint`
exit 0; `dotnet build` of `FormAI.API` and `FormAI.IntegrationTests` - 0 warnings, 0 errors; node and grep proofs exit 0.

Environment note: the repo's own `dotnet run` and `dotnet build FormAI.sln` failed with MSB3027/MSB3021 because
a developer API process (PID 3396, started 6:11 PM, listening on 5155, running from this worktree's
`src/FormAI.API/bin`) holds the output DLLs. I did not stop it. The Playwright and build proofs were run with
the output redirected to a scratch directory instead (`OutDir=...\claude\e2eout\` for playwright,
`-p:OutDir=...` for the two project builds); source and command are otherwise as in `checks.md`. The tree is
unmodified (`git status --porcelain` shows only this untracked report).
