using FormAI.Domain.Enums;

namespace FormAI.Domain.Entities;

public class FormSourceContent
{
    public Guid Id { get; private set; }
    public Guid FormId { get; private set; }
    public SourceType SourceType { get; private set; }
    public string Content { get; private set; }
    public string? FileName { get; private set; }
    public int Order { get; private set; }
    public Form? Form { get; }

    private FormSourceContent()
    {
        Content = string.Empty;
    }

    public static FormSourceContent Create(Guid formId,
    SourceType sourceType, string content, int order, string? fileName = null)
    {
        return new FormSourceContent
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            SourceType = sourceType,
            Content = content,
            Order = order,
            FileName = fileName
        };
    }
}
