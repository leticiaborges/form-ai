# FormAI — Phase 1 Implementation Plan
> Base funcional: projeto .NET, EF Core, PostgreSQL, JWT auth, CRUD de formulários

---

## Context

The FormAI project is being built step by step. The Domain, Application interfaces, Infrastructure (DbContext + Configurations + DependencyInjection), and JwtService/PasswordHasher are all **complete**. Repository implementations are stubs (`throw new NotImplementedException()`). Program.cs is still the template. The current goal is:

1. Wire `Program.cs` + `appsettings.Development.json` just enough for migrations to work
2. Start PostgreSQL via Docker and create the database tables via EF Core
3. Verify the tables exist and look correct
4. Implement the three repositories and verify them

---

## Architecture Overview

Clean Architecture with 4 projects. The dependency rule is strict: inner layers never reference outer ones.

```
Domain ← Application ← Infrastructure
                   ↑          ↑
                   └────API───┘
```

- **Domain** — entities, enums. Zero NuGet packages.
- **Application** — use cases, DTOs, interfaces, validators. No DB/HTTP concerns.
- **Infrastructure** — EF Core, repositories, JWT service, password hasher.
- **API** — controllers, middleware, Program.cs.

---

## Steps

### Step 0 — Prerequisites

Verify toolchain:
```bash
dotnet --version      # must be 8.x or later
docker --version
docker compose version
dotnet tool install --global dotnet-ef
dotnet ef --version   # must be installed
```

---

### Step 1 — Scaffold Solution

```bash
dotnet new sln -n FormAI
dotnet new webapi -n FormAI.API -o src/FormAI.API --no-openapi
dotnet new classlib -n FormAI.Application -o src/FormAI.Application
dotnet new classlib -n FormAI.Domain -o src/FormAI.Domain
dotnet new classlib -n FormAI.Infrastructure -o src/FormAI.Infrastructure
dotnet new xunit -n FormAI.UnitTests -o tests/FormAI.UnitTests
dotnet new xunit -n FormAI.IntegrationTests -o tests/FormAI.IntegrationTests

dotnet sln add src/FormAI.API/FormAI.API.csproj
dotnet sln add src/FormAI.Application/FormAI.Application.csproj
dotnet sln add src/FormAI.Domain/FormAI.Domain.csproj
dotnet sln add src/FormAI.Infrastructure/FormAI.Infrastructure.csproj
dotnet sln add tests/FormAI.UnitTests/FormAI.UnitTests.csproj
dotnet sln add tests/FormAI.IntegrationTests/FormAI.IntegrationTests.csproj

# Project references (enforce dependency direction)
dotnet add src/FormAI.Application references src/FormAI.Domain
dotnet add src/FormAI.Infrastructure references src/FormAI.Application
dotnet add src/FormAI.API references src/FormAI.Application
dotnet add src/FormAI.API references src/FormAI.Infrastructure
dotnet add tests/FormAI.UnitTests references src/FormAI.Application src/FormAI.Domain
dotnet add tests/FormAI.IntegrationTests references src/FormAI.API
```

**Verify:** `dotnet build FormAI.sln` → `Build succeeded. 0 Error(s)`

---

### Step 2 — NuGet Packages

```bash
# Application
dotnet add src/FormAI.Application package FluentValidation --version 11.*
dotnet add src/FormAI.Application package Microsoft.Extensions.DependencyInjection.Abstractions --version 8.*

# Infrastructure
dotnet add src/FormAI.Infrastructure package Microsoft.EntityFrameworkCore --version 8.*
dotnet add src/FormAI.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.*
dotnet add src/FormAI.Infrastructure package Microsoft.EntityFrameworkCore.Design --version 8.*
dotnet add src/FormAI.Infrastructure package BCrypt.Net-Next --version 4.*
dotnet add src/FormAI.Infrastructure package Microsoft.Extensions.DependencyInjection.Abstractions --version 8.*
dotnet add src/FormAI.Infrastructure package Microsoft.Extensions.Configuration.Abstractions --version 8.*

# API
dotnet add src/FormAI.API package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.*
dotnet add src/FormAI.API package FluentValidation.AspNetCore --version 11.*
dotnet add src/FormAI.API package Microsoft.EntityFrameworkCore.Design --version 8.*

# Tests
dotnet add tests/FormAI.UnitTests package FluentAssertions --version 6.*
dotnet add tests/FormAI.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing --version 8.*
dotnet add tests/FormAI.IntegrationTests package FluentAssertions --version 6.*
```

**Why BCrypt:** Slow, adaptive hashing designed for passwords. SHA/MD5 are too fast and vulnerable to brute-force.
**Why EF Design on both Infrastructure AND API:** `dotnet ef` needs the startup project (API) to build the host, but the migrations live in Infrastructure.

---

### Step 3 — Domain Layer

Delete auto-generated `Class1.cs` files, then create:

**File structure:**
```
src/FormAI.Domain/
├── Entities/
│   ├── User.cs
│   ├── Form.cs
│   ├── FormQuestion.cs
│   ├── QuestionOption.cs
│   ├── Submission.cs
│   ├── Answer.cs
│   └── RefreshToken.cs
└── Enums/
    ├── SourceType.cs    # Pdf | Word | Text | Image | Url
    └── QuestionType.cs  # Single | Multiple | Text | Numeric
```

**Key entity fields:**
- `User`: Id (Guid), Name, Email (unique), PasswordHash, CreatedAt + nav: Forms, RefreshTokens
- `Form`: Id, Title, Description?, CreatedBy (FK→User), SourceType (enum), IsPublic, ExpiresAt?, ShowResultsAfterSubmit, CreatedAt + nav: Creator, Questions, Submissions
- `FormQuestion`: Id, FormId, Text, Type (enum), Order, IsRequired, AiGenerated, Points?, CorrectAnswer? + nav: Form, Options, Answers
- `QuestionOption`: Id, QuestionId, Text, Order, IsCorrect? + nav: Question
- `Submission`: Id, FormId, UserId?, RespondentToken (Guid), IpAddress, SubmittedAt, Score? + nav: Form, User?, Answers
- `Answer`: Id, SubmissionId, QuestionId, SelectedOptions (child table `AnswerSelectedOption`), TextValue?, NumericValue? + nav: Submission, Question
- `RefreshToken`: Id, UserId, Token (random string), ExpiresAt, CreatedAt, RevokedAt?, ReplacedByToken?

All navigation properties use `= null!` to satisfy nullable reference type analyzer (EF populates them at runtime).

**Verify:** `dotnet build src/FormAI.Domain` → no packages in `.csproj`, zero errors.

---

### Step 4 — Infrastructure Layer (EF Core)

**File structure:**
```
src/FormAI.Infrastructure/
├── Data/
│   ├── AppDbContext.cs
│   └── Configurations/
│       ├── UserConfiguration.cs
│       ├── FormConfiguration.cs
│       ├── FormQuestionConfiguration.cs
│       ├── QuestionOptionConfiguration.cs
│       ├── SubmissionConfiguration.cs
│       ├── AnswerConfiguration.cs
│       ├── AnswerSelectedOptionConfiguration.cs
│       └── RefreshTokenConfiguration.cs
├── Repositories/
│   ├── UserRepository.cs
│   ├── FormRepository.cs
│   └── RefreshTokenRepository.cs
├── Security/
│   ├── PasswordHasher.cs
│   ├── JwtService.cs
│   └── JwtSettings.cs
└── DependencyInjection.cs
```

**`AppDbContext.cs`:** inherits `DbContext`, has `DbSet<T>` for all entities, calls `modelBuilder.ApplyConfigurationsFromAssembly(...)` in `OnModelCreating`.

**Configuration patterns (use `IEntityTypeConfiguration<T>`):**
- Email: `HasMaxLength(256)`, `HasIndex(...).IsUnique()`
- Enums: store as number
- `SelectedOptions`: separate association table `AnswerSelectedOption` with composite PK (AnswerId, OptionId)
- Cascade deletes: User→Forms, Form→Questions, Question→Options

**Why Fluent API, not Data Annotations:** keeps Domain entities clean, separates EF concerns from business objects.

**`DependencyInjection.cs`** registers DbContext, repositories, PasswordHasher, JwtService via `AddInfrastructure(IConfiguration)` extension method.

**Verify:** `dotnet build src/FormAI.Infrastructure` → zero errors.

---

### Step 5 — Wire Program.cs + appsettings (Migration Prerequisite)

> **Why this step comes before migrations:** `dotnet ef` uses your startup project (FormAI.API) to build the application host and discover how the DbContext is configured. If `Program.cs` doesn't register `AppDbContext` and `appsettings.Development.json` doesn't have the connection string, `dotnet ef` will fail with a "no DbContext found" error.

#### 5a — Edit `appsettings.Development.json`

File: `src/FormAI.API/appsettings.Development.json`

Add the connection string and JWT section that matches the docker-compose credentials:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=form_ai;Username=form_ai;Password=form_ai123"
  },
  "Jwt": {
    "Secret": "super-secret-key-minimum-32-characters-long!!",
    "Issuer": "FormAI",
    "Audience": "FormAI",
    "ExpiresInMinutes": 60
  }
}
```

> **Why these values:** they match exactly what's in `docker-compose.yml` (`POSTGRES_USER=form_ai`, `POSTGRES_PASSWORD=form_ai123`, `POSTGRES_DB=form_ai`). The JWT Secret must be at least 32 characters (256-bit) because that's the minimum for HMAC-SHA256.

#### 5b — Edit `Program.cs`

File: `src/FormAI.API/Program.cs`

Replace the entire file with a minimal but correct version:

```csharp
using FormAI.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

> **Why just this:** We need `AddInfrastructure` so EF Core can find the DbContext configuration. We don't need handlers, middleware, or auth fully wired yet — just enough to generate migrations.

#### 5c — Verify the build compiles

```bash
dotnet build FormAI.sln
```

Expected output: `Build succeeded. 0 Error(s)`. If there are errors, fix them before proceeding — a failing build means `dotnet ef` will also fail.

---

### Step 6 — Start Docker PostgreSQL

> **What this does:** Starts a PostgreSQL 18 database server running in an isolated Docker container. The `docker-compose.yml` already exists with the right credentials.

#### 6a — Start the container

```bash
docker compose up -d
```

The `-d` flag means "detached" — the container runs in the background.

**What you should see:**
```
✔ Container form-ai-db  Started
```

#### 6b — Wait for PostgreSQL to be ready

PostgreSQL takes a few seconds to initialize. Check its status:

```bash
docker compose ps
```

Wait until the `STATUS` column shows `healthy` (not `starting`). The healthcheck in `docker-compose.yml` pings the DB every 5 seconds, so it usually becomes healthy within 10-15 seconds.

If it stays `starting` for more than a minute, check logs:
```bash
docker compose logs postgres
```

#### 6c — Verify you can connect (optional but recommended)

```bash
docker exec -it form-ai-db psql -U form_ai -d form_ai -c "\l"
```

> **What this does:** Runs `psql` (PostgreSQL command-line client) inside the container. `\l` lists all databases. You should see `form_ai` in the list with owner `form_ai`.

At this point, the database `form_ai` exists but has **no tables** — only the empty schema.

---

### Step 7 — Generate and Apply EF Core Migrations

> **What is a migration?** EF Core reads your entity configurations and generates C# code that describes the SQL needed to create (or change) your database tables. You generate this code once, review it, then apply it.

#### 7a — Generate the migration

Run from the repository root:

```bash
dotnet ef migrations add InitialCreate \
  --project src/FormAI.Infrastructure/FormAI.Infrastructure.csproj \
  --startup-project src/FormAI.API/FormAI.API.csproj
```

> **What the flags mean:**
> - `--project` — where the `AppDbContext` lives and where the migration files will be created
> - `--startup-project` — the API project, which provides the DI host so EF can find the DbContext

**Expected output:**
```
Build started...
Build succeeded.
Done. To undo this action, use 'ef migrations remove'
```

This creates a new folder: `src/FormAI.Infrastructure/Migrations/` with two files:
- `<timestamp>_InitialCreate.cs` — the migration code (Up/Down methods)
- `<timestamp>_InitialCreate.Designer.cs` — EF metadata snapshot

#### 7b — Review the generated migration

Open `src/FormAI.Infrastructure/Migrations/<timestamp>_InitialCreate.cs` and verify:

**Tables you should see in the `Up()` method:**
| Table | Key columns |
|---|---|
| `Users` | Id (uuid), Name, Email (unique index), PasswordHash, CreatedAt |
| `Forms` | Id, Title, Description, CreatedBy (FK→Users), SourceType, IsPublic, ExpiresAt, ShowResultsAfterSubmit, CreatedAt |
| `FormQuestions` | Id, FormId (FK→Forms), Text, Type, Order, IsRequired, AiGenerated, Points, CorrectAnswer |
| `QuestionOptions` | Id, QuestionId (FK→FormQuestions), Text, Order, IsCorrect |
| `Submissions` | Id, FormId, UserId (nullable FK→Users), RespondentToken, IpAddress, SubmittedAt, Score |
| `Answers` | Id, SubmissionId (FK→Submissions), QuestionId (FK→FormQuestions), TextValue, NumericValue, Score |
| `AnswerSelectedOptions` | AnswerId + OptionId (composite PK), FK→QuestionOptions |
| `RefreshTokens` | Id, UserId (FK→Users), Token (unique index), ExpiresAt, CreatedAt, RevokedAt, ReplacedByToken |

**Constraints to confirm:**
- `Users.Email` has a unique index
- `Submissions` has a unique composite index on `(FormId, RespondentToken, IpAddress)` for anonymous duplicate blocking
- `Submissions` has a unique filtered index on `(FormId, UserId) WHERE UserId IS NOT NULL` for authenticated duplicate blocking
- Cascade deletes: deleting a User deletes their Forms; deleting a Form deletes its Questions; deleting a Question deletes its Options

If anything looks wrong or missing, **do not apply the migration yet** — fix the configuration and run `dotnet ef migrations remove`, then regenerate.

#### 7c — Apply the migration to the database

```bash
dotnet ef database update \
  --project src/FormAI.Infrastructure/FormAI.Infrastructure.csproj \
  --startup-project src/FormAI.API/FormAI.API.csproj
```

**Expected output:**
```
Build started...
Build succeeded.
Applying migration '20240101000000_InitialCreate'.
Done.
```

EF also creates a special table `__EFMigrationsHistory` that tracks which migrations have been applied. This is how EF knows not to re-run a migration.

---

### Step 8 — Verify Tables in the Database

> This is how you confirm the migration actually worked. You'll connect to the running PostgreSQL container and inspect the schema.

#### 8a — List all tables

```bash
docker exec -it form-ai-db psql -U form_ai -d form_ai -c "\dt"
```

You should see 9 rows: the 8 domain tables plus `__EFMigrationsHistory`.

#### 8b — Inspect a specific table

```bash
docker exec -it form-ai-db psql -U form_ai -d form_ai -c "\d \"Users\""
```

> **Note the quotes:** PostgreSQL is case-sensitive. If EF named the table `Users` (capital U), you need `"Users"`. Without quotes, PostgreSQL lowercases everything.

You should see all columns with their types and the unique index on `Email`.

#### 8c — Check indexes

```bash
docker exec -it form-ai-db psql -U form_ai -d form_ai -c "\di"
```

This lists all indexes. You should see:
- `IX_Users_Email` (unique)
- `IX_Submissions_FormId_RespondentToken_IpAddress` (unique composite)
- `IX_RefreshTokens_Token` (unique)

#### 8d — Inspect migration history

```bash
docker exec -it form-ai-db psql -U form_ai -d form_ai \
  -c "SELECT * FROM \"__EFMigrationsHistory\";"
```

You should see one row: `InitialCreate` with the timestamp applied.

**Milestone reached:** The database schema matches your entity model. From here, the app can read and write to the database.

---

### Step 9 — Implement the Repositories

> **What is a repository?** It's the class that translates between your application's language ("give me the user with this email") and the database's language (SQL). The interfaces are already defined in Application; you're now writing the implementations in Infrastructure.

All three repositories follow the same pattern:
1. Receive `AppDbContext` via constructor injection
2. Use EF Core's LINQ methods to query/insert/update
3. Call `SaveChangesAsync()` after writes

#### 9a — UserRepository

File: `src/FormAI.Infrastructure/Repositories/UserRepository.cs`

```csharp
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FormAI.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        await _context.Users.AddAsync(user, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
        => await _context.Users
            .AnyAsync(u => u.Email == email, cancellationToken);
}
```

> **Why `FirstOrDefaultAsync` instead of `FindAsync`:** `FindAsync` only works with primary keys. For email lookups we need `FirstOrDefaultAsync`. Both are correct for ID lookups, but `FirstOrDefaultAsync` is more consistent.

#### 9b — FormRepository

File: `src/FormAI.Infrastructure/Repositories/FormRepository.cs`

```csharp
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FormAI.Infrastructure.Repositories;

public class FormRepository : IFormRepository
{
    private readonly AppDbContext _context;

    public FormRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Form?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.Forms
            .Include(f => f.Questions)
                .ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public async Task<IEnumerable<Form>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _context.Forms
            .Where(f => f.CreatedBy == userId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Form form, CancellationToken cancellationToken = default)
    {
        await _context.Forms.AddAsync(form, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Form form, CancellationToken cancellationToken = default)
    {
        _context.Forms.Update(form);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var form = await _context.Forms.FindAsync([id], cancellationToken);
        if (form is null) return false;
        _context.Forms.Remove(form);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
```

> **Why `Include(...).ThenInclude(...)`:** `GetByIdAsync` eagerly loads the Questions and their Options in the same SQL query. Without this, accessing `form.Questions` would throw a NullReferenceException because EF doesn't load navigation properties by default (it's called "lazy loading" and is disabled).

#### 9c — RefreshTokenRepository

File: `src/FormAI.Infrastructure/Repositories/RefreshTokenRepository.cs`

```csharp
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FormAI.Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _context;

    public RefreshTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
        => await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == token, cancellationToken);

    public async Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default)
    {
        await _context.RefreshTokens.AddAsync(refreshToken, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var token = await _context.RefreshTokens.FindAsync([id], cancellationToken);
        if (token is null) return;
        token.Revoke();
        await _context.SaveChangesAsync(cancellationToken);
    }
}
```

> **Why `token.Revoke()`:** The `RefreshToken` entity must have a `Revoke()` method that sets `RevokedAt = DateTime.UtcNow`. This follows the domain model pattern — the entity controls its own state changes, not the repository. If the entity doesn't have this method yet, add it.

#### 9d — Verify build

```bash
dotnet build FormAI.sln
```

Expected: `Build succeeded. 0 Error(s)`. All three repositories should now compile with no NotImplementedException stubs.

---

### Step 10 — Test Repositories Manually (Smoke Test)

Before writing unit tests, confirm the repositories actually work against the real database.

#### Option A: Add a temporary test endpoint to Program.cs

Add this temporarily to `Program.cs` (after `app.Build()`):

```csharp
app.MapGet("/test-db", async (IUserRepository userRepo) =>
{
    var testUser = User.Create("Test User", "test@example.com", "hash-placeholder");
    await userRepo.AddAsync(testUser);
    var retrieved = await userRepo.GetByEmailAsync("test@example.com");
    return retrieved is not null 
        ? Results.Ok($"User created and retrieved: {retrieved.Name}") 
        : Results.Problem("User not found after insert");
});
```

Run the API: `dotnet run --project src/FormAI.API`

Visit: `http://localhost:5155/test-db`

Then verify in the database:
```bash
docker exec -it form-ai-db psql -U form_ai -d form_ai -c "SELECT \"Id\", \"Name\", \"Email\" FROM \"Users\";"
```

Remove the test endpoint after confirming it works.

#### Option B: Use Swagger UI

Run the API: `dotnet run --project src/FormAI.API`

Navigate to `http://localhost:5155/swagger` — any controller stub endpoint you call that touches the DB will confirm connectivity.

---

### Step 11 — Write Integration Tests for Repositories

> For full repository testing, integration tests with a real database are the gold standard (stubs and mocks can't catch SQL errors). Unit tests are better suited for pure business logic in the Domain layer.

Add this NuGet package to `FormAI.IntegrationTests`:
```bash
dotnet add tests/FormAI.IntegrationTests package Testcontainers.PostgreSql
```

A minimal integration test for UserRepository:

```csharp
public class UserRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:18")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    [Fact]
    public async Task AddAsync_ShouldPersistUser()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync(); // applies migrations to the test DB

        var repo = new UserRepository(context);
        var user = User.Create("Alice", "alice@example.com", "hash");

        await repo.AddAsync(user);

        var found = await repo.GetByEmailAsync("alice@example.com");
        Assert.NotNull(found);
        Assert.Equal("Alice", found.Name);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}
```

> **Why `MigrateAsync()` in the test:** Each test gets a fresh empty PostgreSQL container. We run the same migrations you created in Step 7 so the schema matches exactly what production will have.

Run the tests:
```bash
dotnet test tests/FormAI.IntegrationTests
```

---

### Step 12 — Global Exception Middleware ✅

Already implemented. Files created:
- `src/FormAI.Application/Common/Exceptions/NotFoundException.cs`
- `src/FormAI.Application/Common/Exceptions/ForbiddenException.cs`
- `src/FormAI.Application/Common/Exceptions/ValidationException.cs`
- `src/FormAI.API/Middleware/ExceptionHandlingMiddleware.cs`

---

### Step 13 — Cleanup Program.cs

Before adding more features, clean up the temporary test endpoints from Program.cs and fix the middleware order.
The middleware must be registered **before** `UseAuthentication` so it wraps the entire request pipeline.

File: `src/FormAI.API/Program.cs`

```csharp
using FormAI.API.Middleware;
using FormAI.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

> **What changed:** removed the `/test-db`, `/test-404`, `/test-500` endpoints; moved `UseMiddleware` to be the first middleware so it catches exceptions from all routes.

Build and confirm: `dotnet build FormAI.sln` → `0 Error(s)`.

---

### Step 14 — Auth Handlers (Register, Login, Refresh)

> **What is a handler?** A handler is a class with a single public method that executes one use case. It receives a request record (the input), talks to repositories/services, and returns a response record (the output). Controllers will call handlers directly — no MediatR yet.

All handlers go in `src/FormAI.Application/Users/Auth/`.

---

#### 14a — RegisterHandler

File: `src/FormAI.Application/Users/Auth/RegisterHandler.cs`

```csharp
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;

namespace FormAI.Application.Users.Auth;

public class RegisterHandler
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;

    public RegisterHandler(IUserRepository users, IPasswordHasher hasher)
    {
        _users = users;
        _hasher = hasher;
    }

    public async Task<RegisterUserResponse> HandleAsync(
        RegisterUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await _users.EmailExistsAsync(request.Email, cancellationToken))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["Email"] = ["This email is already registered."]
            });

        var hash = _hasher.Hash(request.Password);
        var user = User.Create(request.Name, request.Email, hash);

        await _users.AddAsync(user, cancellationToken);

        return new RegisterUserResponse(user.Id, user.Name, user.Email);
    }
}
```

> **Why throw ValidationException instead of a generic error:** the email field is exactly what's wrong, and the client needs to show "this email is already taken" next to the email input — not a generic 500 page.

---

#### 14b — LoginHandler

File: `src/FormAI.Application/Users/Auth/LoginHandler.cs`

```csharp
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;

namespace FormAI.Application.Users.Auth;

public class LoginHandler
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _tokens;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtService _jwt;

    public LoginHandler(
        IUserRepository users,
        IRefreshTokenRepository tokens,
        IPasswordHasher hasher,
        IJwtService jwt)
    {
        _users = users;
        _tokens = tokens;
        _hasher = hasher;
        _jwt = jwt;
    }

    public async Task<LoginResponse> HandleAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByEmailAsync(request.Email, cancellationToken)
            ?? throw new NotFoundException("Invalid email or password.");

        if (!_hasher.Verify(request.Password, user.PasswordHash))
            throw new NotFoundException("Invalid email or password.");

        var accessToken = _jwt.GenerateAccessToken(user);
        var refreshTokenString = _jwt.GenerateRefreshToken();

        var refreshToken = RefreshToken.Create(
            userId: user.Id,
            token: refreshTokenString,
            expiresAt: DateTime.UtcNow.AddDays(7));

        await _tokens.AddAsync(refreshToken, cancellationToken);

        return new LoginResponse(accessToken, refreshTokenString);
    }
}
```

> **Why use NotFoundException for wrong password instead of a specific error:** never tell an attacker whether the email exists or the password is wrong. Both cases return the same vague message "Invalid email or password." — this is called a security-by-obscurity login and is standard practice.

> **Why 7-day refresh token:** the access token is short-lived (60 min) so the user doesn't have to log in every hour. The refresh token is long-lived and stored server-side, so it can be revoked if the user logs out or a breach is detected.

---

#### 14c — RefreshTokenHandler

File: `src/FormAI.Application/Users/Auth/RefreshTokenHandler.cs`

```csharp
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;

namespace FormAI.Application.Users.Auth;

public record RefreshTokenRequest(string RefreshToken);

public class RefreshTokenHandler
{
    private readonly IRefreshTokenRepository _tokens;
    private readonly IUserRepository _users;
    private readonly IJwtService _jwt;

    public RefreshTokenHandler(
        IRefreshTokenRepository tokens,
        IUserRepository users,
        IJwtService jwt)
    {
        _tokens = tokens;
        _users = users;
        _jwt = jwt;
    }

    public async Task<LoginResponse> HandleAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var oldToken = await _tokens.GetByTokenAsync(request.RefreshToken, cancellationToken)
            ?? throw new NotFoundException("Refresh token not found.");

        if (!oldToken.IsActive)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["RefreshToken"] = ["Token is expired or has been revoked."]
            });

        var user = await _users.GetByIdAsync(oldToken.UserId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        var newAccessToken = _jwt.GenerateAccessToken(user);
        var newRefreshTokenString = _jwt.GenerateRefreshToken();

        var newRefreshToken = RefreshToken.Create(
            userId: user.Id,
            token: newRefreshTokenString,
            expiresAt: DateTime.UtcNow.AddDays(7));

        // Revoke old token and link it to the new one (token rotation)
        oldToken.Revoke(replacedByToken: newRefreshTokenString);

        // AddAsync calls SaveChangesAsync — this persists both the revocation and the new token
        // because oldToken is a tracked EF entity from the same DbContext scope
        await _tokens.AddAsync(newRefreshToken, cancellationToken);

        return new LoginResponse(newAccessToken, newRefreshTokenString);
    }
}
```

> **What is token rotation?** Every time you use a refresh token, you get a brand-new one and the old one is invalidated. If someone steals an old refresh token and tries to use it, the server detects it's already been used and can block the attacker.

---

#### 14d — Register handlers in DependencyInjection

Handlers are plain classes with no base class or interface. Register them in `src/FormAI.Infrastructure/DependencyInjection.cs` (or create a new `DependencyInjection.cs` in Application if you prefer — either works). Add to the existing `AddInfrastructure` method or alongside it:

Add to `src/FormAI.Infrastructure/DependencyInjection.cs` inside `AddInfrastructure`:

```csharp
// Auth handlers
services.AddScoped<RegisterHandler>();
services.AddScoped<LoginHandler>();
services.AddScoped<RefreshTokenHandler>();
```

> **Why AddScoped:** handlers use repositories which are scoped (one per HTTP request). If handlers were singletons, they'd hold a scoped dependency — which would cause a runtime error.

Build: `dotnet build FormAI.sln` → `0 Error(s)`.

---

### Step 15 — Wire AuthController

File: `src/FormAI.API/Controllers/AuthController.cs`

```csharp
using FormAI.Application.Users.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FormAI.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly RegisterHandler _register;
    private readonly LoginHandler _login;
    private readonly RefreshTokenHandler _refresh;

    public AuthController(
        RegisterHandler register,
        LoginHandler login,
        RefreshTokenHandler refresh)
    {
        _register = register;
        _login = login;
        _refresh = refresh;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _register.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Register), response);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _login.HandleAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _refresh.HandleAsync(request, cancellationToken);
        return Ok(response);
    }
}
```

> **Why `CreatedAtAction` for register:** HTTP convention says creating a resource returns 201 Created. `CreatedAtAction` also sets the `Location` header pointing to where the resource can be found.

---

#### 15a — Test the full auth flow

Run the API: `dotnet run --project src/FormAI.API`

Open Swagger at `http://localhost:5155/swagger` and test in this order:

**1. Register**
```
POST /api/auth/register
{ "name": "Test User", "email": "test@example.com", "password": "Test1234" }
```
Expected: `201 Created` with `{ "id": "...", "name": "Test User", "email": "test@example.com" }`

**2. Try registering the same email again**
Expected: `400 Bad Request` with `{ "message": "...", "errors": { "Email": ["This email is already registered."] } }`

**3. Login**
```
POST /api/auth/login
{ "email": "test@example.com", "password": "Test1234" }
```
Expected: `200 OK` with `{ "accessToken": "eyJ...", "refreshToken": "..." }`

Save both tokens.

**4. Try wrong password**
Expected: `404` with `{ "message": "Invalid email or password." }` (same message regardless of whether email exists)

**5. Refresh**
```
POST /api/auth/refresh
{ "refreshToken": "<the refreshToken from step 3>" }
```
Expected: `200 OK` with new token pair.

**6. Try the old refresh token again**
Expected: `400 Bad Request` — token was already rotated.

Verify in the database that tokens exist:
```bash
docker exec -it form-ai-db psql -U form_ai -d form_ai \
  -c "SELECT \"Token\", \"ExpiresAt\", \"RevokedAt\" FROM \"RefreshTokens\";"
```

---

### Step 16 — Form DTOs, Domain Methods, and Handlers

Before writing handlers, you need some additions:

#### 16a — Add domain methods to Form entity

The handlers will need to update form data and close a form. Domain entities control their own state, so these operations must be methods on `Form`.

Add to `src/FormAI.Domain/Entities/Form.cs`:

```csharp
public void Update(string title, string? description, bool isPublic,
    DateTime? expiresAt, bool showResultsAfterSubmit)
{
    Title = title;
    Description = description ?? string.Empty;
    IsPublic = isPublic;
    ExpiresAt = expiresAt;
    ShowResultsAfterSubmit = showResultsAfterSubmit;
}

public void Close() => ExpiresAt = DateTime.UtcNow;

public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;

public void ReplaceQuestions(List<FormQuestion> questions)
{
    Questions.Clear();
    Questions.AddRange(questions);
}
```

> **Why `ReplaceQuestions` on the entity:** the list has `private set` which prevents reassigning it from outside (`form.Questions = newList` would fail). But since `Questions` is a `List<T>`, you can call `.Clear()` and `.AddRange()` on it — EF Core tracks these mutations and generates the correct DELETE + INSERT SQL.

---

#### 16b — Add response DTOs for forms

File: `src/FormAI.Application/Forms/GetForm/GetFormResponse.cs`

```csharp
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GetForm;

public record GetFormResponse(
    Guid Id,
    string Title,
    string? Description,
    bool IsPublic,
    DateTime? ExpiresAt,
    bool ShowResultsAfterSubmit,
    DateTime CreatedAt,
    List<QuestionDto> Questions
);

public record QuestionDto(
    Guid Id,
    string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    int? Points,
    List<OptionDto> Options
);

public record OptionDto(
    Guid Id,
    string? Text,
    int Order
);
```

> **Why OptionDto does not include `IsCorrect`:** the answer key is hidden while the form is open. Only after a form is closed should `IsCorrect` be exposed. This will be enforced in the handler.

---

#### 16c — Add request DTO for updating questions

File: `src/FormAI.Application/Forms/UpdateQuestions/UpdateQuestionsRequest.cs`

```csharp
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.UpdateQuestions;

public record UpdateQuestionsRequest(
    Guid FormId,
    Guid RequestingUserId,
    List<QuestionInput> Questions
);

public record QuestionInput(
    string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    int? Points,
    string? CorrectAnswer,
    List<OptionInput> Options
);

public record OptionInput(
    string Text,
    int Order,
    bool? IsCorrect
);
```

---

#### 16d — Add request DTO for updating form metadata

File: `src/FormAI.Application/Forms/UpdateForm/UpdateFormRequest.cs`

```csharp
namespace FormAI.Application.Forms.UpdateForm;

public record UpdateFormRequest(
    Guid FormId,
    Guid RequestingUserId,
    string Title,
    string? Description,
    bool IsPublic,
    DateTime? ExpiresAt,
    bool ShowResultsAfterSubmit
);
```

---

#### 16e — CreateFormHandler

File: `src/FormAI.Application/Forms/CreateForm/CreateFormHandler.cs`

```csharp
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.CreateForm;

public class CreateFormHandler
{
    private readonly IFormRepository _forms;

    public CreateFormHandler(IFormRepository forms)
    {
        _forms = forms;
    }

    public async Task<CreateFormResponse> HandleAsync(
        CreateFormRequest request,
        Guid createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var form = Form.Create(
            title: request.Title,
            description: request.Description ?? string.Empty,
            createdBy: createdByUserId,
            sourceType: SourceType.Text,
            isPublic: request.IsPublic,
            expiresAt: request.ExpiresAt,
            showResultsAfterSubmit: request.ShowResultsAfterSubmit);

        await _forms.AddAsync(form, cancellationToken);

        return new CreateFormResponse(form.Id, form.Title);
    }
}
```

---

#### 16f — GetFormByIdHandler

File: `src/FormAI.Application/Forms/GetForm/GetFormByIdHandler.cs`

```csharp
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;

namespace FormAI.Application.Forms.GetForm;

public class GetFormByIdHandler
{
    private readonly IFormRepository _forms;

    public GetFormByIdHandler(IFormRepository forms)
    {
        _forms = forms;
    }

    public async Task<GetFormResponse> HandleAsync(
        Guid formId,
        Guid? requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(formId, cancellationToken)
            ?? throw new NotFoundException($"Form {formId} not found.");

        if (!form.IsPublic && form.CreatedBy != requestingUserId)
            throw new ForbiddenException("This form is private.");

        return new GetFormResponse(
            Id: form.Id,
            Title: form.Title,
            Description: form.Description,
            IsPublic: form.IsPublic,
            ExpiresAt: form.ExpiresAt,
            ShowResultsAfterSubmit: form.ShowResultsAfterSubmit,
            CreatedAt: form.CreatedAt,
            Questions: form.Questions.Select(q => new QuestionDto(
                Id: q.Id,
                Text: q.Text,
                Type: q.Type,
                Order: q.Order,
                IsRequired: q.IsRequired,
                Points: q.Points,
                Options: (q.Options ?? []).Select(o => new OptionDto(
                    Id: o.Id,
                    Text: o.Text,
                    Order: o.Order)).ToList()
            )).OrderBy(q => q.Order).ToList()
        );
    }
}
```

---

#### 16g — GetFormsByUserHandler

File: `src/FormAI.Application/Forms/GetForm/GetFormsByUserHandler.cs`

```csharp
using FormAI.Application.Interfaces;

namespace FormAI.Application.Forms.GetForm;

public record FormSummaryResponse(
    Guid Id,
    string Title,
    bool IsPublic,
    DateTime? ExpiresAt,
    DateTime CreatedAt
);

public class GetFormsByUserHandler
{
    private readonly IFormRepository _forms;

    public GetFormsByUserHandler(IFormRepository forms)
    {
        _forms = forms;
    }

    public async Task<List<FormSummaryResponse>> HandleAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var forms = await _forms.GetAllByUserIdAsync(userId, cancellationToken);

        return forms.Select(f => new FormSummaryResponse(
            Id: f.Id,
            Title: f.Title,
            IsPublic: f.IsPublic,
            ExpiresAt: f.ExpiresAt,
            CreatedAt: f.CreatedAt)).ToList();
    }
}
```

---

#### 16h — UpdateFormHandler

File: `src/FormAI.Application/Forms/UpdateForm/UpdateFormHandler.cs`

```csharp
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;

namespace FormAI.Application.Forms.UpdateForm;

public class UpdateFormHandler
{
    private readonly IFormRepository _forms;

    public UpdateFormHandler(IFormRepository forms)
    {
        _forms = forms;
    }

    public async Task HandleAsync(
        UpdateFormRequest request,
        CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId, cancellationToken)
            ?? throw new NotFoundException($"Form {request.FormId} not found.");

        if (form.CreatedBy != request.RequestingUserId)
            throw new ForbiddenException("You do not own this form.");

        form.Update(request.Title, request.Description, request.IsPublic,
            request.ExpiresAt, request.ShowResultsAfterSubmit);

        await _forms.UpdateAsync(form, cancellationToken);
    }
}
```

---

#### 16i — DeleteFormHandler

File: `src/FormAI.Application/Forms/DeleteForm/DeleteFormHandler.cs`

```csharp
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;

namespace FormAI.Application.Forms.DeleteForm;

public record DeleteFormRequest(Guid FormId, Guid RequestingUserId);

public class DeleteFormHandler
{
    private readonly IFormRepository _forms;

    public DeleteFormHandler(IFormRepository forms)
    {
        _forms = forms;
    }

    public async Task HandleAsync(
        DeleteFormRequest request,
        CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId, cancellationToken)
            ?? throw new NotFoundException($"Form {request.FormId} not found.");

        if (form.CreatedBy != request.RequestingUserId)
            throw new ForbiddenException("You do not own this form.");

        await _forms.DeleteAsync(request.FormId, cancellationToken);
    }
}
```

---

#### 16j — CloseFormHandler

File: `src/FormAI.Application/Forms/CloseForm/CloseFormHandler.cs`

```csharp
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;

namespace FormAI.Application.Forms.CloseForm;

public record CloseFormRequest(Guid FormId, Guid RequestingUserId);

public class CloseFormHandler
{
    private readonly IFormRepository _forms;

    public CloseFormHandler(IFormRepository forms)
    {
        _forms = forms;
    }

    public async Task HandleAsync(
        CloseFormRequest request,
        CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId, cancellationToken)
            ?? throw new NotFoundException($"Form {request.FormId} not found.");

        if (form.CreatedBy != request.RequestingUserId)
            throw new ForbiddenException("You do not own this form.");

        form.Close();

        await _forms.UpdateAsync(form, cancellationToken);
    }
}
```

---

#### 16k — UpdateQuestionsHandler

File: `src/FormAI.Application/Forms/UpdateQuestions/UpdateQuestionsHandler.cs`

```csharp
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;

namespace FormAI.Application.Forms.UpdateQuestions;

public class UpdateQuestionsHandler
{
    private readonly IFormRepository _forms;

    public UpdateQuestionsHandler(IFormRepository forms)
    {
        _forms = forms;
    }

    public async Task HandleAsync(
        UpdateQuestionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId, cancellationToken)
            ?? throw new NotFoundException($"Form {request.FormId} not found.");

        if (form.CreatedBy != request.RequestingUserId)
            throw new ForbiddenException("You do not own this form.");

        var questions = request.Questions.Select(q =>
        {
            var question = FormQuestion.Create(
                formId: form.Id,
                text: q.Text,
                type: q.Type,
                order: q.Order,
                isRequired: q.IsRequired,
                aiGenerated: false,
                points: q.Points,
                correctAnswer: q.CorrectAnswer ?? string.Empty);

            var options = q.Options.Select(o =>
                QuestionOption.Create(question.Id, o.Text, o.Order, o.IsCorrect))
                .ToList();

            question.SetOptions(options);
            return question;

        }).ToList();

        form.ReplaceQuestions(questions);

        await _forms.UpdateAsync(form, cancellationToken);
    }
}
```

> **Important:** `FormQuestion.Create` and `QuestionOption.Create` already exist. But `question.SetOptions(options)` requires adding a `SetOptions` method to `FormQuestion`. Add it to `src/FormAI.Domain/Entities/FormQuestion.cs`:
>
> ```csharp
> public void SetOptions(List<QuestionOption> options)
> {
>     Options ??= new List<QuestionOption>();
>     Options.Clear();
>     Options.AddRange(options);
> }
> ```

---

#### 16l — Register all form handlers in DependencyInjection

Add to `src/FormAI.Infrastructure/DependencyInjection.cs` inside `AddInfrastructure`:

```csharp
// Form handlers
services.AddScoped<CreateFormHandler>();
services.AddScoped<GetFormByIdHandler>();
services.AddScoped<GetFormsByUserHandler>();
services.AddScoped<UpdateFormHandler>();
services.AddScoped<DeleteFormHandler>();
services.AddScoped<CloseFormHandler>();
services.AddScoped<UpdateQuestionsHandler>();
```

Build: `dotnet build FormAI.sln` → `0 Error(s)`.

---

### Step 17 — Wire FormsController

The controller reads the current user's ID from the JWT token claims. ASP.NET Core parses the JWT and puts the claims in `HttpContext.User` automatically.

File: `src/FormAI.API/Controllers/FormsController.cs`

```csharp
using System.Security.Claims;
using FormAI.Application.Forms.CloseForm;
using FormAI.Application.Forms.CreateForm;
using FormAI.Application.Forms.DeleteForm;
using FormAI.Application.Forms.GetForm;
using FormAI.Application.Forms.UpdateForm;
using FormAI.Application.Forms.UpdateQuestions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FormAI.API.Controllers;

[ApiController]
[Route("api/forms")]
[Authorize]
public class FormsController : ControllerBase
{
    private readonly CreateFormHandler _create;
    private readonly GetFormByIdHandler _getById;
    private readonly GetFormsByUserHandler _getByUser;
    private readonly UpdateFormHandler _update;
    private readonly DeleteFormHandler _delete;
    private readonly CloseFormHandler _close;
    private readonly UpdateQuestionsHandler _updateQuestions;

    public FormsController(
        CreateFormHandler create,
        GetFormByIdHandler getById,
        GetFormsByUserHandler getByUser,
        UpdateFormHandler update,
        DeleteFormHandler delete,
        CloseFormHandler close,
        UpdateQuestionsHandler updateQuestions)
    {
        _create = create;
        _getById = getById;
        _getByUser = getByUser;
        _update = update;
        _delete = delete;
        _close = close;
        _updateQuestions = updateQuestions;
    }

    // Helper: reads the user's Guid from the JWT "sub" claim
    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException());

    // POST /api/forms
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateFormRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _create.HandleAsync(request, CurrentUserId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    // GET /api/forms
    [HttpGet]
    public async Task<IActionResult> GetMyForms(CancellationToken cancellationToken)
    {
        var forms = await _getByUser.HandleAsync(CurrentUserId, cancellationToken);
        return Ok(forms);
    }

    // GET /api/forms/{id}
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        // Try to get userId from JWT if present; anonymous users get null
        Guid? userId = null;
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        if (sub is not null) userId = Guid.Parse(sub);

        var form = await _getById.HandleAsync(id, userId, cancellationToken);
        return Ok(form);
    }

    // PUT /api/forms/{id}
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateFormRequest request,
        CancellationToken cancellationToken)
    {
        var cmd = request with { FormId = id, RequestingUserId = CurrentUserId };
        await _update.HandleAsync(cmd, cancellationToken);
        return NoContent();
    }

    // DELETE /api/forms/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _delete.HandleAsync(new DeleteFormRequest(id, CurrentUserId), cancellationToken);
        return NoContent();
    }

    // PATCH /api/forms/{id}/close
    [HttpPatch("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken cancellationToken)
    {
        await _close.HandleAsync(new CloseFormRequest(id, CurrentUserId), cancellationToken);
        return NoContent();
    }

    // PUT /api/forms/{id}/questions
    [HttpPut("{id:guid}/questions")]
    public async Task<IActionResult> UpdateQuestions(
        Guid id,
        [FromBody] UpdateQuestionsRequest request,
        CancellationToken cancellationToken)
    {
        var cmd = request with { FormId = id, RequestingUserId = CurrentUserId };
        await _updateQuestions.HandleAsync(cmd, cancellationToken);
        return NoContent();
    }
}
```

> **Why `[AllowAnonymous]` on `GetById`:** public forms must be accessible without a token (respondents don't have accounts). The handler checks `IsPublic` and compares the `requestingUserId` — so private forms still return 403 to anonymous callers.

> **Why `request with { FormId = id, ... }`:** C# records support `with` expressions that copy a record and override specific properties. The `FormId` and `RequestingUserId` come from the route/JWT, not the request body — this pattern keeps the body clean while still using the full record.

---

### Step 18 — Test the Forms CRUD

Run: `dotnet run --project src/FormAI.API`

Open Swagger at `http://localhost:5155/swagger`.

**Authenticate first:** click "Authorize" in Swagger, paste your access token from the login step as `Bearer <token>`.

**1. Create a form**
```
POST /api/forms
{
  "title": "Geography Quiz",
  "description": "Test your knowledge",
  "isPublic": true,
  "expiresAt": null,
  "showResultsAfterSubmit": true
}
```
Expected: `201 Created` — save the `id` from the response.

**2. List my forms**
```
GET /api/forms
```
Expected: `200` with the form you just created.

**3. Get form by id (as owner)**
```
GET /api/forms/{id}
```
Expected: `200` with questions array empty `[]`.

**4. Add questions**
```
PUT /api/forms/{id}/questions
{
  "questions": [
    {
      "text": "What is the capital of France?",
      "type": 1,
      "order": 1,
      "isRequired": true,
      "points": 10,
      "correctAnswer": "Paris",
      "options": [
        { "text": "Paris", "order": 1, "isCorrect": true },
        { "text": "London", "order": 2, "isCorrect": false }
      ]
    }
  ]
}
```
Expected: `204 No Content`.

**5. Get form again — questions should appear**
```
GET /api/forms/{id}
```
Expected: `200` with 1 question and 2 options.

**6. Close the form**
```
PATCH /api/forms/{id}/close
```
Expected: `204 No Content`.

**7. Try to edit with a different user → 403**
Register a second user, log in, get their token, then:
```
DELETE /api/forms/{id}    (using second user's token)
```
Expected: `403 Forbidden`.

**8. Delete the form**
```
DELETE /api/forms/{id}   (using original owner's token)
```
Expected: `204 No Content`.

---

## Implementation Order

Follow this sequence — each step is verifiable before the next depends on it:

1. ✅ Scaffold solution + project references
2. ✅ Install NuGet packages
3. ✅ Write Domain entities + enums
4. ✅ Write Application interfaces, DTOs, exception types (no handlers yet)
5. ✅ Write Infrastructure DbContext + configurations + DependencyInjection
6. ✅ Wire appsettings.Development.json + Program.cs minimally (Step 5)
7. ✅ Start Docker PostgreSQL (Step 6)
8. ✅ Generate + apply EF Core migrations → verify tables (Steps 7-8)
9. ✅ Implement repositories (Step 9)
10. ✅ Smoke-test repositories against real DB (Step 10)
11. ← **YOU ARE HERE** Write repository integration tests (Step 11)
12. Implement Auth handlers: Register, Login, Refresh (Step 14)
13. Wire AuthController + test full auth flow (Step 15)
14. Add domain methods + DTOs + Form handlers: Create, GetById, GetByUser, Update, Delete, Close, UpdateQuestions (Step 16)
15. Wire FormsController + test full CRUD (Steps 17-18)
16. Cleanup Program.cs (Step 13)

---

## End-to-End Verification (Final Goal)

```bash
# 1. Register
curl -X POST http://localhost:5155/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"name":"Test","email":"test@example.com","password":"Test1234"}'
# → 201 Created

# 2. Login — copy the accessToken and refreshToken
curl -X POST http://localhost:5155/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","password":"Test1234"}'
# → 200 { "accessToken": "eyJ...", "refreshToken": "..." }

# 3. Set token in shell variable
TOKEN="<paste accessToken here>"

# 4. Protected endpoint without token → 401
curl http://localhost:5155/api/forms

# 5. Create a form
curl -X POST http://localhost:5155/api/forms \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"title":"Quiz 1","isPublic":true,"showResultsAfterSubmit":true}'
# → 201 { "id": "...", "title": "Quiz 1" } — save the id as FORM_ID

FORM_ID="<paste id here>"

# 6. Add questions
curl -X PUT http://localhost:5155/api/forms/$FORM_ID/questions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"questions":[{"text":"2+2?","type":1,"order":1,"isRequired":true,"options":[{"text":"3","order":1,"isCorrect":false},{"text":"4","order":2,"isCorrect":true}]}]}'
# → 204 No Content

# 7. Get the form (verify questions appear)
curl http://localhost:5155/api/forms/$FORM_ID \
  -H "Authorization: Bearer $TOKEN"
# → 200 with questions array

# 8. Close form
curl -X PATCH http://localhost:5155/api/forms/$FORM_ID/close \
  -H "Authorization: Bearer $TOKEN"
# → 204 No Content

# 9. Delete form
curl -X DELETE http://localhost:5155/api/forms/$FORM_ID \
  -H "Authorization: Bearer $TOKEN"
# → 204 No Content
```

---

## Critical Files

| File | Purpose |
|---|---|
| `src/FormAI.API/appsettings.Development.json` | DB connection string + JWT config |
| `src/FormAI.API/Program.cs` | DI wiring, middleware pipeline |
| `src/FormAI.API/Middleware/ExceptionHandlingMiddleware.cs` | Converts exceptions to HTTP responses |
| `src/FormAI.API/Controllers/AuthController.cs` | POST /register, /login, /refresh |
| `src/FormAI.API/Controllers/FormsController.cs` | Full forms CRUD |
| `docker-compose.yml` | PostgreSQL local dev (form_ai / form_ai123 / form_ai) |
| `src/FormAI.Infrastructure/Data/AppDbContext.cs` | EF Core context + config discovery |
| `src/FormAI.Infrastructure/DependencyInjection.cs` | Registers all services, repos, handlers |
| `src/FormAI.Application/Users/Auth/RegisterHandler.cs` | Register use case |
| `src/FormAI.Application/Users/Auth/LoginHandler.cs` | Login use case |
| `src/FormAI.Application/Users/Auth/RefreshTokenHandler.cs` | Token rotation use case |
| `src/FormAI.Application/Forms/CreateForm/CreateFormHandler.cs` | Create form use case |
| `src/FormAI.Application/Forms/GetForm/GetFormByIdHandler.cs` | Get form + questions |
| `src/FormAI.Application/Forms/GetForm/GetFormsByUserHandler.cs` | List user's forms |
| `src/FormAI.Application/Forms/UpdateForm/UpdateFormHandler.cs` | Update metadata (owner only) |
| `src/FormAI.Application/Forms/DeleteForm/DeleteFormHandler.cs` | Delete form (owner only) |
| `src/FormAI.Application/Forms/CloseForm/CloseFormHandler.cs` | Close form (owner only) |
| `src/FormAI.Application/Forms/UpdateQuestions/UpdateQuestionsHandler.cs` | Replace question list (owner only) |
| `src/FormAI.Domain/Entities/Form.cs` | Add Update(), Close(), IsExpired, ReplaceQuestions() |
| `src/FormAI.Domain/Entities/FormQuestion.cs` | Add SetOptions() |
