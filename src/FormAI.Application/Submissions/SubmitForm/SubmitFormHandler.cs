using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Application.Submissions.SubmitForm;

public class SubmitFormHandler
{
    private readonly IFormRepository _forms;
    private readonly ISubmissionRepository _submissions;

    public SubmitFormHandler(IFormRepository forms, ISubmissionRepository submissions)
    {
        _forms = forms;
        _submissions = submissions;
    }

    public async Task<SubmitFormResponse> HandleAsync(SubmitFormRequestCommand request,
        CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId, cancellationToken);

        var errors = await ValidateFormAsync(form, request, cancellationToken);

        var answersByQuestion = request.Answers.ToDictionary(a => a.QuestionId);

        var questionResults = await ValidateAnswers(form!, request, errors, cancellationToken);
        if (errors.Count > 0)
            throw new ValidationException(errors);

        int? totalScore = questionResults.Any(r => r.Score is not null)
          ? questionResults.Sum(r => r.Score ?? 0)
          : null;

        var submission = Submission.Create(form!.Id,
        request.UserId, request.RespondentToken, request.IpAddress,
        totalScore);

        var answers = questionResults.Select(r =>
        {
            var answer = Answer.Create(submission.Id,
            r.Question.Id, null, r.Answer.TextValue,
            (double?)r.Answer.NumericValue, r.Score);

            answer.SetSelectedOptions(r.SelectedOptionsIds.
                Select(id => AnswerSelectedOption.Create(answer.Id, id))
                .ToList());

            return answer;
        }).ToList();

        submission.SetAnswers(answers);
        await _submissions.AddAsync(submission, cancellationToken);

        return new SubmitFormResponse(submission.Id, totalScore);
    }

    private async Task<Dictionary<string, string[]>> ValidateFormAsync(Form? form, SubmitFormRequestCommand request,
        CancellationToken cancellationToken = default)
    {
        if (form is null)
            throw new NotFoundException("Form not found.");

        if (!form.IsPublic && form.CreatedBy != request.UserId)
            throw new NotFoundException("You don't have access to this form.");

        if (form.IsExpired)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["form"] = new[] { "This form is no longer accepting submissions." }
            });

        var existing = await _submissions.GetByRespondentAsync(
            form.Id, request.UserId, request.RespondentToken,
            cancellationToken
        );

        if (existing is not null)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["form"] = new[] { "You have already submitted this form." }
            });

        var answersByQuestion = request.Answers.ToDictionary(a => a.QuestionId);
        var knowQuestionsIds = form.Questions.Select(q => q.Id).ToHashSet();

        var errors = new Dictionary<string, string[]>();
        if (answersByQuestion.Keys.Any(id => !knowQuestionsIds.Contains(id)))
            errors["answers"] = new[] { "One or more answers reference a question that isn't part of this form." };

        return errors;
    }

    private async Task<List<QuestionResult>> ValidateAnswers(Form form, SubmitFormRequestCommand request,
        Dictionary<string, string[]> errors,
        CancellationToken cancellationToken = default)
    {
        var results = new List<QuestionResult>();
        var answersByQuestion = request.Answers.ToDictionary(a => a.QuestionId);

        foreach (var question in form.Questions)
        {
            answersByQuestion.TryGetValue(question.Id, out var answer);

            var isAnswered = IsAnswered(question.Type, answer);

            if (question.IsRequired && !isAnswered)
            {
                errors[question.Id.ToString()] =
                     new[] { "This question is required." };
                continue;
            }

            if (!isAnswered)
                continue;

            var (score, selectedOptionsIds) = ScoreAnswer(question, answer);
            results.Add(new QuestionResult(question, answer!, score, selectedOptionsIds));
        }

        return results;
    }

    private static (int? Score, Guid[] SelectedOptionsIds) ScoreAnswer(FormQuestion question, AnswerRequest? answer)
    {
        switch (question.Type)
        {
            case QuestionType.Single:
            case QuestionType.Multiple:
                return ScoreSingleOrMultiple(question, answer);
            case QuestionType.Numeric:
                return ScoreNumeric(question, answer);
            case QuestionType.Text:
                return ScoreText(question, answer);
            default:
                return (null, Array.Empty<Guid>());
        }
    }

    private static (int? Score, Guid[] SelectedOptionIds) ScoreSingleOrMultiple(FormQuestion question, AnswerRequest? answer)
    {
        if (answer is null)
            return (null, Array.Empty<Guid>());

        var selectedIds = answer.SelectedOptionIds ?? Array.Empty<Guid>();
        var isGraded = question.Options.Any(o => o.IsCorrect.GetValueOrDefault());

        if (!isGraded)
            return (null, selectedIds);

        var correctIds = question.Options.Where(o => o.IsCorrect.GetValueOrDefault())
        .Select(o => o.Id).ToHashSet();

        var isCorrect = selectedIds.ToHashSet().SetEquals(correctIds);

        return (isCorrect ? (question.Points ?? 0) : 0, selectedIds);
    }

    private static (int? Score, Guid[] SelectedOptionIds) ScoreText(FormQuestion question, AnswerRequest? answer)
    {
        if (string.IsNullOrWhiteSpace(question.CorrectAnswer))
            return (null, Array.Empty<Guid>());

        var textMatch = string.Equals(answer?.TextValue?.Trim(),
        question.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase);

        return (textMatch ? (question.Points ?? 0) : 0, Array.Empty<Guid>());
    }


    private static (int? Score, Guid[] SelectedOptionIds) ScoreNumeric(FormQuestion question, AnswerRequest? answer)
    {
        if (answer == null || string.IsNullOrWhiteSpace(question.CorrectAnswer) || answer.NumericValue is null)
            return (null, Array.Empty<Guid>());

        var numericMatch = double.TryParse(question.CorrectAnswer,
        out var expected) && (double)answer.NumericValue.Value == expected;

        return (numericMatch ? (question.Points ?? 0) : 0, Array.Empty<Guid>());
    }


    private static bool IsAnswered(QuestionType type, AnswerRequest? answer)
    {
        if (answer is null) return false;

        switch (type)
        {
            case QuestionType.Text:
                return !string.IsNullOrWhiteSpace(answer?.TextValue);
            case QuestionType.Single:
            case QuestionType.Multiple:
                return answer?.SelectedOptionIds is { Length: > 0 };
            case QuestionType.Numeric:
                return answer?.NumericValue is not null;
            default:
                return false;
        }
    }
};


public record QuestionResult(
    FormQuestion Question,
    AnswerRequest Answer,
    int? Score,
    Guid[] SelectedOptionsIds
);