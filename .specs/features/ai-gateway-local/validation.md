# ai-gateway-local Validation

**Date**: 2026-09-30
**Spec**: `.specs/features/ai-gateway-local/spec.md`
**Diff range**: `63cca9c^..HEAD` (63cca9c..fc5387d; 11 files, +905/-1). The uncommitted header-comment edit to `docker/litellm/config.yaml` is outside the range and was ignored. Config line numbers below are those of HEAD.
**Verifier**: independent sub-agent (author != verifier)

## Validation: ai-gateway-local - PASS-WITH-UNVERIFIED

Verdict: PASS-WITH-UNVERIFIED. 17 of 19 P1 and P2 ACs are evidenced. P1 AC 6 and AC 7 (gpt-5.2 failover) are UNVERIFIED and are not counted as passing. `OPENAI_API_KEY` is unset in `.env` (checked, value not printed), so failover cannot answer, and `smoke.sh` prints UNVERIFIED for both (`docker/litellm/smoke.sh:67-69`). Configuration for the failover exists but has never been executed (`docker/litellm/config.yaml:10-14`, `docker/litellm/config.yaml:21-30`).

Note for the orchestrator: `validate_state.py` reads the token PASS in this heading, so it will treat the feature as done. Whether that is acceptable with two UNVERIFIED ACs is the owner's decision.

---

## Task Completion

| Task | Status | Notes |
| ---- | ------ | ----- |
| T1 | Done | `.env.example:5-9`. tasks.md leaves the `.env` real-values box unchecked, although `.env` is in fact populated and gitignored (`.gitignore:43`) |
| T2 | Done | `docker/litellm/config.yaml` |
| T3 | Partial | Marked [x] but its own note says the fallback ACs are unverified. Service, DB and smoke.sh exist |
| T4 | Done | `docker/litellm/README.md` |
| T5 | Done | `docs/adr/0008-ai-gateway-instead-of-direct-provider-client.md` |
| T6 | Done | `CONTEXT.md:155-161` |
| T7 | Done | `CLAUDE.md:121` |

tasks.md still says `Status: Draft`.

---

## Spec-Anchored Acceptance Criteria

Runtime evidence was gathered by read-only probes against the live stack, none of which reach a provider (no-key, wrong-key and unknown-model calls, docker port, psql, `docker compose config`). I did not re-run `smoke.sh`, per instructions. Its earlier run is corroborated by the spend log contents.

### P1: Run the gateway and call both aliases

| AC | Spec outcome | Evidence | Result |
| -- | ------------ | -------- | ------ |
| 1 exact image tag in compose | `ghcr.io/berriai/litellm:<exact>` | `docker-compose.yml:59` `image: ghcr.io/berriai/litellm:v1.103.1`; asserted by `smoke.sh:43-44` (regex `^v[0-9]+\.[0-9]+\.[0-9]+$`) | PASS |
| 2 config mounted | `docker/litellm/config.yaml` in container | `docker-compose.yml:61` (`--config /app/config.yaml`) and `:72` (`./docker/litellm/config.yaml:/app/config.yaml:ro`) | PASS |
| 3 only the two aliases | model names = `form-generator`, `form-generator-vision` | `config.yaml:5,10,16,21`; asserted by `smoke.sh:42` (`sort -u` compared to both names) | PASS |
| 4 form-generator returns completion | HTTP 200 from mapped text model | `smoke.sh:25` (200) and `:26` (spend-log `model` = `anthropic/claude-haiku-4-5-20251001`). Spend log holds that row (spend 3.3e-05, 8.9e-05) | PASS |
| 5 vision returns claude-sonnet-5-5 | HTTP 200, model `anthropic/claude-sonnet-5-5` | `smoke.sh:27,29`. Spend log holds that row (spend 0.000194) | PASS |
| 6 vision failover to gpt-5.2 | answered by gpt-5.2 | Config only: `config.yaml:21-25`, `router_settings` `:27-30`. Assertion `smoke.sh:73-77` never ran (`OPENAI_API_KEY` empty, `:67-69` prints UNVERIFIED) | UNVERIFIED |
| 7 text failover to gpt-5.2 | answered by gpt-5.2 | Config only: `config.yaml:10-14`. Same skipped assertion | UNVERIFIED |
| 8 non-null cost in spend log | row with cost in USD | `smoke.sh:32-34` (`spend is not null and spend > 0` count equals total per group). Spend log rows carry positive `spend` for all three successful calls | PASS |
| 9 other model gets 4xx | 4xx | Probe: unknown model with master key returned 400. `smoke.sh:37` | PASS |
| 10 no or wrong key gets 401 | 401 | Probe: no key returned 401, wrong key returned 401. `smoke.sh:38-39` | PASS |

### P1: Isolation and secrets

| AC | Spec outcome | Evidence | Result |
| -- | ------------ | -------- | ------ |
| 1 own db, role, password | not `form_ai`, `form_ai_app`, `form_ai_migrator` | `docker-compose.yml:47-49` (`litellm` user, `litellm` db, `LITELLM_DB_PASSWORD`) | PASS |
| 2 secrets from untracked `.env` | read from `.env` | `docker-compose.yml:48,63-68` all `${VAR...}`. `.env` untracked (`git ls-files .env` empty) and ignored (`.gitignore:43`) | PASS |
| 3 only empty entries tracked | `.env.example` empty, no real value in tracked files | `.env.example:5-9` all empty. Probe: `git grep -F` of the master, salt, DB password and Anthropic key values across the tracked tree found 0 hits. `smoke.sh:55-58` | PASS |
| 4 unset secret fails compose | compose refuses | `docker-compose.yml:48,63,64,65,66` use `:?`. Probe: `docker compose config -q` with each of the four set to empty was refused. `smoke.sh:59-61` | PASS |
| 5 no prompt/response in logs or spend log | messages not stored | `config.yaml:34` `turn_off_message_logging: true`, `:40` `store_prompts_in_spend_logs: false`. Probe: every spend-log row has `messages` = `{}` and `response` = `{}` | PASS |
| 6 marker absent from container logs and spend row | 0 occurrences | `smoke.sh:47-51` (both counts compared to 0). Probe: the container log has no `MARKER` text; spend rows hold `{}` | PASS |
| 7 db publishes no port | no host port | `docker-compose.yml:50` (no `ports:` key). Probe: `docker port form-ai-litellm-db` printed nothing. `smoke.sh:63` | PASS |
| 8 gateway bound to 127.0.0.1 | `127.0.0.1` | `docker-compose.yml:70`. Probe: `docker port` printed `127.0.0.1:4000`. `smoke.sh:62` | PASS |
| 9 `form_ai_app` refused by litellm-db | connection refused | Probe: psql as `form_ai_app` to `litellm-db` was refused. `smoke.sh:64` | PASS |

### P2: Documented contract and ADR

| AC | Evidence | Result |
| -- | -------- | ------ |
| 1 base URLs and route | `docker/litellm/README.md:7-12` (`http://litellm:4000`, `http://127.0.0.1:4000`, `POST /v1/chat/completions`) | PASS |
| 2 aliases, models, inputs, fallback rule | `README.md:16-19` table, `:23` failover rule | PASS |
| 3 `user` is guid only | `README.md:45` | PASS |
| 4 which key by whom, env vars | `README.md:51-58` | PASS. The sentence at `:58` ("any of the first four, or the password") does not match the table rows, minor |
| 5 ADR at next number, alternative and why | `docs/adr/0008-ai-gateway-instead-of-direct-provider-client.md:11` names keeping `ClaudeFormGenerationService` plus a ledger and ceiling, with reasons. 0007 was the previous ADR | PASS |
| 6 CONTEXT.md defines gateway and model alias | `CONTEXT.md:155-157` (Gateway), `:159-161` (Model alias) | PASS |
| 7 CLAUDE.md lists gateway and its database | `CLAUDE.md:121` (`litellm`, `litellm-db`, `127.0.0.1:4000`, `.env` keys). The AI integration section is untouched, as the spec requires | PASS |

**Status**: all evidenced ACs match the spec outcome. Two ACs (P1 6 and 7) are UNVERIFIED. No spec-precision gaps.

---

## Edge Cases

- [x] DB not ready: `litellm` waits on `depends_on: condition: service_healthy` (`docker-compose.yml:73-75`), and `litellm-db` has a health check (`:53-57`).
- [ ] Only one provider key valid, so an alias still answers through whichever provider is reachable: UNVERIFIED for the OpenAI side. The Anthropic-only path is demonstrated.
- [x] `down -v` then restart starts from empty: the task log claims it was done, and no manual step exists in compose. Not re-run (forbidden here), so this is taken on the author's word.

---

## Gate Check

- **Gate command**: `docker compose config -q && bash docker/litellm/smoke.sh` (Full) and `git diff --check` (Build).
- `docker compose config -q` exited 0 in every probe above, and `git diff --check 63cca9c^..HEAD` printed nothing.
- `smoke.sh` was NOT re-run (it spends money, and instructions limit it to the author's run). The author reports 22 pass, 0 fail, 2 UNVERIFIED (`tasks.md`, T3). I re-derived the script's static and runtime assertions independently, as listed above, and they hold.
- **Test count**: 24 smoke assertions, none pre-existing. No unit tests touched (the FormAI code is untouched).
- **Skipped**: the 2 failover assertions, justified by the missing `OPENAI_API_KEY`.

---

## Discrimination Sensor

Depth: lightweight, in a scratch dir under the session scratchpad using copies of `docker-compose.yml` and `config.yaml`. The real tree was never modified. `git status --porcelain` was identical before and after (` M docker/litellm/config.yaml` only).

The static assertions of `smoke.sh` (`:42`, `:43-44`, `:60`) were run on each mutated copy. Assertions that read the live container cannot flip without a restart, which was not done, so those are reasoned from the code.

| # | Mutation | Assertion that should catch it | Killed? |
| - | -------- | ------------------------------ | ------- |
| 1 | image `:v1.103.1` to `:latest` | `smoke.sh:44` | Killed. Executed: the pin check flipped |
| 2 | `LITELLM_MASTER_KEY:?` to `:-` | `smoke.sh:60` | Killed. Executed: compose accepted the empty key |
| 3 | third model name (`extra-model`) | `smoke.sh:42` | Killed. Executed: the model-names check flipped |
| 4 | port `"127.0.0.1:4000:4000"` to `"4000:4000"` | `smoke.sh:62` (`docker port` equals `127.0.0.1:4000`) | Killed in principle. Partially executed: the compose JSON shows `host_ip` None, but the script assertion reads the running container, so it needs a restart. Not executed |
| 5 | published port on `litellm-db` | `smoke.sh:63` | Killed in principle, not executed. My sed for this mutant errored, and `docker compose config` was unchanged |
| 6 | `store_prompts_in_spend_logs: true` | `smoke.sh:51` (spend row contains marker) | Killed in principle, not executed. Needs a restart; no static check reads this setting |
| 7 | `turn_off_message_logging: false` | `smoke.sh:50` (marker in container logs) | Uncertain, not executed. LiteLLM may not print prompt text to container logs at default log level, so this mutant could survive. No assertion reads this setting directly |

**Result**: 3 of 3 executed static mutants killed. 4 runtime-only mutants reasoned, not executed. Mutant 7 is the most likely survivor.

---

## Code Quality

| Principle | Status |
| --------- | ------ |
| Minimum code, no scope creep | Yes. `git diff --stat` shows only gateway files and the docs the spec lists, plus the plan and spec files. No `FormAI.*` code touched |
| Matches patterns | Yes. Reuses the health check shape and `${VAR}` style |
| Documented guidelines followed | `CLAUDE.md` and `CONTEXT.md` rules on docs; ADR numbering sequential |
| Tests non-shallow | Mostly. The assertions target exact spec outcomes (200, 401, model string, counts). Weakness below |

---

## Gaps (ranked)

1. **P1 AC 6 and AC 7 UNVERIFIED.** gpt-5.2 failover for both aliases has never run. The `order`-based retry is untested, and `num_retries: 1` may not behave as assumed. Evidence: `smoke.sh:67-69`, `config.yaml:10-14,21-30`, `README.md:25`. Fix: set `OPENAI_API_KEY`, re-run `smoke.sh`.
2. **`smoke.sh` is not re-runnable.** Rejected requests (401 and unknown model) create spend-log rows with `spend` 0 and `model_group` `form-generator`; the spend log currently holds such rows. The cost check `smoke.sh:32-34` compares a count of spend > 0 with the total per group, so a second run would report a false failure. Some of those rows came from this verifier's probes; the rows from the script's own rejections have the same effect. Fix: scope the query to rows after the script start or to `status = success`.
3. **Sensor weakness on log suppression.** No assertion checks `store_prompts_in_spend_logs` / `turn_off_message_logging` statically, and the container-logs marker check (`smoke.sh:50`) may not detect `turn_off_message_logging: false`. Add a static grep of those two settings in `config.yaml`, or raise the log level in the test.
4. **Bookkeeping.** `tasks.md` says `Status: Draft`, T3 is ticked despite its partial note, and the T1 `.env` box is unticked. README `:58` wording ("first four") does not map to the table rows.
5. **Not re-verified.** The `down -v` restart edge case rests on the author's statement; the verifier did not run it, as it is forbidden here.

---

## Requirement Traceability Update

| Requirement | New Status |
| ----------- | ---------- |
| GWY-01, GWY-02, GWY-04..GWY-10 | Verified |
| GWY-03 (AC 6-7 failover) | UNVERIFIED, needs `OPENAI_API_KEY` |

---

## Summary

**Overall**: PASS-WITH-UNVERIFIED (ready except for failover)

**Spec-anchored check**: 17/19 ACs matched the spec outcome, 2 UNVERIFIED, 0 spec-precision gaps
**Sensor**: 3/3 executed mutants killed, 4 reasoned and not executed
**Gate**: `docker compose config -q` and `git diff --check` clean; `smoke.sh` not re-run (author reports 22 pass, 2 UNVERIFIED)

**Next steps**: set `OPENAI_API_KEY`, re-run `smoke.sh`, and close gap 2 before relying on the script as a repeatable check.
