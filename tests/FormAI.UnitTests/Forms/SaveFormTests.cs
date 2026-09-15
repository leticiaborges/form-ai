using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.SaveFormEditor;
using FormAI.Application.Interfaces;
using FormAI.Application.Submissions.RescoreForm;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using NSubstitute;
using static FormAI.UnitTests.TestsHelper.EntityBuilders;

namespace FormAI.UnitTests.Forms;

public class SaveFormTests
{

    private readonly IFormRepository _formRepository = Substitute.For<IFormRepository>();
    private readonly ISubmissionRepository _submissionRepository = Substitute.For<ISubmissionRepository>();
    private readonly RescoreFormSubmissionsHandler _rescore;
    private readonly SaveFormEditorHandler _handler;
    private readonly Form _defaultForm;
    private readonly FormQuestion _questionSingle;
    private readonly FormQuestion _questionText;

    public SaveFormTests()
    {
        _rescore = new RescoreFormSubmissionsHandler(_submissionRepository);
        _handler = new SaveFormEditorHandler(_formRepository, _rescore);

        _defaultForm = CreateGradedFormTwoQuestions(out _questionSingle, out _questionText);
        _formRepository.GetByIdAsync(_defaultForm.Id, Arg.Any<CancellationToken>()).Returns(_defaultForm);
    }

    private static Form CreateGradedFormTwoQuestions(out FormQuestion questionSingle,
    out FormQuestion questionText)
    {
        var form = NewForm(isGraded: true, expiresAt: DateTime.UtcNow.AddDays(7));

        questionSingle = AddQuestion(form, QuestionType.Single, order: 1,
            points: 2,
            options: [("A", true), ("B", false), ("C", false)]);

        questionText = AddQuestion(form, QuestionType.Text, order: 2,
            points: 3,
            correctAnswer: "ABC");

        return form;
    }

    [Fact]
    public async Task UnchangedQuestionsAndOptions_KeepTheirIds()
    {
        var questions = new List<QuestionInput>()
        {
            SaveFormTestsHelper.QuestionInput(_questionSingle),
            SaveFormTestsHelper.QuestionInput(_questionText)
        };

        var requestingUserId = _defaultForm.CreatedBy;
        var request = new SaveFormEditorRequest(_defaultForm.Id, requestingUserId, "New title", "New description",
        true, true, _defaultForm.ExpiresAt.GetValueOrDefault(), questions);

        await _handler.HandleAsync(request);

        await _submissionRepository.DidNotReceive().GetByFormForScoringAsync(_defaultForm.Id, Arg.Any<CancellationToken>());
        await _formRepository.Received(1).UpdateAsync(_defaultForm, Arg.Any<CancellationToken>());

        Assert.Equal(2, _defaultForm.Questions.Count);
        Assert.Equal(_questionSingle.Id, _defaultForm.Questions[0].Id);
        Assert.Equal(_questionText.Id, _defaultForm.Questions[1].Id);
        Assert.Equal(3, _defaultForm.Questions[0].Options.Count);
        Assert.Equal(_questionSingle.Options[0].Id, _defaultForm.Questions[0].Options[0].Id);
        Assert.Equal(_questionSingle.Options[1].Id, _defaultForm.Questions[0].Options[1].Id);
        Assert.Equal(_questionSingle.Options[2].Id, _defaultForm.Questions[0].Options[2].Id);
    }

    [Fact]
    public async Task DeletedQuestion_TriggersRescore()
    {
        var questions = new List<QuestionInput>()
        {
            SaveFormTestsHelper.QuestionInput(_questionSingle)
        };

        var requestingUserId = _defaultForm.CreatedBy;
        var request = new SaveFormEditorRequest(_defaultForm.Id, requestingUserId, "New title", "New description",
        true, true, _defaultForm.ExpiresAt.GetValueOrDefault(), questions);

        await _handler.HandleAsync(request);

        await _submissionRepository.Received(1).GetByFormForScoringAsync(_defaultForm.Id, Arg.Any<CancellationToken>());
        await _formRepository.Received(1).UpdateAsync(_defaultForm, Arg.Any<CancellationToken>());

        Assert.Single(_defaultForm.Questions);
        Assert.Equal(3, _defaultForm.Questions[0].Options.Count);

        var savedSingle = _defaultForm.Questions.FirstOrDefault(q => q.Id == _questionSingle.Id);
        Assert.NotNull(savedSingle);
        var savedOptionA = savedSingle.Options.FirstOrDefault(o => o.Id == _questionSingle.Options[0].Id);
        Assert.NotNull(savedOptionA);
        var savedOptionB = savedSingle.Options.FirstOrDefault(o => o.Id == _questionSingle.Options[1].Id);
        Assert.NotNull(savedOptionB);
        var savedOptionC = savedSingle.Options.FirstOrDefault(o => o.Id == _questionSingle.Options[2].Id);
        Assert.NotNull(savedOptionC);
    }

    [Fact]
    public async Task ChangedQuestionPoints_TriggersRescore()
    {
        var questions = new List<QuestionInput>()
        {
            new QuestionInput(_questionSingle.Id, _questionSingle.Text,
                _questionSingle.Type, _questionSingle.Order, _questionSingle.IsRequired,
                _questionSingle.AiGenerated, 10,
                _questionSingle.CorrectAnswer, _questionSingle.Options.Select(a => new OptionInput(a.Id, a.Text, a.Order, a.IsCorrect)).ToList()),
            SaveFormTestsHelper.QuestionInput(_questionText)
        };

        var requestingUserId = _defaultForm.CreatedBy;
        var request = new SaveFormEditorRequest(_defaultForm.Id, requestingUserId, "New title", "New description",
        true, true, _defaultForm.ExpiresAt.GetValueOrDefault(), questions);

        await _handler.HandleAsync(request);

        await _submissionRepository.Received(1).GetByFormForScoringAsync(_defaultForm.Id, Arg.Any<CancellationToken>());
        await _formRepository.Received(1).UpdateAsync(_defaultForm, Arg.Any<CancellationToken>());

        Assert.Equal(2, _defaultForm.Questions.Count);

        var savedQuestion = _defaultForm.Questions.FirstOrDefault(a => a.Id == _questionSingle.Id);
        Assert.NotNull(savedQuestion);
        Assert.Equal(10, savedQuestion.Points);
    }

    [Fact]
    public async Task TurnedUngraded_ClearsGradingAndTriggersRescore()
    {
        var questions = new List<QuestionInput>()
        {
            SaveFormTestsHelper.QuestionInput(_questionSingle),
            SaveFormTestsHelper.QuestionInput(_questionText),
        };

        var requestingUserId = _defaultForm.CreatedBy;
        var request = new SaveFormEditorRequest(_defaultForm.Id, requestingUserId, "New title", "New description",
        true, IsGraded: false, _defaultForm.ExpiresAt.GetValueOrDefault(), questions);

        await _handler.HandleAsync(request);

        await _submissionRepository.Received(1).GetByFormForScoringAsync(_defaultForm.Id, Arg.Any<CancellationToken>());
        await _formRepository.Received(1).UpdateAsync(_defaultForm, Arg.Any<CancellationToken>());

        Assert.Equal(2, _defaultForm.Questions.Count);
        Assert.False(_defaultForm.IsGraded);

        foreach (var question in _defaultForm.Questions)
        {
            Assert.Null(question.Points);
            Assert.Null(question.CorrectAnswer);

            foreach (var option in question.Options)
                Assert.Null(option.IsCorrect);
        }
    }

    [Fact]
    public async Task NonOwner_ThrowsNotFoundException()
    {
        var questions = new List<QuestionInput>()
        {
            SaveFormTestsHelper.QuestionInput(_questionSingle),
            SaveFormTestsHelper.QuestionInput(_questionText),
        };

        var request = new SaveFormEditorRequest(_defaultForm.Id, Guid.NewGuid(), "New title", "New description",
        true, IsGraded: false, _defaultForm.ExpiresAt.GetValueOrDefault(), questions);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(request));
    }

    [Fact]
    public async Task DuplicateOptionText_ThrowsValidationException()
    {
        var single = SaveFormTestsHelper.QuestionInput(_questionSingle);
        single.Options.Add(new OptionInput(Guid.NewGuid(), single.Options[0].Text, 4, false));
        var questions = new List<QuestionInput>()
        {
            single,
            SaveFormTestsHelper.QuestionInput(_questionText),
        };

        var request = new SaveFormEditorRequest(_defaultForm.Id, _defaultForm.CreatedBy, "New title", "New description",
        true, IsGraded: false, _defaultForm.ExpiresAt.GetValueOrDefault(), questions);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(request));
    }

    [Fact]
    public async Task AddedQuestion_DoesNotTriggerRescore()
    {
        var options = new List<OptionInput>()
        {
            new OptionInput(Guid.NewGuid(), "A", 1, true),
            new OptionInput(Guid.NewGuid(), "B", 2, false),
            new OptionInput(Guid.NewGuid(), "C", 3, true)
        };

        var question3 = new QuestionInput(Guid.NewGuid(), "Question 3",
            QuestionType.Multiple, 3, false,
            false, 5,
            null, options);

        var questions = new List<QuestionInput>()
        {
            SaveFormTestsHelper.QuestionInput(_questionSingle),
            SaveFormTestsHelper.QuestionInput(_questionText),
            question3
        };

        var requestingUserId = _defaultForm.CreatedBy;
        var request = new SaveFormEditorRequest(_defaultForm.Id, requestingUserId, "New title", "New description",
        true, true, _defaultForm.ExpiresAt.GetValueOrDefault(), questions);

        await _handler.HandleAsync(request);

        await _submissionRepository.DidNotReceive().GetByFormForScoringAsync(_defaultForm.Id, Arg.Any<CancellationToken>());
        await _formRepository.Received(1).UpdateAsync(_defaultForm, Arg.Any<CancellationToken>());

        Assert.Equal(3, _defaultForm.Questions.Count);

        var savedSingle = _defaultForm.Questions.FirstOrDefault(q => q.Id == _questionSingle.Id);
        Assert.NotNull(savedSingle);

        var savedText = _defaultForm.Questions.FirstOrDefault(q => q.Id == _questionText.Id);
        Assert.NotNull(savedText);

        var savedQuestion3 = _defaultForm.Questions.FirstOrDefault(q => q.Text == question3.Text);
        Assert.NotNull(savedQuestion3);
    }

    [Fact]
    public async Task RenamedAndRemovedOption_KeepsSurvivorIdsAndDropsRemovedOption()
    {
        var options = new List<OptionInput>()
        {
            new OptionInput(_questionSingle.Options[0].Id, "A1", 1, true),
            new OptionInput(_questionSingle.Options[2].Id, "C", 1, false)
        };

        var questionSingle = new QuestionInput(_questionSingle.Id, _questionSingle.Text,
            _questionSingle.Type, _questionSingle.Order, _questionSingle.IsRequired,
            _questionSingle.AiGenerated, _questionSingle.Points,
            _questionSingle.CorrectAnswer, options);

        var questions = new List<QuestionInput>()
        {
            questionSingle,
            SaveFormTestsHelper.QuestionInput(_questionText)
        };

        var requestingUserId = _defaultForm.CreatedBy;
        var request = new SaveFormEditorRequest(_defaultForm.Id, requestingUserId, "New title", "New description",
        true, true, _defaultForm.ExpiresAt.GetValueOrDefault(), questions);

        await _handler.HandleAsync(request);

        await _formRepository.Received(1).UpdateAsync(_defaultForm, Arg.Any<CancellationToken>());

        Assert.Equal(2, _defaultForm.Questions.Count);

        var savedSingle = _defaultForm.Questions.FirstOrDefault(q => q.Id == _questionSingle.Id);
        Assert.NotNull(savedSingle);
        Assert.Equal(2, savedSingle.Options.Count);

        var savedOptionA = savedSingle.Options.FirstOrDefault(o => o.Id == questionSingle.Options[0].Id);
        Assert.NotNull(savedOptionA);
        Assert.Equal("A1", savedOptionA.Text);
        Assert.DoesNotContain(savedSingle.Options, o => o.Text == "B");
        var savedOptionC = savedSingle.Options.FirstOrDefault(o => o.Id == questionSingle.Options[1].Id);
        Assert.NotNull(savedOptionC);

        var savedText = _defaultForm.Questions.FirstOrDefault(q => q.Id == _questionText.Id);
        Assert.NotNull(savedText);
    }
}
