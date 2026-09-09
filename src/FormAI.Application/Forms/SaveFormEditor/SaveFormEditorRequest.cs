using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.SaveFormEditor;

public record SaveFormEditorRequest(
    Guid FormId,
    Guid RequestingUserId,
    string Title,
    string? Description,
    bool IsPublic,
    bool IsGraded,
    DateTime ExpiresAt,
    List<QuestionInput> Questions
);

public record QuestionInput(Guid Id, string Text,
QuestionType Type, int Order, bool IsRequired,
bool AiGenerated, int? Points,
string? CorrectAnswer,
List<OptionInput> Options);

public record OptionInput(Guid Id, string Text, int Order, bool? IsCorrect);