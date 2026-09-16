using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Application.Submissions.SubmitForm;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using NSubstitute;
using static FormAI.UnitTests.TestsHelper.EntityBuilders;

namespace FormAI.UnitTests.Submissions;

public class SubmitFormTests
{

    private readonly IFormRepository _formRepository = Substitute.For<IFormRepository>();
    private readonly ISubmissionRepository _submissionRepository = Substitute.For<ISubmissionRepository>();
    private readonly SubmitFormHandler _handler;
    private readonly IFormResultsNotifier _notifier = Substitute.For<IFormResultsNotifier>();

    public SubmitFormTests()
    {
        _handler = new SubmitFormHandler(_formRepository, _submissionRepository, _notifier);

        _submissionRepository.GetByRespondentAsync(Arg.Any<Guid>(),
        Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
        .Returns((Submission?)null);
    }

    private static Form CreateGradedFormTwoQuestions(out FormQuestion questionSingle,
    out FormQuestion questionText)
    {
        var form = NewForm(isGraded: true);

        questionSingle = AddQuestion(form, QuestionType.Single, order: 1,
            points: 2,
            options: [("A", true), ("B", false)]);

        questionText = AddQuestion(form, QuestionType.Text, order: 2,
            points: 3,
            correctAnswer: "ABC");

        return form;
    }

    [Fact]
    public async Task GradedForm_AllCorrectAnswers_PersistsSubmissionWithFullScore()
    {
        var form = CreateGradedFormTwoQuestions(out FormQuestion questionSingle, out FormQuestion questionText);

        _formRepository.GetByIdAsync(form.Id, Arg.Any<CancellationToken>()).Returns(form);

        var answers = new AnswerRequest[]
        {
            SubmitFormTestsHelper.Picks(questionSingle.Id, questionSingle.Options[0].Id),
            SubmitFormTestsHelper.Writes(questionText.Id, "ABC"),
        };

        var respondentToken = Guid.NewGuid();
        var request = SubmitFormTestsHelper.Request(form, respondentToken, answers);

        var response = await _handler.HandleAsync(request);

        Assert.Equal(5m, response.TotalScore);

        await _submissionRepository.Received(1).AddAsync(
            Arg.Is<Submission>(s => s.Id == response.Id && s.Score == 5 && s.Answers.Count == 2),
            Arg.Any<CancellationToken>());

        await _notifier.Received(1).NotifyResultsChangedAsync(form.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonPublic_ThrowsNotFoundException()
    {
        var form = NewForm(isGraded: true, isPublic: false);

        _formRepository.GetByIdAsync(form.Id, Arg.Any<CancellationToken>()).Returns(form);

        var respondentToken = Guid.NewGuid();
        var request = SubmitFormTestsHelper.Request(form, respondentToken);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(request));

        await _notifier.DidNotReceive().NotifyResultsChangedAsync(form.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expired_ThrowsValidationException()
    {
        var form = NewForm(isGraded: true, isPublic: true, expiresAt: DateTime.UtcNow.AddDays(-1));

        _formRepository.GetByIdAsync(form.Id, Arg.Any<CancellationToken>()).Returns(form);

        var respondentToken = Guid.NewGuid();
        var request = SubmitFormTestsHelper.Request(form, respondentToken);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(request));
        Assert.Equal(ValidationErrorCode.FormExpired, exception.Code);

        await _notifier.DidNotReceive().NotifyResultsChangedAsync(form.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlreadySubmittedForm_ThrowsValidationException()
    {
        var form = NewForm(isGraded: false, isPublic: true);

        _formRepository.GetByIdAsync(form.Id, Arg.Any<CancellationToken>()).Returns(form);

        var respondentToken = Guid.NewGuid();
        var submission = Submission.Create(form.Id, null, respondentToken, "127.0.0.1", null);

        _submissionRepository.GetByRespondentAsync(form.Id,
        null, respondentToken, Arg.Any<CancellationToken>())
        .Returns(submission);

        var request = SubmitFormTestsHelper.Request(form, respondentToken);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(request));
        Assert.Equal(ValidationErrorCode.AlreadySubmitted, exception.Code);

        await _notifier.DidNotReceive().NotifyResultsChangedAsync(form.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnswersHasQuestionInvalidForForm_ThrowsValidationException()
    {
        var form = CreateGradedFormTwoQuestions(out FormQuestion questionSingle,
        out FormQuestion questionText);

        _formRepository.GetByIdAsync(form.Id, Arg.Any<CancellationToken>()).Returns(form);

        var answers = new AnswerRequest[]
        {
            SubmitFormTestsHelper.Picks(questionSingle.Id, questionSingle.Options[0].Id),
            SubmitFormTestsHelper.Writes(questionText.Id, "ABC"),
            SubmitFormTestsHelper.Writes(Guid.NewGuid(), "X"),
        };

        var respondentToken = Guid.NewGuid();
        var request = SubmitFormTestsHelper.Request(form, respondentToken, answers);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(request));
        Assert.NotEmpty(exception.Errors["answers"]);

        await _notifier.DidNotReceive().NotifyResultsChangedAsync(form.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnansweredRequiredQuestion_ThrowsValidationException()
    {
        var form = NewForm(isGraded: true);

        var questionSingle = AddQuestion(form, QuestionType.Single, order: 1,
            points: 2, isRequired: true,
            options: [("A", true), ("B", false)]);

        var questionSingle2 = AddQuestion(form, QuestionType.Single, order: 1,
           points: 2, isRequired: false,
           options: [("C", true), ("D", false)]);

        _formRepository.GetByIdAsync(form.Id, Arg.Any<CancellationToken>()).Returns(form);

        var answers = new AnswerRequest[]
        {
            SubmitFormTestsHelper.Picks(questionSingle2.Id, questionSingle2.Options[0].Id),
        };

        var respondentToken = Guid.NewGuid();
        var request = SubmitFormTestsHelper.Request(form, respondentToken, answers);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(request));
        Assert.NotEmpty(exception.Errors[questionSingle.Id.ToString()]);

        await _notifier.DidNotReceive().NotifyResultsChangedAsync(form.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuestionSingleSelectionMoreThanOne_ThrowsValidationException()
    {
        var form = NewForm(isGraded: true);

        var questionSingle = AddQuestion(form, QuestionType.Single, order: 1,
            points: 2, isRequired: true,
            options: [("A", true), ("B", false)]);

        _formRepository.GetByIdAsync(form.Id, Arg.Any<CancellationToken>()).Returns(form);

        var answers = new AnswerRequest[]
        {
            SubmitFormTestsHelper.Picks(questionSingle.Id, questionSingle.Options[0].Id, questionSingle.Options[1].Id),
        };

        var respondentToken = Guid.NewGuid();
        var request = SubmitFormTestsHelper.Request(form, respondentToken, answers);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(request));
        Assert.NotEmpty(exception.Errors[questionSingle.Id.ToString()]);

        await _notifier.DidNotReceive().NotifyResultsChangedAsync(form.Id, Arg.Any<CancellationToken>());
    }
}