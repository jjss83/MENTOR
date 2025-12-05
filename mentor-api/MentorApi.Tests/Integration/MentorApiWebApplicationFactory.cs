using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace MentorApi.Tests.Integration;

public class MentorApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _resultsDirectory;

    public MentorApiWebApplicationFactory()
    {
        _resultsDirectory = Path.Combine(Path.GetTempPath(), "mentor-api-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_resultsDirectory);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MentorApi:ResultsDirectory"] = _resultsDirectory
            });
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsyncInternal();

    private async Task DisposeAsyncInternal()
    {
        try
        {
            if (Directory.Exists(_resultsDirectory))
            {
                Directory.Delete(_resultsDirectory, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }

        await base.DisposeAsync();
    }
}
