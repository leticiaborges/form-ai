using FormAI.Application.Forms.UpdateQuestions;
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.SaveFormEditor;

public record SaveFormEditorRequest(
    Guid FormId,
    Guid RequestingUserId,
    string Title,
    string? Description,
    bool IsPublic,
    List<QuestionInput> Questions
);

public record QuestionInput(string Text,
QuestionType Type, int Order, bool IsRequired,
bool AiGenerated, int? Points,
string? CorrectAnswer,
List<OptionInput> Options);

public record OptionInput(string Text, int Order, bool IsCorrect);