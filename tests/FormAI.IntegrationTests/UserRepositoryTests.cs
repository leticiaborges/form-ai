using FormAI.Domain.Entities;
using FormAI.Infrastructure.Data;
using FormAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace FormAI.IntegrationTests;

public class UserRepositoryTests : IAsyncLifetime
{
     private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
     .Build();
    
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }
   
    public async Task DisposeAsync()
    {
         await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task AddAsync_ShouldPersistUser()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;

        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync();

        var repo = new UserRepository(context);
        var user = User.Create("Leticia", "test@example.com", "hash");
        await repo.AddAsync(user);

        var foundUser = await repo.GetByEmailAsync("test@example.com");
        Assert.NotNull(foundUser);
        Assert.Equal("Leticia", foundUser.Name);
    }
}
