using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using FormAI.Domain.Scoring;
using static FormAI.UnitTests.Results.FormResultsCalculatorTestsHelper;

namespace FormAI.UnitTests.Results;

public class SubmissionScorerTests
{
    [Fact]
    public void UngradedForm_NullPointsOnANullPointCorrectQuestion()
    {
        var form = NewForm(isGraded: false);

        var question = AddQuestion(form, QuestionType.Single, order: 1,
            options: [("A", true), ("B", false)]);

        int? result = SubmissionScorer.ScoreAnswer(form, question, ScoredAnswer.From(Picked(question.Id, "A")));
        Assert.Null(result);
    }

    [Fact]
    public void GradedForm_ZeroPointsOnAZeroPointCorrectQuestion()
    {
        var form = NewForm(isGraded: true);

        var question = AddQuestion(form, QuestionType.Single, order: 1,
            points: 0,
            options: [("A", true), ("B", false)]);

        int? result = SubmissionScorer.ScoreAnswer(form, question, ScoredAnswer.From(Picked(question.Id, "A")));
        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(new[] { "A", "C" }, 3)] //correct answer
    [InlineData(new[] { "A", "B" }, 0)] //incorrect answer
    [InlineData(new[] { "A" }, 0)] //incorrect answer
    [InlineData(new[] { "B" }, 0)] //incorrect answer
    public void GradedForm_CorrectPointsOnMultipleSelection(string[] selectedOptions, int expectedPoints)
    {
        var form = NewForm(isGraded: true);

        var question = AddQuestion(form, QuestionType.Multiple, order: 1,
            points: 3,
            options: [("A", true), ("B", false), ("C", true)]);

        int? result = SubmissionScorer.ScoreAnswer(form, question, ScoredAnswer.From(Picked(question.Id, selectedOptions)));
        Assert.Equal(expectedPoints, result);
    }

    [Fact]
    public void GradedForm_ZeroPointsOnMultipleSelectionNoAnswerKey()
    {
        var form = NewForm(isGraded: true);

        var question = AddQuestion(form, QuestionType.Multiple, order: 1,
            points: 3,
            options: [("A", null), ("B", null), ("C", null)]);

        int? result = SubmissionScorer.ScoreAnswer(form, question, ScoredAnswer.From(Picked(question.Id, "A")));
        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData("5.25", 5.25, 2)] //correct
    [InlineData("5.25", 5.255, 0)] //incorrect
    [InlineData("5.25", 5, 0)] //incorrect
    [InlineData("5.25", 5.2465444, 0)]//incorrect
    [InlineData("5.25", 5.250004, 2)]//correct
    [InlineData("5,25", 5.25, 0)]//incorrect
    public void GradedForm_CorrectPointsOnNumericQuestion(string correctAnswer, double numericValue, int expectedPoints)
    {
        var form = NewForm(isGraded: true);

        var question = AddQuestion(form, QuestionType.Numeric, order: 1,
            points: 2, correctAnswer: correctAnswer);

        int? result = SubmissionScorer.ScoreAnswer(form, question, ScoredAnswer.From(Entered(question.Id, numericValue)));
        Assert.Equal(expectedPoints, result);
    }

    [Theory]
    [InlineData("ABC", 2)] //correct
    [InlineData("Abc", 2)] //correct
    [InlineData("Ab", 0)] //incorrect
    public void GradedForm_CorrectPointsOnTextQuestion(string textValue, int expectedPoints)
    {
        var form = NewForm(isGraded: true);

        var question = AddQuestion(form, QuestionType.Text, order: 1,
            points: 2, correctAnswer: "ABC");

        int? result = SubmissionScorer.ScoreAnswer(form, question, ScoredAnswer.From(Wrote(question.Id, textValue)));
        Assert.Equal(expectedPoints, result);
    }

    [Fact]
    public void GradedForm_ZeroPointsUnansweredQuestion()
    {
        var form = NewForm(isGraded: true);

        var question = AddQuestion(form, QuestionType.Text, order: 1,
            points: 2, correctAnswer: "ABC");

        int? result = SubmissionScorer.ScoreAnswer(form, question, ScoredAnswer.Unanswered);
        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(new string[] { "Option 1", "Option 2" }, 12)]
    [InlineData(new string[] { "Option 1" }, 10)]
    public void GradedForm_TotalSumPoints(string[] answerMultiselect, int expectedScore)
    {
        var form = NewForm(isGraded: true);

        var questionNumeric = AddQuestion(form, QuestionType.Numeric, order: 1,
             points: 4, correctAnswer: "123");

        var questionSingle = AddQuestion(form, QuestionType.Single, order: 2,
            points: 2, options: [("A", true), ("B", false)]);

        var questionMultiple = AddQuestion(form, QuestionType.Multiple, order: 3,
            points: 2, options: [("Option 1", true), ("Option 2", true), ("Option 3", false)]);

        var questionText = AddQuestion(form, QuestionType.Text, order: 4,
            points: 4, correctAnswer: "Correct answer test");

        var answers = new Dictionary<Guid, ScoredAnswer>
        {
            [questionNumeric.Id] = ScoredAnswer.From(Entered(questionNumeric.Id, 123)),
            [questionSingle.Id] = ScoredAnswer.From(Picked(questionSingle.Id, "A")),
            [questionMultiple.Id] = ScoredAnswer.From(Picked(questionMultiple.Id, answerMultiselect)),
            [questionText.Id] = ScoredAnswer.From(Wrote(questionText.Id, "Correct answer test")),
        };

        int? result = SubmissionScorer.ScoreSubmission(form, answers);
        Assert.Equal(expectedScore, result);
    }

    [Fact]
    public void GradedForm_TotalSumPointsWithOneUnanswered()
    {
        var form = NewForm(isGraded: true);

        //unanswered is not added in the dictionary
        var questionNumeric = AddQuestion(form, QuestionType.Numeric, order: 1,
             points: 4, correctAnswer: "123");

        var questionSingle = AddQuestion(form, QuestionType.Single, order: 2,
            points: 2, options: [("A", true), ("B", false)]);

        var answers = new Dictionary<Guid, ScoredAnswer>
        {
            [questionSingle.Id] = ScoredAnswer.From(Picked(questionSingle.Id, "A"))
        };

        int? result = SubmissionScorer.ScoreSubmission(form, answers);
        Assert.Equal(2, result);
    }


}