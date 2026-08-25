
using FormAI.Application.Common.Exceptions;
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

        if (form == null)
            throw new NotFoundException("Form not found");

        if (form.CreatedBy != request.RequestingUserId)
            throw new ForbiddenException("You do not own this form.");

        var count = await _submissionRepository.CountByFormAsync(request.FormId,
        cancellationToken);

        return new GetSubmissionCountResponse(count);
    }

}