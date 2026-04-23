# FormAI — Phase 2: Frontend + Email Verification

## Context

Phase 1 delivered the backend (ASP.NET Core Clean Architecture, EF Core, JWT auth, Form CRUD). Phase 2 adds the user-facing interface: a React + TypeScript single-page app with Landing, Register, and Login pages. Registration requires email confirmation, so the backend also needs email verification support.

**Token security design:** Rather than storing the raw token on `User`, a dedicated `UserToken` table holds a SHA-256 hash of the token. Only the raw token is sent to the user (in the email URL); the database never contains it. On verification, the incoming token is hashed and compared to the stored hash — same pattern used for API keys.

The user is learning React/TypeScript and will execute every step manually, so this plan is tutorial-level detail.

---

## Overview

```
Section 1 — Backend: Email Verification (21 steps)
Section 2 — Frontend: Project Setup (5 steps)
Section 3 — Frontend: Implementation (12 steps)
Section 4 — Verification: End-to-End Test Walkthrough
```

---

## Section 1 — Backend: Email Verification

### 1.1 — Install MailKit into Infrastructure

```bash
cd C:\projetos-git\forms-project
dotnet add src/FormAI.Infrastructure/FormAI.Infrastructure.csproj package MailKit
```

MailKit is the standard .NET library for sending email via SMTP.

---

### 1.2 — Extend `User.cs` domain entity

**File:** `src/FormAI.Domain/Entities/User.cs`

Add `IsEmailVerified` and `VerifiedAt` to track the verified state. The token itself moves to a separate table (next steps). Replace the file content:

```csharp
namespace FormAI.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    // Email verification state (Phase 2)
    public DateTime? ConfirmationSentAt { get; private set;}
    public DateTime? PendingRegistrationExpiresAt { get; private set;}
    public bool IsEmailVerified { get; private set; }
    public DateTime? VerifiedAt { get; private set; }

    public List<Form>? Forms { get; private set; }
    public List<RefreshToken>? RefreshTokens { get; private set; }

    private User() { }

    public static User Create(string name, string email, string passwordHash)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = email,
            PasswordHash = passwordHash,
            CreatedAt = DateTime.UtcNow,
            IsEmailVerified = false,
        };
    }

    // Called by VerifyEmailHandler after validating the token
    public void MarkAsVerified()
    {
        IsEmailVerified = true;
        VerifiedAt = DateTime.UtcNow;
    }
}
```

---

### 1.3 — Update `UserConfiguration.cs` (EF Core mapping)

**File:** `src/FormAI.Infrastructure/Data/Configurations/UserConfiguration.cs`

The existing file already maps `Name`, `Email`, `PasswordHash`, `CreatedAt`, and both relationships. Add **only** the two new property mappings before the closing brace of `Configure()`:

```csharp
// Phase 2 additions
builder.Property(u => u.IsEmailVerified)
    .IsRequired()
    .HasDefaultValue(false);

builder.Property(u => u.VerifiedAt)
    .IsRequired(false);
```

---

### 1.4 — Add `UpdateAsync` to `IUserRepository`

**File:** `src/FormAI.Application/Interfaces/IUserRepository.cs`

Add this method signature to the interface:

```csharp
Task UpdateAsync(User user, CancellationToken cancellationToken = default);
```

---

### 1.5 — Implement `UpdateAsync` in `UserRepository.cs`

**File:** `src/FormAI.Infrastructure/Repositories/UserRepository.cs`

Add this method to the class:

```csharp
public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
{
    // Entity was loaded in the same DbContext scope, so it's already tracked.
    // SaveChanges picks up changes from MarkAsVerified() automatically.
    await _context.SaveChangesAsync(cancellationToken);
}
```

---

### 1.6 — Create `TokenPurpose` enum

**New file:** `src/FormAI.Domain/Enums/TokenPurpose.cs`

```csharp
namespace FormAI.Domain.Enums;

public enum TokenPurpose
{
    EmailConfirmation = 0,
}
```

Adding it to the `Enums` folder keeps it alongside the other enums (`QuestionType`, etc.) already in the domain.

---

### 1.7 — Create `UserToken` domain entity

**New file:** `src/FormAI.Domain/Entities/UserToken.cs`

```csharp
using FormAI.Domain.Enums;

namespace FormAI.Domain.Entities;

public class UserToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public TokenPurpose Purpose { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsUsed => UsedAt is not null;
    public bool IsActive => !IsExpired && !IsUsed;

    private UserToken() { }

    public static UserToken Create(Guid userId, string tokenHash, TokenPurpose purpose, DateTime expiresAt)
    {
        return new UserToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            Purpose = purpose,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
        };
    }

    // Consumes the token so it cannot be reused
    public void MarkUsed()
    {
        UsedAt = DateTime.UtcNow;
    }
}
```

---

### 1.8 — Create `UserTokenConfiguration` (EF Core mapping)

**New file:** `src/FormAI.Infrastructure/Data/Configurations/UserTokenConfiguration.cs`

```csharp
using FormAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormAI.Infrastructure.Data.Configurations;

public class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(64); // SHA-256 hex string is always 64 characters

        // Non-unique index: fast lookup by hash; uniqueness is not required
        builder.HasIndex(t => t.TokenHash);

        builder.Property(t => t.Purpose)
            .IsRequired();

        builder.Property(t => t.ExpiresAt)
            .IsRequired();

        builder.Property(t => t.UsedAt)
            .IsRequired(false);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

---

### 1.9 — Add `UserTokens` DbSet to `AppDbContext`

**File:** `src/FormAI.Infrastructure/Data/AppDbContext.cs`

Add one line inside the class, alongside the other DbSet properties:

```csharp
public DbSet<UserToken> UserTokens => Set<UserToken>();
```

---

### 1.10 — Create `IUserTokenRepository`

**New file:** `src/FormAI.Application/Interfaces/IUserTokenRepository.cs`

```csharp
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Application.Interfaces;

public interface IUserTokenRepository
{
    Task AddAsync(UserToken token, CancellationToken cancellationToken = default);
    Task<UserToken?> FindActiveAsync(string tokenHash, TokenPurpose purpose,
        CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

`FindActiveAsync` filters by hash + purpose + not used + not expired in a single query.
`SaveChangesAsync` is exposed so the handler can persist `MarkUsed()` and `MarkAsVerified()` in one round-trip.

---

### 1.11 — Implement `UserTokenRepository`

**New file:** `src/FormAI.Infrastructure/Repositories/UserTokenRepository.cs`

```csharp
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using FormAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FormAI.Infrastructure.Repositories;

public class UserTokenRepository : IUserTokenRepository
{
    private readonly AppDbContext _context;

    public UserTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(UserToken token, CancellationToken cancellationToken = default)
    {
        await _context.UserTokens.AddAsync(token, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserToken?> FindActiveAsync(string tokenHash, TokenPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        return await _context.UserTokens
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash &&
                t.Purpose == purpose &&
                t.UsedAt == null &&
                t.ExpiresAt > DateTime.UtcNow,
                cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
```

---

### 1.12 — Create `IEmailService` interface

**New file:** `src/FormAI.Application/Interfaces/IEmailService.cs`

```csharp
namespace FormAI.Application.Interfaces;

public interface IEmailService
{
    Task SendVerificationEmailAsync(string toEmail, string toName, string token,
        CancellationToken cancellationToken = default);
}
```

---

### 1.13 — Create `EmailSettings` and `EmailService` in Infrastructure

**New file:** `src/FormAI.Infrastructure/Email/EmailSettings.cs`

```csharp
namespace FormAI.Infrastructure.Email;

public class EmailSettings
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
    // Used to build the verification link inserted into the email body
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
}
```

**New file:** `src/FormAI.Infrastructure/Email/EmailService.cs`

```csharp
using FormAI.Application.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FormAI.Infrastructure.Email;

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;

    public EmailService(IOptions<EmailSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task SendVerificationEmailAsync(string toEmail, string toName, string token,
        CancellationToken cancellationToken = default)
    {
        var verifyUrl = $"{_settings.FrontendBaseUrl}/verify-email?token={Uri.EscapeDataString(token)}";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = "Confirm your FormAI account";

        var bodyBuilder = new BodyBuilder
        {
            TextBody = $"Hi {toName},\n\nPlease verify your email by visiting:\n{verifyUrl}\n\nThis link expires in 24 hours.",
            HtmlBody = $"""
                <!DOCTYPE html>
                <html>
                <body style="font-family: sans-serif; color: #333;">
                  <h2>Welcome to FormAI, {toName}!</h2>
                  <p>Click the button below to confirm your email address.</p>
                  <p>
                    <a href="{verifyUrl}"
                       style="display:inline-block;padding:12px 24px;background:#6366f1;
                              color:#fff;border-radius:6px;text-decoration:none;font-weight:bold;">
                      Verify Email
                    </a>
                  </p>
                  <p style="color:#888;font-size:12px;">
                    If you did not create an account, you can safely ignore this email.<br>
                    This link expires in 24 hours.
                  </p>
                </body>
                </html>
                """
        };

        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        // SecureSocketOptions.Auto: uses STARTTLS on 587, plain on 1025 (Mailpit)
        await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort,
            SecureSocketOptions.Auto, cancellationToken);

        if (!string.IsNullOrEmpty(_settings.Username))
            await client.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
```

---

### 1.14 — Create the `VerifyEmail` use case

**New file:** `src/FormAI.Application/Users/Auth/VerifyEmailRequest.cs`

```csharp
namespace FormAI.Application.Users.Auth;

public record VerifyEmailRequest(string Token);
```

**New file:** `src/FormAI.Application/Users/Auth/VerifyEmailHandler.cs`

```csharp
using System.Security.Cryptography;
using System.Text;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Enums;

namespace FormAI.Application.Users.Auth;

public class VerifyEmailHandler
{
    private readonly IUserRepository _users;
    private readonly IUserTokenRepository _userTokens;

    public VerifyEmailHandler(IUserRepository users, IUserTokenRepository userTokens)
    {
        _users = users;
        _userTokens = userTokens;
    }

    public async Task HandleAsync(VerifyEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["Token"] = ["Token is required."]
            });

        // Hash the incoming raw token to find the matching stored hash
        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));

        var userToken = await _userTokens.FindActiveAsync(
            tokenHash, TokenPurpose.EmailConfirmation, cancellationToken);

        if (userToken is null)
            throw new NotFoundException("Invalid or expired verification token.");

        var user = await _users.GetByIdAsync(userToken.UserId, cancellationToken);

        if (user is null)
            throw new NotFoundException("User not found.");

        if (user.IsEmailVerified)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["Email"] = ["This email is already verified."]
            });

        userToken.MarkUsed();
        user.MarkAsVerified();

        // Both entities are tracked by the same scoped DbContext —
        // one SaveChanges call persists both changes atomically.
        await _userTokens.SaveChangesAsync(cancellationToken);
    }
}
```

---

### 1.15 — Update `RegisterHandler.cs`

**File:** `src/FormAI.Application/Users/Auth/RegisterHandler.cs`

Replace the file content:

```csharp
using System.Security.Cryptography;
using System.Text;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Application.Users.Auth;

public class RegisterHandler
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IEmailService _emailService;
    private readonly IUserTokenRepository _userTokens;

    public RegisterHandler(IUserRepository users, IPasswordHasher hasher,
        IEmailService emailService, IUserTokenRepository userTokens)
    {
        _users = users;
        _hasher = hasher;
        _emailService = emailService;
        _userTokens = userTokens;
    }

    public async Task<RegisterUserResponse> HandleAsync(RegisterUserRequest request,
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

        // Generate raw token (sent to user) and hash (stored in DB — raw never persisted)
        var tokenBytes = RandomNumberGenerator.GetBytes(64);
        var rawToken = Convert.ToBase64String(tokenBytes)
            .Replace("+", "-").Replace("/", "_").Replace("=", ""); // URL-safe base64

        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        var userToken = UserToken.Create(
            user.Id, tokenHash, TokenPurpose.EmailConfirmation,
            DateTime.UtcNow.AddHours(24));

        await _userTokens.AddAsync(userToken, cancellationToken);

        // Awaited deliberately — if the email service is down, the caller gets a 500
        // and knows registration did not complete cleanly.
        await _emailService.SendVerificationEmailAsync(
            user.Email, user.Name, rawToken, cancellationToken);

        return new RegisterUserResponse(user.Id, user.Name, user.Email);
    }
}
```

---

### 1.16 — Add `verify-email` endpoint to `AuthController.cs`

**File:** `src/FormAI.API/Controllers/AuthController.cs`

Add the `VerifyEmailHandler` dependency and a new action:

```csharp
// Add field:
private readonly VerifyEmailHandler _verifyEmailHandler;

// Add to constructor parameters and body:
_verifyEmailHandler = verifyEmailHandler;

// New action:
[HttpPost("verify-email")]
public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request,
    CancellationToken cancellationToken)
{
    await _verifyEmailHandler.HandleAsync(request, cancellationToken);
    return Ok(new { message = "Email verified successfully." });
}
```

---

### 1.17 — Register the new services in `DependencyInjection.cs`

**File:** `src/FormAI.Infrastructure/DependencyInjection.cs`

Add this using statement at the top:

```csharp
using FormAI.Infrastructure.Email;
```

Then add these lines inside `AddInfrastructure`, after the `AddScoped<IPasswordHasher>` line:

```csharp
// Email
services.Configure<EmailSettings>(configuration.GetSection("Email"));
services.AddScoped<IEmailService, EmailService>();

// Token repository
services.AddScoped<IUserTokenRepository, UserTokenRepository>();

// New auth handlers
services.AddScoped<VerifyEmailHandler>();
```

---

### 1.18 — Add Email settings and CORS to configuration

**File:** `src/FormAI.API/appsettings.Development.json`

Add the `Email` section (Mailpit listens on port 1025 for SMTP):

```json
"Email": {
  "SmtpHost": "localhost",
  "SmtpPort": 1025,
  "FromAddress": "no-reply@formai.dev",
  "FromName": "FormAI",
  "FrontendBaseUrl": "http://localhost:5173"
}
```

**File:** `src/FormAI.API/Program.cs`

Add CORS support for the Vite dev server. Add this block **before** `var app = builder.Build();`:

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod());
});
```

Then add `app.UseCors("FrontendDev");` **before** `app.UseAuthentication();`:

```csharp
app.UseCors("FrontendDev");
app.UseAuthentication();
```

---

### 1.19 — Add Mailpit to `docker-compose.yml`

**File:** `docker-compose.yml` (repo root)

Add the `mailpit` service and `mailpit_data` volume:

```yaml
  mailpit:
    image: axllent/mailpit:latest
    container_name: form-ai-mailpit
    ports:
      - "1025:1025"   # SMTP — backend connects here
      - "8025:8025"   # Web inbox — open http://localhost:8025
    environment:
      MP_MAX_MESSAGES: 500
      MP_DATA_FILE: /data/mailpit.db
    volumes:
      - mailpit_data:/data
```

Also add `mailpit_data:` under the `volumes:` section at the bottom of the file.

---

### 1.20 — Run the EF Core migration

```bash
cd C:\projetos-git\forms-project

dotnet ef migrations add AddEmailVerification \
  --project src/FormAI.Infrastructure \
  --startup-project src/FormAI.API

dotnet ef database update \
  --project src/FormAI.Infrastructure \
  --startup-project src/FormAI.API
```

**Changes to the database:**
- Column `is_email_verified` (bool, NOT NULL, default false) added to `users`
- Column `verified_at` (timestamptz, nullable) added to `users`
- New table `user_tokens` with columns: `id`, `user_id`, `token_hash` (varchar 64), `purpose` (int), `expires_at`, `used_at` (nullable), `created_at`
- Non-unique index on `user_tokens.token_hash` for fast lookup

---

### 1.21 — Verify the backend compiles

```bash
dotnet build FormAI.sln
```

Expected: `Build succeeded. 0 Error(s)`. Fix any compiler errors before moving on.

---

## Section 2 — Frontend: Project Setup

### 2.1 — Scaffold the Vite + React + TypeScript project

From the **repo root**:

```bash
cd C:\projetos-git\forms-project
npm create vite@latest frontend -- --template react-ts
cd frontend
npm install
```

---

### 2.2 — Install all runtime dependencies

```bash
cd C:\projetos-git\forms-project\frontend
npm install react-router-dom axios react-hook-form zod @hookform/resolvers
```

| Package | Purpose |
|---|---|
| `react-router-dom` | Client-side routing (v6) |
| `axios` | HTTP client with interceptor support |
| `react-hook-form` | Performant forms with minimal re-renders |
| `zod` | TypeScript-first schema validation |
| `@hookform/resolvers` | Bridge between React Hook Form and Zod |

---

### 2.3 — Install and configure Tailwind CSS v4

Tailwind v4 no longer uses a `tailwind.config.js`. It's imported from CSS and configured via a Vite plugin.

```bash
npm install tailwindcss @tailwindcss/vite
```

**File to replace:** `frontend/vite.config.ts`

```typescript
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
  ],
  server: {
    proxy: {
      // Requests to /api are forwarded to the backend — no CORS in dev
      '/api': {
        target: 'http://localhost:5155',
        changeOrigin: true,
      },
    },
  },
})
```

**File to replace:** `frontend/src/index.css`

```css
@import "tailwindcss";
```

That single line is the entire Tailwind v4 setup. The Vite plugin scans your source files automatically.

---

### 2.4 — Clean up scaffold files

The default Vite template creates files you don't need. Delete them:

```bash
# Git Bash
cd C:\projetos-git\forms-project\frontend
rm src/App.css src/assets/react.svg public/vite.svg
```

---

### 2.5 — Create the folder structure

```bash
cd C:\projetos-git\forms-project\frontend\src
mkdir api components context pages types
```

Final structure:
```
frontend/src/
  api/          ← Axios instance + API call functions
  components/   ← Reusable UI components (Button, Input)
  context/      ← AuthContext
  pages/        ← One file per route
  types/        ← TypeScript interfaces
```

---

## Section 3 — Frontend Implementation

### 3.1 — TypeScript types

**New file:** `frontend/src/types/auth.ts`

```typescript
export interface AuthUser {
  id: string;
  name: string;
  email: string;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
}

export interface RegisterResponse {
  id: string;
  name: string;
  email: string;
}
```

---

### 3.2 — Axios instance with interceptors

**New file:** `frontend/src/api/axios.ts`

```typescript
import axios from 'axios';

// baseURL '/api' + Vite proxy = requests go to http://localhost:5155/api
const api = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
});

// Attach the access token to every outgoing request
api.interceptors.request.use((config) => {
  const token = localStorage.getItem('accessToken');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// On 401: clear stored credentials
api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      localStorage.removeItem('accessToken');
      localStorage.removeItem('refreshToken');
      localStorage.removeItem('user');
    }
    return Promise.reject(error);
  }
);

export default api;
```

**New file:** `frontend/src/api/auth.ts`

```typescript
import api from './axios';
import type { LoginResponse, RegisterResponse } from '../types/auth';

export async function registerUser(data: {
  name: string;
  email: string;
  password: string;
}): Promise<RegisterResponse> {
  const response = await api.post<RegisterResponse>('/auth/register', data);
  return response.data;
}

export async function loginUser(data: {
  email: string;
  password: string;
}): Promise<LoginResponse> {
  const response = await api.post<LoginResponse>('/auth/login', data);
  return response.data;
}

export async function verifyEmail(token: string): Promise<void> {
  await api.post('/auth/verify-email', { token });
}
```

---

### 3.3 — AuthContext

**New file:** `frontend/src/context/AuthContext.tsx`

```typescript
import { createContext, useContext, useState, useEffect, type ReactNode } from 'react';
import type { AuthUser } from '../types/auth';

interface AuthContextValue {
  user: AuthUser | null;
  isAuthenticated: boolean;
  login: (accessToken: string, refreshToken: string, user: AuthUser) => void;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);

  // Restore session from localStorage on first render
  useEffect(() => {
    const stored = localStorage.getItem('user');
    if (stored) {
      try {
        setUser(JSON.parse(stored));
      } catch {
        localStorage.removeItem('user');
      }
    }
  }, []);

  function login(accessToken: string, refreshToken: string, user: AuthUser) {
    localStorage.setItem('accessToken', accessToken);
    localStorage.setItem('refreshToken', refreshToken);
    localStorage.setItem('user', JSON.stringify(user));
    setUser(user);
  }

  function logout() {
    localStorage.removeItem('accessToken');
    localStorage.removeItem('refreshToken');
    localStorage.removeItem('user');
    setUser(null);
  }

  return (
    <AuthContext.Provider value={{ user, isAuthenticated: user !== null, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside <AuthProvider>');
  return context;
}
```

---

### 3.4 — Reusable UI components

**New file:** `frontend/src/components/Button.tsx`

```typescript
import type { ButtonHTMLAttributes } from 'react';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'outline';
  isLoading?: boolean;
}

export function Button({
  children,
  variant = 'primary',
  isLoading = false,
  className = '',
  disabled,
  ...props
}: ButtonProps) {
  const base =
    'inline-flex items-center justify-center rounded-lg px-5 py-2.5 text-sm font-semibold ' +
    'transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 ' +
    'disabled:opacity-50 disabled:cursor-not-allowed';

  const variants = {
    primary: 'bg-indigo-600 text-white hover:bg-indigo-700 active:bg-indigo-800',
    outline: 'border border-indigo-600 text-indigo-600 bg-transparent hover:bg-indigo-50',
  };

  return (
    <button className={`${base} ${variants[variant]} ${className}`} disabled={disabled || isLoading} {...props}>
      {isLoading ? (
        <span className="flex items-center gap-2">
          <svg className="animate-spin h-4 w-4" viewBox="0 0 24 24" fill="none">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
          </svg>
          Loading…
        </span>
      ) : children}
    </button>
  );
}
```

**New file:** `frontend/src/components/Input.tsx`

```typescript
import { forwardRef, type InputHTMLAttributes } from 'react';

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  error?: string;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ label, error, id, className = '', ...props }, ref) => {
    const inputId = id ?? label.toLowerCase().replace(/\s+/g, '-');
    return (
      <div className="flex flex-col gap-1">
        <label htmlFor={inputId} className="text-sm font-medium text-gray-700">
          {label}
        </label>
        <input
          id={inputId}
          ref={ref}
          className={
            'w-full rounded-lg border px-3 py-2 text-sm shadow-sm outline-none ' +
            'focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 ' +
            (error ? 'border-red-400 focus:ring-red-400 ' : 'border-gray-300 ') +
            className
          }
          {...props}
        />
        {error && <span className="text-xs text-red-500">{error}</span>}
      </div>
    );
  }
);
Input.displayName = 'Input';
```

---

### 3.5 — App routing and entry point

**File to replace:** `frontend/src/App.tsx`

```typescript
import { Routes, Route, Navigate } from 'react-router-dom';
import { LandingPage } from './pages/LandingPage';
import { RegisterPage } from './pages/RegisterPage';
import { RegisterSuccessPage } from './pages/RegisterSuccessPage';
import { VerifyEmailPage } from './pages/VerifyEmailPage';
import { LoginPage } from './pages/LoginPage';
import { DashboardPage } from './pages/DashboardPage';

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<LandingPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/register/success" element={<RegisterSuccessPage />} />
      <Route path="/verify-email" element={<VerifyEmailPage />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/dashboard" element={<DashboardPage />} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
```

**File to replace:** `frontend/src/main.tsx`

```typescript
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import App from './App';
import './index.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <App />
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>
);
```

---

### 3.6 — Landing page

**New file:** `frontend/src/pages/LandingPage.tsx`

```typescript
import { Link } from 'react-router-dom';
import { Button } from '../components/Button';

export function LandingPage() {
  return (
    <div className="min-h-screen bg-gradient-to-br from-indigo-50 to-white flex flex-col">
      <header className="flex items-center justify-between px-6 py-4 max-w-6xl mx-auto w-full">
        <span className="text-2xl font-bold text-indigo-600">FormAI</span>
        <nav className="flex gap-3">
          <Link to="/login"><Button variant="outline">Log in</Button></Link>
          <Link to="/register"><Button>Get started</Button></Link>
        </nav>
      </header>

      <main className="flex flex-col items-center justify-center flex-1 text-center px-6 gap-8">
        <div className="max-w-2xl">
          <h1 className="text-5xl font-extrabold text-gray-900 leading-tight">
            Build forms <span className="text-indigo-600">powered by AI</span>
          </h1>
          <p className="mt-4 text-lg text-gray-600">
            Describe what you need. FormAI generates smart, beautiful forms in
            seconds — ready to share with the world.
          </p>
        </div>
        <div className="flex gap-4 flex-wrap justify-center">
          <Link to="/register">
            <Button className="px-8 py-3 text-base">Start for free</Button>
          </Link>
          <Link to="/login">
            <Button variant="outline" className="px-8 py-3 text-base">Log in</Button>
          </Link>
        </div>
      </main>

      <footer className="text-center text-sm text-gray-400 py-6">
        © {new Date().getFullYear()} FormAI. All rights reserved.
      </footer>
    </div>
  );
}
```

---

### 3.7 — Register page

**New file:** `frontend/src/pages/RegisterPage.tsx`

```typescript
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Input } from '../components/Input';
import { Button } from '../components/Button';
import { registerUser } from '../api/auth';

const registerSchema = z
  .object({
    name: z.string().min(2, 'Name must be at least 2 characters').max(100),
    email: z.string().email('Enter a valid email address'),
    password: z
      .string()
      .min(8, 'Password must be at least 8 characters')
      .regex(/[A-Z]/, 'Must contain at least one uppercase letter')
      .regex(/[0-9]/, 'Must contain at least one number'),
    confirmPassword: z.string(),
  })
  .refine((data) => data.password === data.confirmPassword, {
    message: 'Passwords do not match',
    path: ['confirmPassword'],
  });

type RegisterFormData = z.infer<typeof registerSchema>;

export function RegisterPage() {
  const navigate = useNavigate();
  const [serverError, setServerError] = useState<string | null>(null);

  const { register, handleSubmit, formState: { errors, isSubmitting } } =
    useForm<RegisterFormData>({ resolver: zodResolver(registerSchema) });

  async function onSubmit(data: RegisterFormData) {
    setServerError(null);
    try {
      await registerUser({ name: data.name, email: data.email, password: data.password });
      navigate('/register/success');
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      setServerError(e.response?.data?.message ?? 'Registration failed. Please try again.');
    }
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-indigo-50 to-white flex items-center justify-center px-4">
      <div className="w-full max-w-md bg-white rounded-2xl shadow-md p-8">
        <div className="text-center mb-6">
          <Link to="/" className="text-2xl font-bold text-indigo-600">FormAI</Link>
          <h1 className="mt-4 text-xl font-semibold text-gray-900">Create your account</h1>
          <p className="text-sm text-gray-500 mt-1">
            Already have an account?{' '}
            <Link to="/login" className="text-indigo-600 hover:underline">Log in</Link>
          </p>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
          <Input label="Full name" type="text" autoComplete="name"
            error={errors.name?.message} {...register('name')} />
          <Input label="Email" type="email" autoComplete="email"
            error={errors.email?.message} {...register('email')} />
          <Input label="Password" type="password" autoComplete="new-password"
            error={errors.password?.message} {...register('password')} />
          <Input label="Confirm password" type="password" autoComplete="new-password"
            error={errors.confirmPassword?.message} {...register('confirmPassword')} />

          {serverError && (
            <div className="rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
              {serverError}
            </div>
          )}

          <Button type="submit" isLoading={isSubmitting} className="mt-2 w-full">
            Create account
          </Button>
        </form>
      </div>
    </div>
  );
}
```

---

### 3.8 — Register success page

**New file:** `frontend/src/pages/RegisterSuccessPage.tsx`

```typescript
import { Link } from 'react-router-dom';
import { Button } from '../components/Button';

export function RegisterSuccessPage() {
  return (
    <div className="min-h-screen bg-gradient-to-br from-indigo-50 to-white flex items-center justify-center px-4">
      <div className="w-full max-w-md bg-white rounded-2xl shadow-md p-8 text-center">
        <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-indigo-100">
          <svg className="h-8 w-8 text-indigo-600" fill="none" viewBox="0 0 24 24"
            stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round"
              d="M21.75 6.75v10.5a2.25 2.25 0 01-2.25 2.25H4.5a2.25 2.25 0 01-2.25-2.25V6.75m19.5 0A2.25 2.25 0 0019.5 4.5H4.5a2.25 2.25 0 00-2.25 2.25m19.5 0-9.75 6.75L2.25 6.75" />
          </svg>
        </div>
        <h1 className="text-2xl font-bold text-gray-900">Check your inbox</h1>
        <p className="mt-3 text-gray-600">
          We sent a verification email to your address. Click the link to activate your account.
        </p>
        <p className="mt-2 text-sm text-gray-400">
          The link expires in 24 hours. Check your spam folder if you don't see it.
        </p>
        <div className="mt-6">
          <Link to="/login">
            <Button variant="outline" className="w-full">Go to login</Button>
          </Link>
        </div>
      </div>
    </div>
  );
}
```

---

### 3.9 — Verify email page

This page reads the `?token=` query parameter, calls the backend on mount, and renders three states: loading → success or error.

**New file:** `frontend/src/pages/VerifyEmailPage.tsx`

```typescript
import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { verifyEmail } from '../api/auth';
import { Button } from '../components/Button';

type Status = 'loading' | 'success' | 'error';

export function VerifyEmailPage() {
  const [searchParams] = useSearchParams();
  const [status, setStatus] = useState<Status>('loading');
  const [errorMessage, setErrorMessage] = useState('');

  useEffect(() => {
    const token = searchParams.get('token');
    if (!token) {
      setErrorMessage('No verification token found in the URL.');
      setStatus('error');
      return;
    }
    verifyEmail(token)
      .then(() => setStatus('success'))
      .catch((err: unknown) => {
        const e = err as { response?: { data?: { message?: string } } };
        setErrorMessage(e.response?.data?.message ?? 'Verification failed. The link may have expired.');
        setStatus('error');
      });
  }, [searchParams]);

  if (status === 'loading') {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="flex flex-col items-center gap-3">
          <svg className="animate-spin h-10 w-10 text-indigo-600" viewBox="0 0 24 24" fill="none">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
          </svg>
          <p className="text-lg font-medium text-gray-700">Verifying your email…</p>
        </div>
      </div>
    );
  }

  if (status === 'success') {
    return (
      <div className="min-h-screen bg-gradient-to-br from-indigo-50 to-white flex items-center justify-center px-4">
        <div className="w-full max-w-md bg-white rounded-2xl shadow-md p-8 text-center">
          <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-green-100">
            <svg className="h-8 w-8 text-green-600" fill="none" viewBox="0 0 24 24"
              stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
            </svg>
          </div>
          <h1 className="text-2xl font-bold text-gray-900">Email verified!</h1>
          <p className="mt-3 text-gray-600">Your account is now active. You can log in.</p>
          <div className="mt-6">
            <Link to="/login"><Button className="w-full">Go to login</Button></Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-indigo-50 to-white flex items-center justify-center px-4">
      <div className="w-full max-w-md bg-white rounded-2xl shadow-md p-8 text-center">
        <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-red-100">
          <svg className="h-8 w-8 text-red-600" fill="none" viewBox="0 0 24 24"
            stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
          </svg>
        </div>
        <h1 className="text-2xl font-bold text-gray-900">Verification failed</h1>
        <p className="mt-3 text-gray-600">{errorMessage}</p>
        <div className="mt-6 flex flex-col gap-3">
          <Link to="/register">
            <Button variant="outline" className="w-full">Register again</Button>
          </Link>
        </div>
      </div>
    </div>
  );
}
```

---

### 3.10 — Login page

The JWT payload already includes `sub` (userId), `email`, and `name` claims — decoded client-side to populate `AuthContext`.

**New file:** `frontend/src/pages/LoginPage.tsx`

```typescript
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Input } from '../components/Input';
import { Button } from '../components/Button';
import { loginUser } from '../api/auth';
import { useAuth } from '../context/AuthContext';

const loginSchema = z.object({
  email: z.string().email('Enter a valid email address'),
  password: z.string().min(1, 'Password is required'),
});

type LoginFormData = z.infer<typeof loginSchema>;

export function LoginPage() {
  const navigate = useNavigate();
  const { login } = useAuth();
  const [serverError, setServerError] = useState<string | null>(null);

  const { register, handleSubmit, formState: { errors, isSubmitting } } =
    useForm<LoginFormData>({ resolver: zodResolver(loginSchema) });

  async function onSubmit(data: LoginFormData) {
    setServerError(null);
    try {
      const response = await loginUser(data);

      // Decode the JWT payload (middle segment, base64-encoded JSON)
      const payload = JSON.parse(atob(response.accessToken.split('.')[1]));
      login(response.accessToken, response.refreshToken, {
        id: payload.sub,
        name: payload.name,
        email: payload.email,
      });

      navigate('/dashboard');
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      setServerError(e.response?.data?.message ?? 'Login failed. Check your credentials.');
    }
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-indigo-50 to-white flex items-center justify-center px-4">
      <div className="w-full max-w-md bg-white rounded-2xl shadow-md p-8">
        <div className="text-center mb-6">
          <Link to="/" className="text-2xl font-bold text-indigo-600">FormAI</Link>
          <h1 className="mt-4 text-xl font-semibold text-gray-900">Welcome back</h1>
          <p className="text-sm text-gray-500 mt-1">
            Don't have an account?{' '}
            <Link to="/register" className="text-indigo-600 hover:underline">Sign up</Link>
          </p>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
          <Input label="Email" type="email" autoComplete="email"
            error={errors.email?.message} {...register('email')} />
          <Input label="Password" type="password" autoComplete="current-password"
            error={errors.password?.message} {...register('password')} />

          {serverError && (
            <div className="rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
              {serverError}
            </div>
          )}

          <Button type="submit" isLoading={isSubmitting} className="mt-2 w-full">
            Log in
          </Button>
        </form>
      </div>
    </div>
  );
}
```

---

### 3.11 — Dashboard stub page

A placeholder so the post-login redirect doesn't crash.

**New file:** `frontend/src/pages/DashboardPage.tsx`

```typescript
import { useAuth } from '../context/AuthContext';
import { Button } from '../components/Button';

export function DashboardPage() {
  const { user, logout } = useAuth();
  return (
    <div className="min-h-screen bg-gray-50 flex flex-col items-center justify-center gap-6">
      <h1 className="text-3xl font-bold text-gray-900">Welcome, {user?.name ?? 'User'}!</h1>
      <p className="text-gray-500">Dashboard coming in Phase 3.</p>
      <Button variant="outline" onClick={logout}>Log out</Button>
    </div>
  );
}
```

---

### 3.12 — Verify the frontend compiles

```bash
cd C:\projetos-git\forms-project\frontend
npm run build
```

Expected: `✓ built in Xs`. Fix any TypeScript errors before running the dev server.

---

## Section 4 — Verification: End-to-End Test

### Step 1 — Start infrastructure

```bash
cd C:\projetos-git\forms-project
docker compose up -d
# Verify: docker ps → should show form-ai-db + form-ai-mailpit
```

Open `http://localhost:8025` — this is the Mailpit inbox where all sent emails appear.

### Step 2 — Start the backend

```bash
dotnet run --project src/FormAI.API
# Listening on http://localhost:5155
```

### Step 3 — Start the frontend

```bash
cd frontend
npm run dev
# Vite dev server on http://localhost:5173
```

### Step 4 — Test registration

1. Open `http://localhost:5173` → Landing page.
2. Click "Get started" → Register page.
3. Test Zod client-side validation first (submit empty form, check all error messages).
4. Fill valid data: Name `Test User`, Email `test@example.com`, Password `Password123`, Confirm `Password123`.
5. Submit → should redirect to `/register/success`.

### Step 5 — Verify email in Mailpit

1. Open `http://localhost:8025` → you should see one email.
2. Open it. Click the "Verify Email" button — it opens `http://localhost:5173/verify-email?token=...`.

### Step 6 — Verify email page

The page shows a spinner briefly, then a green "Email verified!" screen.

### Step 7 — Login

1. Navigate to `/login`.
2. Enter the same credentials.
3. Submit → redirected to `/dashboard` with "Welcome, Test User!".
4. Click "Log out" → user and tokens cleared from localStorage.

### Step 8 — Test error cases

| Scenario | Expected result |
|---|---|
| Invalid token in URL | Red error card: "Invalid or expired verification token." |
| Duplicate email registration | Red banner on Register page: "This email is already registered." |
| Wrong password at login | Red banner: "Invalid user or password." |
| Already-verified token reused | Red banner: "This email is already verified." |

### Step 9 — Inspect the database (optional)

```bash
docker exec -it form-ai-db psql -U postgres -d form_ai
```

```sql
-- Check the user state
SELECT id, name, email, is_email_verified, verified_at FROM users;

-- Check the token table (raw token is never here — only the SHA-256 hash)
SELECT id, user_id, token_hash, purpose, expires_at, used_at FROM user_tokens;
-- After verification: used_at has a timestamp
\q
```

---

## Files created or modified

### Backend — modified
| File | Change |
|---|---|
| `src/FormAI.Domain/Entities/User.cs` | +`IsEmailVerified`, +`VerifiedAt`, +`MarkAsVerified()` |
| `src/FormAI.Infrastructure/Data/Configurations/UserConfiguration.cs` | +2 property mappings |
| `src/FormAI.Infrastructure/Data/AppDbContext.cs` | +`UserTokens` DbSet |
| `src/FormAI.Application/Interfaces/IUserRepository.cs` | +`UpdateAsync` signature |
| `src/FormAI.Infrastructure/Repositories/UserRepository.cs` | +`UpdateAsync` implementation |
| `src/FormAI.Application/Users/Auth/RegisterHandler.cs` | token hashing + `IUserTokenRepository` |
| `src/FormAI.API/Controllers/AuthController.cs` | +verify-email endpoint |
| `src/FormAI.Infrastructure/DependencyInjection.cs` | +Email + `IUserTokenRepository` + `VerifyEmailHandler` |
| `src/FormAI.API/Program.cs` | +CORS middleware |
| `src/FormAI.API/appsettings.Development.json` | +Email section |
| `docker-compose.yml` | +Mailpit service |

### Backend — new files
| File | Purpose |
|---|---|
| `src/FormAI.Domain/Enums/TokenPurpose.cs` | Enum for token types |
| `src/FormAI.Domain/Entities/UserToken.cs` | Token entity (stores hash only) |
| `src/FormAI.Infrastructure/Data/Configurations/UserTokenConfiguration.cs` | EF Core mapping |
| `src/FormAI.Application/Interfaces/IEmailService.cs` | Email interface |
| `src/FormAI.Application/Interfaces/IUserTokenRepository.cs` | Token repository interface |
| `src/FormAI.Infrastructure/Email/EmailSettings.cs` | SMTP configuration POCO |
| `src/FormAI.Infrastructure/Email/EmailService.cs` | MailKit implementation |
| `src/FormAI.Infrastructure/Repositories/UserTokenRepository.cs` | Token repository implementation |
| `src/FormAI.Application/Users/Auth/VerifyEmailRequest.cs` | Request record |
| `src/FormAI.Application/Users/Auth/VerifyEmailHandler.cs` | Use case handler |

### Frontend — new project (`frontend/`)
| File | Purpose |
|---|---|
| `vite.config.ts` | Vite + Tailwind + API proxy |
| `src/index.css` | Tailwind v4 import |
| `src/main.tsx` | App entry point with providers |
| `src/App.tsx` | Route definitions |
| `src/types/auth.ts` | TypeScript interfaces |
| `src/api/axios.ts` | Axios instance + interceptors |
| `src/api/auth.ts` | Auth API functions |
| `src/context/AuthContext.tsx` | Auth state + useAuth hook |
| `src/components/Button.tsx` | Reusable button |
| `src/components/Input.tsx` | Reusable labelled input |
| `src/pages/LandingPage.tsx` | Home `/` |
| `src/pages/RegisterPage.tsx` | Register `/register` |
| `src/pages/RegisterSuccessPage.tsx` | Post-register `/register/success` |
| `src/pages/VerifyEmailPage.tsx` | Email confirmation `/verify-email` |
| `src/pages/LoginPage.tsx` | Login `/login` |
| `src/pages/DashboardPage.tsx` | Stub `/dashboard` |
