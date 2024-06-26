using Azure;
using Azure.Storage.Blobs;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Load configuration settings from environment variables
var blobServiceEndpoint = Environment.GetEnvironmentVariable("BlobServiceEndpoint");
var blobServiceSasToken = Environment.GetEnvironmentVariable("BlobServiceSasToken");
var storageAccountConnectionString = Environment.GetEnvironmentVariable("StorageAccountConnectionString");

if (string.IsNullOrEmpty(blobServiceEndpoint) || string.IsNullOrEmpty(blobServiceSasToken) || string.IsNullOrEmpty(storageAccountConnectionString))
{
    throw new InvalidOperationException("One or more environment variables are not set.");
}

Console.WriteLine($"BlobServiceEndpoint: {blobServiceEndpoint}");
Console.WriteLine($"BlobServiceSasToken: {blobServiceSasToken}");
Console.WriteLine($"StorageAccountConnectionString: {storageAccountConnectionString}");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "CareerShot API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter into field the word 'Bearer' followed by a space and the JWT token",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement {
    {
        new OpenApiSecurityScheme {
            Reference = new OpenApiReference {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        },
        new string[] { }
    }});
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"{builder.Configuration["AzureAd:Instance"]}{builder.Configuration["AzureAd:TenantId"]}";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = $"{builder.Configuration["AzureAd:Instance"]}{builder.Configuration["AzureAd:TenantId"]}/v2.0",
            ValidAudience = builder.Configuration["AzureAd:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["AzureAd:ClientSecret"]))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CareerShot API v1");
    });
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

var blobServiceClient = new BlobServiceClient(new Uri($"{blobServiceEndpoint}?{blobServiceSasToken}"));
var tableClient = new TableClient(storageAccountConnectionString, "careershotinformation");

app.MapPost("/api/register", [Authorize] async (HttpRequest req) =>
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

     var normalizedName = data.Name.ToLower().Replace(" ", "");

    // Save form data to Table Storage
    await tableClient.AddEntityAsync(formData);

    // Upload files to Blob Storage
    foreach (var file in form.Files)
    {
        var extension = Path.GetExtension(file.FileName);
        var blobName = $"{normalizedName}{extension}";
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
    public Dictionary<string, List<Skill>> Skills { get; set; } = new Dictionary<string, List<Skill>>();
}

public class Skill
{
    public string Name { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
}
