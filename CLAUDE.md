# CLAUDE.md

Guidance for Claude Code (claude.ai/code) working in this repository.

**This file describes the system as it is actually built.** Anything described as missing, unused or provisional lives in [`docs/known-gaps.md`](./docs/known-gaps.md) — read it before assuming a feature exists. The vocabulary is defined in [`CONTEXT.md`](./CONTEXT.md); use those terms in code, comments and commit messages.

## Project overview

FormAI turns text supplied by a user into a question form. Claude generates the questions, the user edits them, and the form is answered through a shared link.

**What works end to end today:** register and verify an account → paste text → Claude generates a draft set of questions → edit, reorder, add and delete questions and options in the form editor → publish the form → share the link → respondents (signed in or anonymous) answer once → the owner sees a submission count on the dashboard.

## Vocabulary

Read [`CONTEXT.md`](./CONTEXT.md) before naming anything. Points worth repeating because the code still disagrees in places:

- A form is **published** or **private**. There is no draft, and no closing — a form stops accepting submissions when it **expires**.
- **Answer key** (`IsCorrect`) is for options; **suggested answer** (`CorrectAnswer`) is for Text and Numeric questions. They are different things.
- A **submission** is a whole pass through a form; an **answer** is one question within it. Never call either a "response" — that word is reserved for the `...Response` DTO suffix.

## Data model

Ten entities in `FormAI.Domain/Entities`. Six are the domain proper:

- **User** — account with hashed password and an email-confirmation state
- **Form** — `Title`, `Description`, `CreatedBy`, `SourceType`, `IsPublic`, `ExpiresAt`, `ShowResultsAfterSubmit`, `CreatedAt`. Owns its questions, submissions and source contents. The extracted text is **not** on `Form` — it lives in `FormSourceContent`
- **FormQuestion** — belongs to a Form; `Type` (`Single`, `Multiple`, `Text`, `Numeric`), `Order`, `IsRequired`, `Points`, `CorrectAnswer`, `AiGenerated`
- **QuestionOption** — a choice for a `Single`/`Multiple` question; `Text`, `Order`, `IsCorrect` (nullable — null means "not marked")
- **Submission** — one respondent's pass through a form; `UserId` (null when anonymous), `RespondentToken`, `IpAddress`, `SubmittedAt`, `Score`
- **Answer** — one row per answered question; holds `TextValue`, `NumericValue`, `Score`, and its selected options in the child table **AnswerSelectedOption**, which stores the option's **text**, not its id ([ADR 0001](./docs/adr/0001-selected-option-text-snapshot.md))

The rest are supporting: **FormSourceContent** (extracted text per source, with `SourceType`, `FileName`, `Order`), **RefreshToken**, **UserConfirmationToken**.

Entities use private constructors plus a static `Create()` factory, and every property has a `private set`. EF Core hydrates through the private constructor. Mutation goes through explicit methods (`Update`, `SetOptions`, `ReplaceQuestions`), never property assignment.

## Business rules (as implemented)

Access and ownership:

- A **private form is invisible to everyone but its owner** — viewing, answering and submitting all check `!IsPublic && CreatedBy != requestingUserId` and throw `NotFoundException`, so the client sees **404, not 403**. This applies to signed-in users too, not only anonymous ones.
- Only the owner may edit, update, delete or expire a form (`ForbiddenException` → 403 when the form is visible but not yours).
- Everything under `/api/forms` requires a token except `GET {id}/answer`, `GET {id}/my-submission` and `POST {id}/submit`, which are `[AllowAnonymous]`.

Submitting:

- An **expired form rejects new submissions** (`ExpiresAt` in the past). It can still be read.
- **One submission per respondent per form** — matched on `UserId` when signed in, otherwise on `RespondentToken` ([ADR 0003](./docs/adr/0003-respondent-token-identity.md)).
- An answer referencing a question that isn't on the form, or an option that isn't on the question, is rejected; a `Single` question accepts at most one option; a required question must be answered.
- Answers are validated as a set: every problem is collected into a `ValidationException` keyed by question id, not thrown on the first failure.

Forms and questions:

- `Title` is required, max 255 characters; `Description` max 1024.
- Option text is **required and unique within a question** (trimmed, case-insensitive), enforced by `QuestionOptionValidator` in the application layer only — there is no database constraint.
- Saving the editor diffs against what is stored and **preserves question and option ids** ([ADR 0002](./docs/adr/0002-id-preserving-editor-save.md)). Never regenerate them.
- Generated forms are created **private**, with `ShowResultsAfterSubmit = false` and an expiry **15 days out**.
- Source files are never stored — only the extracted text, in `FormSourceContent`.

## Architecture

Clean Architecture with strict layer boundaries. Dependency direction: `API → Application → Domain`, `Infrastructure → Application`.

```
FormAI.Domain        entities, enums. Zero dependencies, no framework references.
FormAI.Application   use cases, DTOs, interfaces (IFormRepository, IFormGenerationService, ...).
                     References only Domain. Never references Infrastructure.
FormAI.Infrastructure EF Core (AppDbContext), repositories, ClaudeFormGenerationService,
                     email, JWT and password hashing. Implements Application's interfaces.
FormAI.API           controllers, exception middleware, DI entry point.
frontend/            React 19 + TypeScript + Vite + Tailwind.
```

Each use case is a folder named after the operation (`Forms/SaveFormEditor/`, `Submissions/SubmitForm/`, `Users/Auth/`) holding its request/response records and its handler. Handlers are plain classes registered in `Infrastructure/DependencyInjection.cs` — there is no MediatR.

`ExceptionHandlingMiddleware` maps `NotFoundException` → 404, `ForbiddenException` → 403, `ValidationException` → 400 with a per-field error dictionary. Throw these from handlers instead of returning status codes from controllers.

EF Core uses snake_case naming (`UseSnakeCaseNamingConvention`), so `AnswerSelectedOption.OptionText` is `answer_selected_options.option_text`. Mappings live in `Infrastructure/Data/Configurations`.

### API surface

| Endpoint | Notes |
|---|---|
| `POST /api/auth/register` · `login` · `refresh` · `verify-email` | JWT + refresh token; registration sends a confirmation email |
| `POST /api/forms` · `GET /api/forms` · `GET/PUT/DELETE /api/forms/{id}` | Owner-only |
| `PUT /api/forms/{id}/editor` | The editor save — diffed, id-preserving |
| `POST /api/forms/generate/text` | The only generation endpoint |
| `GET /api/forms/{id}/submissions/count` | Owner-only; the only results endpoint that exists |
| `GET /api/forms/{formId}/answer` · `my-submission` · `POST submit` | Anonymous-friendly |
| `PATCH /api/forms/{id}/close` | ⚠️ Unused by the UI and slated for removal — see known gaps |

### AI integration

`IFormGenerationService` in `Application/AI/` is the boundary; `ClaudeFormGenerationService` in Infrastructure is the only class that calls the Anthropic API, via a named `HttpClient` ("claude") pointed at `https://api.anthropic.com/`. `GenerationParameters` carries `QuestionCount`, `AllowedTypes`, `DifficultyLevel` and `IncludeCorrectAnswers` — there is no free-text context parameter. The system prompt is a template at `Infrastructure/AI/Prompt/PromptGenerateForm.txt` with placeholders filled at request time; Claude returns JSON that is parsed into `GeneratedQuestion`/`GeneratedOption` and mapped to entities by `GenerateFormHandler`. Model and token limit come from the `Claude` configuration section.

`IAnalysisService` is declared but has no implementation.

## Commands

```bash
# Build entire solution
dotnet build FormAI.sln

# Run API (http://localhost:5155, Swagger at /swagger)
dotnet run --project src/FormAI.API

# Run all tests
dotnet test FormAI.sln

# Frontend (http://localhost:5173)
cd frontend && npm run dev

# EF Core migrations (run from repo root)
dotnet ef migrations add <MigrationName> --project src/FormAI.Infrastructure --startup-project src/FormAI.API
dotnet ef database update --project src/FormAI.Infrastructure --startup-project src/FormAI.API
```

Local infrastructure is Docker Compose: PostgreSQL and Mailpit (web inbox at http://localhost:8025, which receives all local email).

## Configuration

| Key | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string |
| `Jwt__Secret` / `Jwt__Issuer` / `Jwt__Audience` | JWT signing and validation |
| `Claude__ApiKey` | Anthropic API key (`Claude__Model`, `Claude__MaxTokens` have defaults in `appsettings.json`) |
| `Email__SmtpHost` / `SmtpPort` / `FromAddress` / `FromName` / `FrontendBaseUrl` | Confirmation email delivery and link building |

Local values live in `appsettings.Development.json`, which is untracked — keep it that way — or in user-secrets.

## Tests

- `FormAI.UnitTests` — references Domain + Application. **Currently empty.**
- `FormAI.IntegrationTests` — references FormAI.API, uses `WebApplicationFactory` and PostgreSQL via Testcontainers. Contains `UserRepositoryTests` only.

## Keeping the docs true

When working in this repo:

- Introducing or renaming a domain term? Update [`CONTEXT.md`](./CONTEXT.md) in the same change, and use the canonical term in the code.
- Making a decision that is hard to reverse, surprising to a future reader, and chosen over a real alternative? Propose an ADR in `docs/adr/` (sequential numbering, a paragraph is enough).
- Building something listed in [`docs/known-gaps.md`](./docs/known-gaps.md), or finding a new gap? Update that file in the same change.
- Changing a business rule? Update the "Business rules" section above. A rule documented here that the code no longer enforces is worse than no documentation.

`docs/plans/` holds historical phase plans written before the code existed. **They are not maintained and do not describe current behaviour** — do not use them as a source of truth.

## Known gaps

Detail in [`docs/known-gaps.md`](./docs/known-gaps.md). Headlines: AI result analysis, generation from PDF/Word/image/URL, SignalR realtime updates, rate limiting on generation, aggregated results for the owner, editable expiry, unit tests — **none of these are built**. Scoring is unfinished and unverified; `ShowResultsAfterSubmit` gates nothing; the owner can currently answer their own private form; the close endpoint is dead code awaiting removal.
