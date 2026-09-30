---
status: accepted
---

# Model calls go through an AI gateway (LiteLLM), not a direct provider client

Every model call from the API will go to a LiteLLM gateway that runs as its own container with its own database. The app addresses two **model aliases**, `form-generator` (text) and `form-generator-vision` (text and PDF), and never a real model id. The gateway maps each alias to a primary model with `gpt-5.2` as failover, reports the cost of each call in its spend log, and can enforce a spending ceiling with a budget on the key the app uses. Provider keys live only in the gateway; the prompt and response text are neither logged nor stored there.

Alternatives considered:

- **Keep `ClaudeFormGenerationService` and add a ledger and a ceiling in the app.** Least new infrastructure, and the cost could be computed from token counts. It was rejected because every provider swap or fallback then needs code and a redeploy, the app has to own a price table that goes stale, and adding a second provider (needed for PDFs and failover) means a second client behind the same interface. The gateway does the price lookup and the failover, and swapping a model becomes a config change.
- **Call each provider's SDK behind `IFormGenerationService` with a router class of our own.** It removes the container but rebuilds retries, failover and cost reporting inside the API, which is what the gateway already does.

## Consequences

- **A new container and database to run.** Locally they are in `docker-compose.yml`; the gateway binds to `127.0.0.1` and its database publishes no port. It uses its own role and password, unrelated to `form_ai_app` and `form_ai_migrator`. Production deployment is a separate later slice.
- **The app depends on aliases, not models.** A model can change without a code change, but the answer quality then changes with it, so the evaluation set has to be rerun after any change to an alias.
- **The gateway becomes a single point of failure for generation.** If it is down, generation is unavailable.
- **Secrets move.** Provider keys, the master key and the salt key sit in the gateway's environment from the untracked `.env`. The salt key cannot change once the gateway has stored data.
- **The ceiling has two layers.** The app's own ledger decides what the user sees; the gateway's key budget is the backstop. Neither is in this change.
- **Failover is wired with two deployments per alias ranked by `order`,** so that the config holds only the two alias names. It needs one retry to reach the second deployment.

## Status

Accepted. The gateway runs locally and both aliases answer through their Anthropic primary with cost in the spend log, and no prompt text is stored. Failover to `gpt-5.2` and PDF input through each provider are not yet verified, because no OpenAI key has been configured. The app does not use the gateway yet.
