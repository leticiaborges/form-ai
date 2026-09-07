using FormAI.Domain.Entities;


namespace FormAI.Application.Interfaces
{
    public interface ISubmissionRepository
    {
        Task<int> CountByFormAsync(Guid formId, CancellationToken cancellationToken = default);
        Task<Submission?> GetByRespondentAsync(Guid formId, Guid? userId, Guid respondentToken,
            CancellationToken cancellationToken = default);
        Task AddAsync(Submission submission, CancellationToken cancellationToken = default);

        /// <summary>
        /// Every submission of a form with the answers and selected options needed to rescore it.
        /// The submissions come back tracked and are not saved here: the caller commits them,
        /// so that rescoring and the editor save that triggered it land in one transaction.
        /// </summary>
        Task<IReadOnlyList<Submission>> GetByFormForScoringAsync(Guid formId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Every submission of a form with its answers and selected options, untracked, for the
        /// owner's results view. Separate from <see cref="GetByFormForScoringAsync"/>, which
        /// deliberately returns tracked entities so a rescore can be committed by its caller.
        /// </summary>
        Task<IReadOnlyList<Submission>> GetByFormWithAnswersAsync(Guid formId,
            CancellationToken cancellationToken = default);
    }
}
