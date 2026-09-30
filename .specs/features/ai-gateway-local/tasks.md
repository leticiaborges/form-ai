# Local AI Gateway Tasks

## Execution Protocol (MANDATORY -- do not skip)

Implement these tasks with the `tlc-spec-driven` skill: **activate it by name and follow its Execute flow and Critical Rules.** Do not search for skill files by filesystem path. The skill is the source of truth for the full flow (per-task cycle, sub-agent delegation, adequacy review, Verifier, discrimination sensor).

**If the skill cannot be activated, STOP and tell the user - do not proceed without it.**

---

**Spec**: `.specs/features/ai-gateway-local/spec.md`
**Design**: skipped. This slice is configuration and documentation, and the choices are recorded in the spec's assumptions.
**Status**: Draft

---

## Test Coverage Matrix

> Generated from codebase, project guidelines, and spec - confirm before Execute. Guidelines found: `CLAUDE.md` (Tests section), `.github/workflows/ci.yml`. Neither covers Docker or gateway configuration, so strong defaults apply to the one new executable layer.

| Code Layer | Required Test Type | Coverage Expectation | Location Pattern | Run Command |
| ---------- | ------------------ | -------------------- | ---------------- | ----------- |
| Compose service + gateway config (running system) | integration | Every spec AC that is observable at runtime: each alias, fallback, model rejection, 401, cost in spend log, marker-string leak check, secret-unset failure, `form_ai_app` refusal, port bindings | `docker/litellm/smoke.sh` | `bash docker/litellm/smoke.sh` |
| Env template, README, ADR, CONTEXT.md, CLAUDE.md | none | - (build gate only; README curl examples are executed by hand in T4) | - | build gate only |
| `FormAI.*` and `frontend/` | none | Untouched by this slice | - | not run |

The smoke script calls real providers, so it needs Docker and real keys in `.env`, and each run costs a few cents. It is never run in CI (plan T-1: CI never calls a real model).

## Gate Check Commands

> Generated from codebase - confirm before Execute.

| Gate Level | When to Use | Command |
| ---------- | ----------- | ------- |
| Quick | Not used in this slice (no unit-testable code) | - |
| Full | Tasks with the smoke script | `docker compose config -q && bash docker/litellm/smoke.sh` |
| Build | Config-only and docs-only tasks | `docker compose config -q` plus `git diff --check` |

---

## Execution Plan

Before T1: one `docs` commit adds `docs/plans/phase-37.md`, `.specs/features/ai-gateway-local/spec.md` and this file.

### Phase 1: Gateway, contract and docs

One dependency chain: the docs describe the running stack, so they follow it.

```
T1 → T2 → T3 → T4 → T5 → T6 → T7
```

---

## Task Breakdown

### T1: Add gateway variables to the env template

**What**: Add the gateway's variable names, all empty, to `.env.example`.
**Where**: `.env.example`
**Depends on**: None
**Reuses**: Existing empty-variable style of `.env.example`
**Requirement**: GWY-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] `.env.example` lists `LITELLM_MASTER_KEY`, `LITELLM_SALT_KEY`, `LITELLM_DB_PASSWORD`, `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, each with no value
- [ ] `.env` (untracked) has real values for all five before T3 runs (master, salt and DB password generated; provider keys pending from the user)
- [x] Build gate passes: `git diff --check`

**Tests**: none
**Gate**: build

**Commit**: `chore(ai-gateway): add gateway variables to env template`

---

### T2: Write the gateway config

**What**: Create the LiteLLM config with the two aliases, the vision fallback and message logging off.
**Where**: `docker/litellm/config.yaml`
**Depends on**: T1
**Reuses**: LiteLLM docs for the pinned version (look up the exact setting names; do not guess)
**Requirement**: GWY-01, GWY-03, GWY-07

**Tools**:

- MCP: context7 or WebFetch for LiteLLM docs
- Skill: NONE

**Done when**:

- [x] Only `form-generator` and `form-generator-vision` appear as `model_name`
- [x] `form-generator-vision` maps to `claude-sonnet-5-5` and `form-generator` to `claude-haiku-4-5-20251001`; both fall back to `gpt-5.2`
- [x] Keys are referenced as `os.environ/...`, and the file contains no secret value
- [x] Message logging is off and prompts are not stored in the spend log, using the setting names documented for the pinned version
- [x] The file parses as YAML: `python3 -c "import yaml,sys; yaml.safe_load(open('docker/litellm/config.yaml'))"`
- [x] Build gate passes: `git diff --check`

**Tests**: none (exercised by the T3 smoke script, since the config cannot run without the service)
**Gate**: build

**Commit**: `feat(ai-gateway): add gateway config with model aliases`

---

### T3: Add the gateway service to Compose, with its smoke test

**What**: Add the pinned LiteLLM service and its own Postgres to `docker-compose.yml`, plus the smoke script that proves the spec's runtime ACs.
**Where**: `docker-compose.yml` (modify)
**Also creates**: `docker/litellm/smoke.sh`. The co-located test, `docker/litellm/smoke.sh`, is created in this task.
**Depends on**: T2
**Reuses**: The `postgres` service's health check and `${VAR}` style; the `redis` service's healthcheck shape
**Requirement**: GWY-01, GWY-02, GWY-03, GWY-04, GWY-05, GWY-06, GWY-07, GWY-08

**Tools**:

- MCP: context7 or WebFetch for the LiteLLM image tag and env vars
- Skill: NONE

**Done when**:

- [ ] `litellm-db` is a separate Postgres service with its own database, role and password from `.env`, no published port, and a health check
- [ ] `litellm` uses an exact image tag (no `latest`, `main` or floating minor), mounts `docker/litellm/config.yaml`, publishes its port on `127.0.0.1` only and waits for `litellm-db` to be healthy
- [ ] Required variables use `${VAR:?message}` so an unset one fails `docker compose up`
- [ ] `docker/litellm/smoke.sh` asserts, with the spec-defined outcome for each: a completion from each alias (AC P1-1 4, 5), a non-null cost in the spend log for both (AC 8), a 4xx for an unknown model (AC 9), 401 with no key and with a wrong key (AC 10), `gpt-5.2` answering for each alias when its primary is forced to fail (AC 6, 7, via the mechanism documented for the pinned version), a marker string absent from container logs and the spend-log row (AC S-5, S-6), the compose service failing to start with `LITELLM_MASTER_KEY` unset (AC S-4), the `form_ai_app` role refused on `litellm-db` (AC S-9), the gateway port bound to `127.0.0.1` and no host port on `litellm-db` (AC S-7, S-8)
- [ ] `git grep` finds none of the real secret values in the tracked tree (AC S-3)
- [ ] After `docker compose down -v` and a restart, the stack starts from empty with no manual steps (edge case)
- [ ] Full gate passes: `docker compose config -q && bash docker/litellm/smoke.sh`
- [ ] Test count: every assertion above is present in the script and the script exits 0 (record the count)

**Tests**: integration
**Gate**: full

**Commit**: `feat(ai-gateway): run LiteLLM gateway with its own database in compose`

---

### T4: Write the gateway README

**What**: Document the gateway's contract for later slices.
**Where**: `docker/litellm/README.md`
**Depends on**: T3
**Reuses**: The running stack from T3, to execute the examples
**Requirement**: GWY-09

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] States the base URL from a container on the Compose network and from the host, and the OpenAI-compatible route
- [ ] Lists both aliases, the real model each maps to, which input each accepts (text, PDF) and the fallback rule
- [ ] States that `user` carries the user's guid only, never an email or a name
- [ ] States which key is used by whom (master key now, app key in slice 2) and which environment variables must be set
- [ ] Each `curl` example in the file was run against the T3 stack and returned a completion
- [ ] Build gate passes: `docker compose config -q` and `git diff --check`

**Tests**: none
**Gate**: build

**Commit**: `docs(ai-gateway): document the gateway contract`

---

### T5: Record the gateway decision

**What**: Add the ADR for adopting a gateway.
**Where**: `docs/adr/0008-ai-gateway-instead-of-direct-provider-client.md`
**Depends on**: T4
**Reuses**: The structure and length of `docs/adr/0007-migrations-run-as-a-separate-role-in-a-deploy-job.md`
**Requirement**: GWY-10

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Names the decision, the alternative not chosen (keep `ClaudeFormGenerationService` and add a ledger and ceiling), and why
- [ ] States the consequences: a new container and database, model aliases, provider swap without code changes
- [ ] Build gate passes: `git diff --check`

**Tests**: none
**Gate**: build

**Commit**: `docs(adr): record the decision to adopt an AI gateway`

---

### T6: Define the new terms

**What**: Add **gateway** and **model alias** to the vocabulary.
**Where**: `CONTEXT.md`
**Depends on**: T5
**Reuses**: Existing entries' format in `CONTEXT.md`
**Requirement**: GWY-10

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Both terms are defined in the file's existing style
- [ ] Build gate passes: `git diff --check`

**Tests**: none
**Gate**: build

**Commit**: `docs(context): define gateway and model alias`

---

### T7: List the gateway in the local-infrastructure paragraph

**What**: Add the gateway and its database to the local infrastructure description.
**Where**: `CLAUDE.md`
**Depends on**: T6
**Reuses**: The existing "Local infrastructure is Docker Compose" paragraph
**Requirement**: GWY-10

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] The paragraph names the LiteLLM gateway, its own Postgres, and the loopback-only binding
- [ ] The AI integration section is unchanged (it changes in slice 3)
- [ ] Build gate passes: `git diff --check`

**Tests**: none
**Gate**: build

**Commit**: `docs(claude): list the local AI gateway`

---

## Phase Execution Map

```
Phase 1:  T1 ------→ T2 ------→ T3 ------→ T4 ------→ T5 ------→ T6 ------→ T7
```

Seven tasks fit a single batch, so execution is inline with no sub-agents. The Verifier still runs after T7.

---

## Task Granularity Check

| Task | Scope | Status |
| ---- | ----- | ------ |
| T1: env template | 1 file | ✅ Granular |
| T2: gateway config | 1 file | ✅ Granular |
| T3: compose service + smoke test | 1 file changed, plus its co-located test | ⚠️ Cohesive: the service cannot be tested without the script, so the test stays in the task |
| T4: README | 1 file | ✅ Granular |
| T5: ADR | 1 file | ✅ Granular |
| T6: CONTEXT.md | 1 file | ✅ Granular |
| T7: CLAUDE.md | 1 file | ✅ Granular |

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
| ---- | ---------------------- | ------------- | ------ |
| T1 | None | none | ✅ Match |
| T2 | T1 | T1 → T2 | ✅ Match |
| T3 | T2 | T2 → T3 | ✅ Match |
| T4 | T3 | T3 → T4 | ✅ Match |
| T5 | T4 | T4 → T5 | ✅ Match |
| T6 | T5 | T5 → T6 | ✅ Match |
| T7 | T6 | T6 → T7 | ✅ Match |

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| ---- | --------------------------- | --------------- | --------- | ------ |
| T1: env template | Env template | none | none | ✅ OK |
| T2: gateway config | Gateway config (cannot run without the service) | integration, satisfied by T3 | none | ⚠️ Merged forward: the config is exercised by T3's script, and T2 is not a separate code layer that can run alone |
| T3: compose service | Compose service + config | integration | integration | ✅ OK |
| T4-T7: docs | Docs | none | none | ✅ OK |
