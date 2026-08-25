# Known gaps

Everything here is a place where the code and the intent disagree: features described but not built, code that exists but is unused, and behaviour that is wrong on purpose for now. It exists so that neither the next reader nor an agent builds on something that isn't there.

Last audited against the code on 2026-08-25.

## Not built

| Feature | What actually exists |
|---|---|
| **Result analysis by AI** | `IAnalysisService` and `AnalysisResult` are declared in `Application/AI/`. There is no implementation, no registration and no endpoint. The README used to promise this. |
| **Generation from PDF, Word, image or URL** | Only `POST /api/forms/generate/text` exists. `PdfExtractor` and `UrlScraper` are unreferenced classes that throw `NotImplementedException`. `GenerateFormRequest` accepts `SourceType` and `SourceUrl`, but `GenerateFormHandler` hardcodes `SourceType.Text` and ignores the URL. |
| **Realtime updates (SignalR)** | `FormHub` is an empty class containing two comments. `AddSignalR()` and `MapHub` are absent from `Program.cs`, so the hub isn't even routed, and the frontend has no SignalR client or dependency. Neither `ReceiveSubmission` nor `GenerationProgress` is ever emitted. |
| **Rate limiting on generation** | None. Every call to `generate/text` spends Anthropic credits, and any signed-in user can call it in a loop. |
| **Aggregated results for the owner** | The owner can see how many submissions a form has (`GET /api/forms/{id}/submissions/count`). There is no endpoint returning the submissions themselves, so nothing enforces "only the owner may view individual results". |
| **Multiple source items per form** | `FormSourceContent` is a list and `GenerateFormHandler.CombineItems` can merge several sources, but generation always builds exactly one item from `SourceText`. |
| **Editing the expiry date** | `ExpiresAt` is settable through `PUT /api/forms/{id}`, but the form editor never sends it — `saveFormEditor` preserves whatever is stored. Generated forms get a fixed 15-day expiry that the UI cannot change. |
| **Unit tests** | `FormAI.UnitTests` contains no test files. The only test in the solution is `UserRepositoryTests` in `FormAI.IntegrationTests`. |

## Wrong or incomplete on purpose

| Behaviour | Detail |
|---|---|
| **Scoring is not trustworthy** | Scores *are* computed and stored on submit (`Answer.Score`, `Submission.Score`) and returned by the submit endpoint, but the feature is unfinished and nothing displays a score. Text answers are graded by trimmed, case-insensitive string equality against the suggested answer; numeric answers by exact value. Graded forms do not work end to end yet — treat any stored score as provisional. |
| **The owner can answer their own private form** | Every access check is `!IsPublic && CreatedBy != requestingUserId`, so the owner passes it on the submit path too and produces a real submission counted in their dashboard. Intended behaviour is that a private form is answerable by nobody, with a separate preview mode for the owner to see what the form looks like. Not decided yet. |
| **`ShowResultsAfterSubmit` gates nothing** | The flag is stored, updated and returned by every relevant DTO, and no code ever reads it to decide anything. |
| **`Points` types disagree** | `FormQuestion.Points` and `GetFormResponse.Points` are `int?`; `GenerateFormResponse.Points` is `decimal?`. |
| **A private form answers 404, not 403** | Non-owners get `NotFoundException` → 404 for a private form on every path (view, answer, submit). This is deliberate — it hides the form's existence — but it means "forbidden" never reaches the client. |

## Remove when convenient

| Thing | Why |
|---|---|
| ⚠️ **The close endpoint** | `PATCH /api/forms/{id}/close` → `CloseFormHandler` → `Form.Close()`, which sets `ExpiresAt` to now. It works, it is owner-checked, and **nothing in the UI calls it** — the only "close" in the frontend is dnd-kit's `closestCenter` and the toaster's `closeButton`. FormAI has no concept of closing a form; expiry is the only way a form stops accepting submissions. Delete `CloseFormHandler`, `CloseFormRequest`, `Form.Close()`, the controller action and the DI registration. |
