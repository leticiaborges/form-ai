namespace FormAI.API.Hubs;

public interface IFormResultsClient
{
    Task ResultsUpdated(Guid formId);
}