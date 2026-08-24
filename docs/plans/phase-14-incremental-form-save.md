# Phase 14 — Incremental save in the form editor (change tracking, not replace-all)

## Context

`SaveFormEditorHandler` today rebuilds the whole question tree from scratch on every save:

```csharp
var questions = request.Questions.Select(q => {
    var question = FormQuestion.Create(request.FormId, ...);   // <- new Guid every save
    question.SetOptions(q.Options.Select(o => QuestionOption.Create(question.Id, ...)).ToList());
    return question;
}).ToList();

form.ReplaceQuestions(questions);   // Questions.Clear() + AddRange()
```

`Form.ReplaceQuestions` clears the tracked collection, so EF severs the old `FormQuestion`
rows and deletes them. That has two consequences:

1. **Ids churn on every save.** Even pressing Save with zero edits reissues every
   `FormQuestion.Id` and `QuestionOption.Id`.
2. **Submitted answers are destroyed.** `AnswerConfiguration.cs:28-31` maps
   `Answer -> FormQuestion` with `DeleteBehavior.Cascade`, so deleting the questions
   cascade-deletes every `Answer` and (via `Answer -> SelectedOptions` cascade) every
   `AnswerSelectedOption`. Phase 13's `option_text` snapshot preserved the *label* a
   respondent picked, but the answer rows themselves still disappear the moment the
   owner saves the editor.

The goal: **compute what actually changed, and touch only that.** Unchanged questions and
options keep their ids and produce no SQL. A renamed question keeps its id. Only genuinely
added/removed rows are inserted/deleted.

The user also asked that the "what changed" logic stay separate from the "save it" logic —
so an EF-free differ produces a plain description of the changes, and the handler is the only
thing that mutates the domain. The differ is written **specifically for questions and
options**; a generic abstraction can be extracted later if a third collection ever needs it.

### Decisions already made (do not re-litigate)

- **Identity travels in the payload.** `QuestionInput`/`OptionInput` gain a `Guid? Id`, and
  `frontend/src/api/forms.ts` stops stripping ids. Positional/text matching was rejected —
  it cannot tell "renamed question 2" from "deleted 2, inserted a new 2".
- **`UpdateQuestionsHandler` is out of scope.** It keeps its `ReplaceQuestions` behaviour
  for now.
- **Deleting a question still cascades to its answers.** That only happens when the owner
  genuinely removes the question, which is the intended semantics. No migration, no schema
  change in this phase.

---

## Part A — The diff (Application layer, no EF)

Two concerns kept apart on purpose: the **differ** decides *what* changed; the **handler**
(Part D) decides *what to do about it*. Nothing here touches EF, `DbContext`, or a
repository — it compares two in-memory sequences and returns data.

The types are deliberately **specific to questions and options**, not a generic collection
utility. There are exactly two collections to reconcile, they are known at compile time, and
concrete types keep the call sites readable (`diff.Removed` is a `FormQuestion`, not a
`TExisting`). If a third collection ever needs the same treatment, the shared shape can be
lifted out then — the duplication between the two comparison loops is small and visible,
which is what makes that later extraction safe.

Both files live next to the handler in
`src/FormAI.Application/Forms/SaveFormEditor/`.

### A1. `FormEditorDiff.cs` — the result types

```csharp
using FormAI.Domain.Entities;

namespace FormAI.Application.Forms.SaveFormEditor;

/// <summary>An existing question paired with the incoming input that matched it by id.</summary>
public readonly record struct QuestionMatch(FormQuestion Question, QuestionInput Input);

/// <summary>An existing option paired with the incoming input that matched it by id.</summary>
public readonly record struct OptionMatch(QuestionOption Option, OptionInput Input);

/// <summary>
/// The outcome of comparing a form's stored questions against the editor payload.
/// Pure data: producing it changes nothing.
/// </summary>
public sealed record QuestionsDiff(
    IReadOnlyList<QuestionInput> Added,
    IReadOnlyList<QuestionMatch> Modified,
    IReadOnlyList<QuestionMatch> Unchanged,
    IReadOnlyList<FormQuestion> Removed)
{
    /// <summary>Everything that survives the save — modified and untouched alike.</summary>
    public IEnumerable<QuestionMatch> Surviving => Modified.Concat(Unchanged);

    public bool HasChanges => Added.Count > 0 || Modified.Count > 0 || Removed.Count > 0;
}

/// <summary>The outcome of comparing one question's stored options against its payload.</summary>
public sealed record OptionsDiff(
    IReadOnlyList<OptionInput> Added,
    IReadOnlyList<OptionMatch> Modified,
    IReadOnlyList<OptionMatch> Unchanged,
    IReadOnlyList<QuestionOption> Removed)
{
    public bool HasChanges => Added.Count > 0 || Modified.Count > 0 || Removed.Count > 0;
}
```

`Unchanged` is carried separately from `Modified` on purpose: the handler must still walk
unchanged questions to reconcile their options (a question whose own fields are untouched can
still have had an option renamed), but it must never call `Update` on them — that is what
keeps a no-op save silent in SQL.

### A2. `FormEditorDiffer.cs` — the comparison

```csharp
using FormAI.Domain.Entities;

namespace FormAI.Application.Forms.SaveFormEditor;

/// <summary>
/// Matches the editor payload against what is stored, by id, and classifies each item as
/// added, modified, unchanged or removed. Knows nothing about persistence.
/// </summary>
public static class FormEditorDiffer
{
    public static QuestionsDiff DiffQuestions(
        IEnumerable<FormQuestion> existing,
        IEnumerable<QuestionInput> incoming)
    {
        var byId = existing.ToDictionary(q => q.Id);
        var claimed = new HashSet<Guid>();

        var added = new List<QuestionInput>();
        var modified = new List<QuestionMatch>();
        var unchanged = new List<QuestionMatch>();

        foreach (var input in incoming)
        {
            // No id, an id we don't know, or an id already claimed by an earlier item in the
            // same payload -> treat as a new question. Malformed input can never overwrite
            // an existing row twice.
            if (input.Id is null || !byId.TryGetValue(input.Id.Value, out var question) || !claimed.Add(input.Id.Value))
            {
                added.Add(input);
                continue;
            }

            (IsUnchanged(question, input) ? unchanged : modified).Add(new QuestionMatch(question, input));
        }

        var removed = byId.Values.Where(q => !claimed.Contains(q.Id)).ToList();

        return new QuestionsDiff(added, modified, unchanged, removed);
    }

    public static OptionsDiff DiffOptions(
        IEnumerable<QuestionOption> existing,
        IEnumerable<OptionInput> incoming)
    {
        var byId = existing.ToDictionary(o => o.Id);
        var claimed = new HashSet<Guid>();

        var added = new List<OptionInput>();
        var modified = new List<OptionMatch>();
        var unchanged = new List<OptionMatch>();

        foreach (var input in incoming)
        {
            if (input.Id is null || !byId.TryGetValue(input.Id.Value, out var option) || !claimed.Add(input.Id.Value))
            {
                added.Add(input);
                continue;
            }

            (IsUnchanged(option, input) ? unchanged : modified).Add(new OptionMatch(option, input));
        }

        var removed = byId.Values.Where(o => !claimed.Contains(o.Id)).ToList();

        return new OptionsDiff(added, modified, unchanged, removed);
    }

    // Compared against the *normalised* incoming values — the same trimming the handler
    // stores (phase 13 rule) — so cosmetic whitespace never produces a spurious UPDATE.
    private static bool IsUnchanged(FormQuestion existing, QuestionInput input) =>
        existing.Text == input.Text.Trim()
        && existing.Type == input.Type
        && existing.Order == input.Order
        && existing.IsRequired == input.IsRequired
        && existing.AiGenerated == input.AiGenerated
        && existing.Points == input.Points
        && (existing.CorrectAnswer ?? string.Empty) == (input.CorrectAnswer?.Trim() ?? string.Empty);

    // IsCorrect is coalesced because QuestionOption.IsCorrect is bool? while
    // OptionInput.IsCorrect is bool — without it every legacy null row reports as changed.
    private static bool IsUnchanged(QuestionOption existing, OptionInput input) =>
        existing.Text == input.Text.Trim()
        && existing.Order == input.Order
        && (existing.IsCorrect ?? false) == input.IsCorrect;
}
```

Notes on the shape:

- The two loops are near-identical by design; each is short enough to read at a glance and
  neither has to explain itself through type parameters.
- Both methods materialise their lists before returning, so the caller may safely mutate
  `form.Questions` / `question.Options` while walking the diff.
- `existing.ToDictionary(...)` assumes ids are unique within a form — they are, they're PKs.

## Part B — Domain mutators

The entities currently expose **only** `Clear()+AddRange()` replace methods; there is no way
to update one question in place. Add the minimum needed.

### B1. `src/FormAI.Domain/Entities/QuestionOption.cs`

```csharp
public void Update(string text, int order, bool? isCorrect)
{
    Text = text;
    Order = order;
    IsCorrect = isCorrect;
}
```

### B2. `src/FormAI.Domain/Entities/FormQuestion.cs`

```csharp
public void Update(string text, QuestionType type, int order, bool isRequired,
    bool aiGenerated, int? points, string? correctAnswer)
{
    Text = text;
    Type = type;
    Order = order;
    IsRequired = isRequired;
    AiGenerated = aiGenerated;
    Points = points;
    CorrectAnswer = correctAnswer;
}

public void AddOption(QuestionOption option) => Options.Add(option);

public void RemoveOption(QuestionOption option) => Options.Remove(option);
```

Keep `SetOptions` — `GenerateFormHandler.cs:78-82` and `UpdateQuestionsHandler` still use it.

### B3. `src/FormAI.Domain/Entities/Form.cs`

```csharp
public void AddQuestion(FormQuestion question) => Questions.Add(question);

public void RemoveQuestion(FormQuestion question) => Questions.Remove(question);
```

Keep `ReplaceQuestions` for the same reason.

---

## Part C — Carry ids on the wire

### C1. `src/FormAI.Application/Forms/SaveFormEditor/SaveFormEditorRequest.cs`

Add `Guid? Id` as the **first** positional member of both input records, and drop the
misleading `using FormAI.Application.Forms.UpdateQuestions;` at the top of the file (that
namespace declares its own colliding `QuestionInput`/`OptionInput`; the local ones win, but
the using serves no purpose and invites confusion).

```csharp
public record QuestionInput(Guid? Id, string Text, QuestionType Type, int Order,
    bool IsRequired, bool AiGenerated, int? Points, string? CorrectAnswer,
    List<OptionInput> Options);

public record OptionInput(Guid? Id, string Text, int Order, bool IsCorrect);
```

`Guid?` (not `Guid`) so an absent `id` in the JSON binds to `null` and is unambiguously an
insert. System.Text.Json binds by name, so the change is backwards compatible with any
caller still posting the old body.

### C2. `frontend/src/api/forms.ts` — `saveFormEditor`

Stop stripping ids (leave `updateQuestions` below it untouched — out of scope):

```ts
questions: payload.questions.map((q, i) => ({
    id: q.id,
    text: q.text,
    type: q.type,
    order: i + 1,
    isRequired: q.isRequired,
    aiGenerated: q.aiGenerated,
    points: q.points,
    correctAnswer: q.correctAnswer,
    options: q.options.map((o, oi) => ({
        id: o.id,
        text: o.text,
        order: oi + 1,
        isCorrect: o.isCorrect
    }))
}))
```

`FormQuestion.id` / `FormOption.id` already exist in `frontend/src/types/form.ts` — no type
changes needed.

### C3. `frontend/src/pages/FormEditorPage.tsx` — refresh after save

**Required, not cosmetic.** New questions/options are minted client-side with
`crypto.randomUUID()` (`AddQuestionModal.tsx:22,66`, `CheckBoxList.tsx:28`,
`RadioButtonList.tsx:34`). The server does **not** adopt those client ids (see D2) — it
issues its own. So after a save that added anything, the client's local id no longer matches
the row; a second Save would insert the same question again.

In `handleSave`, replace the local `setForm(f => ...)` patch with a re-fetch:

```ts
await saveFormEditor(id, { title: trimmedTitle, description: trimmedDescription, isPublic, questions });

const refreshed = await getForm(id);
setForm(refreshed);
setQuestions(refreshed.questions);
setTitle(refreshed.title);
setDescription(refreshed.description ?? '');
setIsPublic(refreshed.isPublic);

showSuccess('Form saved.');
setState('ready');
```

`getForm` is already imported at `FormEditorPage.tsx:4`.

---

## Part D — Rewrite `SaveFormEditorHandler`

`src/FormAI.Application/Forms/SaveFormEditor/SaveFormEditorHandler.cs`.

Everything before `form.Update(...)` stays exactly as it is — the ownership check, the
title/description guards, and the `QuestionOptionValidator` loop (phase 13). Only the block
from `form.Update(...)` down changes.

The handler owns all the mutation. It reads the diff and decides what to do; the differ never
touches an entity.

### D1. Replace the rebuild block

```csharp
form.Update(request.Title.Trim(), request.Description?.Trim() ?? string.Empty, request.IsPublic,
    form.ExpiresAt, form.ShowResultsAfterSubmit);

var diff = FormEditorDiffer.DiffQuestions(form.Questions, request.Questions);

// Removals first, so a payload that drops one question and adds another stays consistent.
foreach (var question in diff.Removed)
    form.RemoveQuestion(question);

foreach (var (question, input) in diff.Modified)
    question.Update(input.Text.Trim(), input.Type, input.Order, input.IsRequired,
        input.AiGenerated, input.Points, input.CorrectAnswer?.Trim());

foreach (var input in diff.Added)
    form.AddQuestion(BuildQuestion(form.Id, input));

// Options are reconciled for every question that survived, changed or not: a question whose
// own fields are untouched can still have had an option renamed or reordered. Newly added
// questions already got their options in BuildQuestion.
foreach (var (question, input) in diff.Surviving)
    SyncOptions(question, input);

await _forms.UpdateAsync(form, cancellationToken);
```

### D2. Private helpers in the same file

```csharp
private static FormQuestion BuildQuestion(Guid formId, QuestionInput input)
{
    // Deliberately ignores input.Id: ids for new rows are minted server-side so a client
    // cannot hand us an id belonging to another form.
    var question = FormQuestion.Create(formId, input.Text.Trim(), input.Type, input.Order,
        input.IsRequired, input.AiGenerated, input.Points, input.CorrectAnswer?.Trim());

    question.SetOptions(input.Options
        .Select(o => QuestionOption.Create(question.Id, o.Text.Trim(), o.Order, o.IsCorrect))
        .ToList());

    return question;
}

private static void SyncOptions(FormQuestion question, QuestionInput input)
{
    var diff = FormEditorDiffer.DiffOptions(question.Options, input.Options);

    foreach (var option in diff.Removed)
        question.RemoveOption(option);

    foreach (var (option, o) in diff.Modified)
        option.Update(o.Text.Trim(), o.Order, o.IsCorrect);

    foreach (var o in diff.Added)
        question.AddOption(QuestionOption.Create(question.Id, o.Text.Trim(), o.Order, o.IsCorrect));
}
```

Two things the loops rely on:

- `diff.Modified` is walked but `diff.Unchanged` is **not** — that is what makes a no-op save
  emit no SQL at all.
- The trimming here must match the normalising in `FormEditorDiffer.IsUnchanged` (A2). If the
  two ever drift, an untouched question would report as modified on every save. The unit
  tests in E1/E2 cover exactly that.

**A `Text`/`Numeric` question sends an empty `Options` list**, so its (nonexistent) options
diff to empty and nothing happens. If a question's type is switched from `Single` to `Text`,
its options arrive empty and are correctly removed.

### D3. What EF does with this

`FormRepository.GetByIdAsync` (`FormRepository.cs:17-23`) is a **tracked** query with
`.Include(f => f.Questions).ThenInclude(q => q.Options)`, and `UpdateAsync` is just
`SaveChangesAsync`. So:

- Untouched entities produce no SQL — the change tracker sees identical values.
- `Update(...)` on a matched entity produces a targeted `UPDATE` of only the changed columns.
- `RemoveQuestion` / `RemoveOption` sever a required cascade relationship, which EF turns
  into a `DELETE` for that row only.
- `AddQuestion` produces an `INSERT`; `ValueGenerated.Never` on all PKs
  (`AppDbContext.cs:26-32`) means the factory-minted Guid is what lands in the database.

No repository, DI, controller or migration changes are needed.

---

## Part E — Tests

`tests/FormAI.UnitTests` currently contains no test files. Stack is bare xUnit — **no Moq,
no NSubstitute, no FluentAssertions** — so use plain `Assert` and a hand-written fake, in
the `Method_ShouldExpectedBehavior` naming style used by
`tests/FormAI.IntegrationTests/UserRepositoryTests.cs`.

### E1. `tests/FormAI.UnitTests/Forms/FormEditorDifferTests.cs`

Straight coverage of `DiffQuestions` / `DiffOptions` against seed entities built with the real
domain factories. The differ mutates nothing, so these tests need no fakes at all.

`DiffQuestions`:

- all-new incoming (`Id = null`) → everything in `Added`, `Removed` empty
- payload identical to what is stored → everything in `Unchanged`, `HasChanges` false
- one question renamed → that one in `Modified`, the rest in `Unchanged`
- text differing only by surrounding whitespace → `Unchanged` (normalisation)
- one question reordered → `Modified` carrying the new `Order`
- an existing question missing from the payload → `Removed`
- an `Id` no question has → `Added`, not an error
- the same `Id` twice in the payload → first match classified normally, second `Added`
- empty existing / empty incoming
- `Surviving` returns `Modified` + `Unchanged` and nothing else

`DiffOptions`: the same cases in miniature, plus a stored `IsCorrect = null` against an
incoming `false` → `Unchanged` (the coalesce).

### E2. `tests/FormAI.UnitTests/Forms/SaveFormEditorHandlerTests.cs`

Fake `IFormRepository` holding one in-memory `Form` (`GetByIdAsync` returns it,
`UpdateAsync` is a no-op that records it was called). Build the seed `Form` through the real
domain factories. Assert against the resulting `form.Questions`:

- saving an unmodified payload leaves every `FormQuestion.Id` and `QuestionOption.Id` identical
- renaming a question keeps its id and updates `Text`
- renaming an option keeps its id
- reordering questions keeps ids and only changes `Order`
- a payload item with `Id = null` is added, with a **server-minted** id (not the client's)
- omitting an existing question removes it, and leaves the others' ids alone
- switching `Single` → `Text` empties `Options`

### E3. Optional integration test

If you want end-to-end proof that answers survive, add one Testcontainers test alongside
`UserRepositoryTests.cs` that seeds a form + a submission with answers, runs
`SaveFormEditorHandler` against the real `FormRepository`, and asserts the `answers` rows
are still present. Valuable but not required to ship this phase.

---

## Verification

```bash
dotnet build FormAI.sln
dotnet test FormAI.sln
cd frontend && npm run build
```

Then, against a running API (`dotnet run --project src/FormAI.API`) and frontend:

1. **No-op save.** Note the ids from `GET /api/forms/{id}`. Open the editor, press Save with
   no edits. Re-fetch — every question and option id must be byte-identical. Watch the API
   log with EF SQL logging on: the save should emit **no** `INSERT`/`DELETE` against
   `form_questions` or `question_options`.
2. **The bug this phase exists to fix.** Publish a form, answer it from an incognito window,
   then as the owner rename a question and Save.
   ```sql
   select count(*) from answers;           -- unchanged before and after the save
   select option_text from answer_selected_options;
   ```
   Both must survive. (Before this change, `answers` drops to 0.)
3. **Rename an option.** Its `question_options.id` is unchanged; the historical
   `answer_selected_options.option_text` still shows the old label (phase 13 behaviour).
4. **Reorder by drag-and-drop, then Save.** Only `order` changes; no ids move.
5. **Add a question, Save, then Save again immediately.** The question must appear **once**,
   not twice — this exercises the C3 re-fetch.
6. **Delete a question, Save.** That question and its options disappear; every other
   question keeps its id; that question's answers are cascade-deleted (expected) and other
   questions' answers survive.
7. **Change a question's type from Single to Text, Save.** Its options are removed; the
   question keeps its id.

## Files touched

| File | Change |
|---|---|
| `src/FormAI.Application/Forms/SaveFormEditor/FormEditorDiff.cs` | **New** — `QuestionsDiff` / `OptionsDiff` result types |
| `src/FormAI.Application/Forms/SaveFormEditor/FormEditorDiffer.cs` | **New** — EF-free `DiffQuestions` / `DiffOptions` |
| `src/FormAI.Domain/Entities/Form.cs` | `AddQuestion` / `RemoveQuestion` |
| `src/FormAI.Domain/Entities/FormQuestion.cs` | `Update` / `AddOption` / `RemoveOption` |
| `src/FormAI.Domain/Entities/QuestionOption.cs` | `Update` |
| `src/FormAI.Application/Forms/SaveFormEditor/SaveFormEditorRequest.cs` | `Guid? Id` on both input records; drop stray `using` |
| `src/FormAI.Application/Forms/SaveFormEditor/SaveFormEditorHandler.cs` | Reconcile instead of `ReplaceQuestions` |
| `frontend/src/api/forms.ts` | `saveFormEditor` sends ids |
| `frontend/src/pages/FormEditorPage.tsx` | Re-fetch the form after a successful save |
| `tests/FormAI.UnitTests/Forms/FormEditorDifferTests.cs` | **New** |
| `tests/FormAI.UnitTests/Forms/SaveFormEditorHandlerTests.cs` | **New** |
| `docs/plans/phase-14-incremental-form-save.md` | **New** — copy of this plan (per `CLAUDE.md`) |

## Known follow-ups (deliberately not in this phase)

- `UpdateQuestionsHandler` (`PUT /forms/{id}/questions`) still calls `ReplaceQuestions` and
  still destroys answers. It appears unused by the frontend — `FormEditorPage` imports
  `updateQuestions` but never calls it. Fix or delete it in a later phase. If it is fixed
  rather than deleted, that is the moment to judge whether the two diff loops in Part A are
  worth generalising into a shared utility — not before.
- `SaveFormEditorHandler`'s title/description guards throw `ArgumentException`, which the
  middleware maps to `500` rather than `400` (noted in phase 13, still true).
