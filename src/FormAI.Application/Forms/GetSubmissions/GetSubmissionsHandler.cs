using FormAI.Application.Common.Exceptions;
using FormAI.Application.Common.Pagination;
using FormAI.Application.Interfaces;


namespace FormAI.Application.Forms.GetSubmissions;

public class GetSubmissionsHandler
{
    private readonly IFormRepository _forms;
    private readonly ISubmissionRepository _submissions;

    public GetSubmissionsHandler(IFormRepository forms,
        ISubmissionRepository submissions)
    {
        _forms = forms;
        _submissions = submissions;
    }

    public async Task<PagedResult<GetSubmissionListItemResponse>> HandleAsync
    (GetSubmissionListItemRequest request,
    CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId,
        cancellationToken);

        if (form is null)
            throw new NotFoundException("Form not found");

        if (!form.IsPublic && form.CreatedBy != request.RequestingUserId)
            throw new NotFoundException("You don't have access to this form.");

        if (form.CreatedBy != request.RequestingUserId)
            throw new ForbiddenException("You do not own this form.");

        var pageInfo = PageValidator.GetPageSize(request.Page, request.PageSize);
        var submissions = await _submissions.GetSubmissionListByFormAsync(request.FormId, pageInfo.Page,
        pageInfo.PageSize, cancellationToken);

        return new PagedResult<GetSubmissionListItemResponse>(
            submissions.Items.Select(a => new GetSubmissionListItemResponse(a.SubmissionId, a.SubmittedAt)).ToList(),
                pageInfo.Page, pageInfo.PageSize, submissions.TotalCount);
    }

}