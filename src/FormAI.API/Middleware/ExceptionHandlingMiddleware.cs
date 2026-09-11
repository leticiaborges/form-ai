using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FormAI.Application.Common.Exceptions;

namespace FormAI.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next,
ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception
exception)
    {
        var (statusCode, message, errors, code) = exception switch
        {
            NotFoundException ex => (HttpStatusCode.NotFound, ex.Message, (object?)null, (ValidationErrorCode?)null),
            ForbiddenException ex => (HttpStatusCode.Forbidden, ex.Message, (object?)null, (ValidationErrorCode?)null),
            ValidationException ex => (HttpStatusCode.BadRequest, ex.Message, (object?)ex.Errors, ex.Code),
            UnauthorizedAccessException ex => (HttpStatusCode.Unauthorized, ex.Message, (object?)null, (ValidationErrorCode?)null),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.", (object?)null, (ValidationErrorCode?)null)
        };

        if (statusCode == HttpStatusCode.InternalServerError)
            _logger.LogError(exception, "Unhandled exception");

        if (statusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            _logger.LogInformation(exception, "{StatusCode} on {Path}", statusCode, context.Request.Path);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var body = JsonSerializer.Serialize(new { message, errors, code },
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() }
            });

        await context.Response.WriteAsync(body, context.RequestAborted);
    }
}