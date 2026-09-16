namespace FormAI.Application.Interfaces;

public interface IFormResultsNotifier
{
   Task NotifyResultsChangedAsync(Guid formId, CancellationToken cancellationToken = default);
}
