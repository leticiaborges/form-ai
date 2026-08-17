namespace FormAI.Application.Submissions.GetMySubmission;

public class GetMySubmissionHandler
{
    private readonly ISubmissionRepository _submissions;

    public GetMySubmissionHandler(ISubmissionRepository submissions)
    {
        _submissions = submissions;
    }

    public async Task<GetMySubmissionResponse> HandleAsync(GetMySubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        var submission = await _submissions.GetByRespondentAsync(
            request.FormId, request.RequestingUserId, request.RespondentToken, cancellationToken);

        return submission is null
            ? new GetMySubmissionResponse(false, null)
            : new GetMySubmissionResponse(true, submission.SubmittedAt);
    }
}