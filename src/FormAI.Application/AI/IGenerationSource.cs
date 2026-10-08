namespace FormAI.Application.AI;

public interface IGenerationSource;

public sealed record TextSource(string Content) : IGenerationSource;

public sealed record PdfSource(byte[] Content) : IGenerationSource;