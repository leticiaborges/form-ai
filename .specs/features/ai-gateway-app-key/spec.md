# Gateway App Key, Retries and PDF Check Specification

Slice 2 of [`docs/plans/phase-37.md`](../../../docs/plans/phase-37.md). Covers GW-5, GW-7 and the gateway half of T-6. Builds on [`ai-gateway-local`](../ai-gateway-local/spec.md). No application code changes.

## Problem Statement

Slice 1 left the gateway callable only with the master key, with an unmeasured retry and timeout policy, and with PDF input and `gpt-5.2` failover never exercised (no `OPENAI_API_KEY`). The API needs its own key with a spending ceiling (the backstop for the app-side ceiling of slice 7), a written and enforced retry and timeout budget, and proof that both providers accept a PDF through the vision alias, because the synchronous-generation decision depends on it.

## Goals

- [ ] An app key exists that can call only the two aliases, cannot administer the gateway, and stops working when its monthly budget is spent.
- [ ] Retries and timeouts live only in the gateway config, and a stalled primary fails over within a bounded time.
- [ ] A PDF is answered through `form-generator-vision` by both `claude-sonnet-5-5` and `gpt-5.2`.
- [ ] Slice 1's two UNVERIFIED failover ACs (P1 AC 6 and 7) become verified.

## Out of Scope

| Feature | Reason |
| ------- | ------ |
| The API using the app key, `Ai:*` configuration | Slice 3 |
| App-side monthly ceiling (`Ai:MonthlyBudgetUsd`) and ledger | Slices 6 and 7 |
| Email alerts at 50% and 80% (UL-6) | Slice 8 |
| Production key provisioning, ECS and `deploy-litellm.yml` | Slice 8 |
| Timeout links outside the gateway (load balancer idle timeout, Kestrel, `HttpClient`) | They do not exist yet. Checked in slice 3 (client) and at deploy (load balancer) |
| Local PDF validation | Phase 37 decision: none |

---

## Assumptions & Open Questions

| Assumption / decision | Chosen default | Rationale | Confirmed? |
| --------------------- | -------------- | --------- | ---------- |
| How the app key is created | `docker/litellm/provision-app-key.sh` calls `/key/generate` with the master key and sets the key value from `LITELLM_APP_KEY` in `.env` | The value is known to the API's config without copying output, and rerunning is deterministic | n |
| Idempotency | The script looks the key up by `key_alias` `form-ai-app`; if present it updates budget and models, otherwise it creates it | Safe to rerun after a config change | n |
| Budget values | `LITELLM_APP_MAX_BUDGET_USD` in `.env`, default 10, `budget_duration` `1mo` | Plan UL-6: slightly above the app-side value, which does not exist yet. 10 is a placeholder to be set with slice 7 | n |
| Allowed models on the app key | `form-generator` and `form-generator-vision` only | Least privilege, and matches GW-3 | n |
| Retry policy | `num_retries: 0` and `max_retries: 0` on every deployment; the only second attempt is failover to the next `order` deployment (the fallback), for provider errors, timeouts and provider 4xx. With `num_retries: 1` a stalled pair of providers took 4 attempts (140 s), measured. A failed deployment cools down for 30 s (`allowed_fails: 0`), so the retry never lands on the deployment that just failed. Failover on 4xx cannot be turned off in the pinned version (tested: `num_retries`, `retry_policy` with `BadRequestErrorRetries: 0`, and both) | Matches the plan's "client does not retry" (AI-7). Without the cooldown the retry went to the same failed deployment (measured: two connections to a stalled primary 35 s apart, 72 s total) | y |
| Timeout budget | Gateway per-attempt timeout 35 s; 2 attempts = 70 s worst case < client timeout 80 s < overall 90 s < load balancer idle timeout (to be set above 90 s at deploy) | Plan AI-7 ordering. Numbers are a first proposal, revised if the slow-PDF measurement (P1 PDF AC 4) contradicts them | n |
| Forcing a provider failure in tests | Reuse slice 1's method (invalid `ANTHROPIC_API_KEY` for a failing primary); for timeouts, the mechanism documented for the pinned version, looked up at implementation time | Slice 1 already proved the first method works. Setting names are version-specific and must not be guessed | n |
| PDF fixture | A tracked one-page text PDF, under 20 KB, containing one distinctive word, in `docker/litellm/fixtures/` | Deterministic assertion on the answer without a judge model. The fixture is one trivial page, so its latency (1-2 s) proves the path works, not that a realistic document fits the 35 s timeout. Latency of multi-page and scanned PDFs is measured with the slice 17 evaluation set, and AC 5 of the PDF story stays open until then | n |
| Cost of the checks | A few cents per `smoke.sh` run, never in CI | Same as slice 1 | n |
| `OPENAI_API_KEY` | Required to execute this slice's PDF and failover ACs. If still empty, those assertions print UNVERIFIED and the slice cannot be marked verified | Slice 1 shipped PASS-WITH-UNVERIFIED for this reason | n |

**Open questions:** none - all resolved or logged above. The "Confirmed?" column is `n` because the user has not yet reviewed these defaults.

Remaining dimensions N/A for this slice: auth boundaries beyond the key itself (user-level limits are the app's rate limiter), concurrency (the ceiling race is documented in D-4), state-transition integrity, data lifecycle (only LiteLLM's own spend rows).

---

## User Stories

### P1: App key with a spending ceiling ⭐ MVP

**User Story**: As the owner, I want the API to use a key that can only call the two aliases and stops at a budget, so that a bug or leaked key cannot run up an unbounded bill or reconfigure the gateway.

**Why P1**: GW-5 is the global ceiling's backstop.

**Acceptance Criteria**:

1. The system SHALL provide `docker/litellm/provision-app-key.sh`, which creates a gateway key whose value is `LITELLM_APP_KEY` from `.env`. <!-- ubiquitous -->
2. The app key SHALL be limited to the models `form-generator` and `form-generator-vision`. <!-- ubiquitous -->
3. The app key SHALL have `max_budget` equal to `LITELLM_APP_MAX_BUDGET_USD` and a `budget_duration` of `1mo`. <!-- ubiquitous -->
4. WHEN a chat completion for either alias carries the app key THEN the gateway SHALL return 200. <!-- event-driven -->
5. IF a request with the app key names any other model THEN the gateway SHALL reject it with a 4xx status. <!-- unwanted-behavior -->
6. IF the app key calls a gateway management route (`/key/generate`) THEN the gateway SHALL answer 401 or 403. <!-- unwanted-behavior -->
7. IF a key's spend has reached its `max_budget` THEN the gateway SHALL reject further calls with that key with a 4xx status whose body names the budget. <!-- unwanted-behavior -->
8. WHEN `provision-app-key.sh` runs twice THEN the gateway SHALL hold exactly one key with alias `form-ai-app`, carrying the current budget and models. <!-- event-driven -->
9. WHEN a call is made with the app key THEN its spend-log row SHALL be attributed to the `form-ai-app` key. <!-- event-driven -->
10. IF `LITELLM_APP_KEY` is unset THEN `provision-app-key.sh` SHALL exit non-zero without calling the gateway. <!-- unwanted-behavior -->
11. The repository SHALL contain `LITELLM_APP_KEY` and `LITELLM_APP_MAX_BUDGET_USD` only as empty entries in `.env.example`. <!-- ubiquitous -->

**Independent Test**: Run the script, call both aliases with the app key, call an unknown model and `/key/generate` with it, then create a throwaway key with a tiny budget, spend it, and see the budget rejection.

---

### P1: Bounded retries and timeouts

**User Story**: As a developer of the client slice, I want the gateway alone to retry and time out, with known worst-case latency, so that the client can set its own timeout above it and never retry.

**Why P1**: GW-7, and AI-7 depends on the numbers.

**Acceptance Criteria**:

1. The gateway config SHALL set the retry count and per-attempt timeout, and no other file SHALL configure retries for gateway calls. <!-- ubiquitous -->
2. The gateway SHALL allow at most two attempts per request (the primary once, then the fallback). <!-- ubiquitous -->
3. IF the primary deployment fails with a provider error THEN the gateway SHALL answer from `gpt-5.2`, for each alias. <!-- unwanted-behavior (closes slice 1 P1 AC 6, 7) -->
4. IF an attempt receives no complete response within 35 seconds THEN the gateway SHALL abandon it and use the fallback. <!-- unwanted-behavior -->
5. IF both attempts fail THEN the gateway SHALL return an error to the caller within 75 seconds of the request. <!-- unwanted-behavior -->
6. IF the provider rejects a request with a 4xx THEN the gateway SHALL make at most one further attempt, on the fallback, and SHALL return a 4xx to the caller when the fallback rejects it too, within 75 seconds. <!-- unwanted-behavior (observed behavior: failover on 4xx cannot be disabled in the pinned version) -->

**Independent Test**: Break the primary key and see `gpt-5.2` answer for each alias. Delay the primary past the timeout and see the fallback answer; break both and see the error inside 75 s.

---

### P1: PDF through both providers

**User Story**: As the owner, I want to know that both models accept a PDF through the vision alias before building the upload path, so that the synchronous design and the failover are sound.

**Why P1**: Plan Stage 1 "done when" requires it, and it can invalidate later slices.

**Acceptance Criteria**:

1. WHEN a PDF fixture is sent to `form-generator-vision` and answered by `claude-sonnet-5-5` THEN the gateway SHALL return 200 and the answer SHALL contain the fixture's marker word. <!-- event-driven -->
2. WHEN the same request is answered by the `gpt-5.2` fallback THEN the gateway SHALL return 200 and the answer SHALL contain the marker word. <!-- event-driven -->
3. WHEN either PDF call completes THEN the spend log SHALL hold a row for it with a cost greater than 0 and the model that answered. <!-- event-driven -->
4. WHEN a PDF call runs THEN its latency SHALL be measured and written in the README next to the timeout budget. <!-- event-driven -->
5. IF the measured latency of a realistic PDF exceeds the per-attempt timeout THEN the README SHALL record that the synchronous decision needs revisiting, and the slice SHALL be reported as not passing. <!-- unwanted-behavior -->

**Independent Test**: `smoke.sh` sends the fixture twice (normal, then forced to the fallback) and asserts the marker word in both answers.

---

### P2: Documented contract

**User Story**: As a developer of slices 3 to 8, I want the app key, retry policy and timeout budget written down so that I build the client against them.

**Acceptance Criteria**:

1. `docker/litellm/README.md` SHALL describe the app key (alias, models, budget, how it is provisioned, which variables) and state that it is the key the API uses. <!-- ubiquitous -->
2. `docker/litellm/README.md` SHALL state the timeout budget in the order gateway attempt × attempts < client timeout < 90 s < load balancer idle timeout, with the numbers chosen. <!-- ubiquitous -->
3. `docker/litellm/README.md` SHALL state which checks are verified and which are not, including the load balancer, Kestrel and `HttpClient` links. <!-- ubiquitous -->
4. The local-infrastructure paragraph in `CLAUDE.md` SHALL mention the app key and its budget. <!-- ubiquitous -->

---

## Edge Cases

- IF the gateway is restarted (or its database recreated with `down -v`) THEN rerunning `provision-app-key.sh` SHALL recreate the key with the same value.
- IF `LITELLM_APP_MAX_BUDGET_USD` is unset THEN `provision-app-key.sh` SHALL use 10.
- WHEN the budget period rolls over THEN the key's spend SHALL reset, per LiteLLM's `budget_duration` (documented, not tested: a month cannot be waited for).

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| -------------- | ----- | ----- | ------ |
| GWK-01 | P1: App key (AC 10, 11) | Tasks | Implementing |
| GWK-02 | P1: App key (AC 1-3, 8) | Tasks | Implementing |
| GWK-03 | P1: App key (AC 4-7, 9) | Tasks | Implementing |
| GWK-04 | P1: Retries and timeouts (AC 1-6) | Tasks | Implementing |
| GWK-05 | P1: PDF (AC 1-3) | Tasks | Implementing |
| GWK-06 | P1: PDF (AC 4-5) | Tasks | Implementing |
| GWK-07 | P2: Documented contract (AC 1-4) | Tasks | Pending |

**Coverage:** 7 total, 7 mapped to tasks, 0 unmapped

---

## Success Criteria

- [ ] `smoke.sh` passes with zero UNVERIFIED, including slice 1's two failover assertions.
- [ ] A throwaway key with a tiny budget is refused after it spends it.
- [ ] The README holds a measured PDF latency and a timeout budget consistent with it.
