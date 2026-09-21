# Show results after submit

## Problem

`Form.ShowResultsAfterSubmit` is stored, updated and returned by every form DTO, and nothing reads it
or lets the owner set it: `GenerateFormHandler` hardcodes it to `false`, `SaveFormEditorHandler`
passes the stored value straight back into `Form.Update`, and no screen has a checkbox for it. So an
owner running a graded form has no way to say "let respondents see how they did", and a respondent who
submits a graded form sees only "Thanks! Your response has been recorded." even though
`POST {id}/submit` already computes their score and returns it as `totalScore` - the frontend types
that response with the wrong field names (`submissionId`, `score`) and never reads it.

Evidence: `docs/known-gaps.md` lists both "Showing a respondent their score" and "`ShowResultsAfterSubmit`
gates nothing"; the request itself gives no figures.

When this ships, an owner of a graded form can tick "Show score after submit" on the create page or in
the editor's Configuration tab, and a respondent submitting that form sees their score out of the
form's maximum, e.g. `6/10`. On every other form nothing changes for the respondent.

## Flow

Reuses `SubmissionScorer` for both the score and the maximum, and the existing `Form` entity, the
existing `generate`/`editor`/`submit` routes and `FormConfigTab` instead of adding any of them.

1. Create page: `CreateFormPage` (exists) shows the checkbox while "Graded form" is ticked, sends `showResultsAfterSubmit` -> `POST /api/forms/generate/text` -> `GenerateFormHandler` (exists) -> `Form.Create` (exists) stores `flag && isGraded`.
2. Editor: `FormEditorPage` (exists) holds the flag in state, `FormConfigTab` (exists) shows the checkbox while the form is graded -> `PUT /api/forms/{id}/editor` -> `SaveFormEditorHandler` (exists) -> `Form.Update` (exists) stores `flag && isGraded`, replacing the pass-through of the stored value.
3. Answering: `FormAnswerPage` (exists) -> `POST /api/forms/{formId}/submit` -> `SubmitFormHandler` (exists) scores as today with `SubmissionScorer` (exists), asks `SubmissionScorer` (exists) for the form's maximum, and fills `totalScore` and `maxScore` in `SubmitFormResponse` (exists) only when the form is graded and the flag is set.
4. Out: `FormAnswerPage` (exists) keeps the `submitted` state's message and, when both numbers are non-null, shows `totalScore/maxScore`.
5. Side effect: `FormResultsCalculator` (exists) stops computing its own `TotalPoints` and calls the same `SubmissionScorer` maximum, so the owner's Results tab and the respondent cannot disagree on what the form is worth.

## Impact

| Front | What changes |
| --- | --- |
| domain | existing term: `ShowResultsAfterSubmit` was stored and inert, nothing branched on it; now `SubmitFormHandler` is the only reader, and `Form.Create`/`Form.Update` refuse to hold it `true` on an ungraded form. "Results" here means the respondent's own score and the maximum, not the owner's Results tab - `CONTEXT.md` gets the term. |
| domain | new term: **maximum score** - the sum of a graded form's questions' points (a question with no points counts 0); lives in `SubmissionScorer`. The owner's `TotalPoints` is the same number and now comes from the same function. |
| API | `POST {formId}/submit` response gains `maxScore`, and `totalScore` becomes null unless the form is graded and the flag is set. Only reader today is `FormAnswerPage`, which ignores the body. `frontend/src/types/submission.ts` `SubmitFormResult` names (`submissionId`, `score`) do not match the wire (`id`, `totalScore`) and are corrected. |
| API | `SaveFormEditorRequest` and `GenerateFormRequest` each gain a positional `bool ShowResultsAfterSubmit`; a caller that omits it sends `false`. The only caller is this frontend. Unit-test helpers that build `SaveFormEditorRequest` positionally must be updated. |
| business rule | `CLAUDE.md` says generated forms are created with `ShowResultsAfterSubmit = false`; that becomes "false unless the owner ticks it on a graded form". Editor save no longer preserves the stored value. |
| grading | The flag is not part of `GradingFingerprint`, so an editor save that changes only the flag rescores nothing. |
| stored data | Column exists, nothing to migrate. Rows created through `POST /api/forms` (unused by the UI) may already hold `true` on an ungraded form; the submit handler gates on both `IsGraded` and the flag, so they are harmless and are not backfilled. |
| tooling | Frontend has no test runner today (`package.json` has `build` and `lint` only). See the last `Landing` row. |
| docs | `CLAUDE.md` (business rules, known-gaps headline), `CONTEXT.md` (new term), `docs/known-gaps.md` (remove both rows; add "a respondent who reopens an already-submitted form does not see their score"). |

## Relations

`None - no stored-data shape change`

## Surface

Only routes this adds or whose signature changes.

| Route | In | Out | Status |
| --- | --- | --- | --- |
| `POST /api/forms/{formId}/submit` | unchanged | `id` · `totalScore` · `maxScore` (the last two null unless graded and flag set) | `200`, `400`, `404` |
| `PUT /api/forms/{id}/editor` | + `showResultsAfterSubmit` | none | `204`, `400`, `404` |
| `POST /api/forms/generate/text` | + `showResultsAfterSubmit` | unchanged | `201`, `400` |

## Landing

| One-way door | Literal shape | Alternative rejected |
| --- | --- | --- |
| Where the score is withheld | server: `new SubmitFormResponse(submission.Id, reveal ? totalScore : null, reveal ? maxScore : null)` with `reveal = form.IsGraded && form.ShowResultsAfterSubmit`; wire `{ "id": "...", "totalScore": 6, "maxScore": 10 }` or `{ "id": "...", "totalScore": null, "maxScore": null }` | Gate in the frontend only: the score would still be in the network response of a form whose owner chose not to show it, and `docs/known-gaps.md` already records that as a defect. |
| Where "flag only on graded" holds | domain: `Form.Create` and `Form.Update` store `ShowResultsAfterSubmit = showResultsAfterSubmit && isGraded` | Reject with a `ValidationException`: a stale editor tab that sent `true` after grading was turned off would fail a whole save, when turning grading off is already a silent lossy save. Frontend-only coercion cannot back "always false in the DB" for any other client. |
| Request field name | `showResultsAfterSubmit` (camelCase `bool`) on `PUT /editor` and `POST /generate/text`, the name `GetFormResponse` already returns | A separate `PATCH` for the flag: a second route and a second save button for one checkbox that lives beside the expiry the editor already saves in one `PUT`. |
| Frontend test runner | devDependencies `vitest`, `jsdom`, `@testing-library/react`, `@testing-library/user-event`, `@testing-library/jest-dom`; script `"test": "vitest run"` | No runner, proving the UI criteria with `tsc -b` and `eslint` only: neither can observe that a checkbox is hidden or that `6/10` renders. Live alternative - see Assumptions. |

- Nothing else in this change is hard to reverse

## Criteria

### S1: The owner sets the flag when creating a form (P1)

The create page offers the flag on a graded form, and the created form stores what the owner chose.

**Acceptance Criteria**

1. WHERE the "Graded form" box is ticked on the create page, the system SHALL show a "Show score after submit" checkbox, unticked by default.
2. WHILE the "Graded form" box is unticked, the create page SHALL NOT render the "Show score after submit" checkbox.
3. WHEN the owner submits the create page, the system SHALL send `showResultsAfterSubmit: true` only when both the graded and the show-score boxes are ticked at that moment, and `false` otherwise.
4. WHEN `POST /api/forms/generate/text` arrives with `isGraded: true` and `showResultsAfterSubmit: true`, the system SHALL create the form with `ShowResultsAfterSubmit = true`.
5. IF `POST /api/forms/generate/text` arrives with `isGraded: false` and `showResultsAfterSubmit: true`, THEN the system SHALL create the form with `ShowResultsAfterSubmit = false` and respond `201`.

**Independent test:** on the create page tick Graded, tick Show score, generate; `GET /api/forms/{id}` returns `showResultsAfterSubmit: true`. Repeat with Graded unticked: the checkbox is absent and the value is `false`.

### S2: The owner sets the flag in the editor (P1)

The Configuration tab offers the same flag on a graded form and the save stores it.

**Acceptance Criteria**

6. WHERE the form is graded in the editor, the Configuration tab SHALL show the "Show score after submit" checkbox, reflecting the stored value after load and after every save.
7. WHILE the form is ungraded in the editor, the Configuration tab SHALL NOT render the checkbox.
8. WHEN the owner unticks "Graded form" in the editor, the system SHALL send `showResultsAfterSubmit: false` on the next save.
9. WHEN `PUT /api/forms/{id}/editor` arrives with `isGraded: true` and `showResultsAfterSubmit: true`, the system SHALL store `ShowResultsAfterSubmit = true`, and `GET /api/forms/{id}` SHALL return `showResultsAfterSubmit: true`.
10. IF `PUT /api/forms/{id}/editor` arrives with `isGraded: false` and `showResultsAfterSubmit: true`, THEN the system SHALL store `ShowResultsAfterSubmit = false` and respond `204`.

**Independent test:** open a graded form, tick the box on the Configuration tab, save, reload: still ticked. Untick Graded on the Edit tab: the box disappears from Configuration, and after save the form reads back `false`.

### S3: The respondent sees their score when the owner allowed it (P1)

Submitting a graded form with the flag set shows `score/maximum`; every other form behaves as today.

**Acceptance Criteria**

11. WHEN a submission to a graded form with `ShowResultsAfterSubmit = true` is accepted, `POST /api/forms/{formId}/submit` SHALL return `totalScore` equal to the submission's stored score and `maxScore` equal to the sum of that form's questions' points (a question with no points counts 0) as they stand at that moment, e.g. `6` and `10`.
12. WHEN a submission to a graded form with `ShowResultsAfterSubmit = false` is accepted, `POST /api/forms/{formId}/submit` SHALL return `totalScore: null` and `maxScore: null`.
13. IF the form is ungraded, THEN `POST /api/forms/{formId}/submit` SHALL return `totalScore: null` and `maxScore: null`, whatever `ShowResultsAfterSubmit` holds.
14. The system SHALL persist the submission's score whether or not `ShowResultsAfterSubmit` is set.
15. WHEN `FormAnswerPage` receives a non-null `totalScore` and a non-null `maxScore`, it SHALL show `totalScore/maxScore` (e.g. `6/10`) together with the existing "Thanks! Your response has been recorded." message.
16. WHEN `FormAnswerPage` receives a null `totalScore` or a null `maxScore`, it SHALL show only the existing "Thanks! Your response has been recorded." message.

**Independent test:** publish a graded form worth 10 points with the flag ticked, answer so as to earn 6, submit: the page shows `6/10`. Untick the flag on another graded form, submit: only the thanks message, and the network response carries `null` for both.

## Out of scope

| Excluded | Why |
| --- | --- |
| Showing the score to a respondent who reopens a form they already submitted (`alreadySubmitted`) | `GET my-submission` returns only whether and when; showing the score there is a second route change nobody asked for. Goes into `docs/known-gaps.md`. |
| Showing the respondent which answers were right, or the answer key | The request is the score and the maximum. Answer-key reveal is a different, larger decision. |
| Any change to the owner's Results tab | Owner already sees scores; only the shared maximum function is touched (Flow 5). |
| Warning the owner before turning grading off also clears the flag | Turning grading off is already lossy without a warning (`CLAUDE.md`); unchanged. |
| A UI for `POST /api/forms` | The flag is coerced there too, but that endpoint has no screen. |

## Assumptions

| Assumption | Chosen default | Rationale | Confirmed? |
| --- | --- | --- | --- |
| The score is withheld by the server, not only hidden by the page (Landing row 1) | Server nulls `totalScore` and `maxScore` unless graded and flag set | The request says "if the flag is false, keep the current behavior"; today no screen shows the score, and hiding it only in the page leaves it in the network response. Nothing reads the field today, so nothing regresses. | n |
| An ungraded save that carries `showResultsAfterSubmit: true` is coerced to `false`, not rejected | Silent coerce, in the domain | Matches "always false in the DB" and the existing silent, lossy behaviour of turning grading off. | n |
| Checkbox label | "Show score after submit" on both screens | The stored name says "results", but what the respondent sees is a score; the label says what they get. | n |
| A graded form whose questions are all worth 0 points | The respondent sees `0/0` | Honest and unspecial-cased; rare, and the owner set those points. | n |
| Ticking Graded off and on again in the editor | The flag stays `false` after being turned back on | Untick already forces it to `false` (criterion 8); silently restoring an old value would be the surprise. | n |
| The stored maximum is not snapshotted | `maxScore` is computed from the form at submit time | Scores are derived from the current form (ADR 0004); a snapshot would be a second source of truth. | y |
| Frontend test runner (Landing row 4) | Add Vitest + Testing Library as devDependencies and prove criteria 1-3, 6-8, 15-16 with component tests | Without it those eight criteria have no proof stronger than a type-check. Alternative: skip the runner and verify them by hand, leaving them unproven in `verification.md`. | n |

**Open questions:** none - all resolved or logged above.

## Observable

| Surface | Decision | Landing |
| --- | --- | --- |
| screen `CreateFormPage` checkbox | empty / default state | AC 1 |
| screen `CreateFormPage` checkbox | hidden state | AC 2 |
| screen `CreateFormPage` | error and loading states | existing - the form's `isSubmitting` button and `showError` toast already cover generation |
| screen `CreateFormPage` | destructive action confirms | n/a - ticking a checkbox destroys nothing |
| screen `FormConfigTab` checkbox | empty / default state | AC 6 |
| screen `FormConfigTab` checkbox | hidden state | AC 7 |
| screen `FormConfigTab` | error and loading states | existing - the editor page's `saving` state and `showError` toast |
| screen `FormConfigTab` | destructive action confirms | n/a - the un-grading that clears the flag is confirmed by nothing today, see Out of scope |
| screen `FormAnswerPage` submitted state | shown score | AC 15 |
| screen `FormAnswerPage` submitted state | flag off | AC 16 |
| screen `FormAnswerPage` submitted state | empty / loading / error / unauthorised | existing - `loading`, `error` and `expired` states are unchanged |
| API `POST /api/forms/{formId}/submit` | response shape | AC 11, AC 12, AC 13 |
| API `POST /api/forms/{formId}/submit` | error shape and codes | existing - `ValidationException` and `NotFoundException` unchanged |
| API `PUT /api/forms/{id}/editor` | response shape, error shape and codes | AC 9, AC 10; errors existing |
| API `POST /api/forms/generate/text` | response shape, error shape and codes | AC 4, AC 5; errors existing |
| API `POST /api/forms/{formId}/submit` | who may call it | existing - anonymous, unchanged |
| all three routes | versioning, rate limits | n/a - the API is unversioned and has no rate limiting (`docs/known-gaps.md`); a field added to an existing route changes neither |

## Sources

- The request (this session) - the flag on the create page and editor, visible only on a graded form and always `false` otherwise, and `6/10` on the answer page.
- `CLAUDE.md` "Grading and scoring" and ADR 0004 - scores are derived from the current form, so the maximum is too.
