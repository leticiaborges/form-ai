# Phase 33: End-to-end tests (Playwright) — first test

## Context

Phase 32 covers the frontend with Vitest + MSW: every test there talks to a _fake_ server. The backend has unit tests and one Testcontainers integration test. Nothing checks that the **real frontend, real API, real Postgres and real Redis** work together. The features that only exist across those seams have no protection at all:

- the anonymous respondent identity (`RespondentToken`, ADR 0003) — a `localStorage` value on the browser, matched by the API;
- publish → share link → answer, gated by `CheckUserAnswerAccess`;
- the real-time Results tab (SignalR + Redis backplane, ADR 0005), which no unit test can exercise;
- scoring done on the server and shown to the owner.

Goal of this phase: Playwright installed, wired to a hermetic local stack and to CI, and **one** test that proves the core loop. More tests come later, on top of the scaffolding built here.

## The one test

> **A published form collects a submission from an anonymous respondent, and the owner's open Results tab shows it live.**

Why this one, out of the candidates:

| Candidate                                      | Verdict                                                                                                                                                                                 |
| ---------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Register → verify email → log in               | Already covered piece by piece (Phase 32 `LoginPage`, backend handlers). Needs Mailpit scraping, which the seeding helper already needs, so it becomes cheap afterwards. **Second test**, not first.                                                            |
| Generate a form from text                      | Calls Anthropic: slow, costs money, non-deterministic. Needs a fake generation service. **Later**, once a fake exists.                                                                  |
| Edit questions in the editor                   | Component tests in Phase 32 cover most of it; drag-and-drop is the only real e2e-only part. **Later.**                                                                                  |
| **Publish → anonymous submit → owner sees it** | This is the product. It crosses every layer, includes the anonymous-identity mechanism, the expiry/visibility gate, scoring and SignalR. If this breaks, the app is broken. **Chosen.** |

Scenario (one `test()`, several `test.step()`s so a failure names the step):

1. **Owner logs in through the UI** and opens the form's editor → Results tab. It says "No submissions yet."
2. **An anonymous respondent** (a second, fresh browser context — no cookies, no `localStorage`) opens `/forms/{id}/answer` and sees the form title.
3. They press Submit without answering the required question → "This question is required." and **no** request is sent to `POST /submit`.
4. They pick the correct option and submit → "Thanks! Your response has been recorded."
5. They reload the page → "You've already responded to this form. Thanks!" (one submission per respondent, matched on `RespondentToken`).
6. **Back on the owner's page, with no reload**, the Results tab updates to "1 submission" and the option's count reflects the answer (SignalR `ResultsUpdated` → refetch).

Assertions use Playwright's auto-retrying `expect(locator)` so step 6 waits for the push instead of sleeping. If it turns out flaky, the fix is a longer `expect` timeout on that one assertion, never `waitForTimeout`.

## How the form is created (and why not via Claude, and not via the UI)

The test needs a published, graded form with known questions. Building it through the UI would test the editor, not this flow, and generating it needs Claude. Instead the test **seeds through the real HTTP API**, using Playwright's `request` fixture:

1. `POST /api/auth/register` → a user with a unique email (`e2e-<uuid>@example.com`). The API sends the confirmation email to Mailpit before it responds.
2. **Confirm the email.** `GET http://localhost:8025/api/v1/search?query=to:<email>` (Mailpit) → the message id; `GET /api/v1/message/{id}` → the `Text` body, which contains `.../verify-email?token=<token>`; extract the token and `POST /api/auth/verify-email` with `{ token }`. Login refuses an unconfirmed account (see "Prerequisite"), so this step is mandatory, not optional.
3. `POST /api/auth/login` → access token.
4. `POST /api/forms` (`CreateFormRequest`: `IsPublic`, `ExpiresAt`, `ShowResultsAfterSubmit`, `IsGraded`) → an empty form. This endpoint exists and needs no Claude.
5. `PUT /api/forms/{id}/editor` with two questions and `isPublic: true`, `isGraded: true`. The editor already generates question/option ids client-side (`crypto.randomUUID()`), and the save handler diffs against stored ids, so the seed script can send its own GUIDs.

Seed data: a `Single`, required question with 3 options and one `IsCorrect`, `Points: 2`; and an optional `Text` question. `ExpiresAt` = now + 1 day (must be in the future, or `CheckUserAnswerAccess` rejects it).

No further backend change is needed for this test. A fake `IFormGenerationService` becomes necessary only for the generation test (see "Next tests").

Mailpit is shared by every test running in parallel, so always search by the unique recipient address, never "latest message".

## Prerequisite: login requires a confirmed email (done)

While planning this phase we found that `LoginHandler` never read `IsEmailVerified`: an account could log in without ever opening the confirmation link, so the email step of registration guarded nothing. It was fixed before this phase, and the seeding above depends on it:

- `LoginHandler` now checks the password first, then `IsEmailVerified`. An unconfirmed account with the **correct** password gets `400 { message, code: "EmailNotVerified" }` (a standalone `ValidationException` with the new `ValidationErrorCode.EmailNotVerified`). A wrong password, or an unknown email, still gets the generic `404 "Invalid user or password."`, so the response never reveals that an account exists to someone who does not know its password.
- `LoginPage` already renders `response.data.message`, so the user sees "Please confirm your email address before logging in." with no frontend change.
- Covered by `tests/FormAI.UnitTests/Users/LoginTests.cs`.
- Known consequence: there is **no resend-confirmation endpoint** and the confirmation token lives 15 minutes (`PendingRegistrationExpiresAt`), so an account that misses its window can no longer log in. This is recorded in `docs/known-gaps.md`; it is not part of this phase. It also means the seeding helper must confirm right after registering.

## Decisions

| Concern          | Choice                                                                                                           | Why                                                                                                                                                                                                                                   |
| ---------------- | ---------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Runner           | **@playwright/test**                                                                                             | Auto-waiting locators, multiple browser contexts in one test (owner + anonymous respondent), built-in traces. Cypress cannot drive two contexts in one test, which this scenario needs.                                               |
| Browser          | **Chromium only**                                                                                                | One browser is enough for a smoke suite; add Firefox/WebKit later if a cross-browser bug ever appears. Keeps CI install small.                                                                                                        |
| Location         | `frontend/e2e/` + `frontend/playwright.config.ts`                                                                | One `package.json`, one `npm ci`, same Prettier/ESLint. The tests only talk to the app over HTTP, so a separate package would buy nothing.                                                                                            |
| Stack under test | Real API + real Postgres + real Redis + real Mailpit + **Vite dev server**                                       | The Vite dev server already proxies `/api` and `/hubs` (with `ws: true`), so the browser sees one origin, exactly as in dev and with no CORS. No production build/nginx to maintain.                                                  |
| Isolation        | Dedicated database `form_ai_e2e`, **separate ports** (API `5255`, Vite `5273`), `ASPNETCORE_ENVIRONMENT=Testing` | A run must never write to the developer's data or attach to a dev server that happens to be up. `Testing` also means the untracked `appsettings.Development.json` is not loaded, so the run does not depend on one machine's secrets. |
| Data cleanup     | None. Every test creates its own user with a unique email                                                        | No shared state between tests, so no ordering problems and no teardown to fail. The e2e database is disposable (`DROP DATABASE` when it gets big).                                                                                    |
| Login            | Through the UI in this test                                                                                      | It is the natural first step of the owner's journey. Later tests that only need a session should log in once and reuse `storageState` (keys: `accessToken`, `refreshToken`, `user`).                                                  |
| Selectors        | `getByRole` / `getByLabel` / `getByText`                                                                         | Same rule as Phase 32. No `data-testid`, no class names.                                                                                                                                                                              |

## Setup steps

### 1. Prerequisites (local, once)

Docker Compose up (`postgres`, `redis`, `mailpit`), then create the e2e database. The init script only creates `form_ai`, and the app role cannot create databases, so use the superuser:

```bash
docker exec form-ai-db psql -U postgres -c "CREATE DATABASE form_ai_e2e OWNER form_ai_app"
```

Apply migrations to it (from the repo root; `<APP_DB_PASSWORD>` is the value in your `.env`):

```bash
dotnet ef database update --project src/FormAI.Infrastructure --startup-project src/FormAI.API \
  --connection "Host=localhost;Port=5432;Database=form_ai_e2e;Username=form_ai_app;Password=<APP_DB_PASSWORD>"
```

Re-run this whenever a new migration is added. (Later: fold it into a `pretest:e2e` script.)

### 2. Install (from `frontend/`)

```bash
npm i -D @playwright/test
npx playwright install chromium
```

### 3. `frontend/package.json` — add scripts

```json
"test:e2e": "playwright test",
"test:e2e:ui": "playwright test --ui"
```

### 4. `frontend/vite.config.ts` — two changes

**a) Make the proxy target configurable**, so e2e can point Vite at port 5255 without touching dev defaults:

```ts
const apiTarget = process.env.API_URL ?? "http://localhost:5155";
// ...both "/api" and "/hubs" use `target: apiTarget`
```

**b) Stop Vitest from picking up the e2e specs.** Vitest's default `include` matches `**/*.spec.ts`, so `e2e/*.spec.ts` would be run by `npm test` and fail:

```ts
test: {
  include: ["src/**/*.test.{ts,tsx}"],
  // ...existing options
}
```

### 5. `frontend/playwright.config.ts`

```ts
import { defineConfig, devices } from "@playwright/test";

const API_PORT = 5255;
const WEB_PORT = 5273;
const API_URL = `http://localhost:${API_PORT}`;
const WEB_URL = `http://localhost:${WEB_PORT}`;
const DB =
  process.env.E2E_DB_CONNECTION ??
  "Host=localhost;Port=5432;Database=form_ai_e2e;Username=form_ai_app;Password=" +
    process.env.APP_DB_PASSWORD;

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
        Jwt__Secret: "e2e-only-secret-at-least-32-characters-long",
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
```

Check the exact `Jwt` / `Email` keys against `JwtSettings` and `EmailSettings` when writing (the names above follow the "Configuration" table in `CLAUDE.md`; any extra required property, e.g. token lifetime, must be added). `/health` is used as the readiness probe because it is anonymous and already exists.

### 6. `frontend/e2e/support/api.ts` — seeding helper

```ts
import { randomUUID } from "node:crypto";
import { expect, type APIRequestContext } from "@playwright/test";

const API_URL = "http://localhost:5255";
const MAILPIT_URL = process.env.MAILPIT_URL ?? "http://localhost:8025";
export const PASSWORD = "E2e-Passw0rd!"; // confirm it satisfies the register rules

// Registration awaits the SMTP send, so the mail is normally already there; poll briefly anyway.
async function confirmEmail(request: APIRequestContext, email: string) {
  let token = "";
  await expect
    .poll(async () => {
      const search = await request.get(`${MAILPIT_URL}/api/v1/search`, {
        params: { query: `to:${email}` },
      });
      const { messages } = await search.json();
      if (!messages?.length) return "";

      const mail = await (await request.get(`${MAILPIT_URL}/api/v1/message/${messages[0].ID}`)).json();
      token = /verify-email\?token=([^\s"<>]+)/.exec(mail.Text)?.[1] ?? "";
      return token;
    })
    .not.toBe("");

  const res = await request.post(`${API_URL}/api/auth/verify-email`, {
    data: { token: decodeURIComponent(token) },
  });
  expect(res.ok()).toBeTruthy();
}

export interface SeededForm {
  formId: string;
  title: string;
  owner: { email: string; password: string };
  question: { id: string; text: string; correct: string; wrong: string };
}

export async function seedPublishedGradedForm(request: APIRequestContext): Promise<SeededForm> {
  const email = `e2e-${randomUUID()}@example.com`;
  await request.post(`${API_URL}/api/auth/register`, {
    data: { name: "E2E Owner", email, password: PASSWORD },
  });
  await confirmEmail(request, email);
  const { accessToken } = await (
    await request.post(`${API_URL}/api/auth/login`, { data: { email, password: PASSWORD } })
  ).json();
  const auth = { Authorization: `Bearer ${accessToken}` };

  const expiresAt = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString();
  const title = `E2E form ${randomUUID().slice(0, 8)}`;

  const created = await request.post(`${API_URL}/api/forms`, {
    headers: auth,
    data: {
      title,
      description: "",
      isPublic: false,
      expiresAt,
      showResultsAfterSubmit: false,
      isGraded: true,
    },
  });
  const { id: formId } = await created.json();

  // ...PUT /api/forms/{formId}/editor with isPublic: true, isGraded: true and the two questions
  // described in "How the form is created". Use randomUUID() for question and option ids.
  // Assert every response with `expect(res.ok()).toBeTruthy()` so a seed failure reads as a seed failure.

  return {/* ... */} as SeededForm;
}
```

Failing loudly in the seed helper matters: without it, a bad seed shows up as a confusing UI failure three steps later.

### 7. `frontend/e2e/publish-answer-results.spec.ts` — the test

Shape only; fill in selectors against the real UI (check the labels in `LoginPage`, `QuestionAnswerCard`, `SummaryResultsTab`):

```ts
import { test, expect } from "@playwright/test";
import { seedPublishedGradedForm } from "./support/api";

test("a published form collects an anonymous submission that the owner sees live", async ({
  browser,
  request,
}) => {
  const form = await seedPublishedGradedForm(request);

  const ownerContext = await browser.newContext();
  const respondentContext = await browser.newContext(); // fresh: no localStorage, so a new RespondentToken
  const owner = await ownerContext.newPage();
  const respondent = await respondentContext.newPage();

  await test.step("owner opens the Results tab of the published form", async () => {
    await owner.goto("/login");
    await owner.getByLabel("Email").fill(form.owner.email);
    await owner.getByLabel("Password").fill(form.owner.password);
    await owner.getByRole("button", { name: "Log in" }).click();
    await owner.goto(`/forms/${form.formId}/edit?tab=results`);
    await expect(owner.getByText("No submissions yet.")).toBeVisible();
  });

  await test.step("respondent cannot submit without the required answer", async () => {
    await respondent.goto(`/forms/${form.formId}/answer`);
    await expect(respondent.getByText(form.title)).toBeVisible();
    await respondent.getByRole("button", { name: "Submit" }).click();
    await expect(respondent.getByText("This question is required.")).toBeVisible();
  });

  await test.step("respondent submits the correct answer", async () => {
    await respondent.getByRole("radio", { name: form.question.correct }).check();
    await respondent.getByRole("button", { name: "Submit" }).click();
    await expect(respondent.getByText("Thanks! Your response has been recorded.")).toBeVisible();
  });

  await test.step("respondent cannot submit twice", async () => {
    await respondent.reload();
    await expect(
      respondent.getByText("You've already responded to this form. Thanks!"),
    ).toBeVisible();
  });

  await test.step("owner sees the submission without reloading", async () => {
    await expect(owner.getByText("1 submission")).toBeVisible(); // pushed by SignalR
  });

  await ownerContext.close();
  await respondentContext.close();
});
```

To assert "no request sent" in the required-question step, register `respondent.on("request", ...)` before clicking and expect no `POST` to `/submit`.

### 8. Housekeeping

- `frontend/.gitignore`: add `test-results/`, `playwright-report/`, `blob-report/`.
- `frontend/eslint.config.js`: add `"playwright-report"` and `"test-results"` to `globalIgnores`.
- **TypeScript**: `tsconfig.app.json` only includes `src`, so `e2e/` and `playwright.config.ts` are not type-checked. Add them to `tsconfig.node.json`'s `include` (or a new `tsconfig.e2e.json` referenced from `tsconfig.json`) so `tsc -b` covers them. Node types are already installed.
- Prettier already formats the whole `frontend/` tree; run `npm run format` on the new files.
- The Husky pre-commit hook runs lint only. Do **not** add e2e to it — it needs the whole stack.

### 9. CI — new `e2e` job in `.github/workflows/ci.yml`

Add it beside `frontend` (runs in parallel). Because `deploy.yml` calls `ci.yml`, a red e2e blocks a deploy, which is what we want.

```yaml
e2e:
  runs-on: ubuntu-latest
  timeout-minutes: 15
  services:
    postgres:
      image: postgres:18
      env:
        POSTGRES_USER: postgres
        POSTGRES_PASSWORD: postgres
        POSTGRES_DB: form_ai_e2e
      ports: ["5432:5432"]
      options: >-
        --health-cmd "pg_isready -U postgres" --health-interval 5s --health-timeout 5s --health-retries 10
    redis:
      image: redis:8-alpine
      ports: ["6379:6379"]
    mailpit:
      image: axllent/mailpit:latest
      ports: ["1025:1025", "8025:8025"]
  env:
    E2E_DB_CONNECTION: Host=localhost;Port=5432;Database=form_ai_e2e;Username=postgres;Password=postgres
  steps:
    - uses: actions/checkout@v7
    - uses: actions/setup-dotnet@v6
      with:
        global-json-file: global.json
    - uses: actions/setup-node@v7
      with:
        node-version-file: frontend/.nvmrc
        cache: "npm"
        cache-dependency-path: frontend/package-lock.json
    - name: Apply migrations
      run: |
        dotnet tool install --global dotnet-ef   # skip if .config/dotnet-tools.json exists; use `dotnet tool restore`
        dotnet ef database update --project src/FormAI.Infrastructure --startup-project src/FormAI.API --connection "$E2E_DB_CONNECTION"
    - run: npm ci
      working-directory: frontend
    - run: npx playwright install --with-deps chromium
      working-directory: frontend
    - run: npm run test:e2e
      working-directory: frontend
    - uses: actions/upload-artifact@v5
      if: ${{ !cancelled() }}
      with:
        name: playwright-report
        path: frontend/playwright-report/
        retention-days: 7
```

CI connects as the `postgres` superuser (no init script, no app role), which is fine for a throwaway database. Verify the current major versions of the `actions/*` used here against the rest of the file.

### 10. Docs (per `CLAUDE.md` "Keeping the docs true")

- **Tests** section of `CLAUDE.md`: add an e2e bullet — Playwright in `frontend/e2e/`, `npm run test:e2e` from `frontend/`, needs Docker Compose (Postgres, Redis, Mailpit) and the `form_ai_e2e` database, starts its own API (5255) and Vite (5273), seeds data through the HTTP API, one user per test.
- **Commands** section: add the one-time database creation and migration commands from step 1.
- Already done with the prerequisite change, nothing to do here: `CLAUDE.md` "Business rules" (login requires a confirmed email), the `EmailNotVerified` code in its `ValidationErrorCode` list, and the "No way to resend the confirmation email" row in `docs/known-gaps.md`.

## Conventions for the e2e tests you write next

- One file per user journey, named for the behaviour: `publish-answer-results.spec.ts`. Use `test.step` so failures name the step.
- **Seed through the API, exercise through the UI.** Only drive the UI for the thing the test is about.
- Every test makes its own user and form (`randomUUID()`), never reuses another test's data.
- Two actors = two `browser.newContext()`. Never share a context between the owner and an anonymous respondent, or they share `localStorage`.
- Never `waitForTimeout`. Use web-first assertions (`expect(locator).toBeVisible()`), which retry.
- Use the domain vocabulary from `CONTEXT.md` in names and comments (submission, answer key, expires — not "response").

## Next tests (not in this phase)

Roughly by value:

1. **Register → confirm email → log in, through the UI.** The Mailpit plumbing already exists in the seeding helper (`confirmEmail`); reuse it, but open the link in the browser instead of posting the token. Also assert that logging in _before_ confirming shows "Please confirm your email address before logging in."
2. **Generate a form from text.** Needs a deterministic fake `IFormGenerationService`, registered only when `ASPNETCORE_ENVIRONMENT=Testing` (or point the `claude` `HttpClient` at a stub server). Do not call the real Anthropic API from e2e.
3. **Expired form and non-owner access.** Expiry is a domain rule: `GET {id}/answer` on an expired form shows the expiry message, a private form gives 404 to everyone (including the owner) on the answering path, and another user gets "not found" from the editor.
4. **Editor round-trip.** Edit a question, reorder options (dnd-kit — the one thing jsdom cannot test), save, reload, and check ids survive (ADR 0002).
5. **Grading changes.** Turn "Graded form" off and confirm scores disappear; change the answer key and confirm the owner's score distribution rescores (ADR 0004).

## Known gotchas

- **Vitest collects `*.spec.ts`.** Without the `include` change in step 4b, `npm test` tries to run Playwright specs and fails with a confusing import error.
- **Redis is mandatory at API startup.** `Program.cs` throws if `ConnectionStrings:Redis` is missing, so a missing Redis container shows up as a `webServer` timeout, not a test failure. Check `docker ps` first.
- **Mailpit is required twice over.** `RegisterHandler` calls SMTP synchronously (Mailpit down → registration returns 500), and the seed helper reads the confirmation token from Mailpit's HTTP API on port 8025 because login refuses unconfirmed accounts. In CI both `1025` and `8025` must be mapped (the job in step 9 already does).
- **Expiry is evaluated at request time.** Seeding `ExpiresAt` in the past, or a few seconds ahead, makes the answering page show the expired state. Use +1 day.
- **First run is slow** (dotnet build, browser download). `webServer.timeout` is 120 s for that reason; subsequent runs are fast.
- **SignalR auth goes over `?access_token=`** (not a header) and is scoped to `/hubs`. If step 6 fails while everything else passes, look at the WebSocket in the trace before touching the assertion.
- **The email-verification link uses `Email__FrontendBaseUrl`.** Point it at the e2e Vite port, or the future register test will follow a link to the dev server.

## Verification

1. Docker Compose is up; `form_ai_e2e` exists and is migrated (step 1).
2. `cd frontend && npm run test:e2e` → the API and Vite start on 5255/5273, **1 passed**.
3. `npm test` still passes and does **not** list any `e2e/` file.
4. `npm run lint`, `npm run format:check` and `npm run build` pass (`tsc -b` covers the new files).
5. Make the test fail on purpose, then revert: (a) change `"This question is required."` in `FormAnswerPage.tsx` → step 3 fails; (b) comment out `useFormResultsHub`'s callback in `ResultsTab.tsx` → step 6 fails and the trace shows the owner page never refetched. This proves the test is able to fail for the reasons it claims to guard.
6. Start the normal dev stack (5155/5173) and re-run: the e2e run must ignore it and leave the dev database untouched.
7. Push a branch and confirm the `e2e` job goes green in GitHub Actions and uploads `playwright-report` on failure.
