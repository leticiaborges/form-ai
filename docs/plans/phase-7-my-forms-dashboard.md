# Phase 7: My Forms list + dashboard chart suggestions

## Context

Form generation/saving already works, but there is no way for a logged-in user to see the list of forms they've created or navigate into one to edit it. `DashboardPage` is currently a placeholder stub that the app already redirects to after login (`/dashboard`), and `FormEditorPage` already exists at `/forms/:id/edit` expecting a form id. The backend already exposes everything needed for a basic list: `GET /api/forms` → `GetFormsByUserHandler` → `GetFormSummaryResponse[]` (`Id, Title, IsPublic, ExpiresAt, CreatedAt`), scoped to the authenticated user and ordered by `CreatedAt desc`. So this is a **frontend-only** task: turn the `/dashboard` stub into a real "My Forms" list, per the user's choice to repurpose that route rather than add a new one.

The user also asked for chart ideas for a *future* dashboard (per-form and general). That part is advisory only — no charts are being built now, just documented here, keeping the door open to add stat/chart widgets to this same `/dashboard` page later.

## Implementation

**1. `frontend/src/types/form.ts`** — add a `FormSummary` interface matching the backend DTO:
```ts
export interface FormSummary {
    id: string;
    title: string;
    isPublic: boolean;
    expiresAt: string | null;
    createdAt: string;
}
```

**2. `frontend/src/api/forms.ts`** — add `listForms()`:
```ts
export async function listForms(): Promise<FormSummary[]> {
    const response = await api.get<FormSummary[]>('/forms');
    return response.data;
}
```
Follows the existing `getForm`/`updateQuestions` pattern (same `api` axios instance, same style).

**3. New `frontend/src/components/FormCard.tsx`** — list-item card for one form summary. Mirrors `StatusCard.tsx`'s prop conventions (`Readonly<Props>`, Tailwind utility classes, `brand-*` color scale). Props: `form: FormSummary`, `onClick: () => void`. Shows title, created date (formatted), and a small badge for Public/Private (and "Expired"/expiry date if `expiresAt` is set and in the past/future).

**4. `frontend/src/pages/DashboardPage.tsx`** — replace the placeholder body with the real list. Full replacement code to add manually:

```tsx
import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { FormSummary } from "../types/form";
import { listForms } from "../api/forms";
import { Button } from "../components/Button";
import { FormCard } from "../components/FormCard";
import { useAuth } from "../context/useAuth";

type PageState = 'loading' | 'ready' | 'error';

export function DashboardPage() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const [state, setState] = useState<PageState>('loading');
  const [forms, setForms] = useState<FormSummary[]>([]);

  useEffect(() => {
    listForms().then(data => {
      setForms(data);
      setState('ready');
    }).catch(() => setState('error'));
  }, []);

  return (
    <div className="min-h-screen bg-gray-50">
      <header className="border-b border-gray-200 bg-white px-6 py-4 shadow-sm flex items-center justify-between">
        <h1 className="text-xl font-bold text-gray-900">Welcome, {user?.name ?? 'User'}!</h1>
        <Button variant="outline" onClick={logout}>Log out</Button>
      </header>

      <main className="mx-auto max-w-4xl px-4 py-8">
        <h2 className="text-lg font-semibold text-gray-900 mb-4">My forms</h2>

        {state === 'loading' && (
          <p className="text-gray-500">Loading your forms…</p>
        )}

        {state === 'error' && (
          <p className="text-red-600">Couldn't load your forms. Please try again later.</p>
        )}

        {state === 'ready' && forms.length === 0 && (
          <div className="py-16 text-center text-gray-400">
            <p className="text-lg">You haven't created any forms yet.</p>
          </div>
        )}

        {state === 'ready' && forms.length > 0 && (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            {forms.map(form => (
              <FormCard
                key={form.id}
                form={form}
                onClick={() => navigate(`/forms/${form.id}/edit`)}
              />
            ))}
          </div>
        )}
      </main>
    </div>
  );
}
```

Notes on the code above:
- **Header** — same welcome message + logout button as the current stub, just moved into a proper `<header>` bar (matching `FormEditorPage`'s sticky-header style) instead of a full-page centered layout, since the page now needs room below it for the list.
- **`PageState`** — mirrors `FormEditorPage`'s `'loading' | 'ready' | 'error'` pattern for consistency; `listForms()` runs once on mount in a `useEffect`, same shape as `FormEditorPage`'s `getForm(id).then(...).catch(...)`.
- **Empty state** — reuses the same "no items yet" visual treatment (`py-16 text-center text-gray-400`) that `FormEditorPage` uses for "No questions yet.", so empty states look consistent across the app.
- **Grid + `FormCard`** — a responsive CSS grid (1 column on mobile, up to 3 on large screens); each `FormCard` gets the `onClick` that navigates to `/forms/${form.id}/edit`, which is the prop `FormCard` already expects.
- **Error state** — a plain inline message, same weight/style as `FormEditorPage`'s "Form not found or you don't have access."

No changes needed to `App.tsx` routing (`/dashboard` already maps to `DashboardPage`) or to `FormEditorPage`'s "Back" button (already navigates to `/dashboard`, which is now correct).

No backend changes needed — `GET /api/forms` already returns everything this page requires.

## Future dashboard: chart suggestions (not implemented now)

Keeping it simple — a handful of widgets, reusing the same `/dashboard` page later:

**General (across all of the user's forms)**
- Stat tiles: total forms created, total submissions received, average submissions per form.
- Bar chart: submissions per form (their most/least answered forms at a glance).
- Line chart: submissions over time (daily/weekly), to see engagement trend.

**Per-form (on the form editor or a form detail/results view)**
- Bar chart: answer distribution per question (how many respondents picked each option, for Single/Multiple).
- Stat/bar: average score (%) for forms with graded questions.
- Bar chart: correct-vs-incorrect rate per question — highlights which questions are hardest.
- Small line/sparkline: submissions over time for that one form since publishing.

Recommended starting point when this gets built: 2–3 stat tiles + "submissions per form" bar chart on the general dashboard, and "answer distribution per question" on a per-form results view — the rest can follow later.

## Verification

- Run backend (`dotnet run --project src/FormAI.API`) and frontend dev server.
- Log in with a user that has at least one saved form; confirm `/dashboard` shows the form(s) as cards instead of the old placeholder text.
- Click a card and confirm it navigates to `/forms/:id/edit` and loads that form correctly.
- Log in with a user that has no forms; confirm the empty state renders without errors.
- Confirm the "Back" button on `FormEditorPage` returns to the populated `/dashboard` list.
