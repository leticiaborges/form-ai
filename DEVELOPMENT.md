# Development

How to run FormAI locally, configure it and run its tests. For what the project is and how it works, see the [README](./README.md).

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 20+
- [Docker](https://www.docker.com/) (PostgreSQL, Redis, Mailpit and the AI gateway)
- An [Anthropic API key](https://console.anthropic.com/) and an [OpenAI API key](https://platform.openai.com/) — the gateway tries Anthropic first and fails over to OpenAI (see [`docker/litellm/README.md`](./docker/litellm/README.md))

## Setup

1. **Clone and configure environment variables for Docker**

   ```bash
   cp .env.example .env
   ```

   Fill in `.env`. The passwords and secrets are values of your choice; they are only used by local containers.

   | Variable                                                        | Used for                                                                           |
   | --------------------------------------------------------------- | ---------------------------------------------------------------------------------- |
   | `POSTGRES_PASSWORD`, `APP_DB_PASSWORD`, `MIGRATOR_DB_PASSWORD`  | Main PostgreSQL: superuser, the `form_ai_app` role and the `form_ai_migrator` role |
   | `JWT_SECRET`                                                    | Playwright suite and `docker-compose.app.yml` only                                 |
   | `DEMO_PASSWORD` (optional)                                      | Demo account password in `docker-compose.app.yml`; blank disables the demo         |
   | `LITELLM_MASTER_KEY`, `LITELLM_SALT_KEY`, `LITELLM_DB_PASSWORD` | The AI gateway and its own database                                                |
   | `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`                           | Real provider keys, read by the gateway only                                       |
   | `LITELLM_APP_KEY`                                               | The key the API uses against the gateway (any string starting with `sk-`)          |
   | `LITELLM_APP_MAX_BUDGET_USD`                                    | Monthly budget of that key (default 10)                                            |

2. **Start the local infrastructure**

   ```bash
   docker compose up -d
   ```

   | Service         | Address                                                              | Notes                                                                                                                                                                                                                                                        |
   | --------------- | -------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
   | PostgreSQL      | `localhost:5432`                                                     | On first start (empty volume) it creates `form_ai_migrator` (owns the schema, runs migrations) and `form_ai_app` (what the API uses, data access only). If your volume predates these roles, recreate it with `docker compose down -v` (deletes local data). |
   | Mailpit         | inbox [http://localhost:8025](http://localhost:8025), SMTP on `1025` | Catches all local email instead of a real SMTP provider.                                                                                                                                                                                                     |
   | Redis           | `localhost:6379`                                                     | SignalR backplane for live results. No auth, no volume.                                                                                                                                                                                                      |
   | LiteLLM gateway | `127.0.0.1:4000`                                                     | Every model call goes through it. Needs the `LITELLM_*` and provider keys from `.env`.                                                                                                                                                                       |

3. **Register the API's key in the gateway**

   ```bash
   bash docker/litellm/provision-app-key.sh
   ```

   This creates (or updates) the `form-ai-app` key from `LITELLM_APP_KEY`: access to the two model aliases only, with a monthly budget. It is idempotent. Use the same value as `Ai:ApiKey` in the next step.

4. **Configure backend settings**

   Defaults that are safe to commit live in `src/FormAI.API/appsettings.json`. Secrets and machine-specific values go in the **untracked** (git-ignored) `src/FormAI.API/appsettings.Development.json`, or in [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets). A working `appsettings.Development.json`:

   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Host=localhost;Database=form_ai;Username=form_ai_app;Password=<APP_DB_PASSWORD>",
       "Redis": "localhost:6379"
     },
     "Jwt": { "Secret": "<long random string>", "Issuer": "formai", "Audience": "formai" },
     "Email": {
       "SmtpHost": "localhost",
       "SmtpPort": 1025,
       "FromAddress": "noreply@formai.local",
       "FromName": "FormAI",
       "FrontendBaseUrl": "http://localhost:5173"
     },
     "Ai": { "ApiKey": "<LITELLM_APP_KEY>" },
     "Demo": { "Password": "<password shared by all demo accounts>" }
   }
   ```

   Without `Email`, registration can't send its confirmation link and no account can be verified. Without `Ai:ApiKey`, generation fails. Without `Demo:Password`, `POST /api/auth/demo` answers 404 and creates nothing. Never commit real values.

5. **Apply database migrations**

   ```bash
   dotnet ef database update --project src/FormAI.Infrastructure --startup-project src/FormAI.API --connection "Host=localhost;Database=form_ai;Username=form_ai_migrator;Password=<MIGRATOR_DB_PASSWORD>"
   ```

   Migrations must run as `form_ai_migrator`. The `form_ai_app` role cannot change the schema, so leaving out `--connection` fails. The app never migrates at startup.

6. **Run the backend**

   ```bash
   dotnet run --project src/FormAI.API
   ```

   API available at `http://localhost:5155`, with Swagger UI at `/swagger`.

7. **Run the frontend**

   ```bash
   cd frontend
   npm install
   npm run dev
   ```

   Frontend available at `http://localhost:5173`. Vite proxies `/api` and `/hubs` (SignalR) to the backend, so no extra configuration is needed.

### Running the API as a container (optional)

To test the production Docker image locally, layer `docker-compose.app.yml` on top of the infrastructure. It builds `src/FormAI.API/Dockerfile`, serves the API at `http://localhost:8080`, and reaches the gateway at `http://litellm:4000`. It needs `JWT_SECRET`, `APP_DB_PASSWORD` and `LITELLM_APP_KEY` in `.env` (and the key registered with step 3); set `DEMO_PASSWORD` to enable the demo account.

```bash
docker compose -f docker-compose.yml -f docker-compose.app.yml up -d --build
```

The Vite dev server proxies to `http://localhost:5155` by default; point it at the container with `API_URL=http://localhost:8080 npm run dev`.

## Tests

- **Backend unit tests** — `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj` (no Docker needed). `dotnet test FormAI.sln` also runs the integration tests, which start PostgreSQL through Testcontainers and need Docker.
- **Frontend unit/component tests** — from `frontend/`, `npm test` (Vitest).
- **End-to-end tests** — Playwright, see below.

### End-to-end tests (Playwright)

The Playwright suite starts its own API against a separate database, `form_ai_e2e`, so it never touches your development data. Create it once, with the same roles and grants as `form_ai` (Docker Compose must be running):

```bash
sh docker/postgres/create-e2e-db.sh
```

Then apply the migrations to it as the migrator role (the script is safe to re-run):

```bash
dotnet ef database update --project src/FormAI.Infrastructure --startup-project src/FormAI.API --connection "Host=localhost;Database=form_ai_e2e;Username=form_ai_migrator;Password=<MIGRATOR_DB_PASSWORD>"
```

Run the suite from `frontend/` with `npm run test:e2e`. It needs `JWT_SECRET` and `APP_DB_PASSWORD` in the repo-root `.env`, plus PostgreSQL, Redis and Mailpit from Docker Compose. AI calls go to a fake gateway (`frontend/e2e/support/fake-gateway.mjs`), so no provider keys are needed. It answers by a marker in the source text: `[fake:down]` (503), `[fake:slow]` (never answers), `[fake:truncated]` and `[fake:invalid]` (502).

## Configuration reference

ASP.NET Core configuration: each key can be set in `appsettings*.json`, user-secrets or an environment variable (use `__` for `:`, e.g. `Ai__GatewayUrl`). Defaults are those in `appsettings.json`.

| Key                                                   | Default                    | Purpose                                                                                |
| ----------------------------------------------------- | -------------------------- | -------------------------------------------------------------------------------------- |
| `ConnectionStrings:DefaultConnection`                 | —                          | PostgreSQL connection string (as `form_ai_app`)                                        |
| `ConnectionStrings:Redis`                             | —                          | Redis for the SignalR backplane                                                        |
| `Jwt:Secret`                                          | —                          | JWT signing key                                                                        |
| `Jwt:Issuer` / `Jwt:Audience`                         | —                          | JWT validation parameters                                                              |
| `Jwt:ExpiresInMinutes`                                | 60                         | Access token lifetime; the token lives only in frontend memory                         |
| `Jwt:RefreshTokenExpiryDays`                          | 7                          | Refresh token lifetime; it lives only in an `HttpOnly` cookie                          |
| `Ai:GatewayUrl`                                       | `http://127.0.0.1:4000`    | LiteLLM gateway (OpenAI-compatible API)                                                |
| `Ai:ApiKey`                                           | —                          | Gateway key for the API (`LITELLM_APP_KEY`)                                            |
| `Ai:TextAlias`                                        | `form-generator`           | Model alias for text sources                                                           |
| `Ai:VisionAlias`                                      | `form-generator-vision`    | Model alias used when a PDF is attached                                                |
| `Ai:MaxTokens`                                        | 4096                       | Output token cap per generation                                                        |
| `Ai:TimeoutSeconds`                                   | 80                         | Request timeout to the gateway                                                         |
| `Email:SmtpHost` / `Email:SmtpPort`                   | —                          | SMTP server for confirmation emails (Mailpit locally)                                  |
| `Email:FromAddress` / `Email:FromName`                | —                          | Sender identity                                                                        |
| `Email:FrontendBaseUrl`                               | —                          | Base URL used to build confirmation links                                              |
| `Demo:Password`                                       | —                          | Shared password of demo accounts. Never in `appsettings.json`; blank disables the demo |
| `RateLimiting:Generate`                               | 10 per 60 min, 6 segments  | Per user, on `POST /api/forms/generate`                                                |
| `RateLimiting:ResendVerification`                     | 3 per 15 min, 3 segments   | On resending the confirmation email                                                    |
| `RateLimiting:Demo`                                   | 3 per 15 min, 3 segments   | Per IP, on `POST /api/auth/demo`                                                       |
| `RateLimiting:Login`                                  | 10 per 15 min, 3 segments  | Per IP, on `POST /api/auth/login`; failed and successful attempts both count           |
| `RateLimiting:Register`                               | 5 per 60 min, 6 segments   | Per IP, on `POST /api/auth/register`                                                   |
| `RateLimiting:Submit`                                 | 100 per 10 min, 5 segments | Per user, else per IP, on `POST /api/forms/{id}/submit`                                |
| `RefreshTokenCleanup:RetentionDays` / `IntervalHours` | 10 / 24                    | How long expired or revoked refresh tokens are kept, and how often the cleanup runs    |

Each rate limit takes `PermitLimit`, `WindowMinutes` and `SegmentsPerWindow` (sliding window). Counters are in process memory, so limits apply **per instance**.

Docker Compose reads the `.env` variables listed in step 1 (see `.env.example`); those only apply to the local containers, not to the backend itself.

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

# Frontend (from frontend/)
npm run dev        # dev server
npm run build      # type-check and build
npm run lint
npm test           # Vitest
npm run test:e2e   # Playwright
```

## Project structure

```
FormAI.Domain          entities, enums, pure rules — zero dependencies
FormAI.Application     use cases, DTOs, interfaces
FormAI.Infrastructure  EF Core, repositories, AI gateway client, email, JWT
FormAI.API             controllers, middleware, SignalR hub, DI wiring
frontend/              React + TypeScript + Vite app
docker/                Postgres init scripts, LiteLLM gateway config
infra/                 Terraform (prod and AI gateway)
```
