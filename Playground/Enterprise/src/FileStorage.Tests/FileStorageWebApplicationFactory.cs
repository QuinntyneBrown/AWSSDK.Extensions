using FileStorage.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FileStorage.Tests;

public class FileStorageWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _testDatabasePath;

    public FileStorageWebApplicationFactory()
    {
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"enterprise-storage-test-{Guid.NewGuid()}");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove the existing IStorageService registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IStorageService));

            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Add a test-specific storage service with isolated database
            services.AddSingleton<IStorageService>(sp => new StorageService(_testDatabasePath));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // Clean up test database
        if (disposing && Directory.Exists(_testDatabasePath))
        {
            try
            {
                Directory.Delete(_testDatabasePath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
