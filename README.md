# FormAI

FormAI uses AI to automatically generate question forms from content you upload — PDFs, Word documents, pasted text, images, or web URLs. Upload source material, set the generation context, and Claude turns it into a structured quiz, test, or assessment you can edit, publish, and share via a link. Respondents answer through that link, and the owner gets aggregated results plus an AI-generated analysis.

**Core flow:** upload source material → set context and parameters → Claude generates a structured form → review/edit/reorder questions → publish and share via link → respondents answer → owner views results and AI analysis.

## Tech stack

- **Backend** — .NET 10, Clean Architecture (`Domain` → `Application` → `Infrastructure`/`API`), EF Core + PostgreSQL, JWT auth, SignalR
- **Frontend** — React 19, TypeScript, Vite, Tailwind CSS
- **AI** — Anthropic Claude API (form generation and result analysis)
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

   Fill in `POSTGRES_PASSWORD` and `APP_DB_PASSWORD` in `.env` with values of your choice (these are only used by the local Docker containers).

2. **Start PostgreSQL and Mailpit**

   ```bash
   docker compose up -d
   ```

   PostgreSQL is available at `localhost:5432`. Mailpit's web inbox is at [http://localhost:8025](http://localhost:8025) — the backend sends all local email there instead of a real SMTP provider.

3. **Configure backend secrets**

   The backend reads its configuration from ASP.NET Core's standard configuration sources. For local development, the simplest option is [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets):

   ```bash
   cd src/FormAI.API
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=form_ai;Username=form_ai_app;Password=<APP_DB_PASSWORD from step 1>"
   dotnet user-secrets set "Jwt:Secret" "<any long random string>"
   dotnet user-secrets set "Jwt:Issuer" "formai"
   dotnet user-secrets set "Jwt:Audience" "formai"
   dotnet user-secrets set "Claude:ApiKey" "<your Anthropic API key>"
   ```

   See [Required environment variables](#required-environment-variables) below for the equivalent environment-variable names if you'd rather set them that way (e.g. for a deployed environment).

4. **Apply database migrations**

   ```bash
   dotnet ef database update --project src/FormAI.Infrastructure --startup-project src/FormAI.API
   ```

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

   Frontend available at `http://localhost:5173`.

## Required environment variables

| Variable | Purpose |
|---|---|
| `Claude__ApiKey` | Claude API key for form generation and result analysis |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string |
| `Jwt__Secret` | JWT signing key |
| `Jwt__Issuer` / `Jwt__Audience` | JWT validation params |

Docker Compose also reads `POSTGRES_PASSWORD` and `APP_DB_PASSWORD` from `.env` (see `.env.example`) — these only apply to the local PostgreSQL container, not the backend app itself.

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

# EF Core migrations (run from repo root)
dotnet ef migrations add <MigrationName> --project src/FormAI.Infrastructure --startup-project src/FormAI.API
dotnet ef database update --project src/FormAI.Infrastructure --startup-project src/FormAI.API
```

## Project structure

```
FormAI.Domain          entities, enums — zero dependencies
FormAI.Application     use cases, DTOs, interfaces
FormAI.Infrastructure  EF Core, repositories, Claude API integration
FormAI.API             controllers, SignalR hub, DI wiring
frontend/              React + TypeScript + Vite app
```

See [`CLAUDE.md`](./CLAUDE.md) for a deeper architecture and data-model overview, and [`docs/plans/`](./docs/plans/) for implementation plans by phase.

## License

[MIT](./LICENSE)
