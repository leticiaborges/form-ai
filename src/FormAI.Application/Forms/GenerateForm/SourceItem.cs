using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GenerateForm;

public record SourceItem(
    string SourceText,
    SourceType SourceType,
    String FileName = ""
);