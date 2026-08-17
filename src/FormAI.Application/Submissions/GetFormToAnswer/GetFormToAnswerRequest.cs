
using FormAI.Domain.Enums;

namespace FormAI.Application.Submissions.GetFormToAnswer;

public record GetFormToAnswerRequest(Guid FormId, Guid? RequestingUserId);

public record GetFormToAnswerResponse(
    Guid Id,
    string Title,
    string? Description,
    bool IsExpired,
    List<AnswerQuestionDTO> Questions
);

public record AnswerQuestionDTO(
    Guid Id,
    string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    List<AnswerOptionDTO> Options
);

public record AnswerOptionDTO(Guid Id, string Text, int Order);