using Microsoft.EntityFrameworkCore;

namespace FormAI.Infrastructure.Data;

/// <summary>
/// The one place that says how <see cref="AppDbContext"/> talks to the database. The API host
/// (<see cref="DependencyInjection.AddInfrastructure"/>) and the EF tools' design-time factory
/// (<see cref="AppDbContextFactory"/>) both go through it, so the model the tools see cannot
/// drift from the one the API runs.
/// </summary>
internal static class AppDbContextOptions
{
    public static DbContextOptionsBuilder UseAppDatabase(
        this DbContextOptionsBuilder builder,
        string? connectionString) =>
        builder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
}
