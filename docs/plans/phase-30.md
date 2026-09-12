# Phase 30: Individual Submission View in the Results Tab

## The problem

Today the Results tab only shows aggregated answer distributions (`ResultsTab.tsx` → `getFormResults`). The owner has no way to see what one specific respondent answered. The backend already exposes what's needed for that (`GET /submissions` for a paginated list of submission ids/dates, `GET /submissions/{submissionId}` for one submission's answers with server-computed correctness and score), but nothing in the frontend calls either yet. This phase adds a second, nested "Individual" view inside Results where the owner pages through submissions one at a time and sees each question rendered the same way the respondent saw it, plus (on a graded form) how it was scored.

All decisions below were settled with the user beforehand; nothing here is speculative:

- Nested Summary/Individual sub-tabs use local state (not the URL).
- The Individual tab re-fetches `GET /api/forms/{id}` itself rather than reusing the editor's in-memory `form` — the editor tab may hold unsaved edits that must never leak into what respondents were actually graded against.
- Submission list is fetched in chunks of 20 (`GetSubmissions` `pageSize=20`), cached by chunk index, fetched reactively (only when navigation crosses the loaded window, not prefetched). One UI "page" = one submission; `TotalPages` for the pager = `TotalCount` from any chunk response.
- Once a submission's detail is fetched, it's cached for the tab's lifetime (submissions are immutable).
- `QuestionAnswerCard` is extended in place with optional read-only/grading props; existing usage in `FormAnswerPage` is unaffected.
- Server-computed `IsCorrect`/`Score` per answer are used as-is — no client-side re-grading.
- Option selection in read-only mode is matched by **option text** (case-insensitive/trim), never by id, since `AnswerResponse.SelectedOptions` stores text snapshots (ADR 0001).
- Per-option coloring (Single/Multiple) is independent per selected option (green/red based on whether that pick is in the key); a separate all-or-nothing check/✕ badge next to the score reflects the real scoring outcome (`IsCorrect`) so the two never look contradictory.
- An unanswered question in a graded submission renders as `0/points`, incorrect, nothing selected — but still shows the real answer key below.
- A graded question with no answer key at all shows a gray "No correct answer defined" placeholder instead of a key.
- This is a frontend-only change — no backend/API modifications needed.

## 1. Nested Summary/Individual tabs

- Rename the current body of `frontend/src/components/results/ResultsTab.tsx` into a new `frontend/src/components/results/SummaryResults.tsx` (same props/logic, `getFormResults`, `QuestionResultCard`, `ScoreDistributionChart`), unchanged in behavior.
- `ResultsTab.tsx` becomes a thin container: keeps `formId`/`reloadKey` props, holds a local `subTab: 'summary' | 'individual'` state, renders the existing `Tabs` component (`frontend/src/components/Tabs.tsx`) with those two entries, and renders `SummaryResults` or `IndividualResults` (added in step 4; a simple placeholder is fine until then) based on `subTab`.
- No change to `FormEditorPage.tsx`'s usage of `<ResultsTab formId={form.id} reloadKey={resultsReloadKey} />`.

## 2. Extend `QuestionAnswerCard` for read-only/graded display

File: `frontend/src/components/respond/QuestionAnswerCard.tsx`

- Add `readOnly?: boolean` and make the four `onChange` handlers optional; when `readOnly`, all inputs render `disabled` and handlers are not required.
- Extend `AnswerPayload` (`frontend/src/types/submission.ts`) with an optional `selectedOptionTexts?: string[]` field. In read-only mode, "is this option selected" is computed by normalized-text membership in `selectedOptionTexts` instead of comparing `selectedOptionIds` to `opt.id`. Add a small shared normalizer (trim + lowercase), e.g. `frontend/src/utils/textMatch.ts` exporting `normalizeOptionText`.
- Add an optional `grading?: QuestionGradingInfo` prop (new interface, defined in this file or `types/submission.ts`):
  ```ts
  interface QuestionGradingInfo {
    points: number;
    earnedScore: number;
    isCorrect: boolean;
    correctOptionTexts: string[];   // key options' text, for Single/Multiple
    correctAnswerText: string | null; // suggested answer, for Text/Numeric
    hasAnswerKey: boolean;          // false => show the "no key" placeholder
  }
  ```
  `grading` is only ever passed when the form is graded (caller's responsibility), so the component doesn't need a separate `isGraded` flag.
- When `readOnly && grading`:
  - Selected options get a green/red tint independently, based on `grading.correctOptionTexts` membership (normalized compare) — not on the overall `isCorrect`.
  - Top-right of the card header: an `earnedScore/points` badge plus a green check / red ✕ icon driven by `grading.isCorrect` (the one all-or-nothing signal).
  - Below the answered field: a "Correct answer" box — the single correct option's text (Single), a bullet list of correct options (Multiple), or `correctAnswerText` (Text/Numeric). If `!hasAnswerKey`, render a gray "No correct answer defined" line instead.
- Verify `FormAnswerPage.tsx`'s existing call site needs zero changes (no new props passed → identical rendering to today).

## 3. Types, API client, and the pager component

- New `frontend/src/types/submissionReview.ts`:
  ```ts
  export interface SubmissionListItem { submissionId: string; submittedAt: string; }
  export interface PagedResult<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }
  export interface SubmissionAnswerDetail {
    questionId: string; selectedOptions: string[]; numericValue: number | null;
    textValue: string | null; isCorrect: boolean | null; score: number | null;
  }
  export interface SubmissionDetail {
    formId: string; submissionId: string; submittedAt: string; score: number | null;
    answers: SubmissionAnswerDetail[];
  }
  ```
  (Field names match the camelCase JSON from `GetSubmissionListItemResponse`, `PagedResult<T>`, and `GetSubmissionAnswersResponse`/`AnswerResponse`.)
- Add to `frontend/src/api/forms.ts`, next to `getSubmissionCount`:
  ```ts
  export async function getSubmissions(formId: string, page: number, pageSize: number): Promise<PagedResult<SubmissionListItem>>
  export async function getSubmissionDetail(formId: string, submissionId: string): Promise<SubmissionDetail>
  ```
  hitting `GET /forms/{formId}/submissions?page=&pageSize=` and `GET /forms/{formId}/submissions/{submissionId}`.
- New `frontend/src/components/results/SubmissionPager.tsx`: props `{ page: number; totalPages: number; onPageChange: (page: number) => void; disabled?: boolean }`. Renders Prev/Next buttons (reuse `Button`, `variant="outline"`, disabled at the bounds) and a typeable page-number input that commits (clamped to `[1, totalPages]`) on Enter or blur, plus "of {totalPages}" — no submission id/date ever rendered here.

## 4. Wire the Individual tab

New `frontend/src/components/results/IndividualResults.tsx`, props `{ formId: string; reloadKey: number }`:

- On mount / `formId`/`reloadKey` change: fetch `getForm(formId)` independently (never the editor's in-memory form) to get `FormDetail` (question text/order/type, options with `isCorrect`, `points`, `correctAnswer`, `isGraded`).
- State: `currentPage` (starts at 1), a chunk cache `Record<chunkIndex, SubmissionListItem[]>`, `totalCount: number | null`, and a detail cache `Record<submissionId, SubmissionDetail>`.
- Chunk size fixed at 20. `chunkIndex = Math.ceil(currentPage / 20)`, `offset = (currentPage - 1) % 20`. When the chunk for `currentPage` isn't cached, call `getSubmissions(formId, chunkIndex, 20)` and store `items`/`totalCount`.
- Once the current page's `submissionId` is known, fetch `getSubmissionDetail(formId, submissionId)` if not already cached.
- Empty state (`totalCount === 0`) mirrors `SummaryResults`' "No submissions yet." Loading/error states follow the same pattern as `SummaryResults`.
- Card assembly for the resolved `SubmissionDetail`: iterate `form.questions` (not `detail.answers`, so skipped questions still render), look up the matching `SubmissionAnswerDetail` by `questionId`:
  - Found → `answer = { selectedOptionTexts: found.selectedOptions, textValue: found.textValue ?? undefined, numericValue: found.numericValue ?? undefined }`; when `form.isGraded`, `grading = { points: question.points!, earnedScore: found.score ?? 0, isCorrect: found.isCorrect ?? false, correctOptionTexts: question.options.filter(o => o.isCorrect).map(o => o.text), correctAnswerText: question.correctAnswer, hasAnswerKey: question.options.some(o => o.isCorrect) || !!question.correctAnswer }`.
  - Missing (skipped question) → same `grading` shape but `earnedScore: 0`, `isCorrect: false`, and an empty `answer` (nothing selected/filled) — per the agreed unanswered-question fallback.
  - When `!form.isGraded`, omit `grading` entirely.
- Render `<SubmissionPager page={currentPage} totalPages={totalCount ?? 0} onPageChange={setCurrentPage} />` followed by one `QuestionAnswerCard` per question, all `readOnly`.
- Wire this component into `ResultsTab.tsx`'s `individual` branch (replacing the step-1 placeholder).

## Manual test checklist

1. `cd frontend && npm run dev`, open a form's editor, go to Results.
2. Summary tab: confirm it renders exactly as before (no regression from the extraction in step 1).
3. Individual tab, ungraded form: cards render with disabled controls, no score/badge/correct-answer UI.
4. Individual tab, graded form with ≥21 submissions (to cross a chunk boundary) covering: a fully-correct submission, a partially-wrong Multiple-select answer, a skipped required question, and a question with no answer key set — confirm coloring, the check/✕ badge, `earned/points`, and the "Correct answer" box (including the "No correct answer defined" placeholder) all match the rules above.
5. Pager: Prev/Next at both bounds, typing a page number (including out-of-range values, clamped), and paging across a chunk boundary (verify the reactive chunk fetch and that revisiting an earlier submission doesn't refetch its detail).
6. Edit a graded question's correct answer in the Editor tab *without saving*, switch to Results → Individual: confirm it still shows the previously-saved key (proves the independent `getForm` fetch, not the editor's in-memory state).
