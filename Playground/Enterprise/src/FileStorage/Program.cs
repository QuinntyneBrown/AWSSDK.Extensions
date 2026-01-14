using FileStorage.Endpoints;
using FileStorage.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

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
var configuredPath = builder.Configuration.GetValue<string>("Storage:DatabasePath");
var databaseDirectory = !string.IsNullOrEmpty(configuredPath)
    ? configuredPath
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "enterprise-file-storage");

// Ensure the directory exists
Directory.CreateDirectory(databaseDirectory);

// The CouchbaseS3Client expects a full path including the database name
var databasePath = Path.Combine(databaseDirectory, "filestorage");

builder.Services.AddSingleton<IStorageService>(sp =>
{
    var service = new StorageService(databasePath);
    return service;
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Enterprise File Storage API v1");
        options.RoutePrefix = string.Empty;
    });
}

// Ensure default bucket exists and enable versioning
using (var scope = app.Services.CreateScope())
{
    var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
    await storageService.EnsureBucketExistsAsync("default");
    await storageService.EnableVersioningAsync("default");
}

// Map endpoints
app.MapFileStorageEndpoints();

app.Run();

// Make Program accessible for testing
public partial class Program { }
