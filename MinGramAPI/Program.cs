// ======================================================
// MinGram API
// ======================================================
//
// ASP.NET Core Minimal API.
//
// Authentication is NOT implemented inside the application.
// Authentication will be handled by Azure App Service
// Authentication (Easy Auth) using Microsoft Entra ID.
//
// Azure Easy Auth adds the authenticated user's information
// to the X-MS-CLIENT-PRINCIPAL HTTP header.
//
// Roles:
// Betraktare -> Read images
// Fotograf   -> Read + add images
// Admin      -> Full access
//
// Images are stored in Azure Blob Storage.
// The API stores the image URL and metadata.
// ======================================================


using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;



var builder = WebApplication.CreateBuilder(args);


// ======================================================
// 1. SERVICES
// ======================================================


// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("MinGramPolicy", policy =>
    {
        var origins = builder.Configuration
            .GetSection("AllowedOrigins")
            .Get<string[]>() ?? [];

        policy
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

//Blob

var blobConnectionString =
    Environment.GetEnvironmentVariable("BLOB_STORAGE_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(blobConnectionString))
{
    throw new InvalidOperationException(
        "BLOB_STORAGE_CONNECTION_STRING is not configured.");
}

builder.Services.AddSingleton(
    new BlobServiceClient(blobConnectionString));


var app = builder.Build();

// ======================================================
// 2. HTTP PIPELINE
// ======================================================


app.UseSwagger();

app.UseSwaggerUI();

app.UseCors("MinGramPolicy");


// ======================================================
// 3. TEMPORARY IN-MEMORY DATA
// ======================================================
//
// This is only demo data.
//
// The list is reset whenever the API restarts.
//
// The actual image files will be stored separately
// in Azure Blob Storage.
// ======================================================


var bilder = new List<Bild>
{
    new(
        1,
        "demo.jpg",
        "Welcome to MinGram",
        "Demo image",
        ["demo", "azure"],
        "https://placehold.co/600x400?text=MinGram"
    )
};


var nastaBildId = 2;


// ======================================================
// 4. GET /bilder
// ======================================================
//
// Allowed:
// - Betraktare
// - Fotograf
// - Admin
// ======================================================


app.MapGet("/bilder", () =>
{
    return Results.Ok(bilder);
})
.WithName("HamtaBilder")
.WithSummary("Get all images");


// ======================================================
// 5. GET /bilder/{id}
// ======================================================
//
// Allowed:
// - Betraktare
// - Fotograf
// - Admin
// ======================================================


app.MapGet("/bilder/{id:int}", (int id) =>
{
    var bild =
        bilder.FirstOrDefault(b => b.Id == id);


    if (bild is null)
    {
        return Results.NotFound();
    }


    return Results.Ok(bild);
})
.WithName("HamtaBild")
.WithSummary("Get a specific image");


// ======================================================
// 6. POST /bilder
// ======================================================
//
// Allowed:
// - Fotograf
// - Admin
//
// The image itself should first be uploaded
// to Azure Blob Storage.
//
// The Blob URL is then sent to this endpoint.
// ======================================================

app.MapPost("/bilder", async (
    IFormFile fil,
    [FromForm] string titel,
    [FromForm] string caption,
    [FromForm] string? taggar,
    HttpRequest request,
    BlobServiceClient blobServiceClient) =>
{
    var roll = HamtaRoll(request);

    if (!HarBehorighet(roll, "Fotograf"))
    {
        return Results.StatusCode(403);
    }

    var containerClient = blobServiceClient.GetBlobContainerClient("bilder");
    await containerClient.CreateIfNotExistsAsync();

    var blobNamn = $"{Guid.NewGuid()}{Path.GetExtension(fil.FileName)}";
    var blobClient = containerClient.GetBlobClient(blobNamn);

    await using (var stream = fil.OpenReadStream())
    {
        await blobClient.UploadAsync(stream, overwrite: true);
    }

    var taggLista = (taggar ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(t => t.Trim())
        .ToList();

    var bild = new Bild(
        nastaBildId++,
        fil.FileName,
        titel,
        caption,
        taggLista,
        blobClient.Uri.ToString()
    );

    bilder.Add(bild);

    return Results.Created($"/bilder/{bild.Id}", bild);
})
.WithName("LaddaUppBild")
.WithSummary("Add image - requires Fotograf or Admin")
.DisableAntiforgery();

// ======================================================
// 7. PUT /bilder/{id}
// ======================================================
//
// According to the assignment's role table,
// only Admin has full access.
//
// Fotograf may upload and read,
// but does not need permission to edit.
// ======================================================


app.MapPut("/bilder/{id:int}", (
    int id,
    BildUpdate update,
    HttpRequest request) =>
{
    var roll = HamtaRoll(request);


    if (!HarBehorighet(roll, "Admin"))
    {
        return Results.StatusCode(403);
    }


    var index =
        bilder.FindIndex(b => b.Id == id);


    if (index < 0)
    {
        return Results.NotFound();
    }


    bilder[index] = bilder[index] with
    {
        Titel =
            update.Titel
            ?? bilder[index].Titel,

        Caption =
            update.Caption
            ?? bilder[index].Caption,

        Taggar =
            update.Taggar
            ?? bilder[index].Taggar
    };


    return Results.Ok(bilder[index]);
})
.WithName("UppdateraBild")
.WithSummary("Update image - requires Admin");


// ======================================================
// 8. DELETE /bilder/{id}
// ======================================================
//
// Allowed:
// - Admin only
//
// This is the important endpoint for your
// Betraktare -> 403 Forbidden test.
// ======================================================


app.MapDelete("/bilder/{id:int}", (
    int id,
    HttpRequest request) =>
{
    var roll = HamtaRoll(request);


    if (!HarBehorighet(roll, "Admin"))
    {
        return Results.StatusCode(403);
    }


    var bild =
        bilder.FirstOrDefault(b => b.Id == id);


    if (bild is null)
    {
        return Results.NotFound();
    }


    bilder.Remove(bild);


    return Results.NoContent();
})
.WithName("RaderaBild")
.WithSummary("Delete image - requires Admin");


// ======================================================
// 9. START APPLICATION
// ======================================================


app.Run();


// ======================================================
// 10. READ ROLE FROM AZURE EASY AUTH
// ======================================================
//
// Azure App Service Easy Auth adds:
//
// X-MS-CLIENT-PRINCIPAL
//
// The value is Base64 encoded JSON containing
// the authenticated user's claims.
//
// During LOCAL development only, we return Admin
// so Swagger can test every endpoint.
//
// In Azure, a missing identity becomes Betraktare.
// ======================================================


string HamtaRoll(HttpRequest request)
{
    var header =
        request.Headers[
            "X-MS-CLIENT-PRINCIPAL"
        ]
        .FirstOrDefault();


    // ----------------------------------------------
    // Local development
    // ----------------------------------------------

    if (string.IsNullOrEmpty(header))
    {
        if (app.Environment.IsDevelopment())
        {
            return "Admin";
        }


        return "Betraktare";
    }


    try
    {
        var decodedBytes =
            Convert.FromBase64String(header);


        var json =
            Encoding.UTF8.GetString(
                decodedBytes
            );


        using var document =
            JsonDocument.Parse(json);


        var claims =
            document
                .RootElement
                .GetProperty("claims");


        foreach (var claim in claims.EnumerateArray())
        {
            var type =
                claim
                    .GetProperty("typ")
                    .GetString();


            var value =
                claim
                    .GetProperty("val")
                    .GetString();


            if (type == "roles" &&
                !string.IsNullOrEmpty(value))
            {
                return value;
            }
        }
    }
    catch
    {
        // Invalid or unknown authentication data
        // receives the lowest permission level.
    }


    return "Betraktare";
}


// ======================================================
// 11. AUTHORIZATION
// ======================================================
//
// Role hierarchy:
//
// Betraktare
//     ↓
// Fotograf
//     ↓
// Admin
// ======================================================


bool HarBehorighet(
    string roll,
    string requiredRole)
{
    return (roll, requiredRole) switch
    {
        // Everyone can read
        (_, "Betraktare")
            => true,


        // Fotograf and Admin can upload
        // makes Fotograf and Admin equivalent when the endpoint only requires Fotograf permission
        ("Fotograf" or "Admin", "Fotograf")
            => true,


        // Admin permissions
        ("Admin", "Admin")
            => true,


        // Everything else is denied
        _
            => false
    };
}


// ======================================================
// 12. DATA MODELS
// ======================================================


record Bild(
    int Id,
    string Namn,
    string Titel,
    string Caption,
    List<string> Taggar,
    string Url
);


record NyBild(
    string Namn,
    string Titel,
    string Caption,
    List<string>? Taggar,
    string Url
);


record BildUpdate(
    string? Titel,
    string? Caption,
    List<string>? Taggar
);