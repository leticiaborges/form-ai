---
status: accepted
---

# The refresh token lives in an HttpOnly cookie, the access token in memory

The refresh token travels only in an `HttpOnly; Secure; SameSite=Strict` cookie, scoped with `Path=/api/auth` so it is never sent to the rest of the API. The access token stays in memory only (`src/auth/tokenStore.ts`), not `localStorage`. This was chosen over the simpler option — both tokens in `localStorage`, as the code already had — because `localStorage` is readable by any script running on the page: a single XSS bug anywhere in the frontend (a dependency, a rendered user string, a third-party widget) would hand an attacker a long-lived refresh token, not just the short-lived access token already at risk. An `HttpOnly` cookie removes that token from JavaScript's reach entirely, at the cost of `SameSite=Strict` requiring the frontend and API to share an origin — the browser will not attach the cookie across sites.

Production has no separate API domain today, but the frontend is served from S3 behind CloudFront and the API from an ALB behind a different hostname. Making the cookie work means CloudFront must front both under one origin, routing `/api/*` and `/hubs/*` to the ALB (all methods, caching disabled, cookies and `Authorization` forwarded, WebSockets passed through) and leaving everything else on the S3 behavior. Without that routing change, `SameSite=Strict` silently drops the cookie in production and refresh never fires — this ADR and the CloudFront change are not separable.

Refresh tokens are also now stored hashed (`RefreshToken.TokenHash`, SHA-256), not in plaintext, so a database read no longer hands out live tokens. Rotation (issue a new token, revoke the old one) and hashing are both changes to the same entity and shipped in the same migration.

## Consequences

- **Cross-site refresh is now impossible by construction**, not by server-side validation — a request from another origin simply arrives with no cookie. This is the main reason `SameSite=Strict` and a path-scoped cookie were judged sufficient CSRF protection with no antiforgery token: the refresh and logout endpoints are POST-only and the response carries only an access token a cross-site page cannot read anyway.
- **Local development still uses `Secure` cookies over plain `http://localhost`.** Chrome and Firefox accept this as a documented localhost exception; if Safari or a CI environment turns out not to, the fix is a config flag, not dropping `Secure` globally — nothing has needed that flag yet.
- **The access token, not just the refresh token, still has to be handled carefully in memory.** Keeping it out of `localStorage` closes the persistent-theft window but doesn't remove the token from the page's own JavaScript context — a live XSS payload can still read `tokenStore` while the tab is open. The real mitigation for that is not reading untrusted content into the DOM in the first place; this change narrows the blast radius of a slip, it doesn't eliminate it.
- **A tab that boots signed in makes one extra round trip.** The non-secret `user` hint in `localStorage` triggers a `/auth/refresh` call before children render, so an already-expired hint (or one from a revoked session) costs a request before the app knows to show the login page. Anonymous visitors skip this entirely — no hint, no call.

## Status

Accepted and implemented — `RefreshTokenCookie`, `AuthController`, `LogoutHandler`, `RefreshToken.TokenHash`/`Revoke` in the backend; `src/auth/tokenStore.ts`, `src/api/axios.ts`'s refresh-and-replay interceptor, and `AuthProvider`'s boot-time refresh on the frontend; the CloudFront routing change in `infra/modules/frontend/main.tf`.
