using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.UnitTests.Forms;

public class FormShowResultsTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Create_StoresFlagOnlyOnAGradedForm(bool showResultsAfterSubmit, bool isGraded, bool expected)
    {
        var form = Form.Create("Quiz", "", Guid.NewGuid(), SourceType.Text,
            true, DateTime.UtcNow.AddDays(1), showResultsAfterSubmit, isGraded);

        Assert.Equal(expected, form.ShowResultsAfterSubmit);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Update_StoresFlagOnlyOnAGradedForm(bool showResultsAfterSubmit, bool isGraded, bool expected)
    {
        // Starts from the opposite of the expected value so that the update, not the starting
        // state, is what the assertion observes.
        var form = Form.Create("Quiz", "", Guid.NewGuid(), SourceType.Text,
            true, DateTime.UtcNow.AddDays(1), showResultsAfterSubmit: !expected, isGraded: true);

        form.Update("Quiz", "", true, DateTime.UtcNow.AddDays(1), showResultsAfterSubmit, isGraded);

        Assert.Equal(expected, form.ShowResultsAfterSubmit);
    }
}
