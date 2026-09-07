# Phase 18 — The results endpoint

## Context

Phase 17 left a placeholder Results tab. This phase builds everything behind it, in one go, so that
**phases 19–23 touch no backend code at all**: the DTO shipped here already carries the graded fields
that phase 22 and 23 will read.

`docs/known-gaps.md` lists "Aggregated results for the owner" as not built. Today the owner can learn
only how many submissions a form has (`GET /api/forms/{id}/submissions/count`). This phase adds the
real thing.

### Vocabulary (new, added to `CONTEXT.md` in step 8)

- **Results** — the read-only aggregate of every submission to one form, seen only by the owner.
- **Answer distribution** — for one question, how its answers were spread: how many respondents picked
  each option, or how many gave each distinct text or number.
- **Score distribution** — for a graded form, how many submissions earned each score.

Never *response*, *analytics*, *statistics* or *report*.

### Two findings that shape this phase

**1. `Answer.Score` cannot tell you whether an answer was correct.**
`SubmitFormHandler` stores `Score = correct ? (question.Points ?? 0) : 0`. So a question worth **0
points** — which `CLAUDE.md` explicitly allows and calls meaningful ("a question that is part of a
graded form but does not count") — stores `0` whether the respondent was right or wrong. So does a
question with no answer key. **Correctness must be recomputed, not read.**

The fix is to make `SubmissionScorer`'s existing private `IsCorrect` public as `IsAnswerCorrect`.
One definition of correctness, shared by submitting, rescoring and results. Reimplementing the
comparison here would give the codebase two definitions of "the same text" that drift the first time
someone touches trimming.

**2. A skipped question has no `Answer` row at all.**
`SubmitFormHandler` does `if (!isAnswered) continue;`, so it writes no `Answer` for a question the
respondent left blank. That makes the denominator below a plain row count.

### The denominator, and why it is worth a comment in the code

**Counts and percentages denominate by *answers to that question*, not by submissions.** A form with
6 submissions where 2 people skipped question 3 shows `4 answers` on that card, and `1/4 correct`.

This **deliberately disagrees with the scorer**, which treats an unanswered question as `0` — that is,
as wrong. Both readings are defensible; a question card is about that question, so the question's own
answers are its population. A future reader *will* see `1/4 correct` on a 6-submission form and
suspect a bug, so `FormResultsCalculator` carries a comment saying this is a choice, not an oversight.

Consequence to keep in mind while building phases 22–23: **a green bar and a zero correct-count are
not a contradiction.** On a Multiple question, an option bar is green because that option is in the
answer key; the correct-count counts respondents whose *whole selection* matched the key exactly
(`SubmissionScorer` is all-or-nothing). Everyone can have picked a green option and nobody can be
correct.

**Scope: backend only.** Nothing under `frontend/` changes.

---

## Files

| File | Change |
|---|---|
| `src/FormAI.Domain/Scoring/SubmissionScorer.cs` | `IsCorrect` → public `IsAnswerCorrect` |
| `src/FormAI.Domain/Scoring/ScoredAnswer.cs` | add `From(Answer)` factory |
| `src/FormAI.Domain/Results/FormResults.cs` | **new** — the pure result model |
| `src/FormAI.Domain/Results/FormResultsCalculator.cs` | **new** — the pure aggregation |
| `src/FormAI.Application/Interfaces/ISubmissionRepository.cs` | add `GetByFormWithAnswersAsync` |
| `src/FormAI.Infrastructure/Repositories/SubmissionRepository.cs` | implement it |
| `src/FormAI.Application/Forms/GetFormResults/GetFormResultsRequest.cs` | **new** |
| `src/FormAI.Application/Forms/GetFormResults/GetFormResultsResponse.cs` | **new** |
| `src/FormAI.Application/Forms/GetFormResults/GetFormResultsHandler.cs` | **new** |
| `src/FormAI.API/Controllers/FormsController.cs` | add `GET {id}/results` |
| `src/FormAI.Infrastructure/DependencyInjection.cs` | register the handler |
| `src/FormAI.Application/Submissions/RescoreForm/RescoreFormSubmissionsHandler.cs` | use the new factory |
| `tests/FormAI.UnitTests/Results/FormResultsCalculatorTests.cs` | **new** — first tests in the project |
| `CONTEXT.md`, `CLAUDE.md`, `docs/known-gaps.md` | doc updates |

---

## Step 1 — `SubmissionScorer.IsAnswerCorrect` goes public

In `src/FormAI.Domain/Scoring/SubmissionScorer.cs`, change the signature of the private `IsCorrect`:

```csharp
    /// <summary>
    /// Whether an answer matches the question's answer key or suggested answer, independent of
    /// what the question is worth. Public because the results view needs correctness on its own:
    /// a question worth 0 points stores a score of 0 whether the answer was right or wrong, so
    /// <c>Answer.Score</c> cannot be read as "was this correct".
    /// </summary>
    public static bool IsAnswerCorrect(FormQuestion question, ScoredAnswer answer) => question.Type switch
```

Update the one existing call site, inside `ScoreAnswer`:

```csharp
        return IsAnswerCorrect(question, answer) ? question.Points ?? 0 : 0;
```

Nothing else in the file changes. `IsSelectionCorrect`, `IsTextCorrect` and `IsNumericCorrect` stay
private.

---

## Step 2 — `ScoredAnswer.From(Answer)`

`RescoreFormSubmissionsHandler` has a private `ToScoredAnswer(Answer)`. The calculator needs the same
mapping. Lift it onto the record so there is one of it.

Append to `src/FormAI.Domain/Scoring/ScoredAnswer.cs`, inside the record body:

```csharp
    /// <summary>
    /// A stored answer in the shape the scorer reads. Selected options come back as the text
    /// recorded at submission time, never as option ids — see ADR 0001.
    /// </summary>
    public static ScoredAnswer From(Entities.Answer answer) =>
        new(answer.TextValue, answer.NumericValue,
            answer.SelectedOptions.Select(o => o.OptionText).ToList());
```

Then in `RescoreFormSubmissionsHandler.cs`, delete the private `ToScoredAnswer` method at the bottom
of the file and change its one call site:

```csharp
            var scoredAnswer = ScoredAnswer.From(answer);
```

Behaviour is identical; this just stops the mapping existing twice.

---

## Step 3 — `FormAI.Domain/Results/FormResults.cs` (new)

The result model. It holds the `FormQuestion` entity rather than copying its text and type, so the
Application layer decides what to expose and the Domain stays free of DTO concerns.

Create `src/FormAI.Domain/Results/FormResults.cs`:

```csharp
using FormAI.Domain.Entities;

namespace FormAI.Domain.Results;

/// <summary>
/// Every submission of one form, aggregated. Graded-only figures are null on an ungraded form,
/// following the same rule as the rest of the domain: null means "this form is not graded",
/// 0 means "graded, earned nothing".
/// </summary>
public record FormResults(
    int SubmissionCount,
    int? TotalPoints,
    IReadOnlyList<ScoreBucket> ScoreDistribution,
    IReadOnlyList<QuestionResults> Questions
);

/// <summary>How many submissions earned exactly this score. Only achieved scores get a bucket.</summary>
public record ScoreBucket(int Score, int SubmissionCount);

/// <summary>
/// One question's answer distribution. <paramref name="AnswerCount"/> counts the answers to this
/// question, not the form's submissions — a skipped question has no answer row.
/// <paramref name="CorrectAnswerCount"/> is null on an ungraded form.
/// Exactly one of <paramref name="Options"/> / <paramref name="Values"/> is populated, decided by
/// the question type.
/// </summary>
public record QuestionResults(
    FormQuestion Question,
    int AnswerCount,
    int? CorrectAnswerCount,
    IReadOnlyList<OptionResults> Options,
    IReadOnlyList<ValueResults> Values
);

/// <summary>
/// How many respondents picked one option. <paramref name="IsCorrect"/> mirrors the answer key and
/// is null on an ungraded form, on an unmarked option, and on an option that no longer exists.
/// </summary>
public record OptionResults(string Text, int Count, bool? IsCorrect);

/// <summary>How many respondents gave one distinct text or number.</summary>
public record ValueResults(string Value, int Count, bool? IsCorrect);
```

---

## Step 4 — `FormAI.Domain/Results/FormResultsCalculator.cs` (new)

A pure function over entities: no EF, no DTOs, no async. That is what makes it unit-testable in
step 7, and it sits beside `FormAI.Domain/Scoring/` for the same reason.

Create `src/FormAI.Domain/Results/FormResultsCalculator.cs`:

```csharp
using System.Globalization;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using FormAI.Domain.Scoring;

namespace FormAI.Domain.Results;

/// <summary>
/// Aggregates a form's submissions into what the owner sees on the Results tab.
///
/// Two rules are worth stating because neither is obvious from the numbers on screen:
///
/// 1. Counts denominate by the answers to a question, not by the form's submissions. A question
///    two respondents skipped reports fewer answers than the form has submissions, so "1/4 correct"
///    on a form with 6 submissions is correct, not a bug. This deliberately differs from
///    <see cref="SubmissionScorer"/>, which scores an unanswered question as 0 — that is, as wrong.
///
/// 2. An option marked correct and a correct-answer count of zero are not a contradiction. An
///    option is correct because it is in the answer key; an answer is correct only when the whole
///    selection matches the key, which is all-or-nothing.
/// </summary>
public static class FormResultsCalculator
{
    /// <summary>Option text is unique per question when trimmed and compared case-insensitively.</summary>
    private static readonly StringComparer TextComparer = StringComparer.OrdinalIgnoreCase;

    public static FormResults Calculate(Form form, IReadOnlyList<Submission> submissions)
    {
        var answersByQuestion = submissions
            .SelectMany(s => s.Answers)
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Answer>)g.ToList());

        var questions = form.Questions
            .OrderBy(q => q.Order)
            .Select(question => BuildQuestionResults(form, question,
                answersByQuestion.TryGetValue(question.Id, out var found)
                    ? found
                    : Array.Empty<Answer>()))
            .ToList();

        return new FormResults(
            submissions.Count,
            form.IsGraded ? form.Questions.Sum(q => q.Points ?? 0) : null,
            form.IsGraded ? BuildScoreDistribution(submissions) : Array.Empty<ScoreBucket>(),
            questions);
    }

    /// <summary>
    /// One bar per score somebody actually earned, ascending. A submission with no score is left
    /// out: null means the form is not graded, and on a graded form a rescore fills it in.
    /// </summary>
    private static IReadOnlyList<ScoreBucket> BuildScoreDistribution(IReadOnlyList<Submission> submissions) =>
        submissions
            .Where(s => s.Score.HasValue)
            .GroupBy(s => s.Score!.Value)
            .OrderBy(g => g.Key)
            .Select(g => new ScoreBucket(g.Key, g.Count()))
            .ToList();

    private static QuestionResults BuildQuestionResults(Form form, FormQuestion question,
        IReadOnlyList<Answer> answers)
    {
        int? correctCount = form.IsGraded
            ? answers.Count(a => SubmissionScorer.IsAnswerCorrect(question, ScoredAnswer.From(a)))
            : null;

        var isSelection = question.Type is QuestionType.Single or QuestionType.Multiple;

        return new QuestionResults(
            question,
            answers.Count,
            correctCount,
            isSelection ? BuildOptionResults(form, question, answers) : Array.Empty<OptionResults>(),
            isSelection ? Array.Empty<ValueResults>() : BuildValueResults(form, question, answers));
    }

    /// <summary>
    /// The form's current options first, in editor order, then any option text that appears in a
    /// submission but is no longer an option on the question — because answers record option text,
    /// not option ids (ADR 0001), a rename or a deletion leaves those behind. They are shown rather
    /// than dropped so the counts add up.
    /// </summary>
    private static IReadOnlyList<OptionResults> BuildOptionResults(Form form, FormQuestion question,
        IReadOnlyList<Answer> answers)
    {
        var counts = GroupTexts(answers.SelectMany(a => a.SelectedOptions).Select(o => o.OptionText));

        var results = new List<OptionResults>();

        foreach (var option in question.Options.OrderBy(o => o.Order))
        {
            var key = option.Text.Trim();
            counts.Remove(key, out var found);

            results.Add(new OptionResults(option.Text, found.Count,
                form.IsGraded ? option.IsCorrect : null));
        }

        // Whatever is left matched no current option. It can never be marked correct, because
        // there is no option left carrying an answer key.
        results.AddRange(counts.Values
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Display, TextComparer)
            .Select(v => new OptionResults(v.Display, v.Count, null)));

        return results;
    }

    /// <summary>
    /// One entry per distinct answer, most given first. Text is grouped trimmed and
    /// case-insensitively and numbers by their value, matching how <see cref="SubmissionScorer"/>
    /// compares them — otherwise the list could show two entries where the scorer counted one
    /// correct answer.
    /// </summary>
    private static IReadOnlyList<ValueResults> BuildValueResults(Form form, FormQuestion question,
        IReadOnlyList<Answer> answers)
    {
        var texts = question.Type == QuestionType.Numeric
            ? answers.Where(a => a.NumericValue.HasValue)
                     .Select(a => a.NumericValue!.Value.ToString(CultureInfo.InvariantCulture))
            : answers.Where(a => !string.IsNullOrWhiteSpace(a.TextValue))
                     .Select(a => a.TextValue!);

        return GroupTexts(texts).Values
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Display, TextComparer)
            .Select(v => new ValueResults(v.Display, v.Count,
                form.IsGraded ? IsValueCorrect(question, v.Display) : null))
            .ToList();
    }

    private static bool IsValueCorrect(FormQuestion question, string value)
    {
        var answer = question.Type == QuestionType.Numeric
            ? new ScoredAnswer(null, double.Parse(value, CultureInfo.InvariantCulture), Array.Empty<string>())
            : new ScoredAnswer(value, null, Array.Empty<string>());

        return SubmissionScorer.IsAnswerCorrect(question, answer);
    }

    /// <summary>
    /// Groups texts trimmed and case-insensitively, keeping the casing of the first one seen so the
    /// owner reads back something a respondent actually typed.
    /// </summary>
    private static Dictionary<string, (string Display, int Count)> GroupTexts(IEnumerable<string> texts)
    {
        var grouped = new Dictionary<string, (string Display, int Count)>(TextComparer);

        foreach (var raw in texts)
        {
            var key = raw.Trim();

            grouped[key] = grouped.TryGetValue(key, out var existing)
                ? (existing.Display, existing.Count + 1)
                : (key, 1);
        }

        return grouped;
    }
}
```

`double.Parse` in `IsValueCorrect` is safe: the string it parses was produced two lines earlier by
`double.ToString(InvariantCulture)`, so it round-trips. Numeric grouping is by that invariant string,
which means `5` and `5.0` land in the same bucket — the same normalisation the scorer applies.

---

## Step 5 — Reading the submissions

`GetByFormForScoringAsync` already loads the right graph, but its docstring says it returns **tracked**
entities on purpose so a rescore can be committed by the caller. Results is a pure read, so it gets
its own method rather than paying change-tracking for nothing and overloading a name that says
"scoring".

**5a.** In `src/FormAI.Application/Interfaces/ISubmissionRepository.cs`, add:

```csharp
        /// <summary>
        /// Every submission of a form with its answers and selected options, untracked, for the
        /// owner's results view. Separate from <see cref="GetByFormForScoringAsync"/>, which
        /// deliberately returns tracked entities so a rescore can be committed by its caller.
        /// </summary>
        Task<IReadOnlyList<Submission>> GetByFormWithAnswersAsync(Guid formId,
            CancellationToken cancellationToken = default);
```

**5b.** In `src/FormAI.Infrastructure/Repositories/SubmissionRepository.cs`, add:

```csharp
    public async Task<IReadOnlyList<Submission>> GetByFormWithAnswersAsync(Guid formId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Submissions
            .AsNoTracking()
            .AsSplitQuery()
            .Include(s => s.Answers)
                .ThenInclude(a => a.SelectedOptions)
            .Where(s => s.FormId == formId)
            .ToListAsync(cancellationToken);
    }
```

`AsSplitQuery()` avoids the cartesian product of submissions x answers x selected options, which on a
form with many submissions would otherwise return the same submission row once per selected option.
This read has the same unbounded shape `docs/known-gaps.md` already flags for rescoring; keeping it a
separate, clearly-named method means there will be two obvious call sites to fix when that day comes.

---

## Step 6 — The use case

**6a.** Create `src/FormAI.Application/Forms/GetFormResults/GetFormResultsRequest.cs`:

```csharp
namespace FormAI.Application.Forms.GetFormResults;

public record GetFormResultsRequest(Guid FormId, Guid RequestingUserId);
```

**6b.** Create `src/FormAI.Application/Forms/GetFormResults/GetFormResultsResponse.cs`:

```csharp
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GetFormResults;

/// <summary>
/// One response for both graded and ungraded forms. Everything that exists only on a graded form is
/// nullable and comes back null otherwise, which is how the rest of the codebase already says
/// "not graded" (Submission.Score, Answer.Score, FormQuestion.Points).
/// </summary>
public record GetFormResultsResponse(
    Guid FormId,
    string Title,
    bool IsGraded,
    int SubmissionCount,
    int? TotalPoints,
    List<ScoreBucketResponse> ScoreDistribution,
    List<QuestionResultResponse> Questions
);

public record ScoreBucketResponse(int Score, int SubmissionCount);

public record QuestionResultResponse(
    Guid QuestionId,
    string Text,
    QuestionType Type,
    int Order,
    int AnswerCount,
    int? Points,
    int? CorrectAnswerCount,
    List<OptionResultResponse> Options,
    List<ValueResultResponse> Values
);

public record OptionResultResponse(string Text, int Count, bool? IsCorrect);

public record ValueResultResponse(string Value, int Count, bool? IsCorrect);
```

**6c.** Create `src/FormAI.Application/Forms/GetFormResults/GetFormResultsHandler.cs`:

```csharp
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Results;

namespace FormAI.Application.Forms.GetFormResults;

/// <summary>
/// The owner's view of everything that has been submitted to one form. Owner-only, and for the same
/// reason as <c>GetFormHandler</c>: it carries the answer key.
/// </summary>
public class GetFormResultsHandler
{
    private readonly IFormRepository _forms;
    private readonly ISubmissionRepository _submissions;

    public GetFormResultsHandler(IFormRepository forms, ISubmissionRepository submissions)
    {
        _forms = forms;
        _submissions = submissions;
    }

    public async Task<GetFormResultsResponse> HandleAsync(GetFormResultsRequest request,
        CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId, cancellationToken);
        if (form is null)
            throw new NotFoundException("Form not found.");

        // Same two-step check as GetFormHandler: a private form is hidden from everyone but its
        // owner, and a published form's results still belong to the owner alone.
        if (!form.IsPublic && form.CreatedBy != request.RequestingUserId)
            throw new NotFoundException("You don't have access to this form.");

        if (form.CreatedBy != request.RequestingUserId)
            throw new ForbiddenException("You do not own this form.");

        var submissions = await _submissions.GetByFormWithAnswersAsync(request.FormId, cancellationToken);
        var results = FormResultsCalculator.Calculate(form, submissions);

        return new GetFormResultsResponse(
            form.Id,
            form.Title,
            form.IsGraded,
            results.SubmissionCount,
            results.TotalPoints,
            results.ScoreDistribution
                .Select(b => new ScoreBucketResponse(b.Score, b.SubmissionCount))
                .ToList(),
            results.Questions.Select(q => new QuestionResultResponse(
                q.Question.Id,
                q.Question.Text,
                q.Question.Type,
                q.Question.Order,
                q.AnswerCount,
                q.Question.Points,
                q.CorrectAnswerCount,
                q.Options.Select(o => new OptionResultResponse(o.Text, o.Count, o.IsCorrect)).ToList(),
                q.Values.Select(v => new ValueResultResponse(v.Value, v.Count, v.IsCorrect)).ToList()
            )).ToList());
    }
}
```

Note for later: `GetSubmissionCountHandler` throws `ForbiddenException` even for a private form owned
by someone else, which leaks that the form exists. That is a pre-existing inconsistency with
`GetFormHandler`; **do not fix it in this phase** — just don't copy it.

---

## Step 7 — Wiring

**7a.** `src/FormAI.Infrastructure/DependencyInjection.cs` — add the using and the registration next
to `GetSubmissionCountHandler`:

```csharp
using FormAI.Application.Forms.GetFormResults;
```

```csharp
        services.AddScoped<GetFormResultsHandler>();
```

**7b.** `src/FormAI.API/Controllers/FormsController.cs` — add the using, a field, a constructor
parameter, and the action beside `GetSubmissionCount`:

```csharp
using FormAI.Application.Forms.GetFormResults;
```

```csharp
    private readonly GetFormResultsHandler _getFormResults;
```

```csharp
    // GET /api/forms/{id}/results
    // Owner-only: results carry the answer key, exactly like GET /api/forms/{id}.
    [HttpGet("{id:guid}/results")]
    public async Task<IActionResult> GetResults(Guid id, CancellationToken cancellationToken)
    {
        var response = await _getFormResults.HandleAsync(
            new GetFormResultsRequest(id, CurrentUserId), cancellationToken);

        return Ok(response);
    }
```

The controller is `[Authorize]` at class level and this action is not `[AllowAnonymous]`, so a token
is required. Leave `GET {id}/submissions/count` alone — the delete-confirmation modal uses it and it
costs one `COUNT`.

---

## Step 8 — Tests

`tests/FormAI.UnitTests` is empty today and already references Domain, Application and xUnit 2.9.2 in
its `.csproj` — no package changes needed. `FormResultsCalculator` is a pure function over entities
built by public `Create` factories, so it needs no mocks.

Create `tests/FormAI.UnitTests/Results/FormResultsCalculatorTests.cs`. Build a small local helper
first (entities have private setters, so everything goes through the factories, exactly as
`SubmitFormHandler` does it):

```csharp
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using FormAI.Domain.Results;

namespace FormAI.UnitTests.Results;

public class FormResultsCalculatorTests
{
    private static Form NewForm(bool isGraded) =>
        Form.Create("Quiz", "", Guid.NewGuid(), SourceType.Text, true, null, false, isGraded);

    private static FormQuestion AddQuestion(Form form, QuestionType type, int order,
        int? points = null, string? correctAnswer = null, params (string Text, bool? IsCorrect)[] options)
    {
        var question = FormQuestion.Create(form.Id, $"Q{order}", type, order, false, false, points, correctAnswer);

        question.SetOptions(options
            .Select((o, i) => QuestionOption.Create(question.Id, o.Text, i + 1, o.IsCorrect))
            .ToList());

        form.AddQuestion(question);
        return question;
    }

    private static Submission NewSubmission(Form form, int? score, params Answer[] answers)
    {
        var submission = Submission.Create(form.Id, null, Guid.NewGuid(), "127.0.0.1", score);
        submission.SetAnswers(answers.ToList());
        return submission;
    }

    private static Answer Picked(Guid questionId, params string[] optionTexts)
    {
        var answer = Answer.Create(Guid.NewGuid(), questionId, null, null, null, null);

        answer.SetSelectedOptions(optionTexts
            .Select(t => AnswerSelectedOption.Create(answer.Id, t))
            .ToList());

        return answer;
    }

    private static Answer Wrote(Guid questionId, string text) =>
        Answer.Create(Guid.NewGuid(), questionId, null, text, null, null);

    private static Answer Entered(Guid questionId, double value) =>
        Answer.Create(Guid.NewGuid(), questionId, null, null, value, null);
}
```

Then write these tests. Each name states the rule it pins down:

| Test | What it proves |
|---|---|
| `UngradedForm_HasNoPointsNoScoresAndNoAnswerKey` | `TotalPoints` null, `ScoreDistribution` empty, every `CorrectAnswerCount` null, every `OptionResults.IsCorrect` null — even when the entities still carry an `IsCorrect` |
| `AnswerCount_CountsAnswersToTheQuestionNotSubmissions` | 3 submissions, one skips the question → `AnswerCount == 2`. **This is the denominator decision.** |
| `Options_KeepEditorOrderAndCountByText` | Live options come back in `Order` with their counts, including an option nobody picked (count 0) |
| `Options_NoLongerOnTheQuestionAppearAfterTheLiveOnes` | Rename an option after a submission → the old text is its own entry, after the live ones, ordered by count desc, with `IsCorrect` null |
| `TextValues_AreGroupedTrimmedAndCaseInsensitively` | `"paris"`, `"Paris"`, `" PARIS "` → one entry, count 3, displaying `"paris"` (first seen) |
| `NumericValues_AreGroupedByValue` | `5` and `5.0` → one entry with count 2 |
| `Values_AreOrderedByCountDescending` | Most-given answer first |
| `GradedForm_CountsCorrectAnswersOnAZeroPointQuestion` | **The `Answer.Score` finding.** A question worth 0 points, answered correctly → `CorrectAnswerCount == 1`. Reading `Answer.Score` would give 0 here |
| `GradedForm_PartialMultipleSelectionIsNotCorrect` | Key is A+B, respondent picked only A → `CorrectAnswerCount == 0` while option A is still `IsCorrect: true`. Green bar, zero correct — not a contradiction |
| `GradedForm_QuestionWithNoAnswerKeyEarnsNothing` | No option marked → `CorrectAnswerCount == 0` |
| `ScoreDistribution_HasOneBucketPerAchievedScoreAscending` | Scores 3, 7, 7, 12 → three buckets `(3,1) (7,2) (12,1)`, in that order; no empty buckets for 4, 5, 6 |
| `ScoreDistribution_IgnoresSubmissionsWithNoScore` | A null score is left out |
| `TotalPoints_IsTheSumOfEveryQuestion` | Includes questions worth 0 and questions with no answer key |
| `FormWithNoSubmissions_ReturnsEveryQuestionWithZeroAnswers` | The empty case the Results tab renders as "No submissions yet" |

Run them:

```bash
dotnet test FormAI.sln
```

---

## Step 9 — Docs

**9a. `CONTEXT.md`** — add a new `### Results` section after `### Responding`:

```markdown
### Results

**Results**:
The read-only aggregate of every submission to one form, seen only by the owner. Results show what
was answered, never who answered it.
_Avoid_: analytics, statistics, report, responses.

**Answer distribution**:
How one question's answers were spread: how many respondents picked each option, or how many gave
each distinct text or number. Counted over the answers to that question, so a question some
respondents skipped is measured against fewer answers than the form has submissions.

**Score distribution**:
How many submissions earned each score. Exists only for a graded form.
```

**9b. `CLAUDE.md`** — in the API surface table, add a row under `GET /api/forms/{id}/submissions/count`:

```markdown
| `GET /api/forms/{id}/results` | Owner-only; the form's answer distributions and, when graded, its score distribution |
```

And in "Grading and scoring", add:

```markdown
- **Correctness is recomputed, never read from a stored score.** `Answer.Score` is `Points` when
  right and 0 when wrong, so a question worth 0 points stores 0 either way. Results call
  `SubmissionScorer.IsAnswerCorrect` instead.
- Results count against the **answers to a question**, not the form's submissions. This differs from
  scoring, which treats an unanswered question as 0 — that is, as wrong.
```

**9c. `docs/known-gaps.md`** — the "Aggregated results for the owner" row is now half-true. Amend it to:

```markdown
| **Individual submissions for the owner** | `GET /api/forms/{id}/results` returns the aggregate — answer distributions and, when graded, the score distribution. There is still no endpoint returning the submissions themselves, so an owner cannot see what one respondent answered. |
```

And in the "Unit tests" row, replace "contains no test files" with a note that
`FormAI.UnitTests/Results/` now covers `FormResultsCalculator`, and that `FormAI.Domain/Scoring/`
remains untested.

---

## Verification

### 1. Build and test

```bash
dotnet build FormAI.sln
dotnet test FormAI.sln
```

### 2. Swagger

```bash
dotnet run --project src/FormAI.API      # http://localhost:5155/swagger
```

`GET /api/forms/{id}/results` appears. Authorise with a token and call it for a form you own.

### 3. Manual checks against real data

- **Ungraded form with submissions** → `isGraded: false`, `totalPoints: null`,
  `scoreDistribution: []`, every question's `correctAnswerCount: null`, every option's
  `isCorrect: null`.
- **Graded form** → `totalPoints` equals the sum of every question's points; `scoreDistribution` has
  one entry per distinct score.
- **A question somebody skipped** → its `answerCount` is lower than `submissionCount`. This is the
  denominator rule working, not a bug.
- **Rename an option** on a form that already has submissions, save, then call `/results` → the old
  text is present as its own entry after the live options, with `isCorrect: null`.
- **Form with no submissions** → `submissionCount: 0`, every question present with `answerCount: 0`.

### 4. Access checks

- Another user's **published** form → **403**.
- Another user's **private** form → **404** (its existence stays hidden).
- No token → **401**.
