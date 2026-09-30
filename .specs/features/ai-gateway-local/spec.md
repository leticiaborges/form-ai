# Local AI Gateway Specification

Slice 1 of [`docs/plans/phase-37.md`](../../../docs/plans/phase-37.md). Covers GW-1..4, GW-6, GW-8, GW-9 and ADR D-1. No application code changes.

## Problem Statement

The API calls Anthropic directly. Phase 37 moves every model call behind an AI gateway (LiteLLM) so models can be swapped, cost is reported per call and a spending ceiling can be enforced. Every later slice needs a running, configured, private gateway with stable model aliases. This slice delivers that gateway locally and documents its contract, and nothing else.

## Goals

- [ ] `docker compose up` starts LiteLLM from a pinned image, and `curl` against `form-generator` and `form-generator-vision` each return a completion.
- [ ] LiteLLM's spend log shows a cost for each of those calls, and no prompt or response text is stored.
- [ ] No secret exists in any tracked file, and the gateway's database is unreachable with the app's credentials.
- [ ] `docker/litellm/README.md` and an ADR let a later slice build against the gateway without reading its config.

## Out of Scope

| Feature | Reason |
| ------- | ------ |
| App-specific gateway key with `max_budget` (GW-5) | Slice 2 |
| Gateway retries and the timeout budget (GW-7) | Slice 2 |
| PDF verification with each provider | Slice 2 |
| `deploy-litellm.yml`, production private network, ECS service (GW-10) | Slice 8 |
| Any change to `FormAI.*` code or `IFormGenerationService` | Slice 3 |
| Model comparison and evaluation set (T-3) | Runs before slice 3 |
| Email alerts on spend | Slice 8 |

---

## Assumptions & Open Questions

| Assumption / decision | Chosen default | Rationale | Confirmed? |
| --------------------- | -------------- | --------- | ---------- |
| Where LiteLLM's database lives | A separate `litellm-db` Postgres container in `docker-compose.yml`, with its own database, role and password | Satisfies GW-2 without touching the `form_ai` server, and shares no role or credentials with `form_ai_app` or `form_ai_migrator` | y |
| Image version | An exact version tag (never `latest`, `main` or a floating minor) chosen at implementation time and written in the compose file | GW-1 requires pinning. The current stable tag is looked up when implementing | y |
| Text model behind `form-generator` | `claude-haiku-4-5-20251001` for now | It is a placeholder. The real choice comes from the T-3 evaluation before slice 3 | y |
| `form-generator-vision` mapping | Primary `claude-sonnet-5-5`, fallback to `gpt-5.2` | User decision, replacing the plan's Opus 5.5 / GPT 5.5. Both accept PDFs, so the fallback respects GW-3 | y |
| Fallback scope | Both aliases fall back to `gpt-5.2`, which accepts text and PDFs | User decision, replacing the plan's "vision alias only". GW-3 is respected because `gpt-5.2` supports both inputs | y |
| Local calls in this slice | Made with the master key | The app key is slice 2. The master key is only ever in the untracked `.env` | y |
| Port exposure locally | LiteLLM port published on `127.0.0.1` only. The gateway database publishes no port | GW-8 asks for no public exposure. Loopback is the local equivalent of a private network | y |
| Log-suppression setting names | LiteLLM's settings that disable message logging and prompt storage in spend logs, as documented for the pinned version | Names vary between versions. Verified against the pinned version's docs and by inspecting the spend log | y |
| Docs updated in this slice | `CONTEXT.md` (gateway, model alias), the new ADR, and the local-infrastructure line in `CLAUDE.md` | Repo rule: docs change with the behavior. `CLAUDE.md`'s AI section stays untouched until slice 3 changes the code | y |

**Open questions:** none - all resolved or logged above (required before the spec is confirmed).

Remaining dimensions N/A for this slice: idempotency/retry (retries are GW-7, slice 2), auth boundaries and rate limits (app key is slice 2), concurrency/ordering, state-transition integrity, data lifecycle (nothing is persisted but LiteLLM's own spend records).

---

## User Stories

### P1: Run the gateway and call both aliases ⭐ MVP

**User Story**: As a developer, I want a pinned LiteLLM service in Docker Compose with two model aliases so that later slices can call `form-generator` and `form-generator-vision` without knowing real model ids.

**Why P1**: Every later slice depends on it.

**Acceptance Criteria**:

1. The system SHALL run LiteLLM from an image referenced by an exact version tag as a service in `docker-compose.yml`. <!-- ubiquitous -->
2. The system SHALL load the gateway config from `docker/litellm/config.yaml`, mounted into the container. <!-- ubiquitous -->
3. The gateway config SHALL define the aliases `form-generator` and `form-generator-vision` and no other model names. <!-- ubiquitous -->
4. WHEN a chat completion request for `form-generator` carries a valid key THEN the gateway SHALL return a completion from the mapped text model. <!-- event-driven -->
5. WHEN a chat completion request for `form-generator-vision` carries a valid key THEN the gateway SHALL return a completion from `claude-sonnet-5-5`. <!-- event-driven -->
6. IF the primary model of `form-generator-vision` fails with a provider error THEN the gateway SHALL answer the request from the `gpt-5.2` fallback. <!-- unwanted-behavior -->
7. IF the primary model of `form-generator` fails with a provider error THEN the gateway SHALL answer the request from the `gpt-5.2` fallback. <!-- unwanted-behavior -->
8. WHEN a call to either alias completes THEN the gateway's spend log SHALL contain a row for it with a non-null cost in USD. <!-- event-driven -->
9. IF a request names a model other than the two aliases THEN the gateway SHALL reject it with a 4xx status. <!-- unwanted-behavior -->
10. IF a request carries no key or a wrong key THEN the gateway SHALL answer 401. <!-- unwanted-behavior -->

**Independent Test**: `docker compose up -d litellm`, then `curl` each alias with the master key, and query the spend log for two rows with a cost. Force the primary model of each alias to fail (bad Anthropic key or an unreachable model), repeat, and see `gpt-5.2` answer.

---

### P1: Isolation and secrets

**User Story**: As the owner, I want the gateway to hold its own credentials and never see or store user content, so that a leak in one place does not expose the rest.

**Why P1**: GW-2, GW-4, GW-6 and GW-8 are security requirements, and retrofitting them later means re-auditing.

**Acceptance Criteria**:

1. The gateway SHALL use a database whose name, role and password differ from `form_ai`, `form_ai_app` and `form_ai_migrator`. <!-- ubiquitous -->
2. The system SHALL read provider keys, the master key, the salt key and the gateway database password from the untracked `.env`. <!-- ubiquitous -->
3. The repository SHALL contain `LITELLM_*`, provider key and password variables only as empty or placeholder entries in `.env.example`, and no real value in any tracked file. <!-- ubiquitous -->
4. IF a required secret variable is unset THEN `docker compose up` SHALL fail for the gateway service rather than start with a blank value. <!-- unwanted-behavior -->
5. The gateway SHALL NOT log prompt or response text, and SHALL NOT store them in its spend log. <!-- ubiquitous -->
6. WHEN a completion request with a distinctive marker string in its prompt finishes THEN neither the gateway container logs nor its spend-log row SHALL contain the marker. <!-- event-driven -->
7. The gateway database SHALL NOT publish a port to the host. <!-- ubiquitous -->
8. The gateway port SHALL bind to `127.0.0.1` on the host. <!-- ubiquitous -->
9. IF a connection to the gateway database uses the `form_ai_app` role THEN the database SHALL refuse it. <!-- unwanted-behavior -->

**Independent Test**: Grep the tracked tree for the real values. Unset `LITELLM_MASTER_KEY` and confirm the service refuses to start. Send a marker prompt and search logs and the spend log for it. Connect to the gateway database as `form_ai_app` and see the refusal.

---

### P2: Documented contract and ADR

**User Story**: As a developer of slices 2 to 8, I want a contract document and a recorded decision so that I can build against the gateway and understand why it exists.

**Why P2**: The gateway works without it, but the next slice cannot be written reliably without it.

**Acceptance Criteria**:

1. `docker/litellm/README.md` SHALL state the base URL for a container on the Compose network, the base URL from the host, and the OpenAI-compatible route used. <!-- ubiquitous -->
2. `docker/litellm/README.md` SHALL list both aliases, the real model each maps to, which input each accepts (text, PDF) and the fallback rule. <!-- ubiquitous -->
3. `docker/litellm/README.md` SHALL state that `user` carries the user's guid only, never an email or a name. <!-- ubiquitous -->
4. `docker/litellm/README.md` SHALL state which key is used by whom, and which environment variables must be set. <!-- ubiquitous -->
5. An ADR at the next sequential number in `docs/adr/` SHALL record the decision to adopt a gateway, naming keeping the direct Anthropic client plus a ledger and ceiling as the alternative not chosen, and why. <!-- ubiquitous -->
6. `CONTEXT.md` SHALL define **gateway** and **model alias**. <!-- ubiquitous -->
7. The local-infrastructure paragraph in `CLAUDE.md` SHALL list the gateway and its database. <!-- ubiquitous -->

**Independent Test**: A reader who has only the README can produce a working `curl` for each alias.

---

## Edge Cases

- IF the gateway database is not yet ready THEN the gateway service SHALL wait for its health check rather than crash-loop.
- IF only one provider key is valid THEN each alias SHALL still answer whenever a model of that provider is reachable for it (primary or fallback), and SHALL fail with a provider error otherwise.
- WHEN `docker compose down -v` is run and the stack restarted THEN the gateway SHALL start from an empty database without manual steps.

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| -------------- | ----- | ----- | ------ |
| GWY-01 | P1: Run the gateway and call both aliases (AC 1-3) | Design | Pending |
| GWY-02 | P1: Run the gateway and call both aliases (AC 4-5, 8) | Design | Pending |
| GWY-03 | P1: Run the gateway and call both aliases (AC 6-7, both fallbacks) | Design | Pending |
| GWY-04 | P1: Run the gateway and call both aliases (AC 9-10) | Design | Pending |
| GWY-05 | P1: Isolation and secrets (AC 1, 7, 9) | Design | Pending |
| GWY-06 | P1: Isolation and secrets (AC 2-4) | Design | Pending |
| GWY-07 | P1: Isolation and secrets (AC 5-6) | Design | Pending |
| GWY-08 | P1: Isolation and secrets (AC 8) | Design | Pending |
| GWY-09 | P2: Documented contract and ADR (AC 1-4) | Design | Pending |
| GWY-10 | P2: Documented contract and ADR (AC 5-7) | Design | Pending |

**Coverage:** 10 total, 0 mapped to tasks, 10 unmapped ⚠️ (tasks come after confirmation)

---

## Success Criteria

- [ ] Both aliases answer through the gateway, and each call shows a cost in the spend log.
- [ ] A marker-string prompt leaves no trace in logs or the spend log.
- [ ] `git grep` for every real secret value in the tracked tree returns nothing.
- [ ] Slice 2 can start using only `docker/litellm/README.md` and the ADR.
