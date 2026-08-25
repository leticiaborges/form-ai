namespace FormAI.Application.Forms.Validation;

public static class QuestionPointsValidator
{
    public const int MinPoints = 0;
    public const int MaxPoints = 100;

    /// <summary>
    /// Applies the points rules for one question of a graded form: points are required and within
    /// range. Adds any problem to <paramref name="errors"/> under the key
    /// "questions[{questionIndex}].points".
    ///
    /// The editor coerces the field before it ever sends it, so this is a guard against anything
    /// else reaching the endpoint — never the owner's normal experience.
    /// </summary>
    public static void Validate(
        int questionIndex,
        int? points,
        Dictionary<string, string[]> errors)
    {
        if (points is null)
        {
            errors[$"questions[{questionIndex}].points"] =
                new[] { "Points are required on a graded form." };
            return;
        }

        if (points < MinPoints || points > MaxPoints)
            errors[$"questions[{questionIndex}].points"] =
                new[] { $"Points must be between {MinPoints} and {MaxPoints}." };
    }
}
