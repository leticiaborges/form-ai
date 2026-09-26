# Plan: use the refresh token (httpOnly cookie, hashed, configurable lifetimes)

## Context

The server already issues and rotates refresh tokens (`LoginHandler`, `RefreshTokenHandler`, `POST /api/auth/refresh`), but nothing uses them. The frontend saves `refreshToken` in `localStorage` and never reads it, and `frontend/src/api/axios.ts` logs the user out on the first 401, so every session dies after 60 minutes.

Goal: a session that survives access-token expiry, built the production-grade way:

- The **refresh token** lives only in an `HttpOnly; Secure; SameSite=Strict` cookie. JavaScript never sees it.
- The **access token** is held in memory only (no `localStorage`).
- Refresh tokens are **stored hashed** in the database.
- Both lifetimes come from `appsettings.json`.

Decisions already made:

- Cookie, not `localStorage`.
- No reuse detection.
- Hash the tokens.
- Old rows will be deleted by hand, so no data compatibility work.
- Expired/revoked-row cleanup is a separate task.
- `RefreshTokenHandler` does not check `IsEmailVerified`.
- Access token stays at 60 minutes, but is configurable.
- Production is **same origin via CloudFront** (`/api/*` and `/hubs/*` routed to the ALB).

Stages 1–3 are the core and are done in order. Each stage ends with its tests.

---

## Stage 1: Backend foundation (settings, hashing, lifetimes)

No behavior change visible to the frontend yet.

1. **Settings.**
   - `Infrastructure/Security/JwtSettings.cs`: keep `ExpiresInMinutes`, add `RefreshTokenExpiryDays` (default 7).
   - `src/FormAI.API/appsettings.json`: add a `Jwt` section with just those two keys (`60` and `7`). `Secret`, `Issuer` and `Audience` stay out of tracked config.
   - `Infrastructure/DependencyInjection.cs` (~line 83): fail startup if either value is ≤ 0, in the same style as the existing "section is missing" check.
2. **Remove the `RefreshToken.ExpiryDays` constant** (Domain has no config). Add `TimeSpan RefreshTokenLifetime { get; }` to `IJwtService`, implemented in `JwtService` from the settings. `LoginHandler` and `RefreshTokenHandler` compute expiry as `UtcNow + _jwtService.RefreshTokenLifetime`.
3. **Hash at rest.** In `Domain/Entities/RefreshToken.cs`:
   - Rename `Token` → `TokenHash` and `ReplacedByToken` → `ReplacedByTokenHash`.
   - Add `static string Hash(string rawToken)` (SHA-256, Base64).
   - `Create(userId, rawToken, expiresAt)` stores the hash. The raw value is never persisted.
   - `RefreshTokenRepository.GetByTokenAsync(raw)` hashes before querying.
   - Update `RefreshTokenConfiguration` (column names, max length 64, unique index kept).
4. **Handler return type.** `LoginHandler` and `RefreshTokenHandler` return a new application-level `AuthTokens(AccessToken, RefreshToken, RefreshTokenExpiresAt)`. The controller needs the expiry to set the cookie, so the API layer never reads settings. `LoginResponse` shrinks to `(AccessToken)` and becomes the HTTP body only.
5. **`LogoutHandler`** (new, `Application/Users/Auth/`): takes the raw refresh token and revokes it. Idempotent: unknown, expired or already-revoked tokens are a no-op and never throw. Uses the existing `IRefreshTokenRepository.RevokeAsync`, which is currently unused. Register it in `DependencyInjection.cs` next to the other handlers.
6. **Migration:** `dotnet ef migrations add HashRefreshTokens ...` (column renames). Delete existing `refresh_tokens` rows by hand.
7. **Auth failures answer 401, not 404.** A wrong email/password in `LoginHandler` and an unknown, expired or revoked token in `RefreshTokenHandler` currently throw `NotFoundException` (404). Throw `UnauthorizedAccessException` instead. `ExceptionHandlingMiddleware` already maps it to 401 (and `FormsController` already throws it), so no new exception type or middleware change is needed. Messages stay generic and identical ("Invalid user or password.", "Invalid refresh token"), so nothing about which emails exist leaks. The unverified-email case stays a `ValidationException` (400, `EmailNotVerified`). This must ship together with the Stage 3 interceptor rewrite: the current interceptor treats every 401 as an expired session and redirects to `/login`, which would wipe the login error message.

**Tests (unit):**
- Update `RefreshTokenTests.cs` and `LoginTests.cs` for `AuthTokens`, the lifetime from `IJwtService`, hashing, and `UnauthorizedAccessException` in place of `NotFoundException` for bad credentials, and for an expired, revoked or unknown refresh token.
- New tests:
  - `Create` stores the hash and not the raw token.
  - Lookup by raw token works.
  - `LogoutHandler`: revokes an active token, and is a no-op for unknown or revoked tokens.

---

## Stage 2: Cookie-based auth endpoints (API)

1. **Cookie helper** in `FormAI.API/Auth/` (a small class holding the name `refresh_token` and set/clear methods) so the controller stays thin. Attributes: `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/auth` (sent only to refresh and logout), `Expires` = the token's `ExpiresAt`. Clearing must use the same `Path`.
2. **`AuthController`:**
   - `POST login`: run the handler, set the cookie, return `{ accessToken }` only.
   - `POST refresh`: read the cookie (missing → `401`), run the handler, rotate the cookie, return `{ accessToken }`. It no longer takes a body. Handler failures still surface through `ExceptionHandlingMiddleware`.
   - `POST logout` (new, `[AllowAnonymous]`): run `LogoutHandler`, clear the cookie, return `204`.
3. **CSRF:** `SameSite=Strict`, a path-scoped cookie and POST-only endpoints are enough. The response only carries an access token that a cross-site caller cannot read. No antiforgery tokens.
4. **CORS:** unchanged. Dev goes through the Vite proxy and prod will be same origin, so no `AllowCredentials`.
5. **Local `Secure` cookie:** Chrome and Firefox accept `Secure` cookies on `http://localhost`, so the flag is always on. Verify this in Stage 3. If Safari or the `Testing` environment is a problem, add a config flag rather than dropping it globally.

**Verification at this stage:** `dotnet run`, then log in through Swagger or curl and confirm the `Set-Cookie` attributes and that `/refresh` works with only the cookie.

---

## Stage 3: Frontend session handling

1. **`src/auth/tokenStore.ts`** (new): the in-memory access token, with `get`, `set` and `clear`, plus a helper that reads `exp` and the user claims from the JWT. This replaces the JWT decoding done inline in `LoginPage.tsx`.
2. **`src/api/axios.ts`:**
   - The request interceptor reads from `tokenStore`.
   - On a 401, refresh once and replay the request, but only when the failed request was sent **with an access token** and hasn't already been retried. A 401 on a request that carried no token (for example a wrong password on `/auth/login`) is not an expired session: pass the error through to the caller, with no refresh and no redirect.
   - The refresh call uses a **bare axios instance** (no interceptors) and posts with no body. The browser attaches the cookie. This is the only protection against a refresh loop: the refresh request's own 401 never re-enters the interceptor, so there is no URL check to keep in sync. Without it, a failing refresh would wait on itself and hang (or retry forever).
   - **One refresh at a time per tab** (a shared in-flight promise).
   - **One refresh at a time across tabs**, using `navigator.locks.request(...)` where available. The cookie is shared and rotation revokes the old token, so two tabs refreshing at once would otherwise log one of them out. After acquiring the lock, a tab sends whatever cookie is current. Fall back to per-tab single-flight if `navigator.locks` is missing (jsdom).
   - Any refresh failure (a 401 from the server, or a network error) clears the session and redirects to `/login`.
   - Export `getFreshAccessToken()` (refreshes if the token expires within ~30s) for SignalR.
3. **`AuthProvider` / `AuthContext`:**
   - `login(accessToken, user)` (drop the `refreshToken` parameter).
   - `logout()` becomes async: `POST /auth/logout`, clear state, redirect.
   - **Boot:** keep the non-secret `user` object in `localStorage` as a "probably signed in" hint. On load, if the hint exists and there is no in-memory token, call refresh once before rendering children. If it fails, drop the hint. Visitors without the hint (anonymous respondents) skip the call, so there is no noise or delay.
4. **`LoginPage.tsx`, `types/auth.ts`:** the login response is `{ accessToken }` only.
5. **`hooks/useFormResultsHub.ts`:** `accessTokenFactory` becomes `() => getFreshAccessToken()`. Open connections survive token expiry, but a reconnect after an hour needs a fresh token.
6. `utils/respondentToken.ts` is untouched (anonymous respondents are a separate mechanism).

**Tests (Vitest + MSW, colocated):**
- `axios.ts`:
  - 401, then refresh, then the original request is replayed with the new token.
  - Two parallel 401s cause exactly one refresh call.
  - A failed refresh clears the session and redirects.
  - A 401 from `/auth/login` (no token sent) triggers no refresh and no redirect, and the error reaches the login page.
  - A 401 from the refresh call itself (bare instance) ends in a clean logout, with no hang and no second refresh.
- `AuthProvider`: boots with the hint and refreshes; boots without the hint and does not call refresh; a failed boot refresh drops the hint.
- Update `LoginPage.test.tsx` for the new response shape.

**Tests (Playwright, `frontend/e2e/`):**
- After login, `context.cookies()` shows `refresh_token` as HttpOnly, `SameSite=Strict`, `Path=/api/auth`, and `document.cookie` does not expose it.
- Reload the page and the user stays signed in.
- Invalidate the in-memory access token (for example by waiting or shortening `Jwt__ExpiresInMinutes` for the test API) and confirm a protected page still loads.
- After logout, a `POST /auth/refresh` with the old cookie fails.
- `e2e/support/api.ts` only reads `accessToken` from the login body, so it keeps working.

---

## Stage 4: Production routing (Terraform, `infra/`)

Required for the cookie to work in prod. Cannot be exercised locally, so review with `terraform plan`.

1. `infra/modules/frontend/main.tf`: add an ALB origin (using `api_domain_name`, HTTPS only) and `ordered_cache_behavior` entries for `/api/*` and `/hubs/*`:
   - all HTTP methods allowed;
   - caching disabled;
   - an origin request policy that forwards cookies, `Authorization` and the query string (SignalR sends `?access_token=`);
   - WebSockets pass through.
2. **Important:** the distribution has a `custom_error_response` that turns every 404 into `index.html` with a 200. It applies to all origins, so it would turn the API's deliberate 404s into HTML. Replace it with a viewer-request CloudFront Function on the default (S3) behavior only, which rewrites extensionless paths to `/index.html`.
3. Pass `api_domain_name` into the frontend module from `infra/envs/prod/main.tf`.

---

## Stage 5: Docs

- **`CLAUDE.md`:** add an "Authentication" section: token lifetimes and where they are configured, rotation, hashing, the cookie attributes, and access token in memory only. Also update the `ExceptionHandlingMiddleware` line to say that bad credentials and invalid refresh tokens answer 401 via `UnauthorizedAccessException`.
- **`CONTEXT.md`:** define **access token**, **refresh token** and **session**.
- **New ADR (0006):** the refresh token is in an httpOnly cookie and the access token in memory, chosen over `localStorage`. Note the same-origin requirement and the CloudFront behaviors.
- **`docs/known-gaps.md`:**
  - Add: expired and revoked refresh-token rows are never deleted.
  - Add: no rate limit on login or refresh.
  - Add: no reuse detection, so a stolen rotated token isn't flagged.
  - Leave the `IsEmailVerified` note as it is (the refresh path deliberately doesn't check it).
- **`README.md`:** mention the new `Jwt` settings if it lists configuration.

---

## Files touched (representative)

- Backend: `Domain/Entities/RefreshToken.cs`, `Infrastructure/Security/{JwtSettings,JwtService}.cs`, `Infrastructure/Data/Configurations/RefreshTokenConfiguration.cs`, `Infrastructure/Repositories/RefreshTokenRepository.cs`, `Infrastructure/DependencyInjection.cs`, `Application/Interfaces/IJwtService.cs`, `Application/Users/Auth/*`, `API/Controllers/AuthController.cs`, `API/appsettings.json`, a new EF migration.
- Frontend: `src/api/axios.ts`, `src/context/{AuthProvider,AuthContext}.tsx`, `src/pages/LoginPage.tsx`, `src/types/auth.ts`, `src/hooks/useFormResultsHub.ts`, new `src/auth/tokenStore.ts`.
- Infra: `infra/modules/frontend/main.tf`, `infra/envs/prod/main.tf`.

## End-to-end verification

1. `dotnet build FormAI.sln` and `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj`.
2. `cd frontend && npm test`.
3. With Docker Compose up: `npm run test:e2e`.
4. Manual, in the browser:
   - Log in, then check DevTools → Application → Cookies: `refresh_token` is HttpOnly, Strict, path `/api/auth`, and `localStorage` has no tokens.
   - Reload and stay signed in.
   - Set `Jwt__ExpiresInMinutes=1`, wait, click around, and see one `/auth/refresh` call with no logout.
   - Open two tabs and let both expire, and neither gets logged out.
   - Log out and confirm the cookie is cleared.
   - Leave a Results tab open past expiry, drop the network briefly, and confirm SignalR reconnects.
5. `terraform plan` on `infra/envs/prod` for Stage 4.
