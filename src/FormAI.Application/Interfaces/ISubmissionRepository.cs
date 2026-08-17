using FormAI.Domain.Entities;

public interface ISubmissionRepository
{
    Task<Submission?> GetByRespondentAsync(Guid formId, Guid? userId, Guid respondentToken,
        CancellationToken cancellationToken = default);
    Task AddAsync(Submission submission, CancellationToken cancellationToken = default);
}