# Phase 13: Preserve answered-option history (`answer_selected_options` rework)

## Context

`answer_selected_options` is today a pure join table between an `Answer` and a
`QuestionOption`:

```
answer_selected_options
─────────────────────────
 answer_id  uuid  ─┐ composite PK
 option_id  uuid  ─┘        FK -> question_options.id  ON DELETE RESTRICT
```

Two problems come out of that shape:

1. **Renaming an option rewrites history.** The submitted answer only stores the
   option's *id*, so the label a respondent actually clicked is read back from
   `question_options.text` — whatever it says *now*. If the owner edits
   "Paris" into "Paris, France" (or into something completely different), every
   past submission silently changes meaning.
2. **The `RESTRICT` FK deadlocks the form editor.** `SaveFormEditorHandler` and
   `UpdateQuestionsHandler` both call `Form.ReplaceQuestions(...)`, which clears
   every `FormQuestion` and cascades a delete to its `QuestionOption` rows. Once
   a single submission references one of those options, the `RESTRICT` FK aborts
   the whole save.

The fix is to stop pointing at the option and start **snapshotting the option's
text at submit time**. After this phase the table is:

```
answer_selected_options
─────────────────────────
 id           uuid  PK
 answer_id    uuid  FK -> answers.id  ON DELETE CASCADE
 option_text  text  NOT NULL   (snapshot of question_options.text at submit time)
```

`option_id` is **dropped entirely** — with it, the `RESTRICT` FK disappears and
problem 2 goes away for free.

### Scope decisions (already made — don't re-litigate while implementing)

- **`option_id` is dropped**, not kept FK-less. The text snapshot is the record
  of truth for a submitted answer; grading still happens against live option ids
  *during* submit, before the snapshot is written.
- **`option_text` is a plain column, not a foreign key.** It "references"
  `question_options.text` only in the logical sense — it's a copy taken at
  submit time and is expected to diverge from the live option later. That's the
  whole point.
- **Option-text rules ("required" + "unique per question") are enforced in the
  application layer only.** No `NOT NULL`/`UNIQUE` constraint is added to
  `question_options`, no unique index on `(question_id, text)`. Reason: existing
  rows may already violate it, and the editor needs to give a friendly
  field-level 400 rather than a Postgres error.
- Frontend changes are **out of scope**. The editor already surfaces the API's
  `errors` dictionary through the phase-12 toast; the new validation messages
  ride that path with no React changes.

### Before you start

The working tree has leftovers from an **abandoned earlier attempt** at this
(migration `20260821214005_PreserveAnswerHistory`, which also tried soft-deletes
via `deleted_at`/`is_hidden` — that whole approach is dropped):

- `rollback.sql` at the repo root — the undo script for that migration.
- `src/FormAI.Application/Submissions/Scoring/SubmissionScorer.cs` — untracked,
  a copy-paste of `SubmitFormHandler`'s private scoring methods that nothing
  calls yet.

Do this first:

```bash
# 1. Confirm the abandoned migration is NOT applied to your database.
#    Expected: zero rows.
psql "$ConnectionStrings__DefaultConnection" -c \
  "select migration_id from \"__EFMigrationsHistory\" where migration_id like '%PreserveAnswerHistory%';"

# 2. Confirm the schema is at the last committed migration.
#    Expected: answer_id, option_id — and NO id / option_text columns.
psql "$ConnectionStrings__DefaultConnection" -c "\d answer_selected_options"
```

If step 1 returns a row, run `rollback.sql` against the database first, then
re-check. Once both checks are clean, `git rm`/delete `rollback.sql` — it
describes a migration that will never exist on `main`. Leave
`SubmissionScorer.cs` alone for now; Part E says what to do with it.

---

## Part A — Domain entity

### A1. Rewrite `src/FormAI.Domain/Entities/AnswerSelectedOption.cs`

Replace the whole file:

```csharp
namespace FormAI.Domain.Entities;

public class AnswerSelectedOption
{
    public Guid Id { get; private set; }
    public Guid AnswerId { get; private set; }
    public string OptionText { get; private set; } = string.Empty;

    private AnswerSelectedOption() { }

    public static AnswerSelectedOption Create(Guid answerId, string optionText)
    {
        if (string.IsNullOrWhiteSpace(optionText))
            throw new ArgumentException("Option text is required.", nameof(optionText));

        return new AnswerSelectedOption
        {
            Id = Guid.NewGuid(),
            AnswerId = answerId,
            OptionText = optionText.Trim()
        };
    }
}
```

What changed and why:

- `OptionId` and the `QuestionOption Option` navigation are **gone**. Nothing in
  the codebase reads that navigation today (verified — the only references to
  `SelectedOptions` are `Answer`, the two EF configurations, and
  `SubmitFormHandler`), so removing it breaks no read path.
- `Id` is assigned in the factory, not by the database. That matches every other
  entity here, and `AppDbContext.OnModelCreating` explicitly sets
  `ValueGenerated.Never` on all primary keys — so an unassigned `Id` would be
  saved as `Guid.Empty`, not auto-generated. Do not remove the assignment.
- `OptionText` is stored **trimmed**. The uniqueness check in Part D also trims,
  so the two agree on what "the same option" means.
- The guard throws `ArgumentException`, not `ValidationException`: reaching this
  factory with blank text is a bug in the caller, not user input. User input is
  rejected earlier, in Part C/D.

`Answer.cs` needs **no change** — `SetSelectedOptions(List<AnswerSelectedOption>)`
already works against the new shape.

---

## Part B — EF Core mapping

### B1. Rewrite `src/FormAI.Infrastructure/Data/Configurations/AnswerSelectedOptionConfiguration.cs`

```csharp
using FormAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormAI.Infrastructure.Data.Configurations;

public class AnswerSelectedOptionConfiguration : IEntityTypeConfiguration<AnswerSelectedOption>
{
    public void Configure(EntityTypeBuilder<AnswerSelectedOption> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.OptionText)
            .HasMaxLength(1024)
            .IsRequired();
    }
}
```

- `HasMaxLength(1024)` mirrors `QuestionOptionConfiguration`'s limit on
  `QuestionOption.Text`, so a snapshot can never be truncated relative to its
  source.
- The `HasOne(a => a.Option)...OnDelete(Restrict)` block is deleted along with
  the navigation. **This is the line that was breaking form saves.**
- The `answer_id` FK is *not* declared here. It's already declared from the
  other side in `AnswerConfiguration` (`HasMany(a => a.SelectedOptions)
  .WithOne().HasForeignKey(a => a.AnswerId).OnDelete(Cascade)`) — leave that
  file untouched. Because `answer_id` is no longer part of the primary key, EF
  will now generate a plain index `ix_answer_selected_options_answer_id` for it
  automatically; that's expected and wanted (every read of an answer's options
  filters on it).

### B2. Leave `AnswerConfiguration.cs` and `QuestionOptionConfiguration.cs` alone

Explicitly: **do not** add `.IsRequired()` or a unique index to
`QuestionOption.Text`. Per the scope decision, that rule lives in the
application layer only.

---

## Part C — Submit path

### C1. `src/FormAI.Application/Submissions/SubmitForm/SubmitFormHandler.cs`

Two edits.

**C1a — resolve ids to text when building the answer.** Replace the `answers`
projection in `HandleAsync` (currently around lines 40–51):

```csharp
var answers = questionResults.Select(r =>
{
    var answer = Answer.Create(submission.Id,
    r.Question.Id, null, r.Answer.TextValue,
    (double?)r.Answer.NumericValue, r.Score);

    var optionTextById = r.Question.Options.ToDictionary(o => o.Id, o => o.Text);

    answer.SetSelectedOptions(r.SelectedOptionsIds
        .Select(id => AnswerSelectedOption.Create(answer.Id, optionTextById[id]))
        .ToList());

    return answer;
}).ToList();
```

The indexer `optionTextById[id]` is deliberately *not* defensive — C1b
guarantees every id in `SelectedOptionsIds` belongs to the question, so a
`KeyNotFoundException` here would mean C1b regressed. Don't soften it into a
`TryGetValue` + skip: silently dropping a selection would corrupt the
submission rather than reject it.

**C1b — reject option ids that don't belong to the question.** This validation
does not exist today; before this phase an unknown id merely scored 0, but now
it has no text to snapshot, so it must be a hard error. In `ValidateAnswers`,
after the `if (!isAnswered) continue;` line and before the `ScoreAnswer` call:

```csharp
if (question.Type is QuestionType.Single or QuestionType.Multiple)
{
    var knownOptionIds = question.Options.Select(o => o.Id).ToHashSet();
    var submittedIds = answer!.SelectedOptionIds ?? Array.Empty<Guid>();

    if (submittedIds.Any(id => !knownOptionIds.Contains(id)))
    {
        errors[question.Id.ToString()] =
            new[] { "One or more selected options don't belong to this question." };
        continue;
    }

    if (question.Type == QuestionType.Single && submittedIds.Length > 1)
    {
        errors[question.Id.ToString()] =
            new[] { "This question accepts only one option." };
        continue;
    }
}
```

The `Single`-arity check is bundled in because it's the same shape of bug and
the same error key; drop it if you'd rather keep the diff minimal — it is not
required by the schema change.

`errors` is already threaded through `ValidateAnswers` and thrown as
`ValidationException` by the caller, which `ExceptionHandlingMiddleware` maps to
`400 { message, errors }`. Keying by `question.Id.ToString()` matches the
existing "This question is required." error, so the frontend's per-question
error rendering picks it up unchanged.

### C2. Scoring is untouched

`ScoreSingleOrMultiple` still compares **ids** against
`question.Options.Where(o => o.IsCorrect)` and still returns `Guid[]`. Grading
happens against the live options at submit time, then the result is snapshotted
as text. Do not change the scorer to compare text — an option's text is mutable
and its id is not, so ids are the correct thing to grade on.

---

## Part D — Option text validation in the editor handlers

Both write paths build `QuestionOption`s from an `OptionInput` list, in two
separate namespaces that happen to declare identical records
(`Forms.UpdateQuestions.OptionInput` and `Forms.SaveFormEditor.OptionInput`).
Don't merge them in this phase — just share the validation logic.

### D1. New file `src/FormAI.Application/Forms/Validation/QuestionOptionValidator.cs`

```csharp
namespace FormAI.Application.Forms.Validation;

public static class QuestionOptionValidator
{
    /// <summary>
    /// Applies the option rules for one question: text is required, and text is
    /// unique within the question (trimmed, case-insensitive). Adds any problems
    /// to <paramref name="errors"/> under the key "questions[{questionIndex}].options".
    /// </summary>
    public static void Validate(
        int questionIndex,
        IReadOnlyList<string> optionTexts,
        Dictionary<string, string[]> errors)
    {
        var messages = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < optionTexts.Count; i++)
        {
            var text = optionTexts[i]?.Trim() ?? string.Empty;

            if (text.Length == 0)
                messages.Add($"Option {i + 1}: text is required.");
            else if (text.Length > 1024)
                messages.Add($"Option {i + 1}: text must be at most 1024 characters.");
            else if (!seen.Add(text))
                messages.Add($"Option {i + 1}: \"{text}\" is already used in this question.");
        }

        if (messages.Count > 0)
            errors[$"questions[{questionIndex}].options"] = messages.ToArray();
    }
}
```

Notes on the rule shape:

- **Trimmed + case-insensitive.** `"Paris"`, `"paris "` and `" PARIS"` are one
  option. Since `AnswerSelectedOption.Create` trims and the snapshot is compared
  by humans reading results, letting near-identical labels coexist would make
  aggregation meaningless.
- **`else if` chaining** so one bad option produces one message, not three.
- The 1024 check keeps the handler from handing EF a value that would blow the
  `HasMaxLength(1024)` on both `question_options.text` and
  `answer_selected_options.option_text`.

### D2. `src/FormAI.Application/Forms/SaveFormEditor/SaveFormEditorHandler.cs`

Add `using FormAI.Application.Common.Exceptions;` (already there) and
`using FormAI.Application.Forms.Validation;`. Then, **after** the ownership /
title / description checks and **before** the `form.Update(...)` call:

```csharp
var errors = new Dictionary<string, string[]>();

for (var i = 0; i < request.Questions.Count; i++)
{
    var q = request.Questions[i];

    if (q.Type is QuestionType.Single or QuestionType.Multiple)
        QuestionOptionValidator.Validate(i, q.Options.Select(o => o.Text).ToList(), errors);
}

if (errors.Count > 0)
    throw new ValidationException(errors);
```

Validate before mutating `form` so a rejected save leaves the tracked entity
untouched.

Then trim on the way into the entity, so what's stored matches what was
validated — change the option projection in the same handler:

```csharp
var options = q.Options
    .Select(o => QuestionOption.Create(question.Id, o.Text.Trim(), o.Order, o.IsCorrect))
    .ToList();
```

### D3. `src/FormAI.Application/Forms/UpdateQuestions/UpdateQuestionsHandler.cs`

Apply exactly the same two edits — the validation block after the
`ForbiddenException` ownership check, and `o.Text.Trim()` in the option
projection. Watch the casing difference: this handler's record field is
lowercase `q.options`, not `q.Options`.

### D4. Leave `SaveFormEditorHandler`'s existing `ArgumentException` throws alone

The title/description checks there throw `ArgumentException`, which the
middleware maps to `500`. That's a pre-existing bug and **not** part of this
phase — fixing it changes status codes the frontend may depend on. Note it,
move on.

---

## Part E — `SubmissionScorer.cs` (the untracked file)

`src/FormAI.Application/Submissions/Scoring/SubmissionScorer.cs` is an
in-progress extraction of `SubmitFormHandler`'s four private scoring methods. It
has no namespace, is referenced by nothing, and duplicates logic this phase
touches. Pick one, don't leave it half-done:

- **Recommended — park it.** Delete the file (or leave it untracked and out of
  the commit) and do this phase against `SubmitFormHandler`'s private methods.
  The Part C edits are in `HandleAsync`/`ValidateAnswers`, which stay in the
  handler either way, so the extraction is orthogonal work.
- **Or finish the extraction first**, as its own commit: give it
  `namespace FormAI.Application.Submissions.Scoring;`, delete the four private
  `Score*` methods from `SubmitFormHandler`, and call
  `SubmissionScorer.ScoreAnswer(question, answer)`. Fix the typo'd tuple name
  (`SelectedOptionsIds` on `ScoreAnswer` vs `SelectedOptionIds` on the others)
  while you're in there. **Then** apply Part C.

Do not apply Part C to both copies.

---

## Part F — The migration

> This is the step where the earlier attempt went wrong. Read all of it before
> running anything.

### F1. Scaffold it

```bash
dotnet build FormAI.sln   # must be green first — the scaffolder loads the built model
dotnet ef migrations add PreserveAnswerOptionHistory \
  --project src/FormAI.Infrastructure --startup-project src/FormAI.API
```

### F2. Expect the scaffolder to be wrong, and replace its body

EF sees `option_id` (uuid) disappear and `id` (uuid) appear on the same table
and — as it did on the previous attempt — emits
`migrationBuilder.RenameColumn(name: "option_id", newName: "id", ...)`. **That
is data corruption, not a rename:** every row's primary key would become the id
of the option it used to point at, which is not unique across answers, so the
new primary key would fail to create the moment two respondents pick the same
option. It also drops `option_text` on the floor with no backfill, leaving a
`NOT NULL` column over existing rows.

Open the generated
`src/FormAI.Infrastructure/Migrations/<timestamp>_PreserveAnswerOptionHistory.cs`
and replace the **entire body of `Up`** with a single raw SQL block:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(@"
        -- 1. Add the snapshot column, nullable for now.
        ALTER TABLE answer_selected_options ADD COLUMN option_text text;

        -- 2. Backfill it from the option each row currently points at.
        UPDATE answer_selected_options aso
        SET option_text = qo.text
        FROM question_options qo
        WHERE qo.id = aso.option_id;

        -- 3. Safety net: the FK made orphans impossible, but a NULL here would
        --    abort step 4 and leave the migration half-applied.
        UPDATE answer_selected_options
        SET option_text = '(option removed)'
        WHERE option_text IS NULL OR btrim(option_text) = '';

        ALTER TABLE answer_selected_options ALTER COLUMN option_text SET NOT NULL;

        -- 4. Add the surrogate key and populate it with fresh values.
        --    gen_random_uuid() is built in on PostgreSQL 13+.
        ALTER TABLE answer_selected_options ADD COLUMN id uuid;
        UPDATE answer_selected_options SET id = gen_random_uuid();
        ALTER TABLE answer_selected_options ALTER COLUMN id SET NOT NULL;

        -- 5. Drop the old key and the option FK. This is what unblocks
        --    ReplaceQuestions from deleting answered options.
        ALTER TABLE answer_selected_options
            DROP CONSTRAINT fk_answer_selected_options_question_options_option_id;
        DROP INDEX IF EXISTS ix_answer_selected_options_option_id;
        ALTER TABLE answer_selected_options DROP CONSTRAINT pk_answer_selected_options;
        ALTER TABLE answer_selected_options DROP COLUMN option_id;

        -- 6. Install the new key and the FK-supporting index.
        ALTER TABLE answer_selected_options
            ADD CONSTRAINT pk_answer_selected_options PRIMARY KEY (id);
        CREATE INDEX ix_answer_selected_options_answer_id
            ON answer_selected_options (answer_id);
    ");
}
```

Order matters: the backfill in step 2 must run while `option_id` still exists,
and `option_id` can't be dropped while it's half of the primary key.

`Down` should be the mirror image (it's what a future `rollback.sql` would be
generated from). Replace its body with:

```csharp
protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(@"
        ALTER TABLE answer_selected_options ADD COLUMN option_id uuid;

        -- Best-effort re-link by text, per question. Rows whose option was
        -- renamed or deleted cannot be matched and are dropped — this is lossy
        -- and is exactly why the forward migration exists.
        UPDATE answer_selected_options aso
        SET option_id = qo.id
        FROM answers a
        JOIN question_options qo ON qo.question_id = a.question_id
        WHERE a.id = aso.answer_id
          AND btrim(lower(qo.text)) = btrim(lower(aso.option_text));

        DELETE FROM answer_selected_options WHERE option_id IS NULL;

        ALTER TABLE answer_selected_options ALTER COLUMN option_id SET NOT NULL;
        ALTER TABLE answer_selected_options DROP CONSTRAINT pk_answer_selected_options;
        DROP INDEX IF EXISTS ix_answer_selected_options_answer_id;
        ALTER TABLE answer_selected_options DROP COLUMN id;
        ALTER TABLE answer_selected_options DROP COLUMN option_text;

        ALTER TABLE answer_selected_options
            ADD CONSTRAINT pk_answer_selected_options PRIMARY KEY (answer_id, option_id);
        CREATE INDEX ix_answer_selected_options_option_id
            ON answer_selected_options (option_id);
        ALTER TABLE answer_selected_options
            ADD CONSTRAINT fk_answer_selected_options_question_options_option_id
            FOREIGN KEY (option_id) REFERENCES question_options (id) ON DELETE RESTRICT;
    ");
}
```

Leave the auto-generated `AppDbContextModelSnapshot.cs` changes **exactly as
scaffolded** — only the migration's `Up`/`Down` bodies get hand-edited. The
snapshot is EF's model state, not SQL, and rewriting it by hand desynchronizes
the next migration you generate.

### F3. Review the SQL before touching a database

```bash
dotnet ef migrations script --idempotent \
  --project src/FormAI.Infrastructure --startup-project src/FormAI.API \
  --output "$TMP/phase13.sql"
```

Read the tail of that file and confirm it contains your hand-written SQL and
**no** `ALTER TABLE answer_selected_options RENAME COLUMN`.

### F4. Apply

```bash
# Back up first if the database has submissions you care about.
pg_dump "$ConnectionStrings__DefaultConnection" -t answer_selected_options -t question_options \
  > "$TMP/answer_options_backup.sql"

dotnet ef database update \
  --project src/FormAI.Infrastructure --startup-project src/FormAI.API
```

### F5. Verify the resulting schema

```bash
psql "$ConnectionStrings__DefaultConnection" -c "\d answer_selected_options"
```

Expect exactly: `id uuid not null` (PK `pk_answer_selected_options`),
`answer_id uuid not null` (FK to `answers` `ON DELETE CASCADE`, index
`ix_answer_selected_options_answer_id`), `option_text character varying(1024)
not null`. Expect **no** `option_id` and **no** FK to `question_options`.

Then spot-check that history survived:

```sql
select answer_id, option_text from answer_selected_options limit 10;
```

Every row must have a non-empty `option_text`.

---

## Part G — Manual test pass

Run `dotnet build FormAI.sln` and `dotnet test FormAI.sln` first (the test
projects don't currently touch `AnswerSelectedOption`, so this is a compile
check more than a behavior check).

Then, against a running API + frontend:

1. **The bug this phase exists to fix.** Create a form with a `Single` question
   and options "Paris" / "London". Publish it, answer it from an incognito
   window, then as the owner rename "Paris" to "Rome" and save.
   - The save must now **succeed** (before this phase the `RESTRICT` FK made it
     fail once a submission existed).
   - `select option_text from answer_selected_options` must still read
     `Paris` for the old submission.
2. **Deleting an answered option.** Same form, remove an option entirely and
   save. Save succeeds; the historical row is untouched.
3. **Required text.** Save a form with an option whose text is `"   "`. Expect
   `400` with `errors["questions[0].options"] = ["Option 1: text is required."]`
   and the phase-12 error toast.
4. **Uniqueness.** Save a question with options `"Paris"` and `"paris "`. Expect
   `400` naming option 2 as already used.
5. **Uniqueness is per question, not per form.** Two different questions each
   with a "Yes" option must save fine.
6. **Foreign option id.** `POST /api/forms/{id}/submit` with a
   `selectedOptionIds` entry copied from a *different* form's question. Expect
   `400` keyed by the question id, not a `500` or a corrupted row.
7. **Happy-path submit.** A normal `Multiple` submission writes one
   `answer_selected_options` row per selection, each with a distinct `id` and
   the right `option_text`; the score is unchanged from before the phase.

---

## Files touched

| File | Change |
|---|---|
| `src/FormAI.Domain/Entities/AnswerSelectedOption.cs` | Rewritten: `Id` + `OptionText`, no `OptionId`/`Option` |
| `src/FormAI.Infrastructure/Data/Configurations/AnswerSelectedOptionConfiguration.cs` | Rewritten: single-column key, `option_text` required, option FK removed |
| `src/FormAI.Application/Submissions/SubmitForm/SubmitFormHandler.cs` | Snapshot option text on submit; reject unknown option ids |
| `src/FormAI.Application/Forms/Validation/QuestionOptionValidator.cs` | **New** — required + unique-per-question rules |
| `src/FormAI.Application/Forms/SaveFormEditor/SaveFormEditorHandler.cs` | Validate options; trim text |
| `src/FormAI.Application/Forms/UpdateQuestions/UpdateQuestionsHandler.cs` | Validate options; trim text |
| `src/FormAI.Infrastructure/Migrations/<ts>_PreserveAnswerOptionHistory.cs` | **New** — hand-written `Up`/`Down` SQL |
| `src/FormAI.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | Auto-generated, do not hand-edit |
| `rollback.sql` | **Deleted** — leftover from the abandoned attempt |
| `src/FormAI.Application/Submissions/Scoring/SubmissionScorer.cs` | Deleted, or finished as a separate commit (Part E) |
