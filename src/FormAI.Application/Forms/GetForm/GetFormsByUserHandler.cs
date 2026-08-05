using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using System.Linq;

namespace FormAI.Application.Forms.GetForm;

public class GetFormsByUserHandler
{
    private readonly IFormRepository _formRepository;

    public GetFormsByUserHandler(IFormRepository formRepository)
    {
        _formRepository = formRepository;
    }

    public async Task<List<GetFormSummaryResponse>> HandleAsync(Guid requestingUserId, CancellationToken cancellationToken)
    {
        var forms = await _formRepository.GetAllByUserIdAsync(requestingUserId, cancellationToken);
        return forms.Select(f =>
            new GetFormSummaryResponse(f.Id, f.Title, f.IsPublic, f.ExpiresAt, f.CreatedAt, f.Submissions.Count)).ToList();
    }
}