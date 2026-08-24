using System.Security.AccessControl;
using System.Security.Claims;
using FormAI.Domain.Entities;

namespace FormAI.Application.Forms.SaveFormEditor;

public readonly record struct QuestionMatch(FormQuestion
Question, QuestionInput Input);

public readonly record struct OptionMatch(QuestionOption
Option, OptionInput Input);

public sealed record QuestionsDiff(
    IReadOnlyList<QuestionInput> Added,
    IReadOnlyList<QuestionMatch> Modified,
    IReadOnlyList<QuestionMatch> Unchanged,
    IReadOnlyList<FormQuestion> Removed)
{
    public IEnumerable<QuestionMatch> KeepExisting => Modified.Concat(Unchanged);

    public bool HasChanges => Added.Count > 0 || Modified.Count > 0 || Removed.Count > 0;
}

public sealed record OptionsDiff(
    IReadOnlyList<OptionInput> Added,
    IReadOnlyList<OptionMatch> Modified,
    IReadOnlyList<OptionMatch> Unchanged,
    IReadOnlyList<QuestionOption> Removed
)
{
    public bool HasChanges => Added.Count > 0 || Modified.Count > 0 || Removed.Count > 0;
}

public static class FormEditorDiffer
{
    public static QuestionsDiff DiffQuestions(
        IEnumerable<FormQuestion> existing,
        IEnumerable<QuestionInput> incoming
    )
    {
        var byId = existing.ToDictionary(q => q.Id);
        var alreadyMapped = new HashSet<Guid>();

        var added = new List<QuestionInput>();
        var modified = new List<QuestionMatch>();
        var unchanged = new List<QuestionMatch>();

        foreach (var input in incoming)
        {
            if (!byId.TryGetValue(input.Id, out var question) ||
            !alreadyMapped.Add(input.Id))
            {
                added.Add(input);
                continue;
            }

            if (IsUnchanged(question, input))
                unchanged.Add(new QuestionMatch(question, input));
            else
                modified.Add(new QuestionMatch(question, input));
        }

        var removed = byId.Values.Where(q => !alreadyMapped.Contains(q.Id)).ToList();

        return new QuestionsDiff(added, modified, unchanged, removed);
    }

    public static OptionsDiff DiffOptions(
        IEnumerable<QuestionOption> existing,
        IEnumerable<OptionInput> incoming)
    {
        var byId = existing.ToDictionary(o => o.Id);
        var alreadyMapped = new HashSet<Guid>();

        var added = new List<OptionInput>();
        var modified = new List<OptionMatch>();
        var unchanged = new List<OptionMatch>();

        foreach (var input in incoming)
        {
            if (!byId.TryGetValue(input.Id, out var option)
            || !alreadyMapped.Add(input.Id))
            {
                added.Add(input);
                continue;
            }

            if (IsUnchanged(option, input))
                unchanged.Add(new OptionMatch(option, input));
            else
                modified.Add(new OptionMatch(option, input));
        }

        var removed = byId.Values.Where(o => !alreadyMapped.Contains(o.Id)).ToList();

        return new OptionsDiff(added, modified, unchanged, removed);
    }

    private static bool IsUnchanged(FormQuestion existing,
    QuestionInput input)
    {
        return
        existing.Text == input.Text.Trim() &&
        existing.Type == input.Type &&
        existing.Order == input.Order &&
        existing.IsRequired == input.IsRequired &&
        existing.AiGenerated == input.AiGenerated &&
        existing.Points == input.Points &&
        (existing.CorrectAnswer ?? string.Empty) ==
        (input.CorrectAnswer?.Trim() ?? string.Empty);

    }

    private static bool IsUnchanged(QuestionOption existing, OptionInput input) =>
        existing.Text == input.Text.Trim()
        && existing.Order == input.Order
        && (existing.IsCorrect ?? false) == (input.IsCorrect ?? false);

}