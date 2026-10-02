# Prompt Hardening Tasks

## Execution Protocol (MANDATORY -- do not skip)

Implement these tasks with the `tlc-spec-driven` skill: **activate it by name and follow its Execute flow and Critical Rules.** Do not search for skill files by filesystem path. The skill is the source of truth for the full flow (per-task cycle, sub-agent delegation, adequacy review, Verifier, discrimination sensor).

**If the skill cannot be activated, STOP and tell the user - do not proceed without it.**

---

**Spec**: `.specs/features/ai-prompt-hardening/spec.md`
**Design**: skipped. One pure helper and one prompt change inside an existing class; the choices are in the spec's assumptions.
**Status**: In Progress
**Branch**: `feature/generation-service-improvements`

---

## Test Coverage Matrix

> Generated from codebase, project guidelines, and spec - confirm before Execute. Guidelines found: `CLAUDE.md` (Tests section), `.github/workflows/ci.yml`, and the existing `GatewayFormGenerationServiceTests`.

| Code Layer | Required Test Type | Coverage Expectation | Location Pattern | Run Command |
| ---------- | ------------------ | -------------------- | ---------------- | ----------- |
| Infrastructure AI client and its helpers (`FormAI.Infrastructure/AI`) | unit (stub `HttpMessageHandler`, no Docker) | All branches; 1:1 to spec ACs; every listed edge case. Existing cases are a floor and stay | `tests/FormAI.IntegrationTests/*Gateway*Tests.cs`, `tests/FormAI.IntegrationTests/*Source*Tests.cs` | `dotnet test tests/FormAI.IntegrationTests/FormAI.IntegrationTests.csproj --filter "FullyQualifiedName~GatewayFormGenerationService\|FullyQualifiedName~UntrustedSource"` |
| Prompt template (`PromptGenerateForm.txt`) | unit, through the client | Asserted via the captured system message, never by reading the file | same as above | same as above |
| `CLAUDE.md`, `docs/known-gaps.md` | none | - (build gate only) | - | build gate only |
| `FormAI.Domain`, `FormAI.Application`, `FormAI.API`, `frontend/` | none | Untouched by this slice | - | not run |

The Infrastructure AI tests live in `FormAI.IntegrationTests` because `FormAI.UnitTests` references only Domain and Application. The filter keeps them off the Testcontainers tests, so no Docker is needed.

## Gate Check Commands

> Generated from codebase - confirm before Execute.

| Gate Level | When to Use | Command |
| ---------- | ----------- | ------- |
| Quick | After tasks that change the AI client or its helpers | `dotnet test tests/FormAI.IntegrationTests/FormAI.IntegrationTests.csproj --filter "FullyQualifiedName~GatewayFormGenerationService\|FullyQualifiedName~UntrustedSource"` |
| Full | Not used in this slice (no endpoint or database change) | - |
| Build | After the last code task and for docs-only tasks | `dotnet build FormAI.sln` and `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj` and `git diff --check` |

---

## Execution Plan

Before T1: one `docs` commit adds this spec and this file.

### Phase 1: Fence, prompt and docs

One dependency chain: the client uses the helper, and the docs describe the shipped behavior.

```
T1 → T2 → T3
```

---

## Task Breakdown

### T1: Add the untrusted-source fence helper

**What**: Add a pure helper that creates a per-request marker and wraps source text as the labeled, delimited user message.
**Where**: `src/FormAI.Infrastructure/AI/UntrustedSource.cs` (new)
**Also creates**: `tests/FormAI.IntegrationTests/UntrustedSourceTests.cs`. The co-located test is created in this task.
**Depends on**: None
**Reuses**: Static-helper style of `FormAISchema` and `GeneratedFormAIJSONParser`
**Requirement**: PH-01, PH-02

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] `NewMarker()` returns 32 lowercase hex characters, and 1,000 calls return 1,000 distinct values (spec P1 AC 3, 4)
- [x] `Wrap(sourceText, marker)` returns `Source document`, a newline, `<<<SOURCE {marker}>>>`, a newline, the text, a newline, `<<<END SOURCE {marker}>>>` (AC 1, 2)
- [x] The text is returned byte-for-byte: multi-line text, `<` and `>`, `{marker}`, `{questionCount}` and `<<<END SOURCE>>>` without a marker all survive unchanged (AC 5, 6, edge case)
- [x] Empty and whitespace text still produce both delimiters with nothing between them (edge case)
- [x] Quick gate passes: `dotnet test tests/FormAI.IntegrationTests/FormAI.IntegrationTests.csproj --filter "FullyQualifiedName~GatewayFormGenerationService\|FullyQualifiedName~UntrustedSource"`
- [x] Test count: every existing `GatewayFormGenerationServiceTests` case still runs, plus the new ones; none deleted

**Tests**: unit
**Gate**: quick

**Commit**: `feat(ai): add a marker-delimited fence for untrusted source text`

---

### T2: Use the fence in the request and rewrite the system prompt

**What**: Make `GenerateAsync` generate one marker per call, send the fenced source as the user message, and substitute the marker into the system prompt. `PromptVersion` stays 1.
**Where**: `src/FormAI.Infrastructure/AI/GatewayFormGenerationService.cs` (modify)
**Also modifies**: `src/FormAI.Infrastructure/AI/Prompt/PromptGenerateForm.txt` (adds the untrusted-data rule, the `{marker}` placeholder and the instruction-authority rule; keeps every existing rule) and `tests/FormAI.IntegrationTests/GatewayFormGenerationServiceTests.cs`. The co-located tests are written in this task.
**Depends on**: T1
**Reuses**: `UntrustedSource` from T1; the existing `StubHandler`, `CreateService` and `Envelope` helpers in `GatewayFormGenerationServiceTests`
**Requirement**: PH-01, PH-02, PH-03

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] The captured user message equals `UntrustedSource.Wrap(source, marker)` for the marker found in it, and the source appears once in the body, not in the system message (spec P1 story AC 1, 2, 5)
- [x] Two calls produce two different markers in the captured bodies (P1 story AC 4)
- [x] A source containing `{marker}` and `{questionCount}` arrives unchanged in the user message, and the system message still holds the real marker and `exactly 2 questions` (P1 story AC 6)
- [x] The system message contains the request's marker, the word `untrusted`, and states that only delimiters carrying that marker are real (system-prompt story AC 1, 2)
- [x] The system message states that count, types, difficulty, answer-key rule and output format come only from text outside the delimiters (system-prompt story AC 3)
- [x] The system message matches no `\{[A-Za-z]+\}` placeholder (AC 4)
- [x] The system message still contains each existing rule: `exactly 2 questions`, the allowed types, the difficulty, `Should mark correct answers: True`, the option minimum, the empty-options rule and the 1024 limit (AC 5)
- [x] Quick gate passes: `dotnet test tests/FormAI.IntegrationTests/FormAI.IntegrationTests.csproj --filter "FullyQualifiedName~GatewayFormGenerationService\|FullyQualifiedName~UntrustedSource"`
- [x] Test count: all earlier cases, including the 14 error-mapping, schema and cancellation cases, still run and pass; none deleted or weakened

**Tests**: unit
**Gate**: quick

**Commit**: `feat(ai): fence the source text and mark it untrusted in the prompt`

---

### T3: Document the fence and its limit

**What**: Describe the fence and the per-request marker in `CLAUDE.md`, and record in `known-gaps.md` that injection resistance is unmeasured until slice 17.
**Where**: `CLAUDE.md` (modify)
**Also modifies**: `docs/known-gaps.md`
**Depends on**: T2
**Reuses**: The existing "AI integration" paragraph of `CLAUDE.md` and the format of the other `known-gaps.md` entries
**Requirement**: PH-04

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] The AI integration section says the source reaches the model only between `<<<SOURCE {marker}>>>` delimiters with a 128-bit per-request marker, that the system prompt declares it untrusted data (P2 AC 1)
- [ ] The section still states that the client never retries and that Claude is never asked for points (unchanged text kept)
- [ ] `docs/known-gaps.md` has an entry saying the defense is delimiting plus an instruction, not proven against a model, and that the slice 17 injection documents are the check (P2 AC 2)
- [ ] `docs/known-gaps.md` does not claim more than the code does (no mention of output filtering)
- [ ] Build gate passes: `dotnet build FormAI.sln`, `dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj` and `git diff --check`

**Tests**: none
**Gate**: build

**Commit**: `docs(ai): document the untrusted-source fence`

---

## Phase Execution Map

```
Phase 1:  T1 ------→ T2 ------→ T3
```

Three tasks, one batch: execution is inline with no sub-agents. The Verifier still runs after T3.

---

## Task Granularity Check

| Task | Scope | Status |
| ---- | ----- | ------ |
| T1: fence helper | 1 new class, plus its co-located test file | ✅ Granular |
| T2: use the fence | 1 service change, plus the prompt text and test additions | ⚠️ Cohesive: the placeholder, the substitution and the assertions only make sense together |
| T3: docs | 1 file, plus one gap entry | ⚠️ Cohesive: both describe the same behavior and neither has code |

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
| ---- | ---------------------- | ------------- | ------ |
| T1 | None | none | ✅ Match |
| T2 | T1 | T1 → T2 | ✅ Match |
| T3 | T2 | T2 → T3 | ✅ Match |

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| ---- | --------------------------- | --------------- | --------- | ------ |
| T1: fence helper | Infrastructure AI helper | unit | unit | ✅ OK |
| T2: use the fence | Infrastructure AI client, prompt template | unit | unit | ✅ OK |
| T3: docs | `CLAUDE.md`, `known-gaps.md` | none | none | ✅ OK |
