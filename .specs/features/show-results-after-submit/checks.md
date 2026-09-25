# Show results after submit checks

Profile: light
Plan: `.specs/features/show-results-after-submit/plan.md`

34 checks in 3 slices plus a cross-cutting group · 3 one-way doors · 0 open, of which 0 block

Proof commands run from the repo root. `dotnet test` targets the unit project alone (no Docker);
`vitest` and `playwright` run from `frontend/`. The e2e proofs need the repo's local stack up
(Postgres, Redis, Mailpit) and `.env`, exactly as `npm run test:e2e` already does.

## Checks

### S1 - The owner sets the flag when creating a form · 7 files · 35 KB · ~9k

**C1** - `Form.Create` stores `ShowResultsAfterSubmit = true` only for (flag true, graded true) and `false` for (true, ungraded), (false, graded) and (false, ungraded) (AC 4, AC 5, door 2)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~FormShowResultsTests.Create_StoresFlagOnlyOnAGradedForm"`

**C2** - On the create page, ticking "Graded form" makes a "Show score after submit" checkbox appear, unticked (AC 1)
Proof: `cd frontend && npx vitest run src/pages/CreateFormPage.test.tsx -t "shows an unticked score checkbox once Graded form is ticked"`

**C3** - On the create page, the "Show score after submit" checkbox is absent on load and again after "Graded form" is unticked (AC 2)
Proof: `cd frontend && npx vitest run src/pages/CreateFormPage.test.tsx -t "hides the score checkbox while Graded form is unticked"`

**C4** - The create page sends `showResultsAfterSubmit: true` to `POST /api/forms/generate/text` only when both boxes are ticked at submit, and `false` in the other three combinations, including score ticked and then Graded unticked (AC 3, door 3)
Proof: `cd frontend && npx vitest run src/pages/CreateFormPage.test.tsx -t "sends showResultsAfterSubmit only when graded and score are both ticked"`

**C5** - `GenerateFormHandler` given `IsGraded = true` and `ShowResultsAfterSubmit = true` persists a form whose `ShowResultsAfterSubmit` is `true` (AC 4)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~GenerateFormTests.GradedRequestWithScoreFlag_CreatesFormThatShowsScore"`

**C6** - `GenerateFormHandler` given `IsGraded = false` and `ShowResultsAfterSubmit = true` persists a form whose `ShowResultsAfterSubmit` is `false` and returns without throwing (AC 5)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~GenerateFormTests.UngradedRequestWithScoreFlag_CreatesFormWithFlagOff"`

### S2 - The owner sets the flag in the editor · 8 files · 45 KB · ~12k

**C7** - `Form.Update` stores `ShowResultsAfterSubmit = true` only for (flag true, graded true) and `false` for (true, ungraded), (false, graded) and (false, ungraded) (AC 9, AC 10, door 2)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~FormShowResultsTests.Update_StoresFlagOnlyOnAGradedForm"`

**C8** - `FormConfigTab` on a graded form renders the "Show score after submit" checkbox ticked when its value is `true` and unticked when it is `false` (AC 6)
Proof: `cd frontend && npx vitest run src/components/FormConfigTab.test.tsx -t "shows the score checkbox reflecting its value when the form is graded"`

**C9** - `FormConfigTab` on an ungraded form does not render the checkbox (AC 7)
Proof: `cd frontend && npx vitest run src/components/FormConfigTab.test.tsx -t "hides the score checkbox when the form is ungraded"`

**C10** - `FormEditorPage` loading a graded form stored with the flag `true` shows the Configuration tab checkbox ticked, and after a save whose refetch returns `false` shows it unticked (AC 6)
Proof: `cd frontend && npx vitest run src/pages/FormEditorPage.test.tsx -t "reflects the stored flag on the Configuration tab after load and after save"`

**C11** - `FormEditorPage` sends `showResultsAfterSubmit: false` in `PUT /editor` after the owner unticks "Graded form" on a form loaded with the flag `true`, and the checkbox is gone from the Configuration tab (AC 7, AC 8)
Proof: `cd frontend && npx vitest run src/pages/FormEditorPage.test.tsx -t "sends showResultsAfterSubmit false after Graded form is unticked"`

**C12** - `FormEditorPage` with "Graded form" unticked and ticked again shows the Configuration tab checkbox unticked, and the next save sends `showResultsAfterSubmit: false` (assumption: the flag is not restored)
Proof: `cd frontend && npx vitest run src/pages/FormEditorPage.test.tsx -t "keeps the flag off when Graded form is ticked again"`

**C13** - `SaveFormEditorHandler` given `IsGraded = true` and `ShowResultsAfterSubmit = true` leaves the stored form with `ShowResultsAfterSubmit = true` (AC 9)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~SaveFormTests.GradedSaveWithScoreFlag_StoresFlagOn"`

**C14** - `SaveFormEditorHandler` given `IsGraded = false` and `ShowResultsAfterSubmit = true` leaves the stored form with `ShowResultsAfterSubmit = false` and returns without throwing (AC 10)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~SaveFormTests.UngradedSaveWithScoreFlag_StoresFlagOff"`

**C15** - A save that changes only `ShowResultsAfterSubmit` does not read the submissions for rescoring (Impact: the flag is not in `GradingFingerprint`)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~SaveFormTests.ChangedOnlyTheScoreFlag_DoesNotTriggerRescore"`

**C16** - Over HTTP, `PUT /api/forms/{id}/editor` answers `204` with `showResultsAfterSubmit: true` on a graded form and `GET /api/forms/{id}` then returns `true`; with `isGraded: false` and `showResultsAfterSubmit: true` it answers `204` and `GET` returns `false` (AC 9, AC 10, door 3)
Proof: `cd frontend && npx playwright test e2e/show-results-after-submit.spec.ts -g "editor save stores the flag only on a graded form"`

### S3 - The respondent sees their score when the owner allowed it · 14 files · 100 KB · ~26k

**C17** - `SubmissionScorer.MaximumScore` on a graded form with questions worth 2, null, 0 and 3 points returns 5 (AC 11)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~SubmissionScorerTests.MaximumScore_GradedFormSumsPointsCountingUnsetAsZero"`

**C18** - `SubmissionScorer.MaximumScore` on an ungraded form returns null (AC 13)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~SubmissionScorerTests.MaximumScore_UngradedFormIsNull"`

**C19** - `FormResultsCalculator.Calculate(...).TotalPoints` equals `SubmissionScorer.MaximumScore` for a graded form with a null-points question and for an ungraded form (Flow 5)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~FormResultsCalculatorTests.TotalPoints_EqualsTheSubmissionScorerMaximum"`

**C20** - `SubmitFormHandler` on a graded form with the flag set, questions worth 2 and 3 and only the 2-point one answered right, returns `TotalScore` 2 and `MaxScore` 5 (AC 11, door 1)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~SubmitFormTests.GradedFormWithScoreFlag_ReturnsScoreAndMaximum"`

**C21** - `SubmitFormHandler` on a graded form with the flag off returns `TotalScore` null and `MaxScore` null, yet persists a `Submission` whose `Score` is 5 after a fully right submission (AC 12, AC 14, door 1)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~SubmitFormTests.GradedFormWithoutScoreFlag_WithholdsScoreButPersistsIt"`

**C22** - `SubmitFormHandler` on an ungraded form whose stored flag is `true` (a legacy row) returns `TotalScore` null and `MaxScore` null (AC 13, door 1)
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~SubmitFormTests.UngradedFormHoldingTheFlag_WithholdsScoreAndMaximum"`

**C23** - Over HTTP, `POST /api/forms/{id}/submit` on a graded form with the flag set answers `200` with the wire body `{ id, totalScore: 2, maxScore: 2 }` for a right answer to the 2-point question in a form whose other question is worth 0 (AC 11, door 1)
Proof: `cd frontend && npx playwright test e2e/show-results-after-submit.spec.ts -g "submit returns score and maximum only on a graded form with the flag set"`

**C24** - Over HTTP, the same submit returns `totalScore: null` and `maxScore: null` on a graded form with the flag off and on an ungraded form, in both cases `200` (AC 12, AC 13, door 1)
Proof: `cd frontend && npx playwright test e2e/show-results-after-submit.spec.ts -g "submit returns score and maximum only on a graded form with the flag set"`

**C25** - `FormAnswerPage` after a submit answered `{ id, totalScore: 6, maxScore: 10 }` shows the text `6/10` together with "Thanks! Your response has been recorded." (AC 15)
Proof: `cd frontend && npx vitest run src/pages/FormAnswerPage.test.tsx -t "shows the score out of the maximum after submitting"`

**C26** - `FormAnswerPage` after a submit answered with (`totalScore` null, `maxScore` null), (null, 10) or (6, null) shows only the thanks message and no `n/m` text (AC 16)
Proof: `cd frontend && npx vitest run src/pages/FormAnswerPage.test.tsx -t "shows only the thanks message when the score or the maximum is null"`

**C27** - In a browser, an owner who ticks "Show score after submit" on the Configuration tab of a graded form and saves sees it still ticked after reload, and an anonymous respondent who then submits the right answer sees `2/2` (AC 6, AC 15)
Proof: `cd frontend && npx playwright test e2e/show-results-after-submit.spec.ts -g "owner ticks the score checkbox and the respondent sees the score"`

### Cross-cutting

**C28** - The docs say what the code now does: `CONTEXT.md` defines **maximum score**, `docs/known-gaps.md` no longer lists "`ShowResultsAfterSubmit` gates nothing" and lists that a respondent who reopens a submitted form does not see their score, and `CLAUDE.md` no longer says the flag gates nothing and says the flag is false unless the owner ticks it on a graded form
Proof: `grep -q "maximum score" CONTEXT.md`
Proof: `! grep -q "gates nothing" docs/known-gaps.md`
Proof: `grep -q "reopens" docs/known-gaps.md`
Proof: `! grep -q "gates nothing" CLAUDE.md`
Proof: `grep -q "unless the owner ticks" CLAUDE.md`

**C29** - The whole frontend type-checks and lints with `SubmitFormResult` matching the wire (`id`, `totalScore`, `maxScore`)
Proof: `cd frontend && npm run build`
Proof: `cd frontend && npm run lint`

**C30** - Unchanged by this feature: `SubmitFormHandler` on an expired form still throws `ValidationException`, which `ExceptionHandlingMiddleware` maps to `400`
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~SubmitFormTests.Expired_ThrowsValidationException"`

**C31** - Unchanged by this feature: `SubmitFormHandler` on a non-public form still throws `NotFoundException`, mapped to `404`
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~SubmitFormTests.NonPublic_ThrowsNotFoundException"`

**C32** - Unchanged by this feature: `SaveFormEditorHandler` on duplicate option text still throws `ValidationException`, mapped to `400`
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~SaveFormTests.DuplicateOptionText_ThrowsValidationException"`

**C33** - Unchanged by this feature: `SaveFormEditorHandler` for a non-owner still throws `NotFoundException`, mapped to `404`
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~SaveFormTests.NonOwner_ThrowsNotFoundException"`

**C34** - Unchanged by this feature: `GenerateFormHandler` still rejects an invalid title, source text or expiry with `ValidationException` before generating or saving, mapped to `400`
Proof: `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~GenerateFormTests.InvalidRequest_IsRejectedBeforeGeneratingOrSaving"`

## Coverage

| Set (size) | Member -> proof | Unproven |
| --- | --- | --- |
| `Form.Create` (flag, graded) combinations (4) | true,true C1 · true,false C1 · false,true C1 · false,false C1, table-driven over all 4 | - |
| `Form.Update` (flag, graded) combinations (4) | true,true C7 · true,false C7 · false,true C7 · false,false C7, table-driven over all 4 | - |
| create page payload (graded, score) ticked states (4) | both ticked C4 · graded only C4 · score ticked then graded unticked C4 · neither C4, table-driven over all 4 | - |
| submit reveal matrix (graded, flag) (4) | graded and flag C20 C23 · graded, flag off C21 C24 · ungraded, flag off C24 · ungraded, flag stored on C22 | - |
| `FormAnswerPage` (`totalScore`, `maxScore`) nullness (4) | both set C25 · both null C26 · score null, max set C26 · score set, max null C26 | - |
| `POST /api/forms/{formId}/submit` statuses (3) | 200 C23 · 400 C30 · 404 C31 | - |
| `PUT /api/forms/{id}/editor` statuses (3) | 204 C16 · 400 C32 · 404 C33 | - |
| `POST /api/forms/generate/text` statuses (2) | 201 C5 · 400 C34 | - |
| `Landing` doors (3) | withhold on the server C20 C21 C22 C23 C24 · coerce in the domain C1 C7 C6 C14 · field name `showResultsAfterSubmit` C4 C16 | - |

- Claims naming a status code, route or response shape: C16, C23, C24 - each has a Playwright proof that
  crosses the HTTP boundary. C5 and C6 assert the handler's outcome and name no status; the `201`
  is the controller's unchanged `CreatedAtAction` after the handler returns, and generation cannot
  run over HTTP without a live Claude key
- C30-C34 are the existing `400`/`404` members, unchanged by this feature. They are proven at
  handler level only, by tests that already exist; each claim is worded as the exception the
  handler throws, and the mapping to a status is `ExceptionHandlingMiddleware`, untouched here
- No other check claims more than the single case its proof exercises

## Swept

- validation: C1, C6, C7, C14 - the flag is coerced to `false` on an ungraded form, never rejected
- failure modes: n/a - `MaximumScore` is a pure sum over the questions already loaded for scoring; no new I/O or partial state
- idempotency: existing - `SubmitFormHandler` rejects a second submission (`AlreadySubmitted`, ADR 0003), so a retry after a lost response cannot re-show the score; that gap is recorded in `docs/known-gaps.md` by C28
- authorization: existing - the flag is written only through `PUT /editor` (`FormAccessValidator.CheckOwnerAccess`) and generation (creator is the owner); the score goes back only in the body of the respondent's own `POST /submit`
- concurrency: n/a - the flag and the questions are read once from the same loaded `Form` per submit, so a save racing a submit yields the old or the new state consistently and there is no ordering to enforce
- data lifecycle: C22 - legacy rows holding the flag on an ungraded form are gated by `IsGraded` and not backfilled
- dependency failure: n/a - no external dependency is added or called
- state transitions: C11, C12, C7 - un-grading clears the flag and re-grading does not restore it
- observability: n/a - no logging requirement in this change

## Handoff

- S1 = ~9k (Domain, Application, `CreateFormPage`), S2 = ~12k (`FormEditorPage`, `FormConfigTab`, `SaveFormEditor`), S3 = ~26k (`SubmitForm`, `SubmissionScorer`, `FormAnswerPage`, e2e, docs); ~180 KB of touched files plus ~40 KB of new tests, about 55k in total, under the 150k budget - one builder
- Mechanism: one builder (fits, no ask)
