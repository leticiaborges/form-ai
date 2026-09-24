
using FormAI.Application.Forms.Validation;
using FormAI.Application.Interfaces;

namespace FormAI.Application.Forms.GetSubmissionCount;

public class GetSubmissionCountHandler
{
    private readonly IFormRepository _formRepository;
    private readonly ISubmissionRepository _submissionRepository;

    public GetSubmissionCountHandler(IFormRepository formRepository,
    ISubmissionRepository submissionRepository)
    {
        _formRepository = formRepository;
        _submissionRepository = submissionRepository;
    }

    public async Task<GetSubmissionCountResponse> HandleAsync
    (GetSubmissionCountRequest request, CancellationToken cancellationToken)
    {
        var form = await _formRepository.GetByIdAsync(request.FormId,
        cancellationToken);

        FormAccessValidator.CheckOwnerAccess(form, request.RequestingUserId);

        var count = await _submissionRepository.CountByFormAsync(request.FormId,
        cancellationToken);

        return new GetSubmissionCountResponse(count);
    }

}