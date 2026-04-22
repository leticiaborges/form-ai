using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.UpdateQuestions;

public record UpdateQuestionsRequest(Guid FormId, Guid RequestingUserId, List<QuestionInput> Questions);

public record QuestionInput(string Text, QuestionType Type, int Order, bool IsRequired,
bool AiGenerated, int? Points, string? CorrectAnswer, List<OptionInput> options);

public record OptionInput(string Text, int Order, bool? IsCorrect);