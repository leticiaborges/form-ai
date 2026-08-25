using FormAI.Application.Forms.CloseForm;
using FormAI.Application.Forms.CreateForm;
using FormAI.Application.Forms.DeleteForm;
using FormAI.Application.Forms.GenerateForm;
using FormAI.Application.Forms.GetForm;
using FormAI.Application.Forms.GetSubmissionCount;
using FormAI.Application.Forms.SaveFormEditor;
using FormAI.Application.Forms.UpdateForm;
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
    private readonly UpdateFormHandler _update;
    private readonly DeleteFormHandler _delete;
    private readonly CloseFormHandler _close;
    private readonly GenerateFormHandler _generateForm;
    private readonly SaveFormEditorHandler _saveFormEditor;
    private readonly GetSubmissionCountHandler _getSubmissionCount;

    public FormsController(CreateFormHandler create,
    GetFormHandler getById, GetFormsByUserHandler getByUser,
    UpdateFormHandler update, DeleteFormHandler delete, CloseFormHandler close,
    GenerateFormHandler generateForm,
    SaveFormEditorHandler saveFormEditor,
    GetSubmissionCountHandler submissionCount)
    {
        _create = create;
        _getById = getById;
        _getByUser = getByUser;
        _update = update;
        _delete = delete;
        _close = close;
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
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        // Try to get userId from JWT if present; anonymous users get null
        Guid? userId = null;
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        if (sub is not null) userId = Guid.Parse(sub);

        var response = await _getById.HandleAsync(id, userId, cancellationToken);
        return Ok(response);
    }

    // PUT /api/forms/{id}
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id,
    [FromBody] UpdateFormRequest request,
    CancellationToken cancellationToken)
    {
        var cmd = request with { FormId = id, RequestingUserId = CurrentUserId };
        await _update.HandleAsync(cmd, cancellationToken);
        return NoContent();
    }

    // DELETE /api/forms/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _delete.HandleAsync(new DeleteFormRequest(id, CurrentUserId), cancellationToken);
        return NoContent();
    }

    // PATCH /api/forms/{id}/close
    [HttpPatch("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken cancellationToken)
    {
        await _close.HandleAsync(new CloseFormRequest(id, CurrentUserId), cancellationToken);
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

    // POST   /api/forms/generate/text
    // POST   /api/forms/generate/file
    // POST   /api/forms/generate/url
    // POST   /api/forms/generate/image

    // POST   /api/forms
    // GET    /api/forms
    // GET    /api/forms/{id}
    // PUT    /api/forms/{id}
    // DELETE /api/forms/{id}
    // PATCH  /api/forms/{id}/close
    // PUT    /api/forms/{id}/questions
    // POST   /api/forms/{id}/analyze
}
