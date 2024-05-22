using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CareerShot API v1");
    });
}

// Load configuration settings from environment variables
var blobServiceEndpoint = builder.Configuration["BlobServiceEndpoint"];
var blobServiceSasToken = builder.Configuration["BlobServiceSasToken"];
var storageAccountConnectionString = builder.Configuration["StorageAccountConnectionString"];

var blobServiceClient = new BlobServiceClient(new Uri($"{blobServiceEndpoint}?{blobServiceSasToken}"));
var tableClient = new TableClient(storageAccountConnectionString, "careershotinformation");

app.MapPost("/api/register", async (HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body);
    var body = await reader.ReadToEndAsync();
    var data = JsonConvert.DeserializeObject<RegisterRequest>(body);

    var formData = new UserData
    {
        PartitionKey = data.Name,  // Using full name as PartitionKey
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
    foreach (var file in data.Files)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient("media-dev");
        var blobClient = containerClient.GetBlobClient(file.FileName);
        using var stream = file.OpenReadStream();
        await blobClient.UploadAsync(stream, true);
    }

    return Results.Ok("Data saved to Table Storage and files uploaded to Blob Storage.");
});

app.Run();

public class UserData : ITableEntity
{
    public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string LinkedIn { get; set; }
    public string GitHub { get; set; }
    public string Skills { get; set; } // JSON string representing the skills
    public ETag ETag { get; set; }
}

public class RegisterRequest
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string LinkedIn { get; set; }
    public string GitHub { get; set; }
    public Dictionary<string, List<string>> Skills { get; set; }
    public List<IFormFile> Files { get; set; }
}
