using FormAI.Application.Common.Exceptions;
using FormAI.Application.Common.Pagination;
using FormAI.Application.Forms.Validation;
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

        FormAccessValidator.CheckOwnerAccess(form, request.RequestingUserId);

        var pageInfo = PageValidator.GetPageSize(request.Page, request.PageSize);
        var submissions = await _submissions.GetSubmissionListByFormAsync(request.FormId, pageInfo.Page,
        pageInfo.PageSize, cancellationToken);

        return new PagedResult<GetSubmissionListItemResponse>(
            submissions.Items.Select(a => new GetSubmissionListItemResponse(a.SubmissionId, a.SubmittedAt)).ToList(),
                pageInfo.Page, pageInfo.PageSize, submissions.TotalCount);
    }

}