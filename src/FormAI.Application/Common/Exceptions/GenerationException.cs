namespace FormAI.Application.Common.Exceptions;

public class GenerationException : Exception
{
    public ValidationErrorCode Code { get; }

    public GenerationException(ValidationErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }
}
