using FileStorage.Endpoints;
using FileStorage.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Enterprise File Storage API",
        Version = "v1",
        Description = "A file storage API backed by AWSSDK.Extensions CouchbaseS3Client"
    });
});

// Configure storage service
var databasePath = builder.Configuration.GetValue<string>("Storage:DatabasePath")
    ?? Path.Combine(Path.GetTempPath(), "enterprise-file-storage");

builder.Services.AddSingleton<IStorageService>(sp =>
{
    var service = new StorageService(databasePath);
    return service;
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Enterprise File Storage API v1");
        options.RoutePrefix = string.Empty;
    });
}

// Ensure default bucket exists
using (var scope = app.Services.CreateScope())
{
    var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
    await storageService.EnsureBucketExistsAsync("default");
}

// Map endpoints
app.MapFileStorageEndpoints();

app.Run();

// Make Program accessible for testing
public partial class Program { }
