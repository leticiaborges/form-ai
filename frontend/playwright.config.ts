import path from "node:path";
import { defineConfig, devices } from "@playwright/test";
import { API_URL, WEB_PORT, WEB_URL } from "./e2e/env";

// Locally the database password lives in the repo-root .env (read by Docker Compose, not by Node).
// Resolved relative to this file, not process.cwd() — the Playwright VS Code extension runs with a
// different cwd than the CLI, so a "../.env" relative path silently misses the file there.
// loadEnvFile never overrides a variable that is already set, so CI's own values win.
try {
  process.loadEnvFile(path.resolve(import.meta.dirname, "../.env"));
} catch {
  // No .env (CI): E2E_DB_CONNECTION must be provided instead.
}

if (!process.env.E2E_DB_CONNECTION && !process.env.APP_DB_PASSWORD) {
  throw new Error("Set E2E_DB_CONNECTION, or APP_DB_PASSWORD in the repo-root .env.");
}

if (!process.env.JWT_SECRET) {
  throw new Error("Set JWT_SECRET in the repo-root .env.");
}

const DB =
  process.env.E2E_DB_CONNECTION ??
  `Host=localhost;Port=5432;Database=form_ai_e2e;Username=form_ai_app;Password=${process.env.APP_DB_PASSWORD}`;

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: WEB_URL,
    trace: "on-first-retry",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: [
    {
      command: "dotnet run --project ../src/FormAI.API --no-launch-profile",
      url: `${API_URL}/health`,
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        ASPNETCORE_ENVIRONMENT: "Testing",
        ASPNETCORE_URLS: API_URL,
        ConnectionStrings__DefaultConnection: DB,
        ConnectionStrings__Redis: "localhost:6379",
        Jwt__Secret: process.env.JWT_SECRET!,
        Jwt__Issuer: "formai-e2e",
        Jwt__Audience: "formai-e2e",
        Claude__ApiKey: "unused-in-e2e",
        Email__SmtpHost: "localhost",
        Email__SmtpPort: "1025",
        Email__FromAddress: "no-reply@formai.test",
        Email__FromName: "FormAI E2E",
        Email__FrontendBaseUrl: WEB_URL,
      },
    },
    {
      command: `npm run dev -- --port ${WEB_PORT} --strictPort`,
      url: WEB_URL,
      reuseExistingServer: false,
      env: { API_URL },
    },
  ],
});
