using FormAI.Application.Interfaces;
using FormAI.Application.Submissions.RescoreForm;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using NSubstitute;
using static FormAI.UnitTests.TestsHelper.EntityBuilders;

namespace FormAI.UnitTests.Submissions;

public class RescoreFormTests
{

    private readonly ISubmissionRepository _submissions = Substitute.For<ISubmissionRepository>();
    private readonly RescoreFormSubmissionsHandler _rescoreFormSubmissionsHandler;

    private readonly Form _form;
    private readonly FormQuestion _questionSingle;
    private readonly FormQuestion _questionText;
    private readonly Submission _submissionPickedA;
    private readonly Submission _submissionPickedB;

    public RescoreFormTests()
    {
        _rescoreFormSubmissionsHandler = new RescoreFormSubmissionsHandler(_submissions);

        _form = CreateGradedFormTwoQuestions(out _questionSingle, out _questionText);

        _submissionPickedA = CreateSubmission(_form, _questionSingle, _questionText, "A", "ABC");
        _submissionPickedB = CreateSubmission(_form, _questionSingle, _questionText, "B", "ABC");

        _submissions.GetByFormForScoringAsync(_form.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Submission> { _submissionPickedA, _submissionPickedB });
    }

    private static Submission CreateSubmission(Form form, FormQuestion questionSingle, FormQuestion questionText,
        string selectedOptionSingle, string textAnswer)
    {
        var singleIsCorrect = questionSingle.Options.Single(o => o.IsCorrect == true).Text == selectedOptionSingle;
        var singleScore = singleIsCorrect ? questionSingle.Points ?? 0 : 0;

        var textIsCorrect = questionText.CorrectAnswer == textAnswer;
        var textScore = textIsCorrect ? questionText.Points ?? 0 : 0;

        var submission = Submission.Create(
            formId: form.Id,
            userId: null,
            respondentToken: Guid.NewGuid(),
            ipAddress: "127.0.0.1",
            score: singleScore + textScore);

        var answerSingle = Answer.Create(
            submissionId: submission.Id,
            questionId: questionSingle.Id,
            selectedOptions: null,
            textValue: null,
            numericValue: null,
            score: singleScore);
        answerSingle.SetSelectedOptions([AnswerSelectedOption.Create(answerSingle.Id, selectedOptionSingle)]);

        var answerText = Answer.Create(
            submissionId: submission.Id,
            questionId: questionText.Id,
            selectedOptions: null,
            textValue: textAnswer,
            numericValue: null,
            score: textScore);

        submission.SetAnswers([answerSingle, answerText]);

        return submission;
    }

    private static Form CreateGradedFormTwoQuestions(out FormQuestion questionSingle,
    out FormQuestion questionText, int? pointsQuestionSingle = 2, int? pointsQuestionText = 3)
    {
        var form = NewForm(isGraded: true);

        questionSingle = AddQuestion(form, QuestionType.Single, order: 1,
            points: pointsQuestionSingle,
            options: [("A", true), ("B", false)]);

        questionText = AddQuestion(form, QuestionType.Text, order: 2,
            points: pointsQuestionText,
            correctAnswer: "ABC");

        return form;
    }

    private static Answer AnswerTo(Submission submission, FormQuestion question) =>
        Assert.Single(submission.Answers, a => a.QuestionId == question.Id);

    [Fact]
    public async Task GradedFormChangeQuestionPointsOnForm_RescoresAnswerAndForm()
    {
        _questionSingle.Update(_questionSingle.Text, _questionSingle.Type, _questionSingle.Order,
            _questionSingle.IsRequired, _questionSingle.AiGenerated, 4, _questionSingle.CorrectAnswer);

        await _rescoreFormSubmissionsHandler.HandleAsync(_form);

        await _submissions.Received(1).GetByFormForScoringAsync(_form.Id, Arg.Any<CancellationToken>());

        Assert.Equal(7, _submissionPickedA.Score);
        Assert.Equal(4, AnswerTo(_submissionPickedA, _questionSingle).Score);
        Assert.Equal(3, AnswerTo(_submissionPickedA, _questionText).Score);

        Assert.Equal(3, _submissionPickedB.Score);
        Assert.Equal(0, AnswerTo(_submissionPickedB, _questionSingle).Score);
        Assert.Equal(3, AnswerTo(_submissionPickedB, _questionText).Score);
    }

    [Fact]
    public async Task GradedFormDeleteQuestionFromForm_RescoresAnswerAndFormIgnoringExcludedQuestion()
    {
        _form.RemoveQuestion(_questionText);

        await _rescoreFormSubmissionsHandler.HandleAsync(_form);

        await _submissions.Received(1).GetByFormForScoringAsync(_form.Id, Arg.Any<CancellationToken>());

        Assert.Equal(2, _submissionPickedA.Score);
        Assert.Equal(2, AnswerTo(_submissionPickedA, _questionSingle).Score);
        Assert.Null(AnswerTo(_submissionPickedA, _questionText).Score);

        Assert.Equal(0, _submissionPickedB.Score);
        Assert.Equal(0, AnswerTo(_submissionPickedB, _questionSingle).Score);
        Assert.Null(AnswerTo(_submissionPickedB, _questionText).Score);
    }

    [Fact]
    public async Task GradedFormChangedCorrectAnswer_RescoresAnswerAndForm()
    {
        var itemA = _questionSingle.Options.Single(o => o.Text == "A");
        var itemB = _questionSingle.Options.Single(o => o.Text == "B");
        itemA.Update(itemA.Text, itemA.Order, false);
        itemB.Update(itemB.Text, itemB.Order, true);

        await _rescoreFormSubmissionsHandler.HandleAsync(_form);

        await _submissions.Received(1).GetByFormForScoringAsync(_form.Id, Arg.Any<CancellationToken>());

        Assert.Equal(3, _submissionPickedA.Score);
        Assert.Equal(0, AnswerTo(_submissionPickedA, _questionSingle).Score);
        Assert.Equal(3, AnswerTo(_submissionPickedA, _questionText).Score);

        Assert.Equal(5, _submissionPickedB.Score);
        Assert.Equal(2, AnswerTo(_submissionPickedB, _questionSingle).Score);
        Assert.Equal(3, AnswerTo(_submissionPickedB, _questionText).Score);
    }

    [Fact]
    public async Task GradedFormChangedToUngraded_RescoresAnswerAndFormSettingNull()
    {
        _form.Update(_form.Title, _form.Description!, _form.IsPublic,
            _form.ExpiresAt, _form.ShowResultsAfterSubmit, false);

        await _rescoreFormSubmissionsHandler.HandleAsync(_form);

        await _submissions.Received(1).GetByFormForScoringAsync(_form.Id, Arg.Any<CancellationToken>());

        Assert.Null(_submissionPickedA.Score);
        Assert.Null(AnswerTo(_submissionPickedA, _questionSingle).Score);
        Assert.Null(AnswerTo(_submissionPickedA, _questionText).Score);

        Assert.Null(_submissionPickedB.Score);
        Assert.Null(AnswerTo(_submissionPickedB, _questionSingle).Score);
        Assert.Null(AnswerTo(_submissionPickedB, _questionText).Score);
    }
}
