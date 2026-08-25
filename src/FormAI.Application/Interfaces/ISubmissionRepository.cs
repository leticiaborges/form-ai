using FormAI.Domain.Entities;


namespace FormAI.Application.Interfaces
{
    public interface ISubmissionRepository
    {
        Task<int> CountByFormAsync(Guid formId, CancellationToken cancellationToken = default);
        Task<Submission?> GetByRespondentAsync(Guid formId, Guid? userId, Guid respondentToken,
            CancellationToken cancellationToken = default);
        Task AddAsync(Submission submission, CancellationToken cancellationToken = default);
    }
}
