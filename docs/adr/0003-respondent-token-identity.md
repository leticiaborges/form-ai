---
status: under review
---

# Signed-out respondents are identified by a token their own browser generates

A published form has to be answerable without an account, but each respondent should only submit once. Rather than require sign-in, the browser generates a UUID on first visit to a form, keeps it in `localStorage`, and sends it with the submission; the server stores it on the submission and rejects a second submission carrying the same token. Signed-in respondents are matched on their user id instead, and the token is ignored.

## Consequences

- Duplicate prevention is **advisory, not enforced**. The token is supplied by the client, so clearing site data, opening a private window, or using another device produces a new identity and an accepted second submission. Anyone editing the request can pick any token they like.
- Respondents stay anonymous, which is the trade being made: FormAI cannot tell who answered, only that two answers came from the same browser.
- A respondent's own view of what they submitted (`my-submission`) is retrieved with the same token, so losing the token loses access to their answers.
- The identity is per browser, not per form: one token is reused across every form that browser answers.

## Status

Under review. The open question is whether a client-supplied identifier is worth keeping at all, or whether one-submission-per-respondent should require signing in.
