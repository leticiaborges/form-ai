# CLAUDE.md

Guidance for Claude Code (claude.ai/code) working in this repository.

**This file describes the system as it is actually built.** Anything described as missing, unused or provisional lives in [`docs/known-gaps.md`](./docs/known-gaps.md) — read it before assuming a feature exists. The vocabulary is defined in [`CONTEXT.md`](./CONTEXT.md); use those terms in code, comments and commit messages.

## Project overview

FormAI turns text supplied by a user into a question form. Claude generates the questions, the user edits them, and the form is answered through a shared link.

End to end today: register and verify an account → paste text and pick an expiry → Claude generates a draft → the owner edits, publishes and shares the link → respondents (signed in or anonymous) answer once → graded forms are scored → the owner sees a submission count on the dashboard and, in the form's Results tab, a Summary (answer distributions, score spread) and an Individual view (one submission at a time, with per-question correctness and score on a graded form).

## Vocabulary

Read [`CONTEXT.md`](./CONTEXT.md) before naming anything. Points worth repeating because the code still disagrees in places:

- A form is **published** or **private**. There is no draft, and no closing — a form stops accepting submissions when it **expires**.
- A form is **graded** or it is not (`IsGraded`). Only a graded form has points, an answer key and scores.
- **Answer key** (`IsCorrect`) is for options; **suggested answer** (`CorrectAnswer`) is for Text and Numeric questions. They are different things, and both exist only on a graded form.
- A **submission** is a whole pass through a form; an **answer** is one question within it. Never call either a "response" — that word is reserved for the `...Response` DTO suffix.

## Data model

Entities live in `FormAI.Domain/Entities`: `User`, `Form`, `FormQuestion`, `QuestionOption`, `Submission`, `Answer`, plus the supporting `FormSourceContent`, `RefreshToken` and `UserConfirmationToken`.

- The extracted source text is **not** on `Form` — it lives in `FormSourceContent`. Source files are never stored.
- `AnswerSelectedOption` (child of `Answer`) stores the option's **text**, not its id ([ADR 0001](./docs/adr/0001-selected-option-text-snapshot.md)).
- `QuestionOption.IsCorrect` is nullable — null means "not marked".
- Entities use private constructors plus a static `Create()` factory, and every property has a `private set`. EF Core hydrates through the private constructor. Mutation goes through explicit methods (`Update`, `SetOptions`, `ReplaceQuestions`, `ClearGradingIfUngraded`), never property assignment.

`FormAI.Domain/Scoring/` (`SubmissionScorer`, `ScoredAnswer`, `GradingFingerprint`) and `FormAI.Domain/Results/` (`FormResultsCalculator`, `FormResults`) hold the scoring and results rules as pure functions over entities — no EF, no DTOs, no async. Keep them that way; the Application layer decides what to expose.

## Business rules (as implemented)

Access and ownership:

- Every access check goes through one of two static methods on `FormAccessValidator` (`Application/Forms/Validation/`), and both **always throw `NotFoundException` → 404, never `ForbiddenException` → 403**. `ForbiddenException` is still mapped by `ExceptionHandlingMiddleware`, but nothing throws it. This is deliberate: every throw site uses the identical message, so a non-owner cannot tell "doesn't exist" from "exists but not yours", and cannot enumerate form ids by comparing messages.
- **`CheckOwnerAccess(form, requestingUserId)`** guards every owner-only endpoint (editor read and save, results, submissions, count, delete). It asks only `form.CreatedBy == requestingUserId`, regardless of public, private or expired.
- **`CheckUserAnswerAccess(form, requestingUserId)`** guards the answering path (`GET {id}/answer`, `POST {id}/submit`). It asks only `form.IsPublic` — **no ownership carve-out**, so the owner of a private form gets the same 404 as anyone else. It also rejects an expired form with a `ValidationException` on both read and submit, so nobody, owner included, can open an expired form through the answering link. The owner can still open it through the editor (`GET /api/forms/{id}`), which doesn't check expiry.
- **`GET /api/forms/{id}` and `GET /api/forms/{id}/results` are owner-only even when the form is published**, because they carry the answer key and suggested answers. Respondents read through `GET {id}/answer`, which leaves both out.
- Everything under `/api/forms` requires a token except `GET {id}/answer`, `GET {id}/my-submission` and `POST {id}/submit`, which are `[AllowAnonymous]`.

Submitting:

- An **expired form rejects new submissions** (`ExpiresAt` in the past).
- **One submission per respondent per form** — matched on `UserId` when signed in, otherwise on `RespondentToken` ([ADR 0003](./docs/adr/0003-respondent-token-identity.md)).
- An answer referencing a question that isn't on the form, or an option that isn't on the question, is rejected; a `Single` question accepts at most one option; a required question must be answered.
- Answers are validated as a set: every problem is collected into a `ValidationException` keyed by question id, not thrown on the first failure.

Forms and questions:

- `Title` is required, max 255 characters; `Description` max 1024.
- Option text is **required and unique within a question** (trimmed, case-insensitive), enforced by `QuestionOptionValidator` in the application layer only — there is no database constraint.
- Saving the editor diffs against what is stored and **preserves question and option ids** ([ADR 0002](./docs/adr/0002-id-preserving-editor-save.md)). Never regenerate them.
- Generated forms are created **private**, with `ShowResultsAfterSubmit` false unless the owner ticks it at creation on a graded form, and an expiry **7 days out** by default (chosen by the owner at creation). The owner may change the expiry at any time in the editor; it must always be in the future.
- `POST /api/forms/generate/text` is **rate limited per signed-in user** with ASP.NET Core's built-in limiter, as a **sliding window** (`RateLimiting:Generate` settings: `PermitLimit` 10, `WindowMinutes` 60, `SegmentsPerWindow` 6). Over the limit it answers 429 with the usual `{ message, errors, code }` body and **no `Retry-After` header**, because a sliding window gives the middleware no retry time to report. The limiter runs before the handler, so a request the handler then rejects with 400 still uses a permit. The counters live in the API process's memory, so the limit holds **per server instance** only (see [`docs/known-gaps.md`](./docs/known-gaps.md)). It is wired in `FormAI.API/RateLimiting/`, and `UseRateLimiter()` must stay after `UseAuthentication()` and `UseAuthorization()`, or every request lands in one partition.
- `SourceText` is capped at `GenerateFormHandler.MaxSourceTextLength` (30,000 characters), so a single call can't be made arbitrarily expensive; `CreateFormPage` mirrors the same number.

Grading and scoring:

- **The score reaches the respondent only when the owner allows it.** `ShowResultsAfterSubmit` can be true only on a graded form; when true, `SubmitFormHandler` returns `totalScore` and `maxScore`.
- **The owner sets points; the server validates them.** On a graded form `Points` are required and must be between `QuestionPointsValidator.MinPoints` (0) and `MaxPoints` (100) — a question with no points is rejected, not defaulted. Zero is allowed: part of the graded form but doesn't count.
- `Form.DefaultQuestionPoints` (1) is a starting value — on generation and when the owner ticks "Graded form" in the editor — not a rule.
- `Form.ClearGradingIfUngraded()` runs on generation and on every editor save. On an ungraded form it nulls every `IsCorrect`, `CorrectAnswer` and `Points`; on a graded form it does nothing.
- **Turning grading off is lossy and irreversible.** It nulls every `IsCorrect`, `CorrectAnswer` and `Points`, and the scores of submissions already made. Nothing warns the owner first.
- A graded form may be saved with questions that have no answer key. Those can never be earned and score 0.
- **Scores are derived, not snapshots** ([ADR 0004](./docs/adr/0004-scores-recomputed-from-current-form.md)). `SaveFormEditorHandler` compares a `GradingFingerprint` before and after the save and calls `RescoreFormSubmissionsHandler` when it differs, in the same transaction. The fingerprint covers only questions that already existed, so adding a question never rescores; deleting one does. Renaming an option does too, and silently changes who counts as correct.
- Scoring matches Single and Multiple answers on **option text**, not ids, so submitting and rescoring share one code path. Text is compared trimmed and case-insensitively; numeric suggested answers are parsed with `InvariantCulture`, never the machine's culture.
- A Single or Multiple question is all-or-nothing: the selected texts must equal the correct texts exactly. No partial credit.
- An unanswered question in a graded form scores 0, not null. `null` means "this form is not graded"; `0` means "graded, earned nothing".
- **Correctness is recomputed, never read back from a stored score.** `Answer.Score` is the question's points when right and 0 when wrong, so a 0-point question and a question with no answer key both store 0. Anything that needs to know whether an answer was right calls `SubmissionScorer.IsAnswerCorrect` — one definition, shared by submitting, rescoring and results.
- **Results count against the answers to a question, not the form's submissions.** A skipped question has no `Answer` row, so a form with 6 submissions can show `1/4 correct` on a question 2 people skipped. This deliberately disagrees with scoring, which treats unanswered as 0. A question card is about that question, so its own answers are its population.
- A green bar and a zero correct-count are **not** a contradiction: an option is marked correct because it is in the answer key, but an answer is correct only when the whole selection matches. Everyone can have picked a correct option and nobody can be correct.

## Architecture

Clean Architecture with strict layer boundaries. Dependency direction: `API → Application → Domain`, `Infrastructure → Application`.

```
FormAI.Domain         entities, enums, pure rules (Scoring/, Results/). No dependencies.
FormAI.Application    use cases, DTOs, interfaces. References only Domain.
FormAI.Infrastructure EF Core, repositories, Claude client, email, JWT, password hashing.
                      Implements Application's interfaces.
FormAI.API            controllers, exception middleware, DI entry point.
frontend/             React 19 + TypeScript + Vite + Tailwind.
```

Each use case is a folder named after the operation (`Forms/SaveFormEditor/`, `Submissions/SubmitForm/`) holding its request/response records and its handler. Handlers are plain classes registered in `Infrastructure/DependencyInjection.cs` — there is no MediatR.

**`FormAI.API/Hubs/` is a deliberate exception to that registration pattern.** `FormResultsHub` pushes a bare `ResultsUpdated(formId)` refetch signal to an owner's open Results tab when a submission comes in, over a Redis backplane so it works across API instances. Its DI lives in `Program.cs`, not `DependencyInjection.cs`, because `IHubContext<T>` needs the hosting framework only `FormAI.API` references; `SubmitFormHandler` reaches it through `IFormResultsNotifier` (declared in `Application/Interfaces/`). `JoinFormResults` uses `CheckOwnerAccess`, and authenticates via `?access_token=` because a browser WebSocket can't set an `Authorization` header. See [ADR 0005](./docs/adr/0005-realtime-results-via-signalr-redis.md).

`ExceptionHandlingMiddleware` maps `NotFoundException` → 404, `ForbiddenException` → 403 (unused, see above), `ValidationException` → 400, `UnauthorizedAccessException` → 401. Throw these from handlers instead of returning status codes from controllers. `ValidationException` serializes as `{ message, errors, code }` and comes in two shapes: a dictionary of per-field messages for a batch of problems collected together (`code` null), or a single message plus a `ValidationErrorCode` (`FormExpired`, `AlreadySubmitted`, `EmailNotVerified`, `GenericError`) for a standalone whole-request reason, never merged with field errors — the latter is what `CheckUserAnswerAccess` uses for an expired form. Bad login credentials (`LoginHandler`) and an unknown, expired or revoked refresh token (`RefreshTokenHandler`, and a missing cookie in `AuthController.Refresh`) throw `UnauthorizedAccessException` → 401, with the same generic message regardless of cause, so nothing about which emails exist or which tokens are valid leaks.

EF Core uses snake_case naming (`UseSnakeCaseNamingConvention`); mappings live in `Infrastructure/Data/Configurations`.

### Authentication

The access token (`Jwt:ExpiresInMinutes`, default 60) lives only in memory on the frontend, never in `localStorage`. The refresh token (`Jwt:RefreshTokenExpiryDays`, default 7) lives in an `HttpOnly; Secure; SameSite=Strict` cookie scoped to `Path=/api/auth`, is stored hashed, and rotates on every use — `POST /api/auth/refresh` issues a new pair and revokes the old token; `POST /api/auth/logout` revokes it and clears the cookie. `SameSite=Strict` requires the frontend and API to share an origin in production (see [ADR 0006](./docs/adr/0006-refresh-token-httponly-cookie.md)).

### AI integration

`IFormGenerationService` (`Application/AI/`) is the boundary; `ClaudeFormGenerationService` is the only class that calls the Anthropic API. `GenerationParameters.IncludeCorrectAnswers` is fed from the new form's `IsGraded` — asking Claude for an answer key is the AI-side name for the same decision. Claude is never asked for points: what a question is worth is the owner's decision. The prompt template is `Infrastructure/AI/Prompt/PromptGenerateForm.txt`. `IAnalysisService` is declared but has no implementation.

## Commands

```bash
dotnet build FormAI.sln
dotnet run --project src/FormAI.API          # http://localhost:5155, Swagger at /swagger
dotnet test FormAI.sln

cd frontend && npm run dev                   # http://localhost:5173

# EF Core migrations (run from repo root)
dotnet ef migrations add <MigrationName> --project src/FormAI.Infrastructure --startup-project src/FormAI.API
dotnet ef database update --project src/FormAI.Infrastructure --startup-project src/FormAI.API --connection "Host=localhost;Database=form_ai;Username=form_ai_migrator;Password=<MIGRATOR_DB_PASSWORD>"
```

There are two database roles, created by `docker/postgres/init/01-create-app-user.sh` (Docker init, CI, and in production a one-off ECS task — the same script everywhere). `form_ai_migrator` owns the database and schema and is the only role that runs DDL; `form_ai_app` is what the API connects as and gets `SELECT/INSERT/UPDATE/DELETE` only, so `dotnet ef database update` needs the `--connection` above. The application never applies migrations at startup: `deploy.yml` builds an EF Core migrations bundle image (`src/FormAI.Infrastructure/Dockerfile.migrator`) and runs it as a one-off ECS task before the API rolls out, and a failed migration stops the deploy ([ADR 0007](./docs/adr/0007-migrations-run-as-a-separate-role-in-a-deploy-job.md)). The EF tools and the bundle build `AppDbContext` through `AppDbContextFactory` (`Infrastructure/Data/`), not the API host (whose `Program.cs` throws without `ConnectionStrings:Redis`), so they need no runtime settings. Both go through `AppDbContextOptions.UseAppDatabase`, the single place that says how the context talks to PostgreSQL — change database options there, not in `AddInfrastructure` or the factory.

Local infrastructure is Docker Compose: PostgreSQL, Mailpit (web inbox at http://localhost:8025, receives all local email) and Redis (the SignalR backplane — no auth, no volume; it's a pure pub/sub relay, so losing it on restart is fine).

Configuration keys are in `appsettings.json`; local values go in `appsettings.Development.json`, which is untracked — keep it that way — or in user-secrets.

## Tests

### Backend

- `FormAI.UnitTests` — xUnit + NSubstitute, references Domain + Application. The scoring and results tests (`Results/`) run against entities built by the real `Create()` factories — nothing to stub there, keep it that way. Handler-level tests stub their interface dependencies (`IFormRepository`, `IFormResultsNotifier`, ...) with `Substitute.For<T>()`, since a use-case's collaborators are side-effecting infrastructure, not pure Domain logic.
- `FormAI.IntegrationTests` — `WebApplicationFactory` plus PostgreSQL via Testcontainers. Contains `UserRepositoryTests` only.

`dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj` runs the unit tests alone, without Docker. `--filter` applied to the whole solution reports "No test matches" for every project with no match, which is not a failure.

### Frontend

Two layers, run separately from `frontend/`:

- **Vitest** (`npm test`, also `test:watch` and `test:coverage`) — component and page tests in jsdom with Testing Library. Tests are colocated as `*.test.tsx` next to the code they cover. The network is stubbed with MSW: `src/test/server.ts` has no default handlers and unhandled requests error, so each test declares exactly the requests it expects. `src/test/renderWithProviders.tsx` wraps a component in the app's providers.
- **Playwright** (`npm run test:e2e`, or `test:e2e:ui`) — end-to-end specs in `frontend/e2e/`, excluded from Vitest. `playwright.config.ts` starts its own API (`Testing` environment) and Vite dev server on separate ports, so it needs Docker Compose's PostgreSQL, Redis and Mailpit running, plus `JWT_SECRET` and `APP_DB_PASSWORD` (or `E2E_DB_CONNECTION`) in the repo-root `.env`; the API runs as `form_ai_app`, and the `form_ai_e2e` database must already be migrated as `form_ai_migrator`. Specs seed forms through the real HTTP API via `e2e/support/api.ts`, and some assert over HTTP alone rather than in the browser.

Use Vitest for behaviour a component can prove with the network stubbed; use Playwright only for flows that must cross the real API and database.

## Keeping the docs true

When working in this repo:

- Introducing or renaming a domain term? Update [`CONTEXT.md`](./CONTEXT.md) in the same change, and use the canonical term in the code.
- Making a decision that is hard to reverse, surprising to a future reader, and chosen over a real alternative? Propose an ADR in `docs/adr/` (sequential numbering, a paragraph is enough).
- Building something listed in [`docs/known-gaps.md`](./docs/known-gaps.md), or finding a new gap? Update that file in the same change.
- Changing a business rule? Update the "Business rules" section above. A rule documented here that the code no longer enforces is worse than no documentation.

`docs/plans/` holds historical phase plans written before the code existed. **They are not maintained and do not describe current behaviour** — do not use them as a source of truth.
