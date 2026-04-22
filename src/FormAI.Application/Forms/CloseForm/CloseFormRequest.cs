
namespace FormAI.Application.Forms.CloseForm;

public record CloseFormRequest(Guid FormId, Guid RequestingUserId);