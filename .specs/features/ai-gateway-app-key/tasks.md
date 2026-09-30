# Gateway App Key, Retries and PDF Check Tasks

## Execution Protocol (MANDATORY -- do not skip)

Implement these tasks with the `tlc-spec-driven` skill: **activate it by name and follow its Execute flow and Critical Rules.** Do not search for skill files by filesystem path. The skill is the source of truth for the full flow (per-task cycle, sub-agent delegation, adequacy review, Verifier, discrimination sensor).

**If the skill cannot be activated, STOP and tell the user - do not proceed without it.**

---

**Spec**: `.specs/features/ai-gateway-app-key/spec.md`
**Design**: skipped. Configuration, one shell script and documentation; the choices are in the spec's assumptions.
**Status**: Draft
**Branch**: `feature/litellm-app-key-and-retries`
**Blocker before T3 and T4**: `OPENAI_API_KEY` must be set in `.env`. Without it the failover and `gpt-5.2` PDF assertions print UNVERIFIED and the slice cannot be verified (same outcome as slice 1).

---

## Test Coverage Matrix

> Generated from codebase, project guidelines, and spec - confirm before Execute. Guidelines found: `CLAUDE.md` (Tests section), `.github/workflows/ci.yml`, and the matrix of `ai-gateway-local`. None covers Docker or gateway configuration, so the slice 1 matrix is reused.

| Code Layer | Required Test Type | Coverage Expectation | Location Pattern | Run Command |
| ---------- | ------------------ | -------------------- | ---------------- | ----------- |
| Gateway config, provisioning script, PDF fixture (running system) | integration | Every spec AC observable at runtime: app key scope, budget rejection, idempotency, failover per alias, timeout fallback, no retry on 4xx, PDF via each provider, spend attribution | `docker/litellm/smoke.sh` | `bash docker/litellm/smoke.sh` |
| Env template, README, CLAUDE.md | none | - (build gate only; README commands are executed by hand in T5) | - | build gate only |
| `FormAI.*` and `frontend/` | none | Untouched by this slice | - | not run |

The smoke script calls real providers: it needs Docker, real keys in `.env` (including `OPENAI_API_KEY`) and costs a few cents per run. It is never run in CI (plan T-1).

## Gate Check Commands

> Generated from codebase - confirm before Execute.

| Gate Level | When to Use | Command |
| ---------- | ----------- | ------- |
| Quick | Not used in this slice (no unit-testable code) | - |
| Full | Tasks that change the script, config or smoke test | `docker compose config -q && bash docker/litellm/smoke.sh` |
| Build | Env-template and docs-only tasks | `docker compose config -q` plus `git diff --check` |

---

## Execution Plan

Before T1: one `docs` commit adds this spec and this file.

### Phase 1: Key, policy, PDF and docs

One dependency chain: the docs describe measured behavior, so they follow it.

```
T1 → T2 → T3 → T4 → T5 → T6
```

---

## Task Breakdown

### T1: Add the app-key variables to the env template

**What**: Add `LITELLM_APP_KEY` and `LITELLM_APP_MAX_BUDGET_USD`, both empty, to `.env.example`.
**Where**: `.env.example`
**Depends on**: None
**Reuses**: Existing empty-variable style of `.env.example`
**Requirement**: GWK-01

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] `.env.example` lists both variables with no value
- [ ] `.env` (untracked) has a generated `LITELLM_APP_KEY` starting with `sk-` and a budget value, and a non-empty `OPENAI_API_KEY`
- [ ] Build gate passes: `docker compose config -q` and `git diff --check`

**Tests**: none
**Gate**: build

**Commit**: `chore(ai-gateway): add app key variables to env template`

---

### T2: Provision the app key, with its smoke assertions

**What**: Write the idempotent script that creates or updates the `form-ai-app` key, and add the app-key assertions to the smoke test.
**Where**: `docker/litellm/provision-app-key.sh` (new)
**Also modifies**: `docker/litellm/smoke.sh`. The co-located test is created in this task.
**Depends on**: T1
**Reuses**: `smoke.sh` helpers (`check`, `chat`, `sql`); LiteLLM docs for `/key/generate`, `/key/update` and `/key/info` on the pinned version (look up exact field names; do not guess)
**Requirement**: GWK-01, GWK-02, GWK-03

**Tools**:

- MCP: context7 or WebFetch for LiteLLM key-management docs
- Skill: NONE

**Done when**:

- [ ] The script exits non-zero without calling the gateway when `LITELLM_APP_KEY` is unset (AC 10), and defaults the budget to 10 when `LITELLM_APP_MAX_BUDGET_USD` is unset
- [ ] The script creates the key with alias `form-ai-app`, models limited to the two aliases, `max_budget` from `.env` and `budget_duration` `1mo`, and on a second run updates instead of duplicating (AC 1-3, 8)
- [ ] `smoke.sh` asserts: 200 from each alias with the app key (AC 4); 4xx for another model (AC 5); 401 or 403 on `/key/generate` (AC 6); budget rejection for a throwaway key with a tiny `max_budget` after one call, with a body naming the budget (AC 7); exactly one `form-ai-app` key after two provisioning runs (AC 8); the spend-log row of an app-key call carries the key's alias or hash (AC 9); key info reports the configured budget and duration (AC 3)
- [ ] `smoke.sh` deletes the throwaway key it creates
- [ ] Full gate passes: `docker compose config -q && bash docker/litellm/smoke.sh`
- [ ] Test count: slice 1's 24 assertions still run (none deleted) plus the new ones; new ones all pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(ai-gateway): provision an app key with a monthly budget`

---

### T3: Set the retry and timeout policy, and verify failover

**What**: Configure the per-attempt timeout and the single retry on the fallback in the gateway config, and assert failover and timeout behavior in the smoke test.
**Where**: `docker/litellm/config.yaml` (modify)
**Also modifies**: `docker/litellm/smoke.sh`
**Depends on**: T2
**Reuses**: Slice 1's failing-primary method (invalid `ANTHROPIC_API_KEY`) already in `smoke.sh`; LiteLLM router docs for the pinned version (timeout, retry and cooldown setting names; look up, do not guess)
**Requirement**: GWK-04

**Tools**:

- MCP: context7 or WebFetch for LiteLLM router and timeout docs
- Skill: NONE

**Done when**:

- [ ] `config.yaml` sets a 35 s per-attempt timeout and one retry, and no file outside it configures gateway retries (AC 1, 2)
- [ ] `smoke.sh` no longer prints UNVERIFIED for the two failover assertions: each alias is answered by `gpt-5.2` with a failing primary (AC 3, closes slice 1 P1 AC 6, 7)
- [ ] `smoke.sh` asserts that a primary delayed past 35 s is abandoned and the fallback answers, using the delay mechanism documented for the pinned version (AC 4)
- [ ] `smoke.sh` asserts that with both providers failing the caller gets an error in under 75 s (AC 5)
- [ ] `smoke.sh` asserts that a malformed request (provider 4xx) produces one upstream attempt and no fallback call (AC 6)
- [ ] `smoke.sh` restores the normal stack when it finishes or fails (trap), so a failed run does not leave the gateway broken
- [ ] Full gate passes: `docker compose config -q && bash docker/litellm/smoke.sh`, with `unverified=0`
- [ ] Test count: all earlier assertions still run plus the new ones; none deleted

**Tests**: integration
**Gate**: full

**Commit**: `feat(ai-gateway): bound retries and timeouts in the gateway`

---

### T4: Verify PDF input through both providers

**What**: Add a small PDF fixture and smoke assertions that send it to the vision alias, normally and forced to the fallback, and record the latency.
**Where**: `docker/litellm/fixtures/sample.pdf` (new)
**Also modifies**: `docker/litellm/smoke.sh`
**Depends on**: T3
**Reuses**: The failing-primary mechanism from T3; LiteLLM docs for sending a PDF in an OpenAI-format message (file or base64 content part; look up the form the pinned version accepts)
**Requirement**: GWK-05, GWK-06

**Tools**:

- MCP: context7 or WebFetch for LiteLLM PDF input docs
- Skill: NONE

**Done when**:

- [ ] The fixture is a one-page text PDF under 20 KB with one distinctive marker word, and the marker is not a word the model would produce unprompted
- [ ] `smoke.sh` sends it to `form-generator-vision` and asserts 200, the marker word in the answer and `anthropic/claude-sonnet-5-5` in the spend log (AC 1)
- [ ] `smoke.sh` repeats it with the primary forced to fail and asserts 200, the marker word and `openai/gpt-5.2` in the spend log (AC 2)
- [ ] Both calls have a spend-log row with cost > 0 (AC 3)
- [ ] `smoke.sh` prints each call's latency, and both are below the 35 s attempt timeout (AC 4, 5)
- [ ] If either provider rejects the PDF, the task stops and reports it instead of weakening the assertion: the synchronous decision needs revisiting
- [ ] Full gate passes: `docker compose config -q && bash docker/litellm/smoke.sh`, with `unverified=0`
- [ ] Test count: all earlier assertions still run plus the new ones; none deleted

**Tests**: integration
**Gate**: full

**Commit**: `test(ai-gateway): verify PDF input through both providers`

---

### T5: Document the app key, policy and measurements

**What**: Update the gateway README with the app key, the retry and timeout budget, the measured PDF latency and what remains unverified.
**Where**: `docker/litellm/README.md` (modify)
**Depends on**: T4
**Reuses**: The running stack from T4, to execute every command written
**Requirement**: GWK-07

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Describes the app key: alias, allowed models, budget and period, `provision-app-key.sh`, the two variables, and that the API uses this key (master key is for developers only)
- [ ] States the timeout budget as gateway attempt × attempts < client timeout < 90 s < load balancer idle timeout, with the chosen numbers (35 s, 2, 70 s, 80 s), and the measured PDF latencies from T4
- [ ] The "Status of what is verified" section is rewritten: failover and PDF through both providers are verified; client, Kestrel and load balancer timeouts are not, with the slice that will check each
- [ ] The slice 1 statement that failover needs `OPENAI_API_KEY` is updated, and every command in the file was run against the T4 stack
- [ ] Build gate passes: `docker compose config -q` and `git diff --check`

**Tests**: none
**Gate**: build

**Commit**: `docs(ai-gateway): document the app key and timeout budget`

---

### T6: Mention the app key in the local-infrastructure paragraph

**What**: Add the app key and its budget to the local infrastructure description.
**Where**: `CLAUDE.md`
**Depends on**: T5
**Reuses**: The existing "Local infrastructure is Docker Compose" paragraph
**Requirement**: GWK-07

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] The paragraph names the app key (`form-ai-app`, budgeted, two aliases only) and `provision-app-key.sh`, and still says the API does not call the gateway yet
- [ ] The AI integration section is unchanged (it changes in slice 3)
- [ ] Build gate passes: `git diff --check`

**Tests**: none
**Gate**: build

**Commit**: `docs(claude): mention the gateway app key`

---

## Phase Execution Map

```
Phase 1:  T1 ------→ T2 ------→ T3 ------→ T4 ------→ T5 ------→ T6
```

Six tasks fit a single batch, so execution is inline with no sub-agents. The Verifier still runs after T6.

---

## Task Granularity Check

| Task | Scope | Status |
| ---- | ----- | ------ |
| T1: env template | 1 file | ✅ Granular |
| T2: provisioning script | 1 new file, plus its co-located test additions | ⚠️ Cohesive: the script cannot be tested without the smoke assertions |
| T3: retry and timeout config | 1 config file, plus its co-located test additions | ⚠️ Cohesive: config is only observable through the smoke test |
| T4: PDF check | 1 fixture, plus its co-located test additions | ⚠️ Cohesive: the fixture has no meaning without the assertions |
| T5: README | 1 file | ✅ Granular |
| T6: CLAUDE.md | 1 file | ✅ Granular |

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
| ---- | ---------------------- | ------------- | ------ |
| T1 | None | none | ✅ Match |
| T2 | T1 | T1 → T2 | ✅ Match |
| T3 | T2 | T2 → T3 | ✅ Match |
| T4 | T3 | T3 → T4 | ✅ Match |
| T5 | T4 | T4 → T5 | ✅ Match |
| T6 | T5 | T5 → T6 | ✅ Match |

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| ---- | --------------------------- | --------------- | --------- | ------ |
| T1: env template | Env template | none | none | ✅ OK |
| T2: provisioning script | Gateway script (running system) | integration | integration | ✅ OK |
| T3: retry and timeout config | Gateway config | integration | integration | ✅ OK |
| T4: PDF fixture | Gateway fixture (running system) | integration | integration | ✅ OK |
| T5-T6: docs | Docs | none | none | ✅ OK |
