using FormAI.Application.Forms.CreateForm;
using FormAI.Application.Forms.DeleteForm;
using FormAI.Application.Forms.GenerateForm;
using FormAI.Application.Forms.GetForm;
using FormAI.Application.Forms.GetSubmissionCount;
using FormAI.Application.Forms.SaveFormEditor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Security.Claims;

namespace FormAI.API.Controllers;

[ApiController]
[Route("api/forms")]
[Authorize]
public class FormsController : ControllerBase
{
    private readonly CreateFormHandler _create;
    private readonly GetFormHandler _getById;
    private readonly GetFormsByUserHandler _getByUser;
    private readonly DeleteFormHandler _delete;
    private readonly GenerateFormHandler _generateForm;
    private readonly SaveFormEditorHandler _saveFormEditor;
    private readonly GetSubmissionCountHandler _getSubmissionCount;

    public FormsController(CreateFormHandler create,
    GetFormHandler getById, GetFormsByUserHandler getByUser,
    DeleteFormHandler delete,
    GenerateFormHandler generateForm,
    SaveFormEditorHandler saveFormEditor,
    GetSubmissionCountHandler submissionCount)
    {
        _create = create;
        _getById = getById;
        _getByUser = getByUser;
        _delete = delete;
        _generateForm = generateForm;
        _saveFormEditor = saveFormEditor;
        _getSubmissionCount = submissionCount;
    }

    [HttpPut("{id:guid}/editor")]
    public async Task<IActionResult> SaveEditor(Guid id,
    [FromBody] SaveFormEditorRequest request,
    CancellationToken cancellationToken)
    {
        var cmd = request with { FormId = id, RequestingUserId = CurrentUserId };
        await _saveFormEditor.HandleAsync(cmd, cancellationToken);
        return NoContent();
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException());


    // POST /api/forms
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFormRequest request, CancellationToken cancellationToken)
    {
        var response = await _create.HandleAsync(request, CurrentUserId, cancellationToken);
        return CreatedAtAction(nameof(Create), new { Id = response.Id }, response);
    }

    // GET /api/forms
    [HttpGet]
    public async Task<IActionResult> GetMyForms(CancellationToken cancellationToken)
    {
        var response = await _getByUser.HandleAsync(CurrentUserId, cancellationToken);
        return Ok(response);
    }

    // GET /api/forms/{id}
    // The editor's endpoint: it returns the answer key, so it is owner-only and never anonymous.
    // Respondents read a form through GET /api/forms/{id}/answer instead.
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var response = await _getById.HandleAsync(id, CurrentUserId, cancellationToken);
        return Ok(response);
    }

    // DELETE /api/forms/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _delete.HandleAsync(new DeleteFormRequest(id, CurrentUserId), cancellationToken);
        return NoContent();
    }


    // POST /api/forms/generate/text
    [HttpPost("generate/text")]
    public async Task<IActionResult> GenerateFromText([FromBody] GenerateFormRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _generateForm.HandleAsync(request, CurrentUserId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.FormId }, response);
    }

    // GET /api/forms/{id}/submissions/count
    [HttpGet("{id:guid}/submissions/count")]
    public async Task<IActionResult> GetSubmissionCount(Guid id,
    CancellationToken cancellationToken)
    {
        var response = await _getSubmissionCount.HandleAsync(new GetSubmissionCountRequest(id, CurrentUserId), cancellationToken);

        return Ok(response);
    }

    // POST   /api/forms/generate/file
    // POST   /api/forms/generate/url
    // POST   /api/forms/generate/image
    // POST   /api/forms/{id}/analyze
}
