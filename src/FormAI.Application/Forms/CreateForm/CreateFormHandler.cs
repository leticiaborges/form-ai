using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.CreateForm;

public class CreateFormHandler
{
    private readonly IFormRepository _formRepository;

    public CreateFormHandler(IFormRepository formRepository)
    {
        _formRepository = formRepository;
    }

    public async Task<CreateFormResponse> HandleAsync(CreateFormRequest request,
      Guid createdByUserId, CancellationToken cancellationToken)
    {
        var form = Form.Create(request.Title, request.Description ?? string.Empty,
        createdByUserId, SourceType.Text, request.IsPublic, request.ExpiresAt,
        request.ShowResultsAfterSubmit);

        await _formRepository.AddAsync(form, cancellationToken);

        return new CreateFormResponse(form.Id, form.Title);
    }
}