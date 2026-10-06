namespace FormAI.Application.AI;

public abstract record GenerationSource;

public sealed record TextSource(string Content) : GenerationSource;

public sealed record PdfSource(byte[] Content) : GenerationSource;