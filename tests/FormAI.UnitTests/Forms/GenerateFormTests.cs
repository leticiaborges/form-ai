using FormAI.Application.AI;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.GenerateForm;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using NSubstitute;

namespace FormAI.UnitTests.Forms;

public class GenerateFormTests
{
    private readonly IFormGenerationService _generationService = Substitute.For<IFormGenerationService>();
    private readonly IFormRepository _forms = Substitute.For<IFormRepository>();

    private readonly GenerateFormHandler _handler;

    private List<GeneratedQuestion> _listQuestions;

    public GenerateFormTests()
    {
        _handler = new GenerateFormHandler(_generationService, _forms);

        var questions = CreateGeneratedQuestions();
        _listQuestions = questions;
        SetGenerationServiceQuestions(questions);
    }

    private void SetGenerationServiceQuestions(List<GeneratedQuestion> questions)
    {
        _listQuestions = questions;
        _generationService.GenerateAsync(Arg.Any<string>(), Arg.Any<GenerationParameters>(), Arg.Any<CancellationToken>()).Returns(questions);
    }

    private static List<GeneratedQuestion> CreateGeneratedQuestions()
    {
        var listQuestionsOfEachType = new List<GeneratedQuestion>();
        listQuestionsOfEachType.Add(new GeneratedQuestion("Question 1, single selection?", QuestionType.Single, IsRequired: true, CorrectAnswer: null,
            Options: new List<GeneratedOption>() { new GeneratedOption("A", true), new GeneratedOption("B", false)
        }));

        listQuestionsOfEachType.Add(new GeneratedQuestion("Question 2, multiple selection?", QuestionType.Multiple, IsRequired: false, CorrectAnswer: null,
            Options: new List<GeneratedOption>() { new GeneratedOption("A", true), new GeneratedOption("B", false),  new GeneratedOption("C", false),  new GeneratedOption("D", true)
        }));

        listQuestionsOfEachType.Add(new GeneratedQuestion("Question 3, text?", QuestionType.Text, IsRequired: false, CorrectAnswer: "Test response",
            Options: new List<GeneratedOption>()
        ));

        listQuestionsOfEachType.Add(new GeneratedQuestion("Question 4, numeric?", QuestionType.Numeric, IsRequired: false, CorrectAnswer: "4.5",
            Options: new List<GeneratedOption>()
        ));

        return listQuestionsOfEachType;
    }


    private static GenerateFormRequest CreateRequest(string title = "My form 1", string sourceText = "SourceTextTest",
        bool isGraded = true, int expiresInDays = 7)
    {
        return new GenerateFormRequest(title, "Description test form", sourceText, SourceType.Text,
        string.Empty, QuestionCount: 4,
        AllowedTypes: new QuestionType[] { QuestionType.Single, QuestionType.Multiple, QuestionType.Text, QuestionType.Numeric },
        DifficultyLevel: DifficultyLevel.Medium, IsGraded: isGraded, ExpiresAt: DateTime.UtcNow.AddDays(expiresInDays));
    }

    private async Task<(GenerateFormRequest Request, Guid UserId, Form Form)> GenerateFormAsync(string title = "My form 1", bool isGraded = true)
    {
        var request = CreateRequest(title, isGraded: isGraded);

        Form? form = null;
        _ = _forms.AddAsync(Arg.Do<Form>(f => form = f), Arg.Any<CancellationToken>());

        var userId = Guid.NewGuid();
        await _handler.HandleAsync(request, userId);

        await _forms.Received(1).AddAsync(Arg.Any<Form>(), Arg.Any<CancellationToken>());

        Assert.NotNull(form);
        return (request, userId, form);
    }

    [Fact]
    public async Task GradedRequest_PersistsPrivateFormOwnedByRequestingUser()
    {
        var (request, userId, form) = await GenerateFormAsync();

        Assert.Equal("My form 1", form.Title);
        Assert.Equal("Description test form", form.Description);
        Assert.Equal(userId, form.CreatedBy);
        Assert.False(form.IsPublic);
        Assert.False(form.ShowResultsAfterSubmit);
        Assert.True(form.IsGraded);
        Assert.Equal(request.ExpiresAt, form.ExpiresAt);
    }

    [Fact]
    public async Task GradedRequest_MapsGeneratedQuestionsInOrderWithDefaultPoints()
    {
        var (_, _, form) = await GenerateFormAsync();

        Assert.Equal(_listQuestions.Select(q => q.Text), form.Questions.Select(q => q.Text));
        Assert.Equal([1, 2, 3, 4], form.Questions.Select(q => q.Order));
        Assert.Equal(
            [QuestionType.Single, QuestionType.Multiple, QuestionType.Text, QuestionType.Numeric],
            form.Questions.Select(q => q.Type));
        Assert.Equal([true, false, false, false], form.Questions.Select(q => q.IsRequired));
        Assert.All(form.Questions, q => Assert.True(q.AiGenerated));
        Assert.All(form.Questions, q => Assert.Equal(Form.DefaultQuestionPoints, q.Points));
    }

    [Fact]
    public async Task GradedRequest_KeepsTheAnswerKeyOfEveryQuestion()
    {
        var (_, _, form) = await GenerateFormAsync();

        var single = form.Questions.Single(q => q.Type == QuestionType.Single);
        Assert.Equal(["A", "B"], single.Options.Select(o => o.Text));
        Assert.Equal([true, false], single.Options.Select(o => o.IsCorrect));

        var multiple = form.Questions.Single(q => q.Type == QuestionType.Multiple);
        Assert.Equal(["A", "B", "C", "D"], multiple.Options.Select(o => o.Text));
        Assert.Equal([true, false, false, true], multiple.Options.Select(o => o.IsCorrect));

        Assert.Equal("Test response", form.Questions.Single(q => q.Type == QuestionType.Text).CorrectAnswer);
        Assert.Equal("4.5", form.Questions.Single(q => q.Type == QuestionType.Numeric).CorrectAnswer);
    }

    [Fact]
    public async Task GradedRequest_AsksTheGeneratorForTheRequestedQuestions()
    {
        var (request, _, _) = await GenerateFormAsync();

        await _generationService.Received(1).GenerateAsync(
            "SourceTextTest",
            Arg.Is<GenerationParameters>(p =>
                p.QuestionCount == 4 &&
                p.AllowedTypes != null && p.AllowedTypes.SequenceEqual(request.AllowedTypes) &&
                p.DifficultyLevel == DifficultyLevel.Medium &&
                p.IncludeCorrectAnswers),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GradedRequestWithSingleSelectionMultipleCorrect_KeepOnlyFirstCorrectAsCorrectAnswer()
    {
        var listQuestionsOfEachType = new List<GeneratedQuestion>();
        listQuestionsOfEachType.Add(new GeneratedQuestion("Question 1, single selection?", QuestionType.Single, IsRequired: true, CorrectAnswer: null,
            Options: new List<GeneratedOption>() { new GeneratedOption("A", false), new GeneratedOption("B", true),  new GeneratedOption("C", true)
        }));

        SetGenerationServiceQuestions(listQuestionsOfEachType);

        var (_, _, form) = await GenerateFormAsync();

        var single = form.Questions.Single(q => q.Type == QuestionType.Single);
        Assert.Equal(["A", "B", "C"], single.Options.Select(o => o.Text));
        Assert.Equal([false, true, false], single.Options.Select(o => o.IsCorrect));
    }

    [Fact]
    public async Task GradedRequestWithBlankTitle_FallsBackToGeneratedTitle()
    {
        var (_, _, form) = await GenerateFormAsync(title: string.Empty);

        Assert.StartsWith(GenerateFormHandler.PrefixTitle, form.Title);
    }

    [Fact]
    public async Task UngradedRequest_KeepsScoreAsNullAndIgnoreAnswerKey()
    {
        var (_, _, form) = await GenerateFormAsync(isGraded: false);

        Assert.False(form.IsGraded);
        Assert.Equal([null, null, null, null], form.Questions.Select(q => q.Points));

        var single = form.Questions.Single(q => q.Type == QuestionType.Single);
        Assert.Equal([null, null], single.Options.Select(o => o.IsCorrect));

        var multiple = form.Questions.Single(q => q.Type == QuestionType.Multiple);
        Assert.Equal([null, null, null, null], multiple.Options.Select(o => o.IsCorrect));

        Assert.Null(form.Questions.Single(q => q.Type == QuestionType.Text).CorrectAnswer);
        Assert.Null(form.Questions.Single(q => q.Type == QuestionType.Numeric).CorrectAnswer);

        await _generationService.Received(1).GenerateAsync(
          "SourceTextTest",
          Arg.Is<GenerationParameters>(p => p.IncludeCorrectAnswers == false),
          Arg.Any<CancellationToken>());
    }

    public static TheoryData<string, string, int, string> InvalidRequests => new()
    {
        { new string('a', 256), "SourceTextTest", 7, "title" },
        { "My form 1", string.Empty, 7, "sourceText" },
        { "My form 1", "SourceTextTest", -1, "expiresAt" },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidRequest_IsRejectedBeforeGeneratingOrSaving(string title, string sourceText, int expiresInDays, string expectedErrorKey)
    {
        var request = CreateRequest(title, sourceText, expiresInDays: expiresInDays);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(request, Guid.NewGuid()));

        Assert.Equal([expectedErrorKey], exception.Errors.Keys);

        await _generationService.DidNotReceive().GenerateAsync(
            Arg.Any<string>(), Arg.Any<GenerationParameters>(), Arg.Any<CancellationToken>());
        await _forms.DidNotReceive().AddAsync(Arg.Any<Form>(), Arg.Any<CancellationToken>());
    }
}
