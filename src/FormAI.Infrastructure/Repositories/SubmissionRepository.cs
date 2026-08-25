using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FormAI.Infrastructure.Repositories;

public class SubmissionRepository : ISubmissionRepository
{
    private readonly AppDbContext _context;

    public SubmissionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Submission?> GetByRespondentAsync(Guid formId, Guid? userId, Guid respondentToken,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Submission> query = _context.Submissions.Where(s => s.FormId == formId);

        query = userId is not null
            ? query.Where(s => s.UserId == userId)
            : query.Where(s => s.RespondentToken == respondentToken);

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(Submission submission, CancellationToken cancellationToken = default)
    {
        await _context.Submissions.AddAsync(submission, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> CountByFormAsync(Guid formId, CancellationToken cancellationToken = default)
    {
        return await _context.Submissions.CountAsync(s => s.FormId == formId, cancellationToken);
    }
}