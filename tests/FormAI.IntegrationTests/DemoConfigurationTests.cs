using FormAI.API.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace FormAI.IntegrationTests;

public class DemoConfigurationTests
{
    private static string ApiSettingsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "FormAI.API", "appsettings.json")))
            dir = dir.Parent;

        return Path.Combine(dir!.FullName, "src", "FormAI.API", "appsettings.json");
    }

    [Fact]
    public void ShippedDefaults()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(ApiSettingsPath()).Build();

        var demo = configuration.GetSection(DemoRateLimitOptions.SectionName).Get<DemoRateLimitOptions>()!;

        Assert.Equal(10, demo.PermitLimit);
        Assert.Equal(15, demo.WindowMinutes);
        Assert.Equal(3, demo.SegmentsPerWindow);
        Assert.Null(configuration["Demo:Password"]);
    }
}
