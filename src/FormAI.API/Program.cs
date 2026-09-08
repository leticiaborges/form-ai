using FormAI.API.Middleware;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Infrastructure;
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

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("FrontendDev");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();