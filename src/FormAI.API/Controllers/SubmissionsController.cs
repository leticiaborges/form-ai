using System.Security.Claims;
using FormAI.Application.Submissions.GetFormToAnswer;
using FormAI.Application.Submissions.GetMySubmission;
using FormAI.Application.Submissions.SubmitForm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FormAI.API.Controllers;

[ApiController]
[Route("api/forms/{formId:guid}")]
public class SubmissionsController : ControllerBase
{
    private readonly GetFormToAnswerHandler _getFormToAnswer;
    private readonly GetMySubmissionHandler _getMySubmission;
    private readonly SubmitFormHandler _submitForm;

    public SubmissionsController(GetFormToAnswerHandler getFormToAnswer,
        GetMySubmissionHandler getMySubmission,
         SubmitFormHandler submitForm)
    {
        _getFormToAnswer = getFormToAnswer;
        _getMySubmission = getMySubmission;
        _submitForm = submitForm;
    }

    private Guid? CurrentUserIdOrNull
    {
        get
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub");

            return sub is not null ? Guid.Parse(sub) : null;
        }
    }

    //GET /api/forms/{formId}/answer
    [HttpGet("answer")]
    [AllowAnonymous]
    public async Task<IActionResult> GetFormToAnswer(Guid formId,
    CancellationToken cancellationToken)
    {
        var response = await _getFormToAnswer.HandleAsync(
            new GetFormToAnswerRequest(formId, CurrentUserIdOrNull),
            cancellationToken);

        return Ok(response);
    }

    //GET /api/forms/{formId}/my-submission?respondentToken={token}
    [HttpGet("my-submission")]
    [AllowAnonymous]
    public async Task<IActionResult> GetMySubmission(Guid formId,
    [FromQuery] Guid respondentToken,
    CancellationToken cancellationToken)
    {
        var response = await _getMySubmission.HandleAsync(
            new GetMySubmissionRequest(formId, CurrentUserIdOrNull,
            respondentToken), cancellationToken);

        return Ok(response);
    }

    //POST /api/forms/{formId}/submit
    [HttpPost("submit")]
    [AllowAnonymous]
    public async Task<IActionResult> Submit(Guid formId,
        [FromBody] SubmitFormRequest request,
        CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var cmd = new SubmitFormRequestCommand(formId, CurrentUserIdOrNull, ipAddress,
         request.RespondentToken,
         request.Answers);

        var response = await _submitForm.HandleAsync(cmd, cancellationToken);

        return Ok(response);
    }
}
