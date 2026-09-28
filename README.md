# FormAI

FormAI turns text into a question form. Paste your source material, tell Claude how many questions you want and how hard they should be, and it generates a structured quiz you can edit, reorder and share by link. Respondents answer through that link — signed in or anonymously — and you see the responses come in.

**Core flow:** paste source text → set the generation parameters → Claude generates the questions → review, edit and reorder them → publish and share the link → respondents answer.

This is a work in progress. See [what works](#what-works-today) and [what doesn't yet](#roadmap) before trying it.

## What works today

- **Accounts** — register with email confirmation, log in, JWT with refresh tokens
- **Generation from pasted text** — question count, allowed question types, difficulty, and whether Claude should fill in the answer key
- **Four question types** — single choice, multiple choice, free text, numeric
- **Form editor** — edit question text and type, add and delete questions and options, drag to reorder both. Saves are diffed, so editing a form that already has responses doesn't invalidate them
- **Publishing** — forms start private and are answerable only once you publish them; forms expire on a date
- **Responding** — anonymous or signed in, one submission per respondent, required-question and option validation
- **Dashboard** — your forms and how many submissions each has
- **Results** — a Summary view of each question's answer distribution and, on a graded form, its score distribution, plus an Individual view to page through submissions one at a time and see that respondent's answers and score

## Roadmap

Not built yet — the detail is in [`docs/known-gaps.md`](./docs/known-gaps.md):

- AI analysis of results
- Generation from PDF, Word, images and URLs (only pasted text works today)
- Showing a respondent their own score
- Realtime submission and generation updates
- Rate limiting on generation

## Tech stack

- **Backend** — .NET 10, Clean Architecture (`Domain` → `Application` → `Infrastructure`/`API`), EF Core + PostgreSQL, JWT auth
- **Frontend** — React 19, TypeScript, Vite, Tailwind CSS
- **AI** — Anthropic Claude API
- **Local dev infra** — Docker Compose (PostgreSQL, Mailpit for email testing)

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 20+
- [Docker](https://www.docker.com/) (for PostgreSQL and Mailpit)
- An [Anthropic API key](https://console.anthropic.com/)

## Setup

1. **Clone and configure environment variables for Docker**

   ```bash
   cp .env.example .env
   ```

   Fill in `POSTGRES_PASSWORD`, `APP_DB_PASSWORD` and `MIGRATOR_DB_PASSWORD` in `.env` with values of your choice (these are only used by the local Docker containers).

2. **Start PostgreSQL and Mailpit**

   ```bash
   docker compose up -d
   ```

   PostgreSQL is available at `localhost:5432`. On its first start (empty volume) it creates two roles: `form_ai_migrator`, which owns the database and runs migrations, and `form_ai_app`, which the API connects as and can only read and write data. If you already have a volume from before these roles existed, recreate it with `docker compose down -v` (this deletes the local data).

   Mailpit's web inbox is at [http://localhost:8025](http://localhost:8025) — the backend sends all local email there instead of a real SMTP provider.

3. **Configure backend secrets**

   The backend reads its configuration from ASP.NET Core's standard configuration sources. For local development, the simplest option is [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets):

   ```bash
   cd src/FormAI.API
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=form_ai;Username=form_ai_app;Password=<APP_DB_PASSWORD from step 1>"
   dotnet user-secrets set "Jwt:Secret" "<any long random string>"
   dotnet user-secrets set "Jwt:Issuer" "formai"
   dotnet user-secrets set "Jwt:Audience" "formai"
   dotnet user-secrets set "Claude:ApiKey" "<your Anthropic API key>"
   dotnet user-secrets set "Email:SmtpHost" "localhost"
   dotnet user-secrets set "Email:SmtpPort" "1025"
   dotnet user-secrets set "Email:FromAddress" "noreply@formai.local"
   dotnet user-secrets set "Email:FromName" "FormAI"
   dotnet user-secrets set "Email:FrontendBaseUrl" "http://localhost:5173"
   ```

   Without the `Email` settings, registration can't send its confirmation link and no account can be verified.

4. **Apply database migrations**

   ```bash
   dotnet ef database update --project src/FormAI.Infrastructure --startup-project src/FormAI.API --connection "Host=localhost;Database=form_ai;Username=form_ai_migrator;Password=<MIGRATOR_DB_PASSWORD from step 1>"
   ```

   Migrations must run as `form_ai_migrator`. The `form_ai_app` role from step 3 cannot change the schema, so leaving out `--connection` fails.

5. **Run the backend**

   ```bash
   dotnet run --project src/FormAI.API
   ```

   API available at `http://localhost:5155`, with Swagger UI at `/swagger`.

6. **Run the frontend**

   ```bash
   cd frontend
   npm install
   npm run dev
   ```

   Frontend available at `http://localhost:5173`. Vite proxies `/api` to the backend, so no extra configuration is needed.

## End-to-end tests (Playwright)

The Playwright suite starts its own API against a separate database, `form_ai_e2e`, so it never touches your development data. Create it once, with the same roles and grants as `form_ai` (Docker Compose must be running):

```bash
sh docker/postgres/create-e2e-db.sh
```

Then apply the migrations to it as the migrator role (the script is safe to re-run):

```bash
dotnet ef database update --project src/FormAI.Infrastructure --startup-project src/FormAI.API --connection "Host=localhost;Database=form_ai_e2e;Username=form_ai_migrator;Password=<MIGRATOR_DB_PASSWORD>"
```

Run the suite from `frontend/` with `npm run test:e2e`. It needs `JWT_SECRET` and `APP_DB_PASSWORD` in the repo-root `.env`, plus Redis and Mailpit from Docker Compose.

## Configuration reference

| Variable | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string |
| `Jwt__Secret` | JWT signing key |
| `Jwt__Issuer` / `Jwt__Audience` | JWT validation params |
| `Jwt__ExpiresInMinutes` | Access token lifetime in minutes (default 60) |
| `Jwt__RefreshTokenExpiryDays` | Refresh token lifetime in days (default 7); the refresh token itself lives only in an `HttpOnly` cookie |
| `Claude__ApiKey` | Anthropic API key for form generation |
| `Claude__Model` / `Claude__MaxTokens` | Optional; defaults in `appsettings.json` |
| `Email__SmtpHost` / `Email__SmtpPort` | SMTP server for confirmation emails (Mailpit locally) |
| `Email__FromAddress` / `Email__FromName` | Sender identity |
| `Email__FrontendBaseUrl` | Base URL used to build confirmation links |

Docker Compose also reads `POSTGRES_PASSWORD`, `APP_DB_PASSWORD` and `MIGRATOR_DB_PASSWORD` from `.env` (see `.env.example`) — these only apply to the local PostgreSQL container, not the backend app itself.

## Commands

```bash
# Build entire solution
dotnet build FormAI.sln

# Run API (http://localhost:5155, Swagger at /swagger)
dotnet run --project src/FormAI.API

# Run all tests
dotnet test FormAI.sln

# EF Core migrations (run from repo root)
dotnet ef migrations add <MigrationName> --project src/FormAI.Infrastructure --startup-project src/FormAI.API
# applying needs the migrator role: form_ai_app cannot change the schema
dotnet ef database update --project src/FormAI.Infrastructure --startup-project src/FormAI.API --connection "Host=localhost;Database=form_ai;Username=form_ai_migrator;Password=<MIGRATOR_DB_PASSWORD>"
```

## Project structure

```
FormAI.Domain          entities, enums — zero dependencies
FormAI.Application     use cases, DTOs, interfaces
FormAI.Infrastructure  EF Core, repositories, Claude API integration
FormAI.API             controllers, middleware, DI wiring
frontend/              React + TypeScript + Vite app
```

## Documentation

| File | What it holds |
|---|---|
| [`CONTEXT.md`](./CONTEXT.md) | The glossary — what each domain term means |
| [`CLAUDE.md`](./CLAUDE.md) | Architecture, business rules as implemented, conventions |
| [`docs/adr/`](./docs/adr/) | Why the non-obvious decisions were made |
| [`docs/known-gaps.md`](./docs/known-gaps.md) | What isn't built, what's provisional, what's dead code |

`docs/plans/` holds historical phase plans written before the code existed. They are not maintained and don't describe current behaviour.

## License

[MIT](./LICENSE)
