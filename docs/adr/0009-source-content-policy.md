---
status: accepted
---

# Text is kept with the form; a PDF is read by the model and not stored

Users can generate a form from pasted text or one uploaded file: `.pdf`, `.docx`, `.pptx` or `.txt`. Images are not supported. When the source is pasted text, a `.txt`, a `.docx` or a `.pptx`, the text FormAI extracted is saved with the form (`FormSourceContent`). When it is a PDF, the file is sent to the model in memory, and what is saved is a row with the file name and **empty content**. The original file is never stored in any case.

Alternatives considered:

- **Keep the PDF (object storage).** It would let the owner see or download what a form came from. Rejected: it adds a bucket, retention and deletion rules, and a privacy surface for documents people did not expect to be kept, for a feature nothing needs yet (there is no regeneration).
- **Extract the PDF's text in the API, like the other formats.** It would make PDFs searchable and let every source follow one rule. Rejected: it needs a PDF library, it returns nothing for scanned pages, and it loses tables and figures, which the vision model reads.
- **Support images.** Rejected for now as out of scope; it is not a gap to close.

## Consequences

- **A form made from a PDF has nothing to show as its source.** The editor can say which file it came from, not what it said. Code that reads `FormSourceContent` must expect `Content == ""` for `SourceType.Pdf`.
- **The PDF reaches a third party.** It goes to the provider behind the gateway ([ADR 0008](./0008-ai-gateway-instead-of-direct-provider-client.md)). The Generate button says that text and files are sent to an AI provider.
- **A PDF is not validated locally** beyond its extension, size and being non-empty, so the provider decides whether it is readable; a request it refuses with a 400 or 422 is reported as an unreadable file.
- **A PDF costs far more than text and has no page cap.** The 10 MB limit and `max_tokens` are the cost controls; the app key's budget in the gateway is the ceiling.
- **The 100-character minimum does not apply to a PDF**, since the model reads the file itself; it applies to every other source.

## Status

Accepted.
