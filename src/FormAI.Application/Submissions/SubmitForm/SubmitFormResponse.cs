namespace FormAI.Application.Submissions.SubmitForm;

/// <summary>
/// <c>TotalScore</c> and <c>MaxScore</c> are both null unless the form is graded and its owner
/// chose to show the score after submitting; the score is stored either way.
/// </summary>
public record SubmitFormResponse(
    Guid Id,
    decimal? TotalScore,
    int? MaxScore
);
