# Known gaps

Everything here is a place where the code and the intent disagree: features described but not built, code that exists but is unused, and behaviour that is wrong on purpose for now. It exists so that neither the next reader nor an agent builds on something that isn't there.

Last audited against the code on 2026-09-14 (the Results tab now has an Individual sub-tab, so the individual-submission endpoints are called from the frontend, not just the backend; `FormAccessValidator` still centralizes every access check into `CheckOwnerAccess` and `CheckUserAnswerAccess`; every check in the app answers 404, never 403; the owner can no longer answer or read their own private/expired form through the answering path).

## Not built

| Feature | What actually exists |
|---|---|
| **Result analysis by AI** | `IAnalysisService` and `AnalysisResult` are declared in `Application/AI/`. There is no implementation, no registration and no endpoint. The README used to promise this. |
| **Generation from PDF, Word, image or URL** | Only `POST /api/forms/generate/text` exists. `PdfExtractor` and `UrlScraper` are unreferenced classes that throw `NotImplementedException`. `GenerateFormRequest` accepts `SourceType` and `SourceUrl`, but `GenerateFormHandler` hardcodes `SourceType.Text` and ignores the URL. |
| **Rate limiting on generation** | None. Every call to `generate/text` spends Anthropic credits, and any signed-in user can call it in a loop. |
| **Multiple source items per form** | `FormSourceContent` is a list and `GenerateFormHandler.CombineItems` can merge several sources, but generation always builds exactly one item from `SourceText`. |
| **Showing a respondent their score** | `POST {id}/submit` returns the total, and the Results tab shows the owner the score distribution (Summary sub-tab) and each submission's own score (Individual sub-tab), but no screen shows a respondent what they earned, and `ShowResultsAfterSubmit` still gates nothing. |

## Wrong or incomplete on purpose

| Behaviour | Detail |
|---|---|
| **`ShowResultsAfterSubmit` gates nothing** | The flag is stored, updated and returned by every relevant DTO, and no code ever reads it to decide anything. In particular it does not stop `POST {id}/submit` returning the respondent's score. |
| **Reading every submission at once is unbounded** | Two reads have this shape, and both are deliberately named so they are easy to find: `ISubmissionRepository.GetByFormForScoringAsync` (tracked, so a grading-relevant editor save can rescore and commit) and `GetByFormWithAnswersAsync` (untracked and split-query, for the results view). Each loads every submission of the form with its answers and selected options. Fine at current sizes; the two call sites to revisit if a form ever collects thousands of submissions. |
