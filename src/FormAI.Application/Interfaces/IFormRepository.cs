using FormAI.Domain.Entities;

namespace FormAI.Application.Interfaces;

public interface IFormRepository
{
    Task<Form?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Form>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(Form form, CancellationToken cancellationToken = default);
    Task AddAsync(Form form, IReadOnlyList<FormSourceContent> sourceContents, CancellationToken cancellationToken = default);
    Task UpdateAsync(Form form, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
