using FormAI.Domain.Enums;

namespace FormAI.Domain.Entities;

public class Submission
{
    public Guid Id { get; private set; }
    public Guid FormId { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid RespondentToken { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTime SubmittedAt { get; private set; }
    public int? Score { get; private set; }

    public Form? Form { get; private set; }
    public User? User { get; private set; }
    public List<Answer> Answers { get; private set; } = new();

    private Submission() { }

    public static Submission Create(Guid formId, Guid? userId, Guid respondentToken, string ipAddress,
    int? score)
    {
        return new Submission
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            UserId = userId,
            RespondentToken = respondentToken,
            IpAddress = ipAddress,
            SubmittedAt = DateTime.UtcNow,
            Score = score
        };
    }

    public void SetAnswers(List<Answer> answers)
    {
        Answers.Clear();
        Answers.AddRange(answers);
    }

    public void SetScore(int? score) => Score = score;
}
