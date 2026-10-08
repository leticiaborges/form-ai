using FormAI.API.Contracts;
using FormAI.API.RateLimiting;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.CreateForm;
using FormAI.Application.Forms.DeleteForm;
using FormAI.Application.Forms.GenerateForm;
using FormAI.Application.Forms.GetForm;
using FormAI.Application.Forms.GetFormResults;
using FormAI.Application.Forms.GetSubmissionAnswers;
using FormAI.Application.Forms.GetSubmissionCount;
using FormAI.Application.Forms.GetSubmissions;
using FormAI.Application.Forms.SaveFormEditor;
using FormAI.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly GetSubmissionsHandler _getSubmissionsHandler;

    private readonly GetSubmissionAnswersHandler _getSubmissionAnswersHandler;
    private readonly GetFormResultsHandler _getFormResults;

    // Room for the 10 MB file the later slices accept, plus the form fields and multipart framing.
    private const long GenerateMaxRequestBytes = 11 * 1024 * 1024;


    public FormsController(CreateFormHandler create,
    GetFormHandler getById, GetFormsByUserHandler getByUser,
    DeleteFormHandler delete,
    GenerateFormHandler generateForm,
    SaveFormEditorHandler saveFormEditor,
    GetSubmissionCountHandler submissionCount,
    GetFormResultsHandler getFormResults,
    GetSubmissionsHandler getSubmissionsHandler,
    GetSubmissionAnswersHandler getSubmissionAnswersHandler)
    {
        _create = create;
        _getById = getById;
        _getByUser = getByUser;
        _delete = delete;
        _generateForm = generateForm;
        _saveFormEditor = saveFormEditor;
        _getSubmissionCount = submissionCount;
        _getFormResults = getFormResults;
        _getSubmissionsHandler = getSubmissionsHandler;
        _getSubmissionAnswersHandler = getSubmissionAnswersHandler;
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


    // POST /api/forms/generate
    [HttpPost("generate")]
    [EnableRateLimiting(RateLimitPolicies.Generate)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(GenerateMaxRequestBytes)]
    public async Task<IActionResult> Generate([FromForm] GenerateFormDataRequest form,
        CancellationToken cancellationToken)
    {
        if (Request.Form.Files.Count > 1)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = ["Upload one file at most."]
            });
        }

        SourceFile? file = null;
        if (form.File != null)
        {
            var upload = form.File;
            await using var stream = upload.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            file = new SourceFile(upload.FileName, buffer.ToArray());
        }

        var request = new GenerateFormRequest(
        form.Title,
        form.Description,
        form.SourceText ?? string.Empty,
        SourceType.Text,
        form.QuestionCount.GetValueOrDefault(),
        form.AllowedTypes,
        form.DifficultyLevel ?? DifficultyLevel.Medium,
        form.IsGraded ?? false,
        form.ShowResultsAfterSubmit ?? false,
        form.ExpiresAt.UtcDateTime,
        file);

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

    // GET /api/forms/{id}/submissions?page=1&size=10
    [HttpGet("{id:guid}/submissions")]
    public async Task<IActionResult> GetSubmissions(Guid id,
    CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var response = await _getSubmissionsHandler.HandleAsync(new GetSubmissionListItemRequest(id, CurrentUserId, page, pageSize), cancellationToken);

        return Ok(response);
    }

    // GET /api/forms/{id}/submissions/{submissionId}
    [HttpGet("{id:guid}/submissions/{submissionId:guid}")]
    public async Task<IActionResult> GetSubmissionDetail(Guid id, Guid submissionId,
    CancellationToken cancellationToken)
    {
        var response = await _getSubmissionAnswersHandler.HandleAsync(new GetSubmissionAnswersRequest(id, submissionId, CurrentUserId),
         cancellationToken);

        return Ok(response);
    }

    // GET /api/forms/{id}/results
    // Owner-only: results carry the answer key, exactly like GET /api/forms/{id}.
    [HttpGet("{id:guid}/results")]
    public async Task<IActionResult> GetResults(
        Guid id, CancellationToken cancellationToken
    )
    {
        var response = await _getFormResults.HandleAsync(
            new GetFormResultsRequest(id, CurrentUserId),
            cancellationToken
        );

        return Ok(response);
    }
}
