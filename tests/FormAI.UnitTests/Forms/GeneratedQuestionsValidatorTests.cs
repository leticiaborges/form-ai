using FormAI.Application.AI;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.Validation;
using FormAI.Domain.Enums;

namespace FormAI.UnitTests.Forms;

public class GeneratedQuestionsValidatorTests
{
    private static GeneratedQuestion Q(string text = "Q?", QuestionType type = QuestionType.Text,
      string? correctAnswer = null, params string[] options) =>
      new(text, type, IsRequired: true, correctAnswer, options.Select(o => new GeneratedOption(o, null)).ToList());

    private static void AssertInvalid(params GeneratedQuestion[] questions)
    {
        var ex = Assert.Throws<GenerationException>(() => GeneratedQuestionsValidator.Validate(questions));
        Assert.Equal(ValidationErrorCode.GenerationOutputInvalid, ex.Code);
    }

    [Fact]
    public void ADraftWithOneQuestionOfEachType_Passes()
    {
        GeneratedQuestionsValidator.Validate([
            Q("S?", QuestionType.Single, null, "A", "B"),
            Q("M?", QuestionType.Multiple, null, "A", "B", "C"),
            Q("T?", QuestionType.Text, "Because"),
            Q("N?", QuestionType.Numeric, "42")]);
    }

    [Fact]
    public void ADraftWithNoQuestions_IsInvalid() => AssertInvalid();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void QuestionTextIsRequired(string text) => AssertInvalid(Q(text));

    [Fact]
    public void QuestionTextOver1024Characters_IsInvalid() => AssertInvalid(Q(new string('x', 1025)));

    [Fact]
    public void QuestionTextOf1024Characters_Passes() =>
        GeneratedQuestionsValidator.Validate([Q(new string('x', 1024))]);

    [Fact]
    public void ACorrectAnswerOver1024Characters_IsInvalid() => AssertInvalid(Q(correctAnswer: new string('x', 1025)));

    [Fact]
    public void AnUndefinedType_IsInvalid() => AssertInvalid(Q(type: (QuestionType)0));

    [Fact]
    public void ASingleQuestionWithOneOption_IsInvalid() => AssertInvalid(Q(type: QuestionType.Single, options: "Only"));

    [Fact]
    public void ATextQuestionWithOptions_IsInvalid() => AssertInvalid(Q(type: QuestionType.Text, options: ["A", "B"]));

    [Fact]
    public void AnOptionWithNoText_IsInvalid() => AssertInvalid(Q(type: QuestionType.Single, options: ["A", " "]));

    [Fact]
    public void AnOptionOver1024Characters_IsInvalid() =>
        AssertInvalid(Q(type: QuestionType.Single, options: ["A", new string('x', 1025)]));

    [Fact]
    public void DuplicateOptions_AreInvalid_LikeInTheEditor() =>
        AssertInvalid(Q(type: QuestionType.Single, options: ["A", " a "]));
}
