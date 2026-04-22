using Microsoft.AspNetCore.Mvc;

namespace FormAI.API.Controllers;

[ApiController]
[Route("api/forms/{formId:guid}")]
public class SubmissionsController : ControllerBase
{
    // POST /api/forms/{id}/submit
    // GET  /api/forms/{id}/results
    // GET  /api/forms/{id}/submissions
    // GET  /api/forms/{id}/my-submission
    // GET  /api/forms/{id}/submissions/{sid}
}
