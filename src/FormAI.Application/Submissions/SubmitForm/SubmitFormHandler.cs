using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.Validation;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using FormAI.Domain.Scoring;

namespace FormAI.Application.Submissions.SubmitForm;

public class SubmitFormHandler
{
    private readonly IFormRepository _forms;
    private readonly ISubmissionRepository _submissions;
    private readonly IFormResultsNotifier _notifier;

    public SubmitFormHandler(IFormRepository forms, ISubmissionRepository submissions,
     IFormResultsNotifier notifier)
    {
        _forms = forms;
        _submissions = submissions;
        _notifier = notifier;
    }

    public async Task<SubmitFormResponse> HandleAsync(SubmitFormRequestCommand request,
        CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId, cancellationToken);

        var errors = await ValidateFormAsync(form, request, cancellationToken);

        var questionResults = ValidateAnswers(form!, request, errors);
        if (errors.Count > 0)
            throw new ValidationException(errors);

        // Scoring runs on option text so that submitting and rescoring share one set of rules,
        // so the selected ids are resolved to text before anything is graded. See ADR 0004.
        var scoredAnswers = questionResults.ToDictionary(
            r => r.Question.Id,
            r => new ScoredAnswer(r.Answer.TextValue, (double?)r.Answer.NumericValue,
                r.SelectedOptionTexts));

        var totalScore = SubmissionScorer.ScoreSubmission(form!, scoredAnswers);

        var submission = Submission.Create(form!.Id,
        request.UserId, request.RespondentToken, request.IpAddress,
        totalScore);

        var answers = questionResults.Select(r =>
        {
            var score = SubmissionScorer.ScoreAnswer(form, r.Question, scoredAnswers[r.Question.Id]);

            var answer = Answer.Create(submission.Id,
            r.Question.Id, null, r.Answer.TextValue,
            (double?)r.Answer.NumericValue, score);

            answer.SetSelectedOptions(r.SelectedOptionTexts
                .Select(text => AnswerSelectedOption.Create(answer.Id, text))
                .ToList());

            return answer;
        }).ToList();

        submission.SetAnswers(answers);
        await _submissions.AddAsync(submission, cancellationToken);

        await _notifier.NotifyResultsChangedAsync(form.Id, cancellationToken);

        var revealScore = form.IsGraded && form.ShowResultsAfterSubmit;

        return new SubmitFormResponse(submission.Id,
            revealScore ? totalScore : null,
            revealScore ? SubmissionScorer.MaximumScore(form) : null);
    }

    private async Task<Dictionary<string, string[]>> ValidateFormAsync(Form? form, SubmitFormRequestCommand request,
        CancellationToken cancellationToken = default)
    {
        FormAccessValidator.CheckUserAnswerAccess(form, request.UserId);

        var existing = await _submissions.GetByRespondentAsync(
            form!.Id, request.UserId, request.RespondentToken,
            cancellationToken
        );

        if (existing is not null)
            throw new ValidationException(ValidationErrorCode.AlreadySubmitted,
                "You have already submitted this form.");

        var answersByQuestion = request.Answers.ToDictionary(a => a.QuestionId);
        var knowQuestionsIds = form.Questions.Select(q => q.Id).ToHashSet();

        var errors = new Dictionary<string, string[]>();
        if (answersByQuestion.Keys.Any(id => !knowQuestionsIds.Contains(id)))
            errors["answers"] = new[] { "One or more answers reference a question that isn't part of this form." };

        return errors;
    }

    private static List<QuestionResult> ValidateAnswers(Form form, SubmitFormRequestCommand request,
        Dictionary<string, string[]> errors)
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

            var selectedOptionTexts = Array.Empty<string>();

            if (question.Type is QuestionType.Single or QuestionType.Multiple)
            {
                var optionTextById = question.Options.ToDictionary(o => o.Id, o => o.Text);
                var submittedIds = answer!.SelectedOptionIds ?? Array.Empty<Guid>();

                if (submittedIds.Any(id => !optionTextById.ContainsKey(id)))
                {
                    errors[question.Id.ToString()] =
                        new[] { "One or more selected options don't belong to this question." };
                    continue;
                }

                if (question.Type == QuestionType.Single && submittedIds.Length > 1)
                {
                    errors[question.Id.ToString()] =
                        new[] { "This question accepts only one option." };
                    continue;
                }

                selectedOptionTexts = submittedIds.Select(id => optionTextById[id]).ToArray();
            }

            results.Add(new QuestionResult(question, answer!, selectedOptionTexts));
        }

        return results;
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
    IReadOnlyCollection<string> SelectedOptionTexts
);
