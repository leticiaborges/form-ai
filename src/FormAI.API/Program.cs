using FormAI.API.Hubs;
using FormAI.API.Middleware;
using FormAI.API.RateLimiting;
using FormAI.API.Workers;
using FormAI.Application.Interfaces;
using FormAI.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;


AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromMilliseconds(500));

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers()
     .AddJsonOptions(options =>
         options.JsonSerializerOptions.Converters.Add(
             new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();


builder.Services.AddSwaggerGen(c =>
{
    c.CustomSchemaIds(type => type.FullName);

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
    };
    c.AddSecurityDefinition("Bearer", scheme);

    c.AddSecurityRequirement(document =>
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Two proxies sit in front of the API: CloudFront, then the ALB. Each appends the address it
    // saw, so the client IP is the second entry from the right. Trusting that many entries is safe
    // only because the ALB rejects requests that did not come through CloudFront (infra:
    // X-Origin-Verify header); otherwise a caller could prepend a fake address.
    options.ForwardLimit = 2;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddApiRateLimiting(builder.Configuration);


builder.Services.AddSignalR().AddStackExchangeRedis(
    builder.Configuration.GetConnectionString("Redis") ??
    throw new InvalidOperationException("ConnectionStrings:Redis is missing")
);

builder.Services.AddSingleton<IFormResultsNotifier, SignalRFormResultsNotifier>();

builder.Services.Configure<RefreshTokenCleanupOptions>(builder.Configuration.GetSection("RefreshTokenCleanup"));
builder.Services.AddHostedService<RefreshTokenCleanupWorker>();

builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapHub<FormResultsHub>("/hubs/form-results");
app.MapHealthChecks("/health");

await app.RunAsync();