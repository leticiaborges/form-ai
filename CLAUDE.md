# CLAUDE.md

Guidance for Claude Code working in this repository. It describes the system **as built**. Missing, unused or provisional things live in [`docs/known-gaps.md`](./docs/known-gaps.md) — read it before assuming a feature exists. Vocabulary is in [`CONTEXT.md`](./CONTEXT.md); use those terms in code, comments and commits.

## Overview

FormAI turns pasted text or an uploaded file into a question form: register and verify → paste text and/or attach one file (`.pdf`, `.docx`, `.pptx`, `.txt`; images are not supported), pick an expiry → Claude generates a draft → the owner edits, publishes and shares the link → respondents (signed in or anonymous) answer once → graded forms are scored → the owner sees results (Summary and Individual views) in the form's Results tab.

## Vocabulary

- A form is **published** or **private**. No draft, no closing: it stops accepting submissions when it **expires**.
- **Graded** (`IsGraded`) forms alone have points, an answer key and scores.
- **Answer key** (`IsCorrect`) is for options; **suggested answer** (`CorrectAnswer`) is for Text and Numeric. Both exist only on a graded form.
- A **submission** is a whole pass through a form; an **answer** is one question in it. Never call either a "response" — that is reserved for the `...Response` DTO suffix.

## Data model

Entities (`FormAI.Domain/Entities`): `User`, `Form`, `FormQuestion`, `QuestionOption`, `Submission`, `Answer`, `FormSourceContent`, `RefreshToken`, `UserConfirmationToken`.

- Source text lives in `FormSourceContent`, not on `Form`. Source files are never stored. For pasted text, `.txt`, `.docx` and `.pptx` the extracted text is saved; a PDF is sent to the model in memory and its row has **empty `Content`** and only the file name ([ADR 0009](./docs/adr/0009-source-content-policy.md)), so code reading `FormSourceContent` must expect that for `SourceType.Pdf`. Rows carry their own `SourceType`; `Form.SourceType` is the file's type, or `Text`.
- `AnswerSelectedOption` stores the option's **text**, not its id ([ADR 0001](./docs/adr/0001-selected-option-text-snapshot.md)).
- `QuestionOption.IsCorrect` null means "not marked".
- Private constructors, a static `Create()` factory and `private set` everywhere. Mutate through methods (`Update`, `SetOptions`, `ReplaceQuestions`, `ClearGradingIfUngraded`), never property assignment.
- `Domain/Scoring/` and `Domain/Results/` are pure functions over entities: no EF, DTOs or async. Keep them that way.

## Business rules

**Access**

- Access checks go through the two static methods of `FormAccessValidator` and **always throw `NotFoundException` (404), never `ForbiddenException` (403)**, with one identical message, so a non-owner can't tell "missing" from "not yours". `ForbiddenException` is mapped but nothing throws it.
- `CheckOwnerAccess` (`form.CreatedBy == userId`, any state) guards every owner-only endpoint: editor read/save, results, submissions, count, delete.
- `CheckUserAnswerAccess` (`form.IsPublic` only, **no owner carve-out**) guards `GET {id}/answer` and `POST {id}/submit`. It also rejects an expired form with a `ValidationException`, owner included. The owner can still open an expired form in the editor (`GET /api/forms/{id}`).
- `GET /api/forms/{id}` and `.../results` are owner-only even when published, because they carry the answer key. Respondents use `GET {id}/answer`, which omits it.
- `POST /api/auth/login` and `POST /api/auth/register` are rate limited per IP (`RateLimiting:Login`: 10 per 15 min; `RateLimiting:Register`: 5 per 60 min). Every attempt spends a permit, successful or not. Counters are in process memory, so the limit is per instance.
- Everything under `/api/forms` needs a token except `GET {id}/answer`, `GET {id}/my-submission` and `POST {id}/submit` (`[AllowAnonymous]`).
- `POST /api/auth/demo` is anonymous and creates a demo account (`User.CreateDemo`, verified, `demo-<guid>@demo.invalid`, shared `Demo:Password`). The password is never in `appsettings.json` (untracked `appsettings.Development.json` locally, an ECS secret in production); blank answers 404 and creates nothing. Rate limited per IP (`RateLimiting:Demo`, 3 per 15 min). The token carries `is_demo: "true"`, which drives the frontend banner.

**Submitting**

- An expired form rejects new submissions.
- One submission per respondent per form: matched on `UserId` when signed in, else `RespondentToken` ([ADR 0003](./docs/adr/0003-respondent-token-identity.md)).
- Rejected: an unknown question, an option not on the question, more than one option on `Single`, an unanswered required question. Problems are collected into one `ValidationException` keyed by question id.

**Forms and questions**

- `Title` required, max 255; `Description` max 1024.
- Option text is required and unique per question (trimmed, case-insensitive), enforced by `QuestionOptionValidator` only, no DB constraint.
- The editor save diffs against what is stored and **preserves question and option ids** ([ADR 0002](./docs/adr/0002-id-preserving-editor-save.md)). Never regenerate them.
- Generated forms are **private**, expiry **7 days out** by default (owner's choice at creation, changeable later, always in the future), `ShowResultsAfterSubmit` false unless ticked at creation on a graded form.
- `POST /api/forms/generate` takes **`multipart/form-data` only** (JSON answers 415). The optional `file` field takes **one** file: `.pdf`, `.docx`, `.pptx` or `.txt`, at most 10 MB (`FormSourceContent.MaxFileBytes`); **images are not supported**. Pasted text and a file are two parts of the same source material. The frontend picker is `SourceFilePicker`, its limits live in `frontend/src/utils/sourceFile.ts` (mirrored from the backend, change both together) and error codes map to messages in `frontend/src/utils/generationErrors.ts`.
- That route is **rate limited per user**: ASP.NET sliding window (`RateLimiting:Generate`: 10 per 60 min, 6 segments). Counters are in process memory, so the limit is **per instance**.
- Source text (pasted text and extracted file text together) is capped at `FormSourceContent.MaxSourceTextLength` (100,000, `SourceTextTooLong`) and needs at least `GenerateFormHandler.MinSourceTextLength` (100, `SourceTextTooShort`). **A PDF skips the minimum**, since the model reads it directly. The client skips the minimum whenever a file is attached, because only the server knows how much text a `.txt/.docx/.pptx` holds.

**Grading and scoring**

- The score reaches the respondent only if the owner allows it: `ShowResultsAfterSubmit` can be true only on a graded form, and then `SubmitFormHandler` returns `totalScore` and `maxScore`.
- The owner sets points; on a graded form they are required, between `QuestionPointsValidator.MinPoints` (0) and `MaxPoints` (100), never defaulted. `Form.DefaultQuestionPoints` (1) is only a starting value (generation, ticking "Graded form").
- `Form.ClearGradingIfUngraded()` runs on generation and every editor save: on an ungraded form it nulls `IsCorrect`, `CorrectAnswer` and `Points`.
- **Turning grading off is lossy and irreversible** (it also nulls existing submissions' scores) and nothing warns the owner.
- A graded question with no answer key can't be earned and scores 0.
- **Scores are derived, not snapshots** ([ADR 0004](./docs/adr/0004-scores-recomputed-from-current-form.md)). `SaveFormEditorHandler` compares a `GradingFingerprint` before and after the save and runs `RescoreFormSubmissionsHandler` in the same transaction when it differs. The fingerprint covers only pre-existing questions: adding one never rescores; deleting one or renaming an option does.
- Single/Multiple match on **option text** (trimmed, case-insensitive), not ids. Numeric answers parse with `InvariantCulture`. Single/Multiple are all-or-nothing, no partial credit.
- Unanswered on a graded form scores 0; `null` means "not graded".
- **Correctness is recomputed, never read from a stored score** (`Answer.Score` is 0 for a wrong answer, a 0-point question and an unkeyed question alike). Always use `SubmissionScorer.IsAnswerCorrect`, shared by submitting, rescoring and results.
- **Results count against the answers to a question, not the form's submissions**: a skipped question has no `Answer` row, so `1/4 correct` on a form with 6 submissions is valid. This deliberately differs from scoring.
- A green bar with a zero correct-count is not a contradiction: an option is in the key, but an answer is correct only if the whole selection matches.

## Architecture

Dependencies: `API → Application → Domain`, `Infrastructure → Application`.

```
FormAI.Domain         entities, enums, pure rules. No dependencies.
FormAI.Application    use cases, DTOs, interfaces. References only Domain.
FormAI.Infrastructure EF Core, repositories, AI client, email, JWT, hashing.
FormAI.API            controllers, request contracts, middleware, DI entry point.
frontend/             React 19 + TypeScript + Vite + Tailwind.
```

Each use case is a folder (`Forms/SaveFormEditor/`) with its request/response records and handler. Handlers are plain classes registered in `Infrastructure/DependencyInjection.cs`; no MediatR.

**Exception: `FormAI.API/Hubs/`.** `FormResultsHub` pushes a bare `ResultsUpdated(formId)` signal to the owner's open Results tab over a Redis backplane. Its DI is in `Program.cs` (`IHubContext<T>` needs the hosting framework); `SubmitFormHandler` reaches it via `IFormResultsNotifier`. `JoinFormResults` uses `CheckOwnerAccess` and authenticates with `?access_token=` (a browser WebSocket can't set `Authorization`). [ADR 0005](./docs/adr/0005-realtime-results-via-signalr-redis.md).

**Errors.** Throw from handlers; `ExceptionHandlingMiddleware` maps them: `NotFoundException` 404, `ValidationException` 400, `UnauthorizedAccessException` 401, `ForbiddenException` 403 (unused), `GenerationException` 502 for `GenerationOutputInvalid` and 503 for `GenerationUnavailable` / `GenerationBudgetReached` (declared, not yet thrown). Bodies are `{ message, errors, code }`.

- `ValidationException` has two shapes: per-field messages for a batch (`code` null), or one message plus a `ValidationErrorCode` (`FormExpired`, `AlreadySubmitted`, `EmailNotVerified`, `GenericError`) for a whole-request reason, never merged with field errors.
- `GenerationException` (message plus code, `errors` null) is for failures that aren't the caller's fault. It is logged as a warning with the inner exception; the user-facing message stays generic.
- Bad login credentials and an unknown, expired or revoked refresh token (including a missing cookie) throw `UnauthorizedAccessException` with one generic message, so nothing leaks about which emails or tokens exist.

EF Core uses snake_case (`UseSnakeCaseNamingConvention`); mappings are in `Infrastructure/Data/Configurations`.

### Authentication

The access token (`Jwt:ExpiresInMinutes`, 60) lives only in frontend memory, never `localStorage`. The refresh token (`Jwt:RefreshTokenExpiryDays`, 7) is an `HttpOnly; Secure; SameSite=Strict` cookie on `Path=/api/auth`, stored hashed, rotated on every use (`POST /api/auth/refresh`; `POST /api/auth/logout` revokes it). `SameSite=Strict` needs the frontend and API on one origin in production ([ADR 0006](./docs/adr/0006-refresh-token-httponly-cookie.md)).

### AI integration

`IFormGenerationService` (`Application/AI/`) is the boundary. `GatewayFormGenerationService` is the only class that calls a model, through the LiteLLM gateway's OpenAI-compatible API (`Ai:GatewayUrl`, `Ai:ApiKey`, `Ai:TimeoutSeconds`). It sends the user's guid as `user`, never an email or name.

- `GenerationParameters.IncludeCorrectAnswers` comes from the form's `IsGraded`. Claude is never asked for points.
- The prompt is `Infrastructure/AI/Prompt/PromptGenerateForm.txt`. It doesn't describe the JSON shape: the request carries a strict schema (`response_format`, built per request in `FormAISchema`). `GatewayFormGenerationService.PromptVersion` identifies the revision.
- `GeneratedFormAIJSONParser` (Infrastructure) only reads the reply: has choices, `finish_reason` not `length`/`content_filter`, has content, deserializes. It knows no limits.
- `GeneratedQuestionsValidator` (Application, `Forms/Validation/`, called by `GenerateFormHandler` before the form is built) applies what a schema can't: at least one question, valid type, non-empty text, strings at most 1024 characters (rejected, not truncated), 2+ options on Single/Multiple, none on Text/Numeric, plus `QuestionOptionValidator`.
- Both throw `GenerationException` / `GenerationOutputInvalid` (502): a bad draft is a model fault, never a 400. It still spends a rate-limit permit.
- A non-2xx, a timeout or a connection error is `GenerationUnavailable` (503). The client never retries; a caller's cancellation propagates as cancellation.
- `GenerateAsync` takes a list of `GenerationSource` (`TextSource`, at most one `PdfSource`). With a PDF the service uses `Ai:VisionAlias` (`form-generator-vision`), else `Ai:TextAlias` (`form-generator`). The PDF goes as a `file` part named `source.pdf` (the user's file name never reaches the gateway); only text is fenced as untrusted.
- Text is extracted from `.txt`, `.docx` and `.pptx` by `ISourceTextExtractor` (Infrastructure) before generation.
- `IAnalysisService` is declared with no implementation.

## Commands

Setup, running the app, local infrastructure and the configuration keys are in [`README.md`](./README.md); don't duplicate them here. The ones you will use most:

```bash
dotnet build FormAI.sln
dotnet test FormAI.sln

# EF Core migrations (from repo root)
dotnet ef migrations add <MigrationName> --project src/FormAI.Infrastructure --startup-project src/FormAI.API
dotnet ef database update --project src/FormAI.Infrastructure --startup-project src/FormAI.API --connection "Host=localhost;Database=form_ai;Username=form_ai_migrator;Password=<MIGRATOR_DB_PASSWORD>"
```

**Database roles** (`docker/postgres/init/01-create-app-user.sh`, the same script in Docker, CI and production): `form_ai_migrator` owns the schema and is the only role that runs DDL; `form_ai_app` (what the API uses) gets `SELECT/INSERT/UPDATE/DELETE` only. That's why `database update` needs `--connection`. The app never migrates at startup: `deploy.yml` runs an EF migrations bundle (`Dockerfile.migrator`) as a one-off ECS task before the API rolls out, and a failed migration stops the deploy ([ADR 0007](./docs/adr/0007-migrations-run-as-a-separate-role-in-a-deploy-job.md)). EF tools and the bundle build `AppDbContext` through `AppDbContextFactory`, not the API host, so they need no runtime settings. Database options belong in `AppDbContextOptions.UseAppDatabase`, nowhere else.

**Local infrastructure** is Docker Compose (PostgreSQL, Mailpit, Redis, the LiteLLM gateway); see the README. Without the real gateway, `frontend/e2e/support/fake-gateway.mjs` answers by a marker in the source text: `[fake:down]` (503), `[fake:slow]` (never answers), `[fake:truncated]` and `[fake:invalid]` (502).

**Production AI gateway** is LiteLLM on ECS Fargate, in its own Terraform root (`infra/envs/ai-gateway`, apply `prod` first), reached by the API over private DNS and never through the ALB. Its database is separate from the app's (`form_ai_app` and `form_ai_migrator` have no access to it). The gateway stores prompts and responses in its spend log, so user source text lives in the `litellm` database, not in the app's. Details: `docs/deployment/` and `docker/litellm/README.md`.

Config keys are in `appsettings.json`; local values go in the **untracked** `appsettings.Development.json` or user-secrets. Never commit secrets.

## Tests

**Backend**

- `FormAI.UnitTests` — xUnit + NSubstitute, references Domain + Application. Scoring and results tests use real `Create()` factories, nothing stubbed. Handler tests stub interface dependencies with `Substitute.For<T>()`.
- `FormAI.IntegrationTests` — tests that need real infrastructure (PostgreSQL via Testcontainers, `WebApplicationFactory`) and so need Docker. Use it for repositories and wiring, not for rules that a unit test can cover.
- `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj` runs without Docker. A solution-wide `--filter` printing "No test matches" for projects with no match is not a failure.

**Frontend** (run from `frontend/`)

- **Vitest** (`npm test`) — jsdom + Testing Library, colocated `*.test.tsx`. MSW stubs the network; `src/test/server.ts` has no default handlers and unhandled requests error, so each test declares its requests. `src/test/renderWithProviders.tsx` wraps the app's providers.
- **Playwright** (`npm run test:e2e`) — specs in `frontend/e2e/`. It starts its own API (`Testing` environment) and Vite server against a separate `form_ai_e2e` database; setup is in the README. Specs seed through the real HTTP API (`e2e/support/api.ts`).

Use Vitest for what a component proves with the network stubbed; Playwright only for flows that must cross the real API and database.

## Keeping the docs true

- New or renamed domain term → update `CONTEXT.md` in the same change.
- Hard-to-reverse, surprising decision chosen over a real alternative → propose an ADR in `docs/adr/` (sequential, a paragraph is enough).
- Building or finding something in `docs/known-gaps.md` → update it in the same change.
- Changing a business rule → update "Business rules" above. A rule the code no longer enforces is worse than none.
