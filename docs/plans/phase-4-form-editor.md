# Phase 4: Form Editor Frontend

## Context

Phase 3 delivered the AI form generation backend. Calling `POST /api/forms/generate/text` returns a form ID and a complete set of AI-generated questions. Phase 4 adds the editor page where the user reviews and adjusts that draft before publishing.

The editor lives at `/forms/:id/edit`. After generation the app redirects there automatically. The user can:

- **View** all AI-generated questions
- **Reorder** questions with up/down arrows
- **Rename** question text (click to edit inline)
- **Rename** option text (click to edit inline)
- **Toggle** which options are marked as correct answers
- **Remove** a question
- **Remove** an option from a Single/Multiple question
- **Add an option** to an existing Single/Multiple question
- **Add a question manually** — pick type, enter label, configure options
- **Save** all changes via `PUT /api/forms/{id}/questions`

**This phase is mostly frontend.** One small backend fix is required: `GetFormResponse.OptionDTO` currently omits `IsCorrect` and `QuestionDTO` omits `AiGenerated`, both of which the editor needs.

---

## What Already Exists — Do NOT Rewrite

| Already Exists | Notes |
|---|---|
| `GET /api/forms/{id}` → `GetFormResponse` | Returns the form with questions and options |
| `PUT /api/forms/{id}/questions` → 204 | Replaces the entire question list (the editor's save action) |
| `Button`, `Input` components | `frontend/src/components/` — reuse in editor |
| Axios instance with bearer auth | `frontend/src/api/axios.ts` — already attaches JWT |
| `AuthContext` + `useAuth` | Provides authenticated user state |
| Routes in `App.tsx` | `/dashboard`, `/login`, etc. already wired up |

---

## Backend Fix: Add `IsCorrect` and `AiGenerated` to `GetFormResponse`

`OptionDTO` only returns `Id`, `Text`, and `Order`. The editor needs `IsCorrect` to show which answers are correct. `QuestionDTO` needs `AiGenerated` so it can be round-tripped back to the PUT endpoint unchanged.

---

### Step 1 — Update `GetFormResponse.cs`

**File:** `src/FormAI.Application/Forms/GetForm/GetFormResponse.cs`

Replace `QuestionDTO` and `OptionDTO`:

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
    List<QuestionDTO> Questions
);

public record QuestionDTO(
    Guid Id,
    string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    bool AiGenerated,
    int? Points,
    string? CorrectAnswer,
    List<OptionDTO> Options
);

public record OptionDTO(
    Guid Id,
    string? Text,
    int Order,
    bool IsCorrect
);
```

---

### Step 2 — Update `GetFormHandler.cs`

**File:** `src/FormAI.Application/Forms/GetForm/GetFormHandler.cs`

Replace `BuildQuestionDTOList` to pass the two new fields:

```csharp
private List<QuestionDTO> BuildQuestionDTOList(Form form)
{
    return form.Questions
        .OrderBy(q => q.Order)
        .Select(q => new QuestionDTO(
            q.Id, q.Text, q.Type, q.Order, q.IsRequired, q.AiGenerated,
            q.Points, q.CorrectAnswer,
            q.Options
                .OrderBy(o => o.Order)
                .Select(o => new OptionDTO(o.Id, o.Text, o.Order, o.IsCorrect ?? false))
                .ToList()))
        .ToList();
}
```

> `q.Points` is `int?` on `FormQuestion` — the existing `QuestionDTO` already had `int? Points`, so no cast needed.

---

### Step 3 — Build and verify the backend

```bash
dotnet build FormAI.sln
```

Expected: `Build succeeded. 0 Error(s)`. Fix any compiler errors before going to the frontend.

---

## Frontend: TypeScript Types

### Step 4 — `frontend/src/types/form.ts`

**New file.** Mirrors the updated `GetFormResponse` shape and the `UpdateQuestionsRequest` payload:

```typescript
export type QuestionType = 'Single' | 'Multiple' | 'Text' | 'Numeric';

export interface FormOption {
  id: string;
  text: string;
  order: number;
  isCorrect: boolean;
}

export interface FormQuestion {
  id: string;
  text: string;
  type: QuestionType;
  order: number;
  isRequired: boolean;
  aiGenerated: boolean;
  points: number | null;
  correctAnswer: string | null;
  options: FormOption[];
}

export interface FormDetail {
  id: string;
  title: string;
  description: string | null;
  isPublic: boolean;
  expiresAt: string | null;
  showResultsAfterSubmit: boolean;
  createdAt: string;
  questions: FormQuestion[];
}
```

---

## Frontend: API Functions

### Step 5 — `frontend/src/api/forms.ts`

**New file.** Two functions: fetch a form and save its question list.

```typescript
import api from './axios';
import type { FormDetail, FormQuestion } from '../types/form';

export async function getForm(formId: string): Promise<FormDetail> {
  const response = await api.get<FormDetail>(`/forms/${formId}`);
  return response.data;
}

export async function updateQuestions(
  formId: string,
  questions: FormQuestion[]
): Promise<void> {
  const payload = {
    questions: questions.map((q, i) => ({
      text: q.text,
      type: q.type,
      order: i + 1,      // re-number to match array position
      isRequired: q.isRequired,
      aiGenerated: q.aiGenerated,
      points: q.points,
      correctAnswer: q.correctAnswer,
      options: q.options.map((o, oi) => ({
        text: o.text,
        order: oi + 1,
        isCorrect: o.isCorrect,
      })),
    })),
  };
  await api.put(`/forms/${formId}/questions`, payload);
}
```

> The PUT controller fills in `FormId` and `RequestingUserId` from the URL and JWT, so the body only needs `questions`.

---

## Frontend: Components

### Step 6 — `frontend/src/components/editor/OptionRow.tsx`

**New file.** One editable row for a single option. Clicking the text switches it to an `<input>`. The checkbox marks it as correct.

```typescript
import { useState } from 'react';

interface OptionRowProps {
  text: string;
  isCorrect: boolean;
  showCorrect: boolean;
  onTextChange: (text: string) => void;
  onCorrectChange: (isCorrect: boolean) => void;
  onRemove: () => void;
}

export function OptionRow({
  text,
  isCorrect,
  showCorrect,
  onTextChange,
  onCorrectChange,
  onRemove,
}: OptionRowProps) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(text);

  function commitEdit() {
    const trimmed = draft.trim();
    if (trimmed) onTextChange(trimmed);
    else setDraft(text);
    setEditing(false);
  }

  return (
    <div className="flex items-center gap-2 py-1">
      {showCorrect && (
        <input
          type="checkbox"
          checked={isCorrect}
          onChange={e => onCorrectChange(e.target.checked)}
          className="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
          title="Mark as correct answer"
        />
      )}

      {editing ? (
        <input
          autoFocus
          value={draft}
          onChange={e => setDraft(e.target.value)}
          onBlur={commitEdit}
          onKeyDown={e => e.key === 'Enter' && commitEdit()}
          className="flex-1 rounded border border-indigo-400 px-2 py-0.5 text-sm
                     focus:outline-none focus:ring-1 focus:ring-indigo-500"
        />
      ) : (
        <span
          className="flex-1 cursor-text text-sm text-gray-700 hover:text-gray-900"
          onClick={() => { setDraft(text); setEditing(true); }}
        >
          {text || <span className="italic text-gray-400">empty option</span>}
        </span>
      )}

      <button
        onClick={onRemove}
        className="text-gray-400 hover:text-red-500 transition-colors"
        title="Remove option"
      >
        ✕
      </button>
    </div>
  );
}
```

---

### Step 7 — `frontend/src/components/editor/QuestionCard.tsx`

**New file.** The card for one question. Handles inline label editing, option list, type badge, and reorder/delete buttons.

```typescript
import { useState } from 'react';
import type { FormQuestion, FormOption } from '../../types/form';
import { OptionRow } from './OptionRow';

type QuestionType = FormQuestion['type'];

const TYPE_LABEL: Record<QuestionType, string> = {
  Single: 'Single choice',
  Multiple: 'Multiple choice',
  Text: 'Open text',
  Numeric: 'Numeric',
};

const TYPE_COLOR: Record<QuestionType, string> = {
  Single: 'bg-indigo-100 text-indigo-700',
  Multiple: 'bg-purple-100 text-purple-700',
  Text: 'bg-gray-100 text-gray-600',
  Numeric: 'bg-amber-100 text-amber-700',
};

interface QuestionCardProps {
  question: FormQuestion;
  index: number;
  total: number;
  onChange: (q: FormQuestion) => void;
  onRemove: () => void;
  onMoveUp: () => void;
  onMoveDown: () => void;
}

export function QuestionCard({
  question,
  index,
  total,
  onChange,
  onRemove,
  onMoveUp,
  onMoveDown,
}: QuestionCardProps) {
  const [editingLabel, setEditingLabel] = useState(false);
  const [labelDraft, setLabelDraft] = useState(question.text);

  const hasOptions = question.type === 'Single' || question.type === 'Multiple';

  function commitLabel() {
    const trimmed = labelDraft.trim();
    if (trimmed) onChange({ ...question, text: trimmed });
    else setLabelDraft(question.text);
    setEditingLabel(false);
  }

  function updateOption(optionIndex: number, patch: Partial<FormOption>) {
    onChange({
      ...question,
      options: question.options.map((o, i) => i === optionIndex ? { ...o, ...patch } : o),
    });
  }

  function removeOption(optionIndex: number) {
    onChange({ ...question, options: question.options.filter((_, i) => i !== optionIndex) });
  }

  function addOption() {
    const newOption: FormOption = {
      id: crypto.randomUUID(),
      text: 'New option',
      order: question.options.length + 1,
      isCorrect: false,
    };
    onChange({ ...question, options: [...question.options, newOption] });
  }

  return (
    <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm">
      <div className="flex items-start gap-3">
        {/* Question number */}
        <span className="mt-1 min-w-[24px] text-sm font-semibold text-gray-400">
          {index + 1}.
        </span>

        <div className="flex-1 min-w-0">
          {/* Inline label edit */}
          {editingLabel ? (
            <textarea
              autoFocus
              value={labelDraft}
              onChange={e => setLabelDraft(e.target.value)}
              onBlur={commitLabel}
              onKeyDown={e => {
                if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); commitLabel(); }
              }}
              rows={2}
              className="w-full resize-none rounded border border-indigo-400 px-2 py-1 text-sm
                         font-medium text-gray-900 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          ) : (
            <p
              className="cursor-text text-sm font-medium text-gray-900 hover:text-indigo-700"
              onClick={() => { setLabelDraft(question.text); setEditingLabel(true); }}
            >
              {question.text}
            </p>
          )}

          {/* Type badge */}
          <span className={`mt-1.5 inline-block rounded-full px-2 py-0.5 text-xs font-medium ${TYPE_COLOR[question.type]}`}>
            {TYPE_LABEL[question.type]}
          </span>

          {/* Options list */}
          {hasOptions && (
            <div className="mt-3 space-y-0.5">
              {question.options.map((opt, oi) => (
                <OptionRow
                  key={opt.id}
                  text={opt.text}
                  isCorrect={opt.isCorrect}
                  showCorrect={true}
                  onTextChange={text => updateOption(oi, { text })}
                  onCorrectChange={isCorrect => updateOption(oi, { isCorrect })}
                  onRemove={() => removeOption(oi)}
                />
              ))}
              <button
                onClick={addOption}
                className="mt-1 text-xs text-indigo-600 hover:text-indigo-800 hover:underline"
              >
                + Add option
              </button>
            </div>
          )}
        </div>

        {/* Reorder + delete column */}
        <div className="flex flex-col items-center gap-1 shrink-0">
          <button
            disabled={index === 0}
            onClick={onMoveUp}
            title="Move up"
            className="rounded p-1 text-gray-400 hover:text-gray-700 disabled:cursor-not-allowed disabled:opacity-30"
          >
            ▲
          </button>
          <button
            disabled={index === total - 1}
            onClick={onMoveDown}
            title="Move down"
            className="rounded p-1 text-gray-400 hover:text-gray-700 disabled:cursor-not-allowed disabled:opacity-30"
          >
            ▼
          </button>
          <button
            onClick={onRemove}
            title="Delete question"
            className="rounded p-1 text-red-400 hover:text-red-600"
          >
            🗑
          </button>
        </div>
      </div>
    </div>
  );
}
```

---

### Step 8 — `frontend/src/components/editor/AddQuestionModal.tsx`

**New file.** A modal overlay with a controlled form. The user picks a type, fills in the label, and (for Single/Multiple) configures options with correct-answer checkboxes.

```typescript
import { useState } from 'react';
import type { FormQuestion, FormOption, QuestionType } from '../../types/form';
import { Button } from '../Button';
import { Input } from '../Input';

interface AddQuestionModalProps {
  onAdd: (question: FormQuestion) => void;
  onClose: () => void;
}

const TYPES: { value: QuestionType; label: string }[] = [
  { value: 'Single',   label: 'Single choice' },
  { value: 'Multiple', label: 'Multiple choice' },
  { value: 'Text',     label: 'Open text' },
  { value: 'Numeric',  label: 'Numeric' },
];

function makeOption(): FormOption {
  return { id: crypto.randomUUID(), text: '', order: 0, isCorrect: false };
}

export function AddQuestionModal({ onAdd, onClose }: AddQuestionModalProps) {
  const [text, setText] = useState('');
  const [type, setType] = useState<QuestionType>('Single');
  const [options, setOptions] = useState<FormOption[]>([makeOption(), makeOption()]);
  const [error, setError] = useState('');

  const hasOptions = type === 'Single' || type === 'Multiple';

  function updateOptionText(index: number, value: string) {
    setOptions(opts => opts.map((o, i) => i === index ? { ...o, text: value } : o));
  }

  function toggleCorrect(index: number) {
    setOptions(opts => opts.map((o, i) => i === index ? { ...o, isCorrect: !o.isCorrect } : o));
  }

  function addOption() {
    setOptions(opts => [...opts, makeOption()]);
  }

  function removeOption(index: number) {
    setOptions(opts => opts.filter((_, i) => i !== index));
  }

  function handleSubmit() {
    if (!text.trim()) {
      setError('Question text is required.');
      return;
    }
    if (hasOptions && options.length < 2) {
      setError('At least 2 options are required.');
      return;
    }
    if (hasOptions && options.some(o => !o.text.trim())) {
      setError('All option texts must be filled in.');
      return;
    }

    const newQuestion: FormQuestion = {
      id: crypto.randomUUID(),
      text: text.trim(),
      type,
      order: 0,           // parent re-numbers on append
      isRequired: true,
      aiGenerated: false,
      points: null,
      correctAnswer: null,
      options: hasOptions
        ? options.map((o, i) => ({ ...o, order: i + 1 }))
        : [],
    };

    onAdd(newQuestion);
    onClose();
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4">
      <div className="w-full max-w-lg rounded-2xl bg-white p-6 shadow-xl">
        <h2 className="mb-4 text-lg font-semibold text-gray-900">Add question</h2>

        <div className="flex flex-col gap-4">
          {/* Label */}
          <Input
            label="Question text"
            value={text}
            onChange={e => setText(e.target.value)}
            placeholder="e.g. What is the capital of France?"
          />

          {/* Type selector */}
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">Type</label>
            <div className="flex flex-wrap gap-2">
              {TYPES.map(t => (
                <button
                  key={t.value}
                  onClick={() => setType(t.value)}
                  className={`rounded-full border px-3 py-1 text-sm font-medium transition-colors ${
                    type === t.value
                      ? 'border-indigo-600 bg-indigo-600 text-white'
                      : 'border-gray-300 bg-white text-gray-600 hover:border-indigo-400'
                  }`}
                >
                  {t.label}
                </button>
              ))}
            </div>
          </div>

          {/* Options (only for Single / Multiple) */}
          {hasOptions && (
            <div>
              <label className="mb-2 block text-sm font-medium text-gray-700">
                Options <span className="text-gray-400 font-normal">(check correct answers)</span>
              </label>
              <div className="space-y-2">
                {options.map((opt, i) => (
                  <div key={opt.id} className="flex items-center gap-2">
                    <input
                      type="checkbox"
                      checked={opt.isCorrect}
                      onChange={() => toggleCorrect(i)}
                      className="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                      title="Mark as correct"
                    />
                    <input
                      value={opt.text}
                      onChange={e => updateOptionText(i, e.target.value)}
                      placeholder={`Option ${i + 1}`}
                      className="flex-1 rounded border border-gray-300 px-3 py-1.5 text-sm
                                 focus:outline-none focus:ring-2 focus:ring-indigo-500"
                    />
                    <button
                      onClick={() => removeOption(i)}
                      disabled={options.length <= 2}
                      className="text-gray-400 hover:text-red-500 disabled:opacity-30 disabled:cursor-not-allowed"
                    >
                      ✕
                    </button>
                  </div>
                ))}
                <button
                  onClick={addOption}
                  className="text-xs text-indigo-600 hover:underline"
                >
                  + Add option
                </button>
              </div>
            </div>
          )}

          {error && <p className="text-sm text-red-600">{error}</p>}
        </div>

        <div className="mt-6 flex justify-end gap-3">
          <Button variant="outline" onClick={onClose}>Cancel</Button>
          <Button onClick={handleSubmit}>Add question</Button>
        </div>
      </div>
    </div>
  );
}
```

---

## Frontend: Page

### Step 9 — `frontend/src/pages/FormEditorPage.tsx`

**New file.** Fetches the form on mount, holds an editable `questions` array in state, renders all cards, and saves on button click.

```typescript
import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { getForm, updateQuestions } from '../api/forms';
import type { FormDetail, FormQuestion } from '../types/form';
import { QuestionCard } from '../components/editor/QuestionCard';
import { AddQuestionModal } from '../components/editor/AddQuestionModal';
import { Button } from '../components/Button';

type PageState = 'loading' | 'ready' | 'saving' | 'error';

export function FormEditorPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [state, setState] = useState<PageState>('loading');
  const [form, setForm] = useState<FormDetail | null>(null);
  const [questions, setQuestions] = useState<FormQuestion[]>([]);
  const [showAddModal, setShowAddModal] = useState(false);
  const [saveError, setSaveError] = useState('');

  useEffect(() => {
    if (!id) return;
    getForm(id)
      .then(data => {
        setForm(data);
        setQuestions(data.questions);
        setState('ready');
      })
      .catch(() => setState('error'));
  }, [id]);

  function updateQuestion(index: number, updated: FormQuestion) {
    setQuestions(qs => qs.map((q, i) => i === index ? updated : q));
  }

  function removeQuestion(index: number) {
    setQuestions(qs => qs.filter((_, i) => i !== index));
  }

  function moveQuestion(index: number, direction: -1 | 1) {
    setQuestions(qs => {
      const next = [...qs];
      const target = index + direction;
      if (target < 0 || target >= next.length) return qs;
      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });
  }

  function appendQuestion(question: FormQuestion) {
    setQuestions(qs => [...qs, question]);
  }

  async function handleSave() {
    if (!id) return;
    setSaveError('');
    setState('saving');
    try {
      await updateQuestions(id, questions);
      setState('ready');
    } catch {
      setSaveError('Failed to save. Please try again.');
      setState('ready');
    }
  }

  if (state === 'loading') {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <p className="text-gray-500">Loading form…</p>
      </div>
    );
  }

  if (state === 'error' || !form) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <p className="text-red-600">Form not found or you don't have access.</p>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gray-50">
      {/* Sticky top bar */}
      <header className="sticky top-0 z-10 border-b border-gray-200 bg-white px-6 py-3 shadow-sm flex items-center justify-between">
        <div>
          <h1 className="max-w-xs truncate text-base font-semibold text-gray-900">
            {form.title}
          </h1>
          <p className="text-xs text-gray-400">
            {questions.length} question{questions.length !== 1 ? 's' : ''}
          </p>
        </div>
        <div className="flex items-center gap-3">
          <Button variant="outline" onClick={() => navigate('/dashboard')}>
            Back
          </Button>
          <Button onClick={handleSave} isLoading={state === 'saving'}>
            Save
          </Button>
        </div>
      </header>

      {/* Editor area */}
      <main className="mx-auto max-w-2xl px-4 py-8 flex flex-col gap-4">
        {questions.length === 0 && (
          <div className="py-16 text-center text-gray-400">
            <p className="text-lg">No questions yet.</p>
            <p className="mt-1 text-sm">Use the button below to add one.</p>
          </div>
        )}

        {questions.map((q, i) => (
          <QuestionCard
            key={q.id}
            question={q}
            index={i}
            total={questions.length}
            onChange={updated => updateQuestion(i, updated)}
            onRemove={() => removeQuestion(i)}
            onMoveUp={() => moveQuestion(i, -1)}
            onMoveDown={() => moveQuestion(i, 1)}
          />
        ))}

        {/* Add question trigger */}
        <button
          onClick={() => setShowAddModal(true)}
          className="flex items-center justify-center gap-2 rounded-xl border-2 border-dashed
                     border-gray-300 py-4 text-sm text-gray-500 transition-colors
                     hover:border-indigo-400 hover:text-indigo-600"
        >
          + Add question
        </button>

        {saveError && (
          <p className="text-center text-sm text-red-600">{saveError}</p>
        )}
      </main>

      {showAddModal && (
        <AddQuestionModal
          onAdd={appendQuestion}
          onClose={() => setShowAddModal(false)}
        />
      )}
    </div>
  );
}
```

---

## Frontend: Wire Up the Route

### Step 10 — Update `frontend/src/App.tsx`

Add the import and route:

```typescript
import { FormEditorPage } from './pages/FormEditorPage';

// Inside <Routes>, add:
<Route path="/forms/:id/edit" element={<FormEditorPage />} />
```

Full updated `App.tsx`:

```typescript
import { Routes, Route, Navigate } from 'react-router-dom';
import { LandingPage } from './pages/LandingPage';
import { RegisterPage } from './pages/RegisterPage';
import { RegisterSuccessPage } from './pages/RegisterSuccessPage';
import { VerifyEmailPage } from './pages/VerifyEmailPage';
import { LoginPage } from './pages/LoginPage';
import { DashboardPage } from './pages/DashboardPage';
import { FormEditorPage } from './pages/FormEditorPage';

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<LandingPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/register/success" element={<RegisterSuccessPage />} />
      <Route path="/verify-email" element={<VerifyEmailPage />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/dashboard" element={<DashboardPage />} />
      <Route path="/forms/:id/edit" element={<FormEditorPage />} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
```

---

## Generation Hook-up (Bridge to Phase 3)

The form editor is ready for use, but a user currently has no way to trigger generation from the UI — that generation form lives in Phase 5 (the full dashboard/generation flow). For now, you can navigate directly to the editor by pasting a URL:

```
http://localhost:5173/forms/<formId>/edit
```

Use a `formId` returned by `POST /api/forms/generate/text` via Swagger to test the editor manually.

When the generation frontend is built (Phase 5), it will call `POST /api/forms/generate/text`, receive `{ formId, ... }` in the response, and redirect with:

```typescript
navigate(`/forms/${response.formId}/edit`);
```

No changes to the editor page are needed at that point.

---

## Files Changed Summary

### Backend

| Action | File |
|---|---|
| Edit | `src/FormAI.Application/Forms/GetForm/GetFormResponse.cs` |
| Edit | `src/FormAI.Application/Forms/GetForm/GetFormHandler.cs` |

### Frontend — new files

| File | Purpose |
|---|---|
| `frontend/src/types/form.ts` | TypeScript interfaces for `FormDetail`, `FormQuestion`, `FormOption` |
| `frontend/src/api/forms.ts` | `getForm()` and `updateQuestions()` API functions |
| `frontend/src/components/editor/OptionRow.tsx` | Inline-editable option row with correct-answer checkbox |
| `frontend/src/components/editor/QuestionCard.tsx` | Question card: inline edit, options, reorder, delete |
| `frontend/src/components/editor/AddQuestionModal.tsx` | Modal form for manually adding a question |
| `frontend/src/pages/FormEditorPage.tsx` | Editor page (`/forms/:id/edit`) |

### Frontend — modified files

| File | Change |
|---|---|
| `frontend/src/App.tsx` | Add `/forms/:id/edit` route |

---

## Verification

### Step 1 — Build the backend
```bash
dotnet build FormAI.sln
```

### Step 2 — Start the API and generate a test form
```bash
dotnet run --project src/FormAI.API
```
Open `http://localhost:5155/swagger`.

1. `POST /api/auth/login` → copy `accessToken`
2. Swagger **Authorize** → `Bearer <token>`
3. `POST /api/forms/generate/text` with:
   ```json
   {
     "sourceText": "Photosynthesis converts sunlight, water, and CO2 into glucose and oxygen inside chloroplasts.",
     "sourceType": 1,
     "sourceUrl": null,
     "questionCount": 3,
     "allowedTypes": null,
     "difficultyLevel": "medium",
     "includeCorrectAnswers": true
   }
   ```
4. Copy the returned `formId`.
5. `GET /api/forms/{formId}` → confirm `isCorrect` and `aiGenerated` are now present in the response.

### Step 3 — Start the frontend
```bash
cd frontend
npm run dev
```

### Step 4 — Test the editor

Open `http://localhost:5173/forms/<formId>/edit`.

| Test | Expected |
|---|---|
| Page loads | All AI questions appear in cards |
| Click a question label | Label turns into an editable `<textarea>` |
| Press Enter or click away | New text is saved in state |
| Click an option text | Turns into an input |
| Check a correct-answer checkbox | Checkbox toggles |
| Click ▲ / ▼ on a question | Order changes, arrows disable at boundaries |
| Click 🗑 on a question | Question is removed |
| Click ✕ on an option | Option is removed from the list |
| Click "+ Add option" | A new "New option" row appears |
| Click "+ Add question" | `AddQuestionModal` opens |
| Fill modal (Single type) → Add question | New card appears at the bottom |
| Try adding with empty label | Validation error shown in modal |
| Try adding Single with < 2 options | Validation error shown |
| Click **Save** | HTTP 204 — no error banner |
| Refresh page | All saved changes persist |
