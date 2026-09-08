using System.Net;
using System.Text.Json;
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
        var (statusCode, message, errors) = exception switch
        {
            NotFoundException ex => (HttpStatusCode.NotFound, ex.Message,
(object?)null),
            ForbiddenException ex => (HttpStatusCode.Forbidden, ex.Message,
(object?)null),
            ValidationException ex => (HttpStatusCode.BadRequest, ex.Message,
(object?)ex.Errors),
            UnauthorizedAccessException ex => (HttpStatusCode.Unauthorized, ex.Message,
            (object?)null),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.", (object?)null)
        };

        if (statusCode == HttpStatusCode.InternalServerError)
            _logger.LogError(exception, "Unhandled exception");

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var body = JsonSerializer.Serialize(new { message, errors },
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
JsonNamingPolicy.CamelCase
            });

        await context.Response.WriteAsync(body, context.RequestAborted);
    }
}