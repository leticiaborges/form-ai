namespace FormAI.Application.Forms.DeleteForm;

public record DeleteFormRequest(Guid FormId, Guid RequestingUserId);