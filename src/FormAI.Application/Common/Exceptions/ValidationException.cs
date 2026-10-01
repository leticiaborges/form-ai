namespace FormAI.Application.Common.Exceptions;

public class ValidationException : Exception
{

    public IReadOnlyDictionary<string, string[]> Errors { get; }
    public ValidationErrorCode? Code { get; }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
       : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public ValidationException(ValidationErrorCode code, string message)
       : base(message)
    {
        Errors = new Dictionary<string, string[]>();
        Code = code;
    }
}
