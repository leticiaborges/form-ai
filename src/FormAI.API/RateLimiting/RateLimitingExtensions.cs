using System.Globalization;
using System.Net.Mime;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FormAI.API.RateLimiting;

public static class RateLimitPolicies
{
    public const string Generate = "generate";
    public const string ResendVerification = "resend-verification";
    public const string Demo = "demo";
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services,
    IConfiguration configuration)
    {
        var generate = configuration.GetSection(GenerateRateLimitOptions.SectionName).Get<GenerateRateLimitOptions>() ?? new GenerateRateLimitOptions();
        var resend = configuration.GetSection(ResendVerificationRateLimitOptions.SectionName).Get<ResendVerificationRateLimitOptions>() ?? new ResendVerificationRateLimitOptions();

        var demo = configuration.GetSection(DemoRateLimitOptions.SectionName).Get<DemoRateLimitOptions>() ?? new DemoRateLimitOptions();

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

            options.AddPolicy(RateLimitPolicies.ResendVerification, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: GetPartitionKey(httpContext),
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = resend.PermitLimit,
                        Window = TimeSpan.FromMinutes(resend.WindowMinutes),
                        SegmentsPerWindow = resend.SegmentsPerWindow,
                        QueueLimit = 0
                    }));

            options.AddPolicy(RateLimitPolicies.Demo, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: GetPartitionKey(httpContext),
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = demo.PermitLimit,
                        Window = TimeSpan.FromMinutes(demo.WindowMinutes),
                        SegmentsPerWindow = demo.SegmentsPerWindow,
                        QueueLimit = 0
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;

                var policy = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                var message = policy switch
                {
                    RateLimitPolicies.ResendVerification =>
                        $"You have reached the limit of {resend.PermitLimit} verification emails " +
                        $"per {resend.WindowMinutes} minutes. Please try again later.",
                    RateLimitPolicies.Demo =>
                        $"You have reached the limit of {demo.PermitLimit} demo sessions " +
                        $"per {demo.WindowMinutes} minutes. Please try again later.",
                    _ =>
                        $"You have reached the limit of {generate.PermitLimit} form generations " +
                        $"per {generate.WindowMinutes} minutes. Please try again later."
                };

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
