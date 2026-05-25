# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

FormAI uses AI to automatically generate question forms from content uploaded by the user — PDFs, Word documents, pasted text, images, or web URLs. The target user wants to produce quizzes, tests, and assessments quickly.

**Core flow:** user uploads source material → sets context and parameters → Claude API generates a structured form with questions → user edits/reorders before publishing → form is shared via link → respondents answer → owner views aggregated results and AI-generated analysis.

### Data model (6 entities)

- **User** — account with hashed password
- **Form** — the generated form; holds source metadata (`sourceType`, `sourceContent`, `sourceUrl`), generation context (`aiPromptContext`), and publication settings (`isPublic`, `expiresAt`, `showResultsAfterSubmit`)
- **FormQuestion** — belongs to a Form; types: `Single`, `Multiple`, `Text`, `Numeric`; carries `order`, `points`, `correctAnswer`, and `aiGenerated` flag
- **QuestionOption** — choices for `Single`/`Multiple` questions; has `isCorrect` for grading
- **Submission** — one complete response set per respondent; anonymous respondents are identified by `respondentToken` (UUID from localStorage) + IP
- **Answer** — one row per question per Submission; stores `selectedOptions` in a child table, `textValue`, or `numericValue`

### Business rules

- Expired forms reject new submissions
- Duplicate submission blocked by `userId` (authenticated) or `respondentToken` + IP (anonymous)
- Only the form creator can edit, close, or view individual submission results
- Private forms return `403` to unauthenticated users
- Answer key and scores are hidden until the form is closed (configurable)
- Source files are never stored permanently — only the extracted text is saved in `sourceContent`
- Generation endpoints must be rate-limited (each call costs Anthropic API credits)

### AI generation flow

1. Controller receives the source (file, text, URL, or image)
2. `ContentExtractor` pulls plain text from the source
3. `ClaudeFormGenerationService` builds a prompt with extracted text + `GenerationParameters`
4. Claude API returns structured JSON with questions and options
5. Application layer validates and maps to domain entities
6. Form is saved as a draft pending user review
7. SignalR streams generation progress to the frontend

## Plans

Implementation plans for this project live in `docs/plans/`. Each plan covers one phase or major feature. When Claude creates a new plan, save a copy there.

| File | Covers |
|---|---|
| `docs/plans/phase-1-base.md` | EF Core setup, migrations, repositories, JWT auth, Form CRUD |
| `docs/plans/phase-2-frontend.md` | React frontend, login/register pages, email verification |
| `docs/plans/phase-3-ai-generation.md` | AI form generation via Anthropic API, provider abstraction, question editing |

## Commands

```bash
# Build entire solution
dotnet build FormAI.sln

# Run API (http://localhost:5155, Swagger at /swagger)
dotnet run --project src/FormAI.API

# Run all tests
dotnet test FormAI.sln

# Run only unit tests
dotnet test tests/FormAI.UnitTests

# Run only integration tests
dotnet test tests/FormAI.IntegrationTests

# Run a single test by name
dotnet test tests/FormAI.UnitTests --filter "FullyQualifiedName~TestMethodName"

# EF Core migrations (run from repo root)
dotnet ef migrations add <MigrationName> --project src/FormAI.Infrastructure --startup-project src/FormAI.API
dotnet ef database update --project src/FormAI.Infrastructure --startup-project src/FormAI.API
```

## Architecture

Clean Architecture with strict layer boundaries. Dependency direction: `API → Application → Domain`, `Infrastructure → Application`.

```
FormAI.Domain        — entities, enums. Zero dependencies. No framework references.
FormAI.Application   — use cases, DTOs, interfaces (IUserRepository, IFormGenerationService, IAnalysisService).
                       References only Domain. Never references Infrastructure.
FormAI.Infrastructure — EF Core (AppDbContext), repository implementations, ClaudeFormGenerationService.
                       Implements interfaces declared in Application.
FormAI.API           — controllers, SignalR hub (FormHub), middlewares. Wires everything via DI.
```

### Domain entities

Entities use private constructors + static `Create()` factory methods. EF Core hydrates via the private constructor. All properties have `private set`. Example: `User.Create(name, email, passwordHash)`.

### Application layer conventions

Each use case lives in its own folder named after the operation (`Forms/CreateForm/`, `Forms/GenerateForm/`, `Submissions/SubmitForm/`, `Users/Auth/`). The folder holds the request/response records. The actual handler class will live alongside them.

### AI integration

`IFormGenerationService` and `IAnalysisService` (in `Application/AI/`) are the boundary. `ClaudeFormGenerationService` in Infrastructure is the only class that calls the Anthropic API. The `GenerationParameters` record (defined next to the interface) carries `questionCount`, `allowedTypes`, `difficultyLevel`, `context`, and `includeCorrectAnswers`.

### SignalR

`FormHub` at `/hubs/form` emits two events:
- `ReceiveSubmission` — broadcast to form owner when a response is submitted
- `GenerationProgress` — streams AI generation progress to the creator

### Required environment variables

| Variable | Purpose |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key for form generation and result analysis |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string |
| `Jwt__Secret` | JWT signing key |
| `Jwt__Issuer` / `Jwt__Audience` | JWT validation params |

### Test projects

- `FormAI.UnitTests` — references Domain + Application; tests business rules (duplicate submission, expired forms, scoring, type mapping)
- `FormAI.IntegrationTests` — references FormAI.API; uses `WebApplicationFactory`; mock `IFormGenerationService`, real PostgreSQL via Testcontainers
