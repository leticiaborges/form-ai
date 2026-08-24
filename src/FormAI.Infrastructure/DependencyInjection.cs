using System.Text;
using FormAI.Application.AI;
using FormAI.Application.Forms.CloseForm;
using FormAI.Application.Forms.CreateForm;
using FormAI.Application.Forms.DeleteForm;
using FormAI.Application.Forms.GenerateForm;
using FormAI.Application.Forms.GetForm;
using FormAI.Application.Forms.SaveFormEditor;
using FormAI.Application.Forms.UpdateForm;
using FormAI.Application.Interfaces;
using FormAI.Application.Submissions.GetFormToAnswer;
using FormAI.Application.Submissions.GetMySubmission;
using FormAI.Application.Submissions.SubmitForm;
using FormAI.Application.Users.Auth;
using FormAI.Infrastructure.AI;
using FormAI.Infrastructure.Data;
using FormAI.Infrastructure.Email;
using FormAI.Infrastructure.Repositories;
using FormAI.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace FormAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")).UseSnakeCaseNamingConvention());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IFormRepository, FormRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUserTokenConfirmationRepository, UserTokenConfirmationRepository>();
        services.AddScoped<ISubmissionRepository, SubmissionRepository>();

        services.Configure<ClaudeSettings>(configuration.GetSection("Claude"));
        services.AddHttpClient("claude", (client) =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/");
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        });
        services.AddScoped<IFormGenerationService, ClaudeFormGenerationService>();

        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.Configure<EmailSettings>(configuration.GetSection("Email"));
        services.AddScoped<IEmailService, EmailService>();

        services.AddScoped<RegisterHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RefreshTokenHandler>();
        services.AddScoped<VerifyEmailHandler>();

        services.AddScoped<CreateFormHandler>();
        services.AddScoped<CloseFormHandler>();
        services.AddScoped<DeleteFormHandler>();
        services.AddScoped<GetFormHandler>();
        services.AddScoped<GetFormsByUserHandler>();
        services.AddScoped<UpdateFormHandler>();
        services.AddScoped<GenerateFormHandler>();
        services.AddScoped<SaveFormEditorHandler>();
        services.AddScoped<GetFormToAnswerHandler>();
        services.AddScoped<GetMySubmissionHandler>();
        services.AddScoped<SubmitFormHandler>();

        services.AddScoped<IJwtService, JwtService>();

        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));

        var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()
            ?? throw new InvalidOperationException("Jwt configuration section is missing.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                    ClockSkew = TimeSpan.Zero,
                };
            });

        return services;
    }
}
