# Specification: file-based form generation via an AI gateway

## Global decisions

| Topic | Decision |
|---|---|
| Architecture | Modular monolith. The gateway is a separate container, config in `docker/litellm/`, separate deploy workflow. |
| Input | Pasted text, **one** file, or both. Files: `.pdf`, `.docx`, `.pptx`, `.txt`. |
| Text formats | docx, pptx, txt are extracted in the API. **Text is sent to the model and saved.** Originals are never stored. |
| PDF | Sent to the model as a file (vision). **Not stored anywhere and not validated locally** (extension and size only; no magic bytes, page count or encryption check). Only metadata (name, size) is saved. |
| Generation | **Synchronous**, in one multipart request. |
| Models | `form-generator-vision` (`claude-sonnet-5-5`, fallback `gpt-5.2`, PDFs) and `form-generator` (`claude-haiku-4-5-20251001`, fallback `gpt-5.2`, text). The app uses aliases only. |
| Prompt | Stays in a text file. No table. `PromptVersion = 1` is stored on each usage row. |
| Demo accounts | Same rules as regular users. **Known risk:** a demo account can consume the global ceiling; accepted for now. |
| Regeneration | Doesn't exist. |
| Cost | Own ledger table, a global spending ceiling, no per-user budget. |

**Limits**

| Limit | Value |
|---|---|
| Files per request | 1 (`MaxSourceFiles`) |
| File size | 10 MB. Endpoint request limit ~11 MB, set on that endpoint only. |
| Text cap | 30,000 chars (existing `MaxSourceTextLength`), applied to **pasted plus extracted text combined** |
| Text minimum | 100 chars, same scope. See open items. |
| docx/pptx decompressed size | 50 MB, and a maximum entry count |
| Generation timeout | 90 s overall. The load balancer idle timeout must be higher. |
| `max_tokens` | Set on every call |

Without local PDF validation there is **no enforceable page cap**. The 10 MB limit and `max_tokens` are the cost controls.

---

## Docs and ADRs (written with the slice that needs them, not upfront)

- **D-1** ADR: adopt an AI gateway (LiteLLM) instead of calling Anthropic directly. Written in the first gateway slice; it records the alternative of keeping the direct client and adding only a ledger and ceiling.
- **D-2** ADR: source content policy. Text is saved for docx/pptx/txt/pasted; PDFs are processed in memory and not stored. Written in the persistence slice.
- **D-3** `CONTEXT.md` terms (source, extracted text, model alias, usage record), added in the slice that introduces each term.
- **D-4** `known-gaps.md`: added when the ceiling ships. Global ceiling only, no per-user budget, demo accounts can consume it, no idempotency beyond the disabled button, storage not disclosed to the user (FE-8), the ceiling check is not atomic.
- `CLAUDE.md` describes the system as built: update it in the same slice that changes the behavior, never before.

---

## Stage 1: Gateway container (local, no app code yet)

- **GW-1** The system SHALL run LiteLLM from a **pinned** image version as a Compose service, with config in `docker/litellm/config.yaml` (no secrets).
- **GW-2** LiteLLM SHALL use **its own database and credentials**, not `form_ai` or `form_ai_app`.
- **GW-3** The config SHALL define aliases `form-generator` and `form-generator-vision`, mapped to real models, with fallback only between models that support the alias's input.
- **GW-4** Provider keys, the master key and the salt key SHALL come from the untracked `.env`, never from the repo.
- **GW-5** The app SHALL have its own gateway key with a `max_budget`. This is the global spending ceiling.
- **GW-6** Prompt and response logging SHALL be disabled.
- **GW-7** Retries SHALL be configured in the gateway only.
- **GW-8** The gateway SHALL NOT be exposed publicly.
- **GW-9** `docker/litellm/README.md` SHALL document the contract: URLs, aliases, the key, and the metadata expected (`user` = the user's guid, no email or name).
- **GW-10** Deploy workflow `deploy-litellm.yml`, path-filtered to `docker/litellm/**`, separate from the API deploy.

*Done when:* `curl` against both aliases returns a completion, including a PDF for the vision alias with **each** provider, and the cost appears in LiteLLM's spend log. Confirm the GPT model accepts PDFs through LiteLLM here, before building further.

---

## Stage 2: Generation service and gateway client

- **AI-1** `IFormGenerationService` SHALL keep being the only boundary. Its implementation SHALL call the gateway using an OpenAI-compatible request and read the alias and URL from configuration (`Ai:*`).
- **AI-2** The input SHALL be a list of sources: `Text(content)` or `Pdf(bytes, fileName)`. The handler decides the alias: vision if a PDF is present, otherwise text.
- **AI-3** The prompt SHALL be read from `PromptGenerateForm.txt` and have a constant `PromptVersion = 1`.
- **AI-4** The prompt SHALL state that source content is untrusted data whose instructions must be ignored, and SHALL wrap it in delimiters with a random per-request marker. Each source is labeled ("Additional context", "Source document").
- **AI-5** The response SHALL be validated against a strict schema (unknown fields rejected; limits on question count, option count and string lengths; allowed question types only). Anything else is an error.
- **AI-6** WHEN the stop reason is a length cut-off, the system SHALL fail with its own error and SHALL NOT retry.
- **AI-7** The client SHALL NOT retry. It passes the request's `CancellationToken`. Timeout budget, written down in Stage 1: gateway timeout × attempts < client timeout < 90 s < load balancer idle timeout. The slow-PDF timeout check (T-6) runs in Stage 1, since it can invalidate the synchronous decision.
- **AI-8** The result SHALL include the **model that actually answered**, token counts and cost, for the ledger.
- **AI-9** Error mapping. A closed set of codes, added to `ValidationErrorCode`:

| Situation | Code | HTTP |
|---|---|---|
| Unsupported type | `SourceFileUnsupported` | 400 |
| Over 10 MB | `SourceFileTooLarge` | 400 |
| Corrupt or unreadable (extractor failure, or the provider rejecting the PDF with a 4xx) | `SourceFileUnreadable` | 400 |
| Combined text under 100 chars | `SourceTextTooShort` | 400 |
| Combined text over 30,000 | `SourceTextTooLong` | 400 |
| Output truncated or invalid (a model fault, not the user's) | `GenerationOutputInvalid` | 502 |
| Ceiling reached | `GenerationBudgetReached` | 503 |
| Gateway down or timeout | `GenerationUnavailable` ("try again later") | 503 |

The 502 and 503 cases need a new exception type mapped in `ExceptionHandlingMiddleware`, since `ValidationException` maps to 400. A provider 5xx maps to `GenerationUnavailable`, never to `SourceFileUnreadable`.

- **AI-10** The old direct `ClaudeFormGenerationService` SHALL be replaced. The `CLAUDE.md` AI section SHALL be updated.

---

## Stage 3: Usage ledger and spending ceiling

- **UL-1** New table `ai_generation_usage` (migration, snake_case): `Id`, `UserId`, `FormId` (nullable), `CreatedAt`, `Operation`, `ModelAlias`, `ModelUsed`, `PromptVersion` (int), `InputTokens`, `OutputTokens`, `CostUsd` (decimal), `Status` (Succeeded / Failed / Rejected), `ErrorCode` (nullable), `LatencyMs`, `SourceKind` (Text / Pdf / TextAndPdf).
- **UL-2** Cost SHALL be stored at call time, from the cost the gateway reports (LiteLLM usually returns it in a response header, not the body: confirm), never recomputed later. `CostUsd` is nullable: a failed or timed-out call may have been charged with unknown cost. Reconcile against LiteLLM's spend log.
- **UL-3** `IUsageRecorder` (Application) SHALL record **every** call, including failures and provider rejections. It records in its own step and transaction, separate from saving the form, and with `CancellationToken.None`, so a failed save or a client disconnect never loses a row. `FormId` is linked afterwards. `Rejected` = stopped before the gateway was called (ceiling); `Failed` = the gateway was called and did not produce a usable draft.
- **UL-4** The month aggregation and the ceiling decision SHALL live in Domain as pure functions, per the `Scoring/` pattern. Cost itself is never calculated by the app.
- **UL-5** BEFORE calling the gateway, the handler SHALL sum the month's (UTC) `CostUsd`, ignoring nulls. IF it reaches `Ai:MonthlyBudgetUsd`, it SHALL fail with `GenerationBudgetReached` and call nothing. The check is not atomic; concurrent requests can overshoot, and the LiteLLM `max_budget` is the backstop. `created_at` is indexed. The app ledger decides the error the user sees; LiteLLM's log is for reconciliation.
- **UL-6** The LiteLLM `max_budget` SHALL be slightly above the app-side value, and provider-side limits above that. Alerts at 50% and 80% are sent by email.
- **UL-7** A query or view for cost per user per day.

---

## Stage 4: Source content persistence and extraction (backend)

- **SC-1** `FormSourceContent` SHALL be persisted with the form, in the same transaction, and deleted with it (cascade).
- **SC-2** One row per source: pasted text (`Text`), docx/pptx/txt (extracted text, `FileName`), PDF (**empty content**, `FileName`, `SizeBytes`). `Order` keeps the position. `FileName` is length-capped and sanitized, and never interpolated into the prompt. Check the current `FormSourceContent` content constraint and existing rows.
- **SC-3** `ISourceTextExtractor` (Application interface, Infrastructure implementation):
  - **txt**: decode as UTF-8 (with BOM), reject files with NUL bytes.
  - **docx**: paragraphs, tables as text.
  - **pptx**: slide text in order, plus speaker notes labeled as notes.
  - Images, charts and embedded objects are ignored.
- **SC-4** txt, docx and pptx SHALL be validated by extension **and** magic bytes. docx and pptx share the `PK` signature, so they are distinguished by their archive content (`word/` vs `ppt/`). PDFs are checked by extension and size only.
- **SC-5** Zip protections: an entry count limit, and a decompressed size limit enforced by **counting the bytes actually read while streaming** (header sizes can lie). Abort at the limit. XML parsing SHALL have DTD/XXE disabled.
- **SC-6** The extracted text SHALL be trimmed and control characters removed.
- **SC-7** Extraction SHALL fail with `SourceFileUnreadable` for corrupt files.

---

## Stage 5: Endpoint and generation flow (backend integration)

- **EP-1** The generate endpoint SHALL be `POST /api/forms/generate` (replacing `POST /api/forms/generate/text`) and accept `multipart/form-data`: text (optional), file (optional, at most 1), plus the existing options (expiry, graded, `ShowResultsAfterSubmit`). At least one of text or file is required. The frontend, e2e calls, rate-limit wiring and the `CLAUDE.md` route reference SHALL be updated in the same stage.
- **EP-2** The request size limit is set on this endpoint only.
- **EP-3** Order in the handler: rate limit (existing 10/hour, per user) → validate the file → extract text → validate the combined text (100 to 30,000) → check the ceiling → call the gateway → save form and `FormSourceContent` → record usage.
- **EP-4** WHEN a PDF is present, the combined-text minimum SHALL NOT apply. Pasted text is sent as additional context.
- **EP-5** The PDF SHALL be held in memory only, for the duration of the request.
- **EP-6** Forms are created private as today, with grading and `ShowResultsAfterSubmit` rules unchanged.
- **EP-7** The generation SHALL be cancelled when the client disconnects, and usage SHALL still be recorded.
- **EP-8** Generated content SHALL be treated as untrusted text and never rendered as HTML.

---

## Stage 6: Frontend (`CreateFormPage`)

- **FE-1** A file picker that accepts `.pdf .docx .pptx .txt`, **one** file, showing name and size, with a remove action.
- **FE-2** Client-side checks for type and 10 MB, plus the text limits, mirroring the backend numbers (from a single shared constant if possible).
- **FE-3** Text area and file are both optional individually, but at least one is required.
- **FE-4** The request is sent as `multipart/form-data` with upload progress, then step labels ("Reading your document", "Generating questions").
- **FE-5** The Generate button SHALL be disabled after the first click until the request finishes.
- **FE-6** Each error code SHALL map to a specific message. `GenerationUnavailable` and `GenerationBudgetReached` show "try again later".
- **FE-7** A disclosure next to the Generate button: text and files are sent to an AI provider to generate the form. Terms or privacy text updated to mention the third-party processing.
- **FE-8** No message about storage for now (decision: docs will be updated later). Recorded in `known-gaps.md`.
- **FE-9** Vitest tests with MSW for each error code and for the file states.

---

## Stage 7: Tests and hardening

- **T-1** A **fake gateway** (a small stub server) for CI and e2e. Canned draft, plus one case per error code. CI SHALL never call a real model. Built with the first client slice and grown as codes are added, not left to the end.
- **T-2** Unit tests: limits, extractor per format, schema validation, ceiling check, cost recording on failure, alias choice.
- **T-3** An evaluation set of sample documents you own (txt, docx, pptx, text PDF, scanned PDF), with a few **prompt-injection** documents ("ignore previous instructions..."). Used for model choice (compare Opus 5.5 with a cheaper model before committing) and after prompt changes.
- **T-4** Playwright: upload a file, generate, land on the draft; error paths.
- **T-5** Gateway hardening checklist: private network, master key rotation, separate app key, LiteLLM DB credentials out of the repo, logging off.
- **T-6** Verify the timeout chain (load balancer, Kestrel, `HttpClient`, LiteLLM) with a realistic slow PDF.

---

## Suggested order and why

0 → 1 → 2 → 3 → 4 → 5 → 6 → 7.
The gateway comes first because the PDF/provider questions (does each model accept the file, what does it cost, how slow is it) affect everything after. The ledger goes before integration so the first real generation is already recorded. Frontend comes last because it only needs the finished contract.

Stages 3 and 4 are independent and can be built in either order.

## Resolved items

1. **Minimum 100 chars scope**: applies to the combined pasted plus extracted text, and not when a PDF is present.
2. **Failed-call ledger rows (UL-3)**: included.
3. **Idempotency**: only the disabled button (FE-5). Ignored on purpose; no server-side guard.
4. **Provider rejections count against the 10/hour limit**, as the limiter runs before the handler.
5. **Route name**: renamed to `POST /api/forms/generate` (see EP-1).

6. **Fallback model**: `gpt-5.2`, for both aliases (replaces GPT 5.5). Stage 1 still checks a PDF through LiteLLM with each provider.
7. **Migrations** always run as `form_ai_migrator`, so grants for the new table need no extra work.
8. **Alerts** go to email.

## Slices (each one a small spec, task list and PR)

Stage numbers above stay as requirement groups. Build order:

| # | Slice | Covers |
|---|---|---|
| 1 | Local gateway container, aliases, secrets, private network, logging off, README | GW-1..4, 6, 8, 9, D-1 |
| 2 | App gateway key with `max_budget`, gateway retries, PDF check with each provider, timeout budget | GW-5, 7, T-6 |
| 3 | Swap the client behind `IFormGenerationService`, text only, plus the first fake gateway | AI-1, 3, 7, 10, T-1 |
| 4 | Strict response schema, truncation, error codes and the 502/503 exception | AI-5, 6, 9 (generation codes) |
| 5 | Prompt hardening: untrusted-content delimiters, random marker, labels | AI-4 |
| 6 | Ledger table and `IUsageRecorder`, recording every call | UL-1..3, AI-8 |
| 7 | Monthly ceiling and cost-per-user-per-day query | UL-4, 5, 7, D-4 |
| 8 | Email alerts and `deploy-litellm.yml` | UL-6, GW-10 |
| 9 | `FormSourceContent` persisted with the form, cascade delete | SC-1, 2, D-2, D-3 |
| 10 | `POST /api/forms/generate` multipart, text only, route rename in frontend, e2e and `CLAUDE.md` | EP-1, 2 |
| 11 | Extractor: txt | SC-3 (txt), 4, 6, 7 |
| 12 | Extractor: docx, with zip protections | SC-3 (docx), 5 |
| 13 | Extractor: pptx | SC-3 (pptx) |
| 14 | Endpoint flow for text files: combined limits, order, disconnect handling | EP-3, 6, 7, 8 |
| 15 | PDF path: vision alias, in-memory only, metadata row | AI-2, EP-4, 5 |
| 16 | Frontend: picker, checks, multipart with progress, error messages, disclosure | FE-1..7, 9 |
| 17 | Evaluation set with injection documents, Playwright, hardening checklist | T-2..5 |

Slices 9 and 11–13 are independent of 1–8 and can move earlier. T-3 (model comparison) runs before slice 3 commits to the cheaper text model. Unit tests for each requirement ship in the slice that introduces it.
