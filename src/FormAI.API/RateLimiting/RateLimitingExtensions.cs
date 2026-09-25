using System.Globalization;
using System.Net.Mime;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace FormAI.API.RateLimiting;

public static class RateLimitPolicies
{
    public const string Generate = "generate";
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services,
    IConfiguration configuration)
    {
        var generate = configuration.GetSection(GenerateRateLimitOptions.SectionName).Get<GenerateRateLimitOptions>() ?? new GenerateRateLimitOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicies.Generate, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: GetPartitionKey(httpContext),
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = generate.PermitLimit,
                        Window = TimeSpan.FromMinutes(generate.WindowMinutes),
                        SegmentsPerWindow = generate.SegmentsPerWindow,
                        QueueLimit = 0
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;

                var message = $"You have reached the limit of {generate.PermitLimit} form generations " +
                    $"per {generate.WindowMinutes} minutes. Please try again later.";

                await response.WriteAsJsonAsync(
                    new { message, errors = (object?)null, code = (string?)null }, cancellationToken);
            };
        });

        return services;
    }

    private static string GetPartitionKey(HttpContext httpContext) =>
        httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? httpContext.User.FindFirstValue("sub")
        ?? httpContext.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";


}
