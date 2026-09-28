# Resend verification email

## Problem

An account that misses its 15-minute confirmation window can never log in: `LoginHandler`
rejects any user whose `IsEmailVerified` is `false`, and there is no way to get a second
verification email — no resend endpoint, no re-register (the email is already taken), no
cleanup. This is documented as a known gap (`docs/known-gaps.md` line 19): "an account that
misses its window can never log in." `RegisterSuccessPage.tsx` is a static confirmation screen
with a "Go to login" link and no recovery path if the first email is lost, delayed, or filtered
as spam.

After this ships, a user who lands on the success page and does not see the email within a few
minutes can click a link on that same page to have a new one sent, without contacting support or
re-registering under a different address.

## Flow

Reuses the exact token-issuing sequence `RegisterHandler` already runs (`User.SetConfirmationSent`,
`IConfirmationTokenGenerator.Generate`, `UserConfirmationToken.Create`, `IEmailService.SendVerificationEmailAsync`)
instead of inventing a second way to mint a confirmation token.

1. `RegisterPage.tsx` (existing, changed) — on successful registration, navigates to
   `/register/success` carrying the registered email as router state instead of nothing.
2. `RegisterSuccessPage.tsx` (existing, changed) — reads `location.state.email`; if present,
   renders a "Resend verification email" link with explanatory copy; if absent (direct or
   refreshed visit), renders the page exactly as today with no resend affordance.
3. Click → `resendVerificationEmail(email)` in `frontend/src/api/auth.ts` (new function,
   placement) → existing `api` axios instance → `POST /api/auth/resend-verification`.
4. `AuthController` (existing, new action) — no `[Authorize]` at the controller today, so the
   action is anonymous by default like `Register`/`VerifyEmail`; decorated with
   `[EnableRateLimiting(RateLimitPolicies.ResendVerification)]` (new policy, door 1).
5. `ResendVerificationEmailHandler` (new, `Application/Users/Auth/`) — `IUserRepository.GetByEmailAsync`
   (existing). Branch:
   - user not found, or the existing `User.IsEmailVerified` field is already `true` → do nothing
     further, fall through to the same response as success.
   - user found and unverified → `user.SetConfirmationSent()` (existing method, resets the
     15-minute window), `IConfirmationTokenGenerator.Generate()` (existing), `UserConfirmationToken.Create(...)`
     (existing factory), `IUserTokenConfirmationRepository.AddAsync` (existing — persists the new
     token and the user's updated `ConfirmationSentAt`/`PendingRegistrationExpiresAt` in one
     `SaveChangesAsync` since both are tracked by the same `AppDbContext`), `IEmailService.SendVerificationEmailAsync`
     (existing).
6. out: `200 { message }` — identical text in every branch above (door 2) — and the frontend
   shows a confirmation and starts a client-side cooldown on the button.

## Impact

| Front | What changes |
| --- | --- |
| domain | existing behaviour: a user may now have more than one valid (unused, unexpired) `UserConfirmationToken` row for `EmailConfirmation` at once. Previously only `RegisterHandler` created one per user, ever. `VerifyEmailHandler.FindActiveAsync` looks up by token hash, not by user, so nothing today assumes at-most-one and nothing needs to change there — but it's the first code path to knowingly create a second live one. |
| docs | `docs/known-gaps.md` line 19 ("No way to resend the confirmation email") is closed by this feature and its row is removed in the same change, per `CLAUDE.md`'s "Keeping the docs true." The rest of that row (no cleanup of expired unconfirmed users) is a separate, still-true gap and is not addressed here. |
| stored data | Nothing to migrate or backfill — only new rows of an existing shape (`UserConfirmationToken`), created at request time. |

## Relations

`None - no stored-data shape change` (new rows of the existing `UserConfirmationToken`/`User`
shape only; no new entity, field, or index).

## Surface

| Route | In | Out | Status |
| --- | --- | --- | --- |
| `POST /api/auth/resend-verification` | `email` | `message` | `200`, `400`, `429` |

## Landing

| One-way door | Literal shape | Alternative rejected |
| --- | --- | --- |
| The response never reveals whether the email is registered or already verified | `POST /api/auth/resend-verification` returns `200 { message: "If an account exists for this email and isn't verified yet, we've sent a new link." }` for all three of: unknown email, already-verified email, and a freshly issued resend. The only other statuses are `400` (missing/blank email) and `429` (rate limited) — no branch returns `404` or a distinct message for "no such account" or "already verified." | A distinct `404`/message per case — disqualified because it lets an anonymous caller enumerate which emails are registered, the same class of leak `CLAUDE.md` already closed elsewhere on purpose: `FormAccessValidator` throws `NotFoundException` for both "doesn't exist" and "exists but not yours" so the two are indistinguishable, and `LoginHandler` returns the same generic message for a wrong password and an unknown email. A second, differently-shaped anonymous endpoint that does leak would reopen that closed gap via a new door. |

- Nothing else in this change is hard to reverse: the rate-limit numbers are config
  (`appsettings.json`), the client-side cooldown is component state, and whether prior
  outstanding tokens get invalidated on resend is a behavioural default, not a persisted
  constraint (see `## Assumptions`).

## Criteria

### S1: A user who doesn't see the first email can ask for a new one from the success page (P1)

The success page offers a way to trigger a resend when it knows which email to resend to.

**Acceptance Criteria**

1. WHEN registration succeeds THEN the system SHALL navigate to `/register/success` carrying the
   registered email as router state.
2. WHILE `/register/success` has a known email (from router state) THEN the system SHALL render a
   "Resend verification email" link with copy telling the user to click it if nothing arrives in a
   few minutes.
3. WHILE `/register/success` has no known email (direct or refreshed visit, no router state) THEN the system SHALL NOT render the resend affordance, leaving the rest of the page unchanged.
4. WHEN the user clicks resend and the request succeeds THEN the system SHALL show a confirmation
   message and disable the resend control for 60 seconds with a visible countdown.
5. IF the resend request fails (network error or non-429 error status) THEN the system SHALL show
   a generic retry message and re-enable the control immediately.

**Independent test:** register a new account, land on the success page with the email in router
state, click resend, and see the confirmation message and a disabled/counting-down button.

### S2: The resend endpoint issues a fresh token without leaking account state (P1)

`POST /api/auth/resend-verification` behaves identically to a caller whether or not the account
exists or is already verified.

**Acceptance Criteria**

6. WHEN `POST /api/auth/resend-verification` is called with the email of an existing, unverified user THEN the system SHALL create a new `UserConfirmationToken` (purpose `EmailConfirmation`, 15-minute expiry from `User.SetConfirmationSent()`), update that user's `ConfirmationSentAt` and `PendingRegistrationExpiresAt`, and send a new verification email to that address.
7. WHEN `POST /api/auth/resend-verification` is called with an email that belongs to no user, or to an already-verified user, THEN the system SHALL NOT create a token, SHALL NOT send an email, and SHALL NOT change any stored verification state.
8. The system SHALL return the identical `200` response body for the three cases in criteria 6 and 7 (unknown email, already-verified email, freshly-resent email) — same status, same message.
9. IF the `email` field is missing or blank THEN the system SHALL return `400` with
   `{"email": ["Email is required."]}` under `errors`, matching the shape `RegisterHandler` and
   `VerifyEmailHandler` already use for field validation.

**Independent test:** call the endpoint directly with a known unverified email (new token row and
email sent), an unknown email (200, no token, no email — assert via the email-sending
substitute/Mailpit), and an already-verified email (200, no token, no email).

### S3: Resend is rate limited per caller (P1)

Repeated resend requests from one caller are capped, so a lost-in-spam-filter email can be
retried a few times without letting the endpoint be used to mail-bomb an arbitrary address or
burn SES budget.

**Acceptance Criteria**

10. The system SHALL rate-limit `POST /api/auth/resend-verification` to `RateLimiting:ResendVerification:PermitLimit`
    (default 3) requests per `RateLimiting:ResendVerification:WindowMinutes` (default 15) sliding
    window per caller, using the same `GetPartitionKey` partitioning `RateLimitPolicies.Generate`
    already uses (falls back to remote IP address, since this endpoint is always anonymous).
11. IF the caller exceeds that limit THEN the system SHALL respond `429` with
    `{ message, errors: null, code: null }`, matching the existing `OnRejected` shape used by the
    `generate` policy, with a message naming the limit and window.

**Independent test:** call the endpoint 4 times in a row from the same client within the window
and see the 4th call answered `429` with the documented body shape.

## Out of scope

| Excluded | Why |
| --- | --- |
| Cleanup of expired, never-confirmed accounts | Pre-existing, separate known gap (`docs/known-gaps.md` line 19); resend fixes the lockout for a user who comes back, not the accumulation of abandoned rows. |
| Re-register with a taken-but-unverified email | Resend removes the need for it; adding a second recovery path is not asked for. |
| A resend affordance on the login page (for a user who hits `EmailNotVerified` there) | The task scopes this to `RegisterSuccessPage`; `LoginHandler`'s existing `ValidationErrorCode.EmailNotVerified` path is unchanged. |
| Cross-instance / distributed rate limiting | Matches the accepted, documented limitation of the existing `generate` policy (in-memory, per-instance only) — not a regression this feature introduces. |
| Invalidating a user's still-outstanding confirmation tokens when a new one is issued | See `## Assumptions`. |

## Assumptions

| Assumption | Chosen default | Rationale | Confirmed? |
| --- | --- | --- | --- |
| Whether resend invalidates prior outstanding tokens | No — let old tokens expire on their own `ExpiresAt` | Both tokens are equally hard to guess (64 bytes, random); `UserConfirmationToken` has no `Invalidate`/`Reissue` method today and adding one is out of proportion to the risk, since the coexistence window is at most 15 minutes | n |
| Exact rate-limit numbers | `PermitLimit = 3`, `WindowMinutes = 15`, `SegmentsPerWindow = 3` | Ties the budget to the same 15-minute window the token itself lives in (a 4th attempt inside one token's lifetime is a signal, not a normal retry); tighter than `generate`'s 10/60 because this endpoint has no authenticated caller to hold accountable and each permit costs a real outbound email | n |
| Client-side cooldown length after a successful resend | 60 seconds, button disabled with a visible countdown | Stops a user from burning the whole 3-per-15-minute server budget by rapid re-clicking, using only component state (no new persistence) | n |
| Behaviour when the success page has no email in router state | Hide the resend affordance entirely; keep the rest of the page as-is | Nothing to resend to, and no session exists yet at this pre-login screen to look the email up another way | n |

**Open questions:** none — all resolved above with a stated default.

## Observable

| Surface | Decision | Landing |
| --- | --- | --- |
| screen `/register/success` | empty state | n/a - static confirmation content, nothing paginated or listed |
| screen `/register/success` | loading state | AC 4 (button shows a loading state while the request is in flight, via the existing `Button` `isLoading` prop) |
| screen `/register/success` | error state | AC 5 |
| screen `/register/success` | unauthorised state | n/a - page requires no auth today; unchanged |
| screen `/register/success` | destructive action confirms | n/a - resend is not destructive |
| API `POST /api/auth/resend-verification` | response shape | AC 6, 7, 8 |
| API `POST /api/auth/resend-verification` | error shape and codes | AC 9, 11 |
| API `POST /api/auth/resend-verification` | who may call it | n/a - anonymous by design, mirrors `Register`/`VerifyEmail` on the same controller |
| API `POST /api/auth/resend-verification` | versioning | n/a - no API versioning scheme exists anywhere in this codebase |
| API `POST /api/auth/resend-verification` | what happens at the rate limit | AC 10, 11 |

## Sources

- User's task description (this conversation) — scopes the change to `RegisterSuccessPage.tsx`,
  sets the copy tone ("if you don't receive an e-mail in a few instants retry by clicking here"),
  and requires rate limiting.
- `docs/known-gaps.md` line 19 — the gap this closes, and the 15-minute token window the resend
  flow must respect.
