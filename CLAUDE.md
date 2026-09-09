# CLAUDE.md

Guidance for Claude Code (claude.ai/code) working in this repository.

**This file describes the system as it is actually built.** Anything described as missing, unused or provisional lives in [`docs/known-gaps.md`](./docs/known-gaps.md) — read it before assuming a feature exists. The vocabulary is defined in [`CONTEXT.md`](./CONTEXT.md); use those terms in code, comments and commit messages.

## Project overview

FormAI turns text supplied by a user into a question form. Claude generates the questions, the user edits them, and the form is answered through a shared link.

**What works end to end today:** register and verify an account → paste text and pick an expiry date → Claude generates a draft set of questions → edit, reorder, add and delete questions and options in the form editor, and change the expiry → publish the form → share the link → respondents (signed in or anonymous) answer once → submissions to a graded form are scored → the owner sees a submission count on the dashboard, and opens the form's Results tab to see each question's answer distribution and, on a graded form, how the scores were spread.

## Vocabulary

Read [`CONTEXT.md`](./CONTEXT.md) before naming anything. Points worth repeating because the code still disagrees in places:

- A form is **published** or **private**. There is no draft, and no closing — a form stops accepting submissions when it **expires**.
- A form is **graded** or it is not (`IsGraded`). Only a graded form has points, an answer key and scores.
- **Answer key** (`IsCorrect`) is for options; **suggested answer** (`CorrectAnswer`) is for Text and Numeric questions. They are different things, and both exist only on a graded form.
- A **submission** is a whole pass through a form; an **answer** is one question within it. Never call either a "response" — that word is reserved for the `...Response` DTO suffix.

## Data model

Ten entities in `FormAI.Domain/Entities`. Six are the domain proper:

- **User** — account with hashed password and an email-confirmation state
- **Form** — `Title`, `Description`, `CreatedBy`, `SourceType`, `IsPublic`, `ExpiresAt`, `ShowResultsAfterSubmit`, `IsGraded`, `CreatedAt`. Owns its questions, submissions and source contents. The extracted text is **not** on `Form` — it lives in `FormSourceContent`
- **FormQuestion** — belongs to a Form; `Type` (`Single`, `Multiple`, `Text`, `Numeric`), `Order`, `IsRequired`, `Points`, `CorrectAnswer`, `AiGenerated`
- **QuestionOption** — a choice for a `Single`/`Multiple` question; `Text`, `Order`, `IsCorrect` (nullable — null means "not marked")
- **Submission** — one respondent's pass through a form; `UserId` (null when anonymous), `RespondentToken`, `IpAddress`, `SubmittedAt`, `Score`
- **Answer** — one row per answered question; holds `TextValue`, `NumericValue`, `Score`, and its selected options in the child table **AnswerSelectedOption**, which stores the option's **text**, not its id ([ADR 0001](./docs/adr/0001-selected-option-text-snapshot.md))

The rest are supporting: **FormSourceContent** (extracted text per source, with `SourceType`, `FileName`, `Order`), **RefreshToken**, **UserConfirmationToken**.

Entities use private constructors plus a static `Create()` factory, and every property has a `private set`. EF Core hydrates through the private constructor. Mutation goes through explicit methods (`Update`, `SetOptions`, `ReplaceQuestions`, `ClearGradingIfUngraded`), never property assignment.

`FormAI.Domain/Scoring/` holds the scoring rules as pure functions over those entities: `SubmissionScorer` (what an answer and a submission are worth, and whether one is correct at all), `ScoredAnswer` (an answer in the shape every scoring path shares, built from an `Answer` by `ScoredAnswer.From`) and `GradingFingerprint` (whether a save needs a rescore).

`FormAI.Domain/Results/` holds the owner's view of what has been submitted, in the same pure style: `FormResultsCalculator.Calculate(form, submissions)` is a plain function with no EF, no DTOs and no async, and `FormResults` is the model it returns (`ScoreBucket`, `QuestionResults`, `OptionResults`, `ValueResults`). It holds the `FormQuestion` entity rather than copying its text and type, leaving the Application layer to decide what to expose.

## Business rules (as implemented)

Access and ownership:

- A **private form is invisible to everyone but its owner** — viewing, answering and submitting all check `!IsPublic && CreatedBy != requestingUserId` and throw `NotFoundException`, so the client sees **404, not 403**. This applies to signed-in users too, not only anonymous ones.
- Only the owner may edit, update, delete or expire a form (`ForbiddenException` → 403 when the form is visible but not yours).
- **`GET /api/forms/{id}` is owner-only even when the form is published**, because it is the editor's response and carries the answer key and the suggested answers. Respondents read a form through `GET {id}/answer`, which leaves both out.
- **`GET /api/forms/{id}/results` is owner-only for the same reason** — it carries the answer key too. It runs the same two-step check as `GetFormHandler`: `NotFoundException` when the form is private and not yours (its existence stays hidden), `ForbiddenException` when it is published and not yours.
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
- Generated forms are created **private**, with `ShowResultsAfterSubmit = false` and an expiry **7 days out** (default, chosen by the owner at creation). The owner may change the expiry at any time in the editor; the expiry must always be in the future.
- Source files are never stored — only the extracted text, in `FormSourceContent`.

Grading and scoring:

- **The owner sets points; the server validates them.** The editor sends `Points` per question. On a graded form they are required and must be between `QuestionPointsValidator.MinPoints` (0) and `MaxPoints` (100) — a question with no points is rejected, not defaulted, because the editor coerces the field before sending. Zero is allowed: a question that is part of a graded form but does not count.
- `Form.DefaultQuestionPoints` (1) is what a question starts at — on generation, and when the owner ticks "Graded form" in the editor. It is a starting value, not a rule.
- `Form.ClearGradingIfUngraded()` runs on generation and on every editor save. On an ungraded form it nulls every `IsCorrect`, `CorrectAnswer` and `Points`; on a graded form it does nothing, because the points there are the owner's.
- **Turning grading off is lossy and irreversible.** Saving a form with `IsGraded = false` nulls every `IsCorrect`, every `CorrectAnswer` and every `Points`, and nulls the scores of the submissions already made. Nothing warns the owner first.
- A graded form may be saved with questions that have no answer key. Those questions can never be earned and score 0.
- **Scores are derived, not snapshots** ([ADR 0004](./docs/adr/0004-scores-recomputed-from-current-form.md)). `SaveFormEditorHandler` compares a `GradingFingerprint` from before and after the save and calls `RescoreFormSubmissionsHandler` when it differs, in the same transaction. The fingerprint covers only the questions that already existed, so adding a question never rescores; deleting one does. Renaming an option does too, and will silently change who counts as correct.
- Scoring matches Single and Multiple answers on **option text**, not ids, so submitting and rescoring share one code path. Text is compared trimmed and case-insensitively; numeric suggested answers are parsed with `InvariantCulture`, never the machine's culture.
- A Single or Multiple question is all-or-nothing: the selected texts must equal the correct texts exactly. There is no partial credit.
- An unanswered question in a graded form scores 0, not null. `null` means "this form is not graded"; `0` means "graded, earned nothing".
- **Correctness is recomputed, never read back from a stored score.** `Answer.Score` is the question's points when right and 0 when wrong, so a question worth **0 points** stores 0 either way, and so does a question with no answer key. Anything that needs to know whether an answer was right calls `SubmissionScorer.IsAnswerCorrect` — one definition of correctness, shared by submitting, rescoring and results.
- **Results count against the answers to a question, not the form's submissions.** A skipped question has no `Answer` row at all, so a form with 6 submissions can show `1/4 correct` on a question 2 people skipped. This deliberately disagrees with scoring, which treats an unanswered question as 0 — that is, as wrong. Both readings are defensible; a question card is about that question, so the question's own answers are its population.
- A green bar and a zero correct-count are **not** a contradiction. An option is marked correct because it is in the answer key; an answer is correct only when the whole selection matches the key, which is all-or-nothing. Everyone can have picked a correct option and nobody can be correct.

## Architecture

Clean Architecture with strict layer boundaries. Dependency direction: `API → Application → Domain`, `Infrastructure → Application`.

```
FormAI.Domain        entities, enums, and the pure rules over them (Scoring/, Results/).
                     Zero dependencies, no framework references.
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
| `POST /api/forms` · `GET /api/forms` · `GET/DELETE /api/forms/{id}` | Owner-only. `GET {id}` returns the answer key, so it is owner-only even for a published form |
| `PUT /api/forms/{id}/editor` | The editor save — diffed, id-preserving, applies grading and rescores |
| `POST /api/forms/generate/text` | The only generation endpoint |
| `GET /api/forms/{id}/submissions/count` | Owner-only; one `COUNT`, used by the delete-confirmation modal |
| `GET /api/forms/{id}/results` | Owner-only; the form's answer distributions and, when graded, its score distribution |
| `GET /api/forms/{formId}/answer` · `my-submission` · `POST submit` | Anonymous-friendly |

### AI integration

`IFormGenerationService` in `Application/AI/` is the boundary; `ClaudeFormGenerationService` in Infrastructure is the only class that calls the Anthropic API, via a named `HttpClient` ("claude") pointed at `https://api.anthropic.com/`. `GenerationParameters` carries `QuestionCount`, `AllowedTypes`, `DifficultyLevel` and `IncludeCorrectAnswers` (fed from the new form's `IsGraded` — asking Claude for an answer key is the AI-side name for the same decision) — there is no free-text context parameter. Claude is never asked for points: what a question is worth is the owner's decision, so `GeneratedQuestion` has no such field. The system prompt is a template at `Infrastructure/AI/Prompt/PromptGenerateForm.txt` with placeholders filled at request time; Claude returns JSON that is parsed into `GeneratedQuestion`/`GeneratedOption` and mapped to entities by `GenerateFormHandler`. Model and token limit come from the `Claude` configuration section.

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

- `FormAI.UnitTests` — references Domain + Application; xUnit, no mocking library. Contains `Results/FormResultsCalculatorTests` (with its entity builders in `FormResultsCalculatorTestsHelper`), which covers the aggregation and, through it, most of `SubmissionScorer`. Everything there is a pure function over entities built by the real `Create()` factories, so there is nothing to stub — keep it that way.
- `FormAI.IntegrationTests` — references FormAI.API, uses `WebApplicationFactory` and PostgreSQL via Testcontainers. Contains `UserRepositoryTests` only.

`dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj` runs the unit tests alone, without Docker. Note that `--filter` applied to the whole solution reports "No test matches" for every project that has no match, which is not a failure.

## Keeping the docs true

When working in this repo:

- Introducing or renaming a domain term? Update [`CONTEXT.md`](./CONTEXT.md) in the same change, and use the canonical term in the code.
- Making a decision that is hard to reverse, surprising to a future reader, and chosen over a real alternative? Propose an ADR in `docs/adr/` (sequential numbering, a paragraph is enough).
- Building something listed in [`docs/known-gaps.md`](./docs/known-gaps.md), or finding a new gap? Update that file in the same change.
- Changing a business rule? Update the "Business rules" section above. A rule documented here that the code no longer enforces is worse than no documentation.

`docs/plans/` holds historical phase plans written before the code existed. **They are not maintained and do not describe current behaviour** — do not use them as a source of truth.

## Known gaps

Detail in [`docs/known-gaps.md`](./docs/known-gaps.md). Headlines: AI result analysis, generation from PDF/Word/image/URL, SignalR realtime updates, rate limiting on generation, editable expiry, editable points — **none of these are built**. Scores are shown to the owner in aggregate on the Results tab, but never to the respondent who earned them. There is still no endpoint returning the individual submissions, so an owner cannot see what one respondent answered. `ShowResultsAfterSubmit` gates nothing; the owner can currently answer their own private form. The results read loads every submission of a form at once, the same unbounded shape as rescoring.
