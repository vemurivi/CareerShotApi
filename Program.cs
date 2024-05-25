using Azure;
using Azure.Storage.Blobs;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CareerShot API",
        Version = "v1"
    });
});

var app = builder.Build();

// Conditionally use HTTPS redirection based on environment variable
var useHttpsRedirection = Environment.GetEnvironmentVariable("USE_HTTPS_REDIRECTION")?.ToLower() == "true";

if (useHttpsRedirection)
{
    app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CareerShot API v1");
    });
}

app.UseRouting();

// Load configuration settings from environment variables
var blobServiceEndpoint = Environment.GetEnvironmentVariable("BlobServiceEndpoint");
var blobServiceSasToken = Environment.GetEnvironmentVariable("BlobServiceSasToken");
var storageAccountConnectionString = Environment.GetEnvironmentVariable("StorageAccountConnectionString");

Console.WriteLine($"BlobServiceEndpoint: {blobServiceEndpoint}");
Console.WriteLine($"BlobServiceSasToken: {blobServiceSasToken}");
Console.WriteLine($"StorageAccountConnectionString: {storageAccountConnectionString}");

if (string.IsNullOrEmpty(blobServiceEndpoint) || string.IsNullOrEmpty(blobServiceSasToken) || string.IsNullOrEmpty(storageAccountConnectionString))
{
    throw new InvalidOperationException("One or more environment variables are not set.");
}

var blobServiceClient = new BlobServiceClient(new Uri($"{blobServiceEndpoint}?{blobServiceSasToken}"));
var tableClient = new TableClient(storageAccountConnectionString, "careershotinformation");

app.MapPost("/api/register", async (HttpRequest req) =>
{
    if (!req.HasFormContentType)
    {
        return Results.BadRequest("Unsupported media type");
    }

    var form = await req.ReadFormAsync();
    var jsonData = form["jsonData"];
    var data = JsonConvert.DeserializeObject<RegisterRequest>(jsonData);

       var firstLetter = data.Name[0].ToString().ToUpper();  // Get the first letter and convert to uppercase

    var formData = new UserData
    {
        PartitionKey = firstLetter,  // Using the first letter of the name as the PartitionKey
        RowKey = Guid.NewGuid().ToString(),  // Unique identifier for each form submission
        Name = data.Name,
        Description = data.Description,
        LinkedIn = data.LinkedIn,
        GitHub = data.GitHub,
        Skills = JsonConvert.SerializeObject(data.Skills)
    };

    // Save form data to Table Storage
    await tableClient.AddEntityAsync(formData);

    // Upload files to Blob Storage
    foreach (var file in form.Files)
    {
        var extension = Path.GetExtension(file.FileName);
        var blobName = $"{data.Name}{extension}";
        var containerClient = blobServiceClient.GetBlobContainerClient("mediadev");
        var blobClient = containerClient.GetBlobClient(blobName);
        using var stream = file.OpenReadStream();
        await blobClient.UploadAsync(stream, true);
    }

    return Results.Ok("Data saved to Table Storage and files uploaded to Blob Storage.");
})
.WithName("Register");

app.Run();

public class UserData : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LinkedIn { get; set; } = string.Empty;
    public string GitHub { get; set; } = string.Empty;
    public string Skills { get; set; } = string.Empty; // JSON string representing the skills
    public ETag ETag { get; set; }
}

public class RegisterRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LinkedIn { get; set; } = string.Empty;
    public string GitHub { get; set; } = string.Empty;
    public Dictionary<string, List<string>> Skills { get; set; } = new Dictionary<string, List<string>>();
    public List<IFormFile> Files { get; set; } = new List<IFormFile>();
}
