# Selected options are stored as text, not as a reference

A submitted answer used to point at the `question_options` row the respondent picked, which meant renaming an option silently rewrote history — every past respondent appeared to have answered the new text — and deleting one broke the foreign key. Since a form stays editable after it has been answered, we dropped the reference entirely: `answer_selected_options` now has its own `id` and stores `option_text`, a copy of the option's text taken at submission time.

## Consequences

- What a respondent answered is fixed at submission and cannot be altered by later edits to the form. This is the point of the decision.
- There is **no** column linking a selected option back to the option it came from. Answers cannot be grouped or counted by option id — aggregation has to match on text, and an option renamed mid-collection produces two distinct texts for what the owner thinks is one choice.
- Option text is required and unique within a question, enforced in the application only (`QuestionOptionValidator`), never in the database. Uniqueness is what makes text a usable grouping key.
- The existing selected-option rows were migrated by renaming `option_id` to `id` and defaulting `option_text` to `''`, so answers submitted before this change carry an empty text.
