using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using FormAI.Domain.Results;
using static FormAI.UnitTests.Results.FormResultsCalculatorTestsHelper;

namespace FormAI.UnitTests.Results;

public class FormResultsCalculatorTests
{
    [Fact]
    public void GradedForm_CountsCorrectAnswersOnAZeroPointQuestion()
    {
        var form = NewForm(isGraded: true);

        var question = AddQuestion(form, QuestionType.Single, order: 1,
            points: 0,
            options: [("A", true), ("B", false)]);

        var submissions = new List<Submission>
        {
            NewSubmission(form, score: 0,
            Picked(question.Id, "A"))
        };

        var results = FormResultsCalculator.Calculate(form, submissions);

        var questionResults = Assert.Single(results.Questions);
        Assert.Equal(1, questionResults.AnswerCount);
        Assert.Equal(1, questionResults.CorrectAnswerCount);
    }

    [Fact]
    public void GradedForm_NoSubmissionsShowQuestionsWithZeroAnswers()
    {
        var form = NewForm(isGraded: true);
        AddQuestion(form, QuestionType.Single, order: 1, points: 2, options: [("A", true), ("B", false)]);
        AddQuestion(form, QuestionType.Text, order: 2, points: 3, correctAnswer: "x");

        var results = FormResultsCalculator.Calculate(form, new List<Submission>());

        Assert.Equal(0, results.SubmissionCount);
        Assert.Equal(5, results.TotalPoints);
        Assert.Empty(results.ScoreDistribution);
        Assert.Equal(2, results.Questions.Count);

        Assert.Equal(0, results.Questions[0].AnswerCount);
        Assert.Equal(0, results.Questions[0].CorrectAnswerCount);
        Assert.Equal(2, results.Questions[0].Options.Count);
        Assert.Equal(0, results.Questions[0].Options[0].Count);
        Assert.Empty(results.Questions[1].Values);
    }

    [Fact]
    public void GradedForm_CountsCorrectAnswersQuestionWithSomeUnansweredQuestions()
    {
        var form = NewForm(isGraded: true);

        var question = AddQuestion(form, QuestionType.Single, order: 1,
            points: 4,
            options: [("A", true), ("B", false)]);

        var submissions = new List<Submission>
        {
            NewSubmission(form, score: 4, Picked(question.Id, "A")),
            NewSubmission(form, score: 0, Picked(question.Id, "B")),
            NewSubmission(form, score: null, [])
        };

        var results = FormResultsCalculator.Calculate(form, submissions);

        var questionResults = Assert.Single(results.Questions);
        Assert.Equal(2, questionResults.AnswerCount);
        Assert.Equal(1, questionResults.CorrectAnswerCount);
    }

    [Theory]
    [InlineData(new[] { "A", "C", "D" }, 4, 1)] //correct answer
    [InlineData(new[] { "A", "B", "D" }, 0, 0)] //incorrect answer
    [InlineData(new[] { "A", "C" }, 0, 0)] //incorrect answer
    public void GradedForm_CountsCorrectAnswersMultipleSelectionQuestion(string[] selectedOptions,
    int storedScore,
    int expectedCorrectCount)
    {
        var form = NewForm(isGraded: true);

        var question = AddQuestion(form, QuestionType.Multiple, order: 1,
            points: 4,
            options: [("A", true), ("B", false), ("C", true), ("D", true)]);

        var submissions = new List<Submission>
        {
            NewSubmission(form, score: storedScore, Picked(question.Id, selectedOptions)),
        };

        var results = FormResultsCalculator.Calculate(form, submissions);

        var questionResults = Assert.Single(results.Questions);
        Assert.Equal(1, questionResults.AnswerCount);
        Assert.Equal(expectedCorrectCount, questionResults.CorrectAnswerCount);
    }

    [Fact]
    public void GradedForm_CountsCorrectAnswersMultipleSelectionQuestionMultipleSubmissions()
    {
        var form = NewForm(isGraded: true);

        var question = AddQuestion(form, QuestionType.Multiple, order: 1,
            points: 4,
            options: [("A", true), ("B", false), ("C", true), ("D", true)]);

        var submissions = new List<Submission>
        {
            NewSubmission(form, score: 0, Picked(question.Id, "A", "B")),
            NewSubmission(form, score: question.Points, Picked(question.Id, "A", "C", "D")),
        };

        var results = FormResultsCalculator.Calculate(form, submissions);

        var questionResults = Assert.Single(results.Questions);
        Assert.Equal(2, questionResults.AnswerCount);
        Assert.Equal(1, questionResults.CorrectAnswerCount);

        Assert.Equal(4, questionResults.Options.Count);

        Assert.Equal("A", questionResults.Options[0].Text);
        Assert.Equal(2, questionResults.Options[0].Count);
        Assert.True(questionResults.Options[0].IsCorrect);

        Assert.Equal("B", questionResults.Options[1].Text);
        Assert.Equal(1, questionResults.Options[1].Count);
        Assert.False(questionResults.Options[1].IsCorrect);

        Assert.Equal("C", questionResults.Options[2].Text);
        Assert.Equal(1, questionResults.Options[2].Count);
        Assert.True(questionResults.Options[2].IsCorrect);

        Assert.Equal("D", questionResults.Options[3].Text);
        Assert.Equal(1, questionResults.Options[3].Count);
        Assert.True(questionResults.Options[3].IsCorrect);
    }

    [Fact]
    public void GradedForm_CountsMultipleSelectionQuestionWithOldOptions()
    {
        var form = NewForm(isGraded: true);

        var question = AddQuestion(form, QuestionType.Multiple, order: 1,
            points: 4,
            options: [("A", true), ("B", true), ("C", false), ("D", true)]);

        var submissions = new List<Submission>
        {
            NewSubmission(form, score: 0, Picked(question.Id, "A", "B", "C"))
        };

        // change option text to simulate an old option
        question.Options.Single(o => o.Text == "B").Update("X", question.Options[1].Order, question.Options[1].IsCorrect);

        var results = FormResultsCalculator.Calculate(form, submissions);

        var questionResults = Assert.Single(results.Questions);
        Assert.Equal(1, questionResults.AnswerCount);
        Assert.Equal(0, questionResults.CorrectAnswerCount);

        Assert.Equal(5, questionResults.Options.Count);

        Assert.Equal("A", questionResults.Options[0].Text);
        Assert.Equal(1, questionResults.Options[0].Count);
        Assert.True(questionResults.Options[0].IsCorrect);

        Assert.Equal("X", questionResults.Options[1].Text);
        Assert.Equal(0, questionResults.Options[1].Count);
        Assert.True(questionResults.Options[1].IsCorrect);

        Assert.Equal("C", questionResults.Options[2].Text);
        Assert.Equal(1, questionResults.Options[2].Count);
        Assert.False(questionResults.Options[2].IsCorrect);

        Assert.Equal("D", questionResults.Options[3].Text);
        Assert.Equal(0, questionResults.Options[3].Count);
        Assert.True(questionResults.Options[3].IsCorrect);

        Assert.Equal("B", questionResults.Options[4].Text);
        Assert.Equal(1, questionResults.Options[4].Count);
        Assert.Null(questionResults.Options[4].IsCorrect);
    }

    [Fact]
    public void GradedForm_CountsCorrectAnswersTextQuestionIgnoringCaseAndTrim()
    {
        var form = NewForm(isGraded: true);

        var question = AddQuestion(form, QuestionType.Text, order: 1,
            points: 4, correctAnswer: "ABC");

        var submissions = new List<Submission>
        {
            NewSubmission(form, score: question.Points, Wrote(question.Id, "ABC")),
            NewSubmission(form, score: question.Points, Wrote(question.Id, "ABC")),
            NewSubmission(form, score: question.Points, Wrote(question.Id, "abC")),
            NewSubmission(form, score: question.Points, Wrote(question.Id, " abC ")),
            NewSubmission(form, score: 0, Wrote(question.Id, "AD")),
            NewSubmission(form, score: 0, []),
        };

        var results = FormResultsCalculator.Calculate(form, submissions);

        var questionResults = Assert.Single(results.Questions);
        Assert.Equal(5, questionResults.AnswerCount);
        Assert.Equal(4, questionResults.CorrectAnswerCount);

        Assert.Equal(2, questionResults.Values.Count);
        Assert.Equal("ABC", questionResults.Values[0].Value);
        Assert.Equal(4, questionResults.Values[0].Count);
        Assert.True(questionResults.Values[0].IsCorrect);

        Assert.Equal("AD", questionResults.Values[1].Value);
        Assert.Equal(1, questionResults.Values[1].Count);
        Assert.False(questionResults.Values[1].IsCorrect);
    }

    [Fact]
    public void GradedForm_AreAnswersTextOrderedByCountDescending()
    {
        var form = NewForm(isGraded: true);

        var question = AddQuestion(form, QuestionType.Text, order: 1,
            points: 4, correctAnswer: "ABC");

        var submissions = new List<Submission>
        {
            NewSubmission(form, score: 0, Wrote(question.Id, "DCE")),
            NewSubmission(form, score: question.Points, Wrote(question.Id, "ABC")),
            NewSubmission(form, score: question.Points, Wrote(question.Id, "ABC"))
        };

        var results = FormResultsCalculator.Calculate(form, submissions);

        var questionResults = Assert.Single(results.Questions);
        Assert.Equal(3, questionResults.AnswerCount);
        Assert.Equal(2, questionResults.CorrectAnswerCount);

        Assert.Equal("ABC", questionResults.Values[0].Value);
        Assert.Equal(2, questionResults.Values[0].Count);
        Assert.True(questionResults.Values[0].IsCorrect);

        Assert.Equal("DCE", questionResults.Values[1].Value);
        Assert.Equal(1, questionResults.Values[1].Count);
        Assert.False(questionResults.Values[1].IsCorrect);
    }

    [Fact]
    public void GradedForm_CountsCorrectAnswersNumericQuestion()
    {
        var form = NewForm(isGraded: true);

        var question = AddQuestion(form, QuestionType.Numeric, order: 1,
            points: 4, correctAnswer: "123");

        var submissions = new List<Submission>
        {
            NewSubmission(form, score: question.Points, Entered(question.Id, 123)),
            NewSubmission(form, score: 0, Entered(question.Id, 1))
        };

        var results = FormResultsCalculator.Calculate(form, submissions);

        var questionResults = Assert.Single(results.Questions);
        Assert.Equal(2, questionResults.AnswerCount);
        Assert.Equal(1, questionResults.CorrectAnswerCount);
    }

    [Fact]
    public void GradedForm_CountsCorrectOneQuestionEachTypeAllCorrect()
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

        var submissions = new List<Submission>
        {
            NewSubmission(form, score: 12, answers:
                [
                    Entered(questionNumeric.Id, 123),
                    Picked(questionSingle.Id, "A"),
                    Picked(questionMultiple.Id, "Option 1", "Option 2"),
                    Wrote(questionText.Id, "Correct answer test")
                ]),
        };

        var results = FormResultsCalculator.Calculate(form, submissions);

        Assert.Equal(4, results.Questions.Count);
        var questionResultsNumeric = results.Questions[0];
        Assert.Equal(1, questionResultsNumeric.AnswerCount);
        Assert.Equal(1, questionResultsNumeric.CorrectAnswerCount);

        var questionResultsSingle = results.Questions[1];
        Assert.Equal(1, questionResultsSingle.AnswerCount);
        Assert.Equal(1, questionResultsSingle.CorrectAnswerCount);

        var questionResultsMultiple = results.Questions[2];
        Assert.Equal(1, questionResultsMultiple.AnswerCount);
        Assert.Equal(1, questionResultsMultiple.CorrectAnswerCount);

        var questionResultsText = results.Questions[3];
        Assert.Equal(1, questionResultsText.AnswerCount);
        Assert.Equal(1, questionResultsText.CorrectAnswerCount);
    }

    [Fact]
    public void GradedForm_SumTotalPointsQuestionEachTypeAllCorrect()
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

        var totalSumScore = 12;

        var submissions = new List<Submission>
        {
            NewSubmission(form, score: totalSumScore, answers:
                [
                    Entered(questionNumeric.Id, 123),
                    Picked(questionSingle.Id, "A"),
                    Picked(questionMultiple.Id, "Option 1", "Option 2"),
                    Wrote(questionText.Id, "Correct answer test")
                ]),
        };

        var results = FormResultsCalculator.Calculate(form, submissions);
        Assert.Equal(totalSumScore, results.TotalPoints);
        Assert.Equal(1, results.SubmissionCount);
        Assert.Single(results.ScoreDistribution);
        Assert.Equal(1, results.ScoreDistribution[0].SubmissionCount);
        Assert.Equal(totalSumScore, results.ScoreDistribution[0].Score);
    }

    [Fact]
    public void GradedForm_SumTotalPointsMultipleSubmissionsIsGrouped()
    {
        var form = NewForm(isGraded: true);

        var questionSingle = AddQuestion(form, QuestionType.Single, order: 2,
            points: 2, options: [("A", true), ("B", false)]);

        var questionMultiple = AddQuestion(form, QuestionType.Multiple, order: 3,
            points: 3, options: [("Option 1", true), ("Option 2", true), ("Option 3", false)]);

        var submissions = new List<Submission>
        {
            NewSubmission(form, score: 5, answers:
                [
                    Picked(questionSingle.Id, "A"),
                    Picked(questionMultiple.Id, "Option 1", "Option 2"),
                ]),

            NewSubmission(form, score: 3, answers:
                [
                    Picked(questionSingle.Id, "B"),
                    Picked(questionMultiple.Id, "Option 1", "Option 2"),
                ]),

            NewSubmission(form, score: 3, answers:
                [
                    Picked(questionSingle.Id, "B"),
                    Picked(questionMultiple.Id, "Option 1", "Option 2"),
                ]),

            NewSubmission(form, score: 0, answers:
                [
                    Picked(questionSingle.Id, "B"),
                    Picked(questionMultiple.Id, "Option 3"),
                ]),
        };

        var results = FormResultsCalculator.Calculate(form, submissions);
        Assert.Equal(5, results.TotalPoints);
        Assert.Equal(4, results.SubmissionCount);
        Assert.Equal(3, results.ScoreDistribution.Count);

        Assert.Equal(1, results.ScoreDistribution[0].SubmissionCount);
        Assert.Equal(0, results.ScoreDistribution[0].Score);

        Assert.Equal(2, results.ScoreDistribution[1].SubmissionCount);
        Assert.Equal(3, results.ScoreDistribution[1].Score);

        Assert.Equal(1, results.ScoreDistribution[2].SubmissionCount);
        Assert.Equal(5, results.ScoreDistribution[2].Score);
    }

    [Fact]
    public void UngradedForm_CountsAnswersQuestionWithSomeUnansweredQuestions()
    {
        var form = NewForm(isGraded: false);

        var question = AddQuestion(form, QuestionType.Single, order: 1,
            points: null,
            options: [("A", null), ("B", null)]);

        var submissions = new List<Submission>
        {
            NewSubmission(form, score: null, Picked(question.Id, "A")),
            NewSubmission(form, score: null, Picked(question.Id, "B")),
            NewSubmission(form, score: null, [])
        };

        var results = FormResultsCalculator.Calculate(form, submissions);

        var questionResults = Assert.Single(results.Questions);
        Assert.Equal(2, questionResults.AnswerCount);
        Assert.Equal(3, results.SubmissionCount);
        Assert.Null(questionResults.CorrectAnswerCount);
    }

    [Fact]
    public void UngradedForm_CountsQuestionsAnsweredEachType()
    {
        var form = NewForm(isGraded: false);

        var questionNumeric = AddQuestion(form, QuestionType.Numeric, order: 1);

        var questionSingle = AddQuestion(form, QuestionType.Single, order: 2,
         options: [("A", true), ("B", false)]);

        var questionMultiple = AddQuestion(form, QuestionType.Multiple, order: 3,
            options: [("Option 1", true), ("Option 2", true), ("Option 3", false)]);

        var questionText = AddQuestion(form, QuestionType.Text, order: 4);

        var submissions = new List<Submission>
        {
            NewSubmission(form, score: null, answers:
                [
                    Entered(questionNumeric.Id, 123),
                    Picked(questionSingle.Id, "A"),
                    Picked(questionMultiple.Id, "Option 1", "Option 2"),
                    Wrote(questionText.Id, "Correct answer test")
                ]),
        };

        var results = FormResultsCalculator.Calculate(form, submissions);

        Assert.Null(results.TotalPoints);

        Assert.Equal(4, results.Questions.Count);
        var questionResultsNumeric = results.Questions[0];
        Assert.Equal(1, questionResultsNumeric.AnswerCount);
        Assert.Null(questionResultsNumeric.CorrectAnswerCount);

        var questionResultsSingle = results.Questions[1];
        Assert.Equal(1, questionResultsSingle.AnswerCount);
        Assert.Null(questionResultsSingle.CorrectAnswerCount);

        var questionResultsMultiple = results.Questions[2];
        Assert.Equal(1, questionResultsMultiple.AnswerCount);
        Assert.Null(questionResultsMultiple.CorrectAnswerCount);

        var questionResultsText = results.Questions[3];
        Assert.Equal(1, questionResultsText.AnswerCount);
        Assert.Null(questionResultsText.CorrectAnswerCount);
    }
}