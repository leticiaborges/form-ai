# The form editor saves a diff, and question and option ids never change

Saving the editor used to replace a form's questions wholesale, which gave every question and option a fresh id on each save. That is fine for an unanswered form and destructive for an answered one: answers reference questions by id, so a save would orphan them. `SaveFormEditorHandler` now sends the ids it was given back to the server, and `FormEditorDiffer` compares them against what is stored to work out what was added, modified, removed or left alone. Renaming a question, retyping it or dragging it to a new position updates the existing row; only genuinely new questions and options get new ids.

## Consequences

- **Ids are the contract between an editor payload and stored answers.** Any new write path into a form's questions must preserve them; regenerating ids is data loss, not a refactor.
- The diff is per-entity: questions are diffed first, then options are diffed within each question that survived.
- An incoming id that isn't found on the form — including a repeat of an id already matched in the same payload — is treated as an addition, and the server assigns the id. Ids sent for new questions are ignored.
- "Unchanged" is decided field by field (`IsUnchanged`), which is what lets a pure reorder update `Order` without touching anything else.
- Removing a question or an option still deletes it. The respondent's answer survives only because of the text snapshot in [ADR 0001](./0001-selected-option-text-snapshot.md) — the two decisions only work together.
