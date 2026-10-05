namespace FormAI.Application.Interfaces;

public interface ISourceTextExtractor
{
    string Extract(string fileName, byte[] content);
}
