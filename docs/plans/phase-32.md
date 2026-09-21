# Phase 32: Frontend test suite (Vitest + Testing Library + MSW)

## Context

`frontend/` (React 19, TypeScript 6, Vite 8, react-hook-form + zod, axios, SignalR, recharts, dnd-kit) has **zero tests**. The CI `frontend` job only runs `npm run lint` and `npm run build`, so a regression in the answer flow, the editor payload or the axios 401 handling ships silently. The backend already has unit and integration tests, so the frontend is the unprotected half.

Goal: a small, fast, behaviour-focused suite that guards the flows that matter, plus one fully working example (`LoginPage`) that the other tests copy.

## Recommendation

| Concern | Choice | Why this over the alternative |
|---|---|---|
| Runner | **Vitest 5** | Reuses `vite.config.ts` (same React plugin, Tailwind plugin, TS/ESM handling). Jest would need a second transform pipeline (babel/ts-jest/SWC) and ESM workarounds for a Vite 8 + `"type": "module"` project. Jest-compatible API, so nothing to learn. Peer range covers Vite 8; Node 24 (`.nvmrc`) is fine. |
| Component testing | **@testing-library/react + user-event + jest-dom** | Queries by role/label/text, so tests read like the user's behaviour and survive refactors. Enzyme is dead; shallow rendering pins implementation details. The `Input` component already wires `label`↔`id`, so `getByLabelText` works out of the box. |
| Network | **MSW 2** (`msw/node`) | Intercepts at the network boundary, so the *real* `api/*.ts` modules, the axios instance and its interceptors all run. `vi.mock('../api/auth')` would skip the code most likely to break (interceptors, error shape `e.response.data.message`, `code === 'FormExpired'`). `onUnhandledRequest: 'error'` fails a test that makes an unexpected call. |
| DOM | **jsdom** | More complete than happy-dom (forms, `localStorage`, XHR that axios uses). Slower, but the suite is small. |
| Coverage | `@vitest/coverage-v8` | Zero config with Vitest. Report only at first, no threshold gate. |
| E2E (later, phase 2) | Playwright | Not now. Needs API + Postgres + Mailpit; worth 2–3 smoke flows only once the unit/integration layer exists. |

## Priorities — what gives useful coverage

Ordered by risk × cost. Write them in this order.

**Tier 1 — pure logic, cheapest, do first**
- `utils/textMatch.ts` — trim + lowercase (mirrors the backend scoring comparison).
- `utils/dateUtils.ts` — `utcToLocalDateTime`, `getDefaultFormatStringDateTime` (both formats, `null`), `addDays` across a month boundary. Pin `TZ` in the test or build dates with local constructors.
- `utils/respondentToken.ts` — generates once, then returns the same stored value (this is the anonymous-identity mechanism, ADR 0003).
- `utils/getErrorMessage.ts` — message present / fallback.
- `context/AuthProvider.tsx` — `login` persists three keys and sets user; `logout` clears them; **corrupt `user` JSON in localStorage is discarded**, not thrown.
- `api/axios.ts` — request interceptor adds `Bearer`; a 401 clears the three keys and redirects to `/login` (stub `window.location`).

**Tier 2 — critical flows, page level with MSW**
- `LoginPage` (**the example below**), `RegisterPage`, `VerifyEmailPage`.
- `FormAnswerPage` — the highest-value page: every `PageState` (`loading`, `alreadySubmitted`, `expired` via `code: 'FormExpired'`, generic `error`, `ready`, `submitted`); required-question validation blocks the POST; the submitted payload is right for each type (Single/Multiple send `selectedOptionIds`, Text `textValue`, Numeric `numericValue`, unanswered optional questions are omitted); server failure shows the error toast and returns to `ready`. `isAnswered` is not exported — test it through the page rather than exporting it.
- `CreateFormPage` — validation, the generate request payload, expiry default 7 days.
- `DashboardPage` — list, empty state, submission count.
- `FormEditorPage` / `FormEditorTab` — add/edit/delete question and option, duplicate option text rejected (mirrors `QuestionOptionValidator`), ticking "Graded form" seeds points to 1 (`DefaultQuestionPoints`), the `PUT /forms/{id}/editor` body keeps existing question/option ids (ADR 0002), expiry change.

**Tier 3 — components and hooks**
- `QuestionAnswerCard` — read-only mode, green/red tint against `grading.correctOptionTexts`, "No correct answer defined" box, Single vs Multiple vs Text vs Numeric inputs.
- `QuestionCard` / `OptionsList` / `PointsInput` (0–100 bounds) / `NumberInput`.
- `results/*` — `SummaryResultsTab`, `IndividualResultsTab`, `SubmissionPager`, `ValueAnswerList`. Assert the numbers and labels, **not the SVG**. recharts needs `ResizeObserver` and layout that jsdom lacks: stub `ResizeObserver` in `setup.ts` or `vi.mock` the chart components (`OptionBarChart`, `ScoreDistributionChart`, `SubmissionsPerFormChart`).
- `hooks/useFormResultsHub` — `vi.mock('@microsoft/signalr')`; assert it joins after start, calls back only when the payload `formId` matches, re-joins on `onreconnected`, calls `stop()` on unmount, and uses the latest callback without reconnecting.

**Deliberately not tested**
- Tailwind classes, exact layout, `main.tsx`, `types/`.
- dnd-kit drag gestures in jsdom (pointer geometry is unreliable). Extract the reorder into a pure function and test that, or leave it to Playwright.
- Chart pixel output.

## Setup steps (manual)

### 1. Install (from `frontend/`)

```bash
npm i -D vitest jsdom @testing-library/react @testing-library/dom \
  @testing-library/user-event @testing-library/jest-dom msw @vitest/coverage-v8
```

`@testing-library/dom` is a peer dependency of `@testing-library/react` v16 and must be installed explicitly.

### 2. `frontend/package.json` — add scripts

```json
"test": "vitest run",
"test:watch": "vitest",
"test:coverage": "vitest run --coverage"
```

### 3. `frontend/vite.config.ts` — import from `vitest/config` and add a `test` block

```ts
import { defineConfig } from 'vitest/config'
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
      '/hubs': {
        target: 'http://localhost:5155',
        changeOrigin: true,
        ws: true
      }
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    restoreMocks: true,
    unstubGlobals: true,
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/main.tsx', 'src/types/**', 'src/test/**', 'src/**/*.test.{ts,tsx}'],
    },
  },
})
```

No `globals: true`: tests import `describe/it/expect` from `vitest` explicitly, so `tsconfig.app.json` (`"types": ["vite/client"]`) needs no change. The jest-dom matcher types come from the `@testing-library/jest-dom/vitest` import in the setup file, which is inside `src` and therefore covered by `tsc -b`.

### 4. `frontend/src/test/server.ts`

```ts
import { setupServer } from 'msw/node'

// No default handlers: each test declares exactly the requests it expects.
export const server = setupServer()
```

### 5. `frontend/src/test/setup.ts`

```ts
import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { server } from './server'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))

afterEach(() => {
  cleanup() // no `globals: true`, so RTL cannot auto-register this
  server.resetHandlers()
  localStorage.clear()
})

afterAll(() => server.close())
```

### 6. `frontend/src/test/renderWithProviders.tsx`

```tsx
import { render } from '@testing-library/react'
import type { ReactElement } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { AuthProvider } from '../context/AuthProvider'

interface Options {
  /** URL the MemoryRouter starts on. */
  route?: string
  /** Route pattern the component under test is mounted on (needed for useParams). */
  path?: string
}

export function renderWithProviders(ui: ReactElement, { route = '/', path = '*' }: Options = {}) {
  return render(
    <MemoryRouter initialEntries={[route]}>
      <AuthProvider>
        <Routes>
          <Route path={path} element={ui} />
          {/* Navigation target used by LoginPage; lets tests assert "we navigated". */}
          <Route path="/dashboard" element={<p>Dashboard page</p>} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )
}
```

It uses the real `AuthProvider` on purpose, so tests can assert what really lands in `localStorage`.

### 7. `frontend/src/pages/LoginPage.test.tsx` — the example

```tsx
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { LoginPage } from './LoginPage'
import { server } from '../test/server'
import { renderWithProviders } from '../test/renderWithProviders'

const LOGIN_URL = '*/api/auth/login'

// LoginPage reads the user out of the JWT payload with atob(token.split('.')[1]).
function fakeJwt(payload: object) {
  return `header.${btoa(JSON.stringify(payload))}.signature`
}

function renderLogin() {
  return renderWithProviders(<LoginPage />, { route: '/login', path: '/login' })
}

async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Email'), 'ana@example.com')
  await user.type(screen.getByLabelText('Password'), 'Secret123!')
  await user.click(screen.getByRole('button', { name: 'Log in' }))
}

describe('LoginPage', () => {
  it('shows validation errors and does not call the API when the form is empty', async () => {
    let calls = 0
    server.use(
      http.post(LOGIN_URL, () => {
        calls++
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()
    renderLogin()

    await user.click(screen.getByRole('button', { name: 'Log in' }))

    expect(await screen.findByText('Enter a valid email address')).toBeInTheDocument()
    expect(screen.getByText('Password is required')).toBeInTheDocument()
    expect(calls).toBe(0)
  })

  it('stores the session and navigates to the dashboard on success', async () => {
    const accessToken = fakeJwt({ sub: 'user-1', name: 'Ana', email: 'ana@example.com' })
    let requestBody: unknown
    server.use(
      http.post(LOGIN_URL, async ({ request }) => {
        requestBody = await request.json()
        return HttpResponse.json({ accessToken, refreshToken: 'refresh-1' })
      }),
    )
    const user = userEvent.setup()
    renderLogin()

    await fillAndSubmit(user)

    expect(await screen.findByText('Dashboard page')).toBeInTheDocument()
    expect(requestBody).toEqual({ email: 'ana@example.com', password: 'Secret123!' })
    expect(localStorage.getItem('accessToken')).toBe(accessToken)
    expect(localStorage.getItem('refreshToken')).toBe('refresh-1')
    expect(JSON.parse(localStorage.getItem('user')!)).toEqual({
      id: 'user-1',
      name: 'Ana',
      email: 'ana@example.com',
    })
  })

  it('shows the server message and stays on the page when credentials are wrong', async () => {
    // LoginHandler throws NotFoundException -> 404 { message }. (Not 401: the axios
    // interceptor would treat a 401 as an expired session and redirect.)
    server.use(
      http.post(LOGIN_URL, () =>
        HttpResponse.json({ message: 'Invalid user or password.' }, { status: 404 }),
      ),
    )
    const user = userEvent.setup()
    renderLogin()

    await fillAndSubmit(user)

    expect(await screen.findByText('Invalid user or password.')).toBeInTheDocument()
    expect(screen.queryByText('Dashboard page')).not.toBeInTheDocument()
    expect(localStorage.getItem('accessToken')).toBeNull()
  })

  it('falls back to a generic message when the server cannot be reached', async () => {
    server.use(http.post(LOGIN_URL, () => HttpResponse.error()))
    const user = userEvent.setup()
    renderLogin()

    await fillAndSubmit(user)

    expect(
      await screen.findByText('Login failed. Check your credentials.'),
    ).toBeInTheDocument()
  })
})
```

### 8. CI — `.github/workflows/ci.yml`, `frontend` job, between Lint and Build

```yaml
      - name: Test
        run: npm test
```

### 9. Docs (per CLAUDE.md "Keeping the docs true")

Add a frontend bullet to the **Tests** section of `CLAUDE.md` (Vitest + RTL + MSW, `npm test` from `frontend/`, tests colocated as `*.test.tsx`, helpers in `src/test/`).

## Conventions for the tests you write next

- Colocate: `Foo.tsx` → `Foo.test.tsx`. Shared helpers only in `src/test/`.
- Query by role/label/text (`getByRole`, `getByLabelText`); avoid `data-testid` and class names.
- Use `userEvent.setup()` (not `fireEvent`), and `findBy*` for anything async.
- Declare MSW handlers **per test** with `server.use(...)`; assert on the request body where the payload is the behaviour under test.
- For pages using `useParams`, pass `route: '/forms/abc/answer', path: '/forms/:id/answer'`.
- `showError`/`showSuccess` (sonner): `vi.mock('../utils/toast')` and assert the call; no need to render `<Toaster />`.
- Use `TZ=UTC` (or local-constructor dates) in date tests so they pass on CI and on this machine.
- Test names describe the rule, in the domain vocabulary from `CONTEXT.md` (submission, answer key, expires — not "response").

## Known gotchas

- **401 handling**: `api/axios.ts` sets `window.location.href = '/login'` on any 401; jsdom logs "not implemented: navigation". In tests that need a 401, `vi.stubGlobal('location', { ...window.location, href: '' })` and assert on it.
- **recharts** in jsdom: stub `ResizeObserver` (in `setup.ts` once you write the first results test) or mock the chart components.
- **`react-refresh/only-export-components`** may warn on `renderWithProviders.tsx`; if it does, add an eslint override for `src/test/**` rather than restructuring.
- **`tsc -b` includes test files** (`tsconfig.app.json` includes `src`), so type errors in tests break the build — intended.

## Verification

1. `cd frontend && npm i -D …` (step 1), then apply steps 2–7.
2. `npm test` → 4 passing tests in `LoginPage.test.tsx`.
3. Sanity-check that the test can fail: temporarily change `'Password is required'` in `LoginPage.tsx` and confirm the first test goes red, then revert.
4. `npm run lint` and `npm run build` still pass (test files type-check under `tsc -b`).
5. `npm run test:coverage` prints a report (baseline only; no threshold yet).
6. Push a branch and confirm the new `Test` step runs in the `frontend` CI job.
