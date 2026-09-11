using FormAI.Application.Interfaces;
using FormAI.Application.Submissions;
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

    public async Task<IReadOnlyList<Submission>> GetByFormForScoringAsync(Guid formId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Submissions
            .AsSplitQuery()
            .Include(s => s.Answers)
                .ThenInclude(a => a.SelectedOptions)
            .Where(s => s.FormId == formId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Submission>> GetByFormWithAnswersAsync(Guid formId, CancellationToken cancellationToken = default)
    {
        return await _context.Submissions
        .AsNoTracking().AsSplitQuery()
        .Include(a => a.Answers)
        .ThenInclude(a => a.SelectedOptions)
        .Where(a => a.FormId == formId)
        .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<SubmissionListItem> Items, int TotalCount)> GetSubmissionListByFormAsync(Guid formId,
    int page, int pageSize,
    CancellationToken cancellationToken = default)
    {
        int skip = pageSize * (page - 1);

        int totalCount = await _context.Submissions.AsNoTracking()
              .Where(a => a.FormId == formId).CountAsync(cancellationToken);

        return (await _context.Submissions.AsNoTracking()
              .Where(a => a.FormId == formId)
              .OrderByDescending(a => a.SubmittedAt).ThenBy(a => a.Id)
              .Select(a => new SubmissionListItem(a.Id, a.SubmittedAt))
              .Skip(skip).Take(pageSize)
              .ToListAsync(cancellationToken), totalCount);
    }

    public async Task<Submission?> GetByIdWithAnswersNoTrackingAsync(Guid id, Guid formId, CancellationToken cancellationToken = default)
    {
        return await _context.Submissions
            .AsNoTracking().AsSplitQuery()
            .Include(a => a.Answers)
            .ThenInclude(a => a.SelectedOptions)
            .Where(a => a.Id == id && a.FormId == formId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}