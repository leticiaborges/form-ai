using FormAI.Application.AI;

namespace FormAI.Infrastructure.AI;

public class ClaudeFormGenerationService : IFormGenerationService
{
    public Task<IReadOnlyList<GeneratedQuestion>> GenerateAsync(
        string sourceText,
        GenerationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
