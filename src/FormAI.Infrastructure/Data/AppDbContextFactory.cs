using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FormAI.Infrastructure.Data;

/// <summary>
/// Lets the EF tools and migration bundles create <see cref="AppDbContext"/> without booting the
/// API host: <c>Program.cs</c> throws without <c>ConnectionStrings:Redis</c>, and any future required
/// setting would break migrations too, for configuration a migration job has no reason to hold.
/// The connection string here is only a placeholder: <c>dotnet ef ... --connection</c> and
/// <c>efbundle --connection</c> supply the real one. Everything else comes from
/// <see cref="AppDbContextOptions"/>, shared with the API.
/// </summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        builder.UseAppDatabase("Host=localhost;Database=form_ai");

        return new AppDbContext(builder.Options);
    }
}
