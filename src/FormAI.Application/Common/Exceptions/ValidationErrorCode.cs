namespace FormAI.Application.Common.Exceptions;

public enum ValidationErrorCode
{
    GenericError,
    FormExpired,
    AlreadySubmitted,
    EmailNotVerified,
    GenerationOutputInvalid,
    GenerationBudgetReached,
    GenerationUnavailable,
    SourceFileUnsupported,
    SourceFileUnreadable,
    SourceFileTooLarge,
}