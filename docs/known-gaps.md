# Known gaps

Everything here is a place where the code and the intent disagree: features described but not built, code that exists but is unused, and behaviour that is wrong on purpose for now. It exists so that neither the next reader nor an agent builds on something that isn't there.

Last audited against the code on 2026-08-25 (grading and scoring).

## Not built

| Feature | What actually exists |
|---|---|
| **Result analysis by AI** | `IAnalysisService` and `AnalysisResult` are declared in `Application/AI/`. There is no implementation, no registration and no endpoint. The README used to promise this. |
| **Generation from PDF, Word, image or URL** | Only `POST /api/forms/generate/text` exists. `PdfExtractor` and `UrlScraper` are unreferenced classes that throw `NotImplementedException`. `GenerateFormRequest` accepts `SourceType` and `SourceUrl`, but `GenerateFormHandler` hardcodes `SourceType.Text` and ignores the URL. |
| **Realtime updates (SignalR)** | `FormHub` is an empty class containing two comments. `AddSignalR()` and `MapHub` are absent from `Program.cs`, so the hub isn't even routed, and the frontend has no SignalR client or dependency. Neither `ReceiveSubmission` nor `GenerationProgress` is ever emitted. |
| **Rate limiting on generation** | None. Every call to `generate/text` spends Anthropic credits, and any signed-in user can call it in a loop. |
| **Aggregated results for the owner** | The owner can see how many submissions a form has (`GET /api/forms/{id}/submissions/count`). There is no endpoint returning the submissions themselves, so nothing enforces "only the owner may view individual results". |
| **Multiple source items per form** | `FormSourceContent` is a list and `GenerateFormHandler.CombineItems` can merge several sources, but generation always builds exactly one item from `SourceText`. |
| **Editing the expiry date** | Nothing sends `ExpiresAt` after creation — `SaveFormEditorHandler` preserves whatever is stored, and the endpoint that could set it (`PUT /api/forms/{id}`) has been removed. Generated forms get a fixed 15-day expiry that the UI cannot change. |
| **Showing a score to anyone** | `Submission.Score` and `Answer.Score` are computed and stored, and `POST {id}/submit` returns the total, but no screen displays it. There is no results screen, so nothing computes the denominator either — a score of 7 is stored without anything recording that it was out of 8. Now that points vary per question, the owner cannot see a form's total either. |
| **Unit tests** | `FormAI.UnitTests` contains no test files. The only test in the solution is `UserRepositoryTests` in `FormAI.IntegrationTests`. `FormAI.Domain/Scoring/` is pure and has no dependencies, so it is the obvious first thing to test — the null/0 rules and the invariant-culture numeric parsing are currently unverified. |

## Wrong or incomplete on purpose

| Behaviour | Detail |
|---|---|
| **Nothing warns about a graded form with no answer key** | Ticking "Graded form" and saving without marking anything is allowed on purpose — the owner needs to be able to tick the box first and fill in the keys second. But such a question can never be earned, scores 0 for everyone, and no message anywhere says so. |
| **Renaming an option silently rescores past submissions** | Deliberate, and the direct consequence of [ADR 0001](./adr/0001-selected-option-text-snapshot.md) — see [ADR 0004](./adr/0004-scores-recomputed-from-current-form.md). A respondent who picked the correct option is marked wrong if the owner later renames it. Nothing warns the owner and nothing tells the respondent. |
| **The owner can answer their own private form** | Every access check on the answering path is `!IsPublic && CreatedBy != requestingUserId`, so the owner passes it on the submit path too and produces a real submission counted in their dashboard. Intended behaviour is that a private form is answerable by nobody, with a separate preview mode for the owner to see what the form looks like. Not decided yet. |
| **`ShowResultsAfterSubmit` gates nothing** | The flag is stored, updated and returned by every relevant DTO, and no code ever reads it to decide anything. In particular it does not stop `POST {id}/submit` returning the respondent's score. |
| **A private form answers 404, not 403** | Non-owners get `NotFoundException` → 404 for a private form on the answering paths. This is deliberate — it hides the form's existence. `GET /api/forms/{id}` does return 403 for a published form that isn't yours, since its existence is not a secret but its answer key is. |
| **Rescoring is unbounded** | A grading-relevant editor save loads every submission of the form with its answers and rewrites them. Fine at current sizes; the first thing to revisit if a form ever collects thousands of submissions. |
