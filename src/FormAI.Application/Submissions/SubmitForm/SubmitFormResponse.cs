namespace FormAI.Application.Submissions.SubmitForm;

public record SubmitFormResponse(
    Guid Id,
    decimal? TotalScore
);