using ResourceOntology.Api.Models;
using ResourceOntology.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<OntologyParser>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Resource Ontology API",
        Version = "v1",
        Description = "Parse and explore OWL ontologies."
    });
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("dev", policy => policy
        .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors("dev");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Serve the built Svelte SPA (client/dist copied into wwwroot) in production.
app.UseDefaultFiles();
app.UseStaticFiles();

var parser = app.Services.GetRequiredService<OntologyParser>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var ontologyCache = new System.Collections.Concurrent.ConcurrentDictionary<string, OntologyDto>();
 
string? ResolveOntologyDir()
{
    var candidates = new[]
    {
        Path.Combine(app.Environment.ContentRootPath, "..", "ontology"),
        Path.Combine(app.Environment.ContentRootPath, "ontology"),
    };
    foreach (var c in candidates)
        if (Directory.Exists(c))
            return Path.GetFullPath(c);
    return null;
}

// (Removed deprecated default/source endpoints and helpers)

// Parse an uploaded ontology. Accepts the raw OWL/RDF-XML as the request body.
app.MapPost("/api/ontology/parse", async (HttpRequest request) =>
{
    string name = request.Query["name"].FirstOrDefault() ?? "uploaded.owl";
    try
    {
        if (request.HasFormContentType && request.Form.Files.Count > 0)
        {
            var file = request.Form.Files[0];
            name = file.FileName;
            using var stream = file.OpenReadStream();
            using var sr = new StreamReader(stream);
            return Results.Ok(parser.Parse(sr, name));
        }

        using var reader = new StreamReader(request.Body);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text))
            return Results.BadRequest(new { error = "Request body was empty." });
        using var sr2 = new StringReader(text);
        return Results.Ok(parser.Parse(sr2, name));
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to parse uploaded ontology {Name}", name);
        return Results.BadRequest(new { error = $"Could not parse '{name}': {ex.Message}" });
    }
}).Produces<OntologyDto>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status400BadRequest);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/ontology/files", () =>
{
    var dir = ResolveOntologyDir();
    if (dir == null)
        return Results.Ok(new { files = Array.Empty<object>() });

    var files = Directory.GetFiles(dir, "*.owl")
        .Select(f => new
        {
            name = Path.GetFileName(f),
            displayName = Path.GetFileNameWithoutExtension(f)
        })
        .OrderBy(f => f.displayName)
        .ToList();

    return Results.Ok(new { files });
});

app.MapGet("/api/ontology/load", (string file) =>
{
    if (string.IsNullOrWhiteSpace(file) || file.Contains("..") || file.Contains('/') || file.Contains('\\'))
        return Results.BadRequest(new { error = "Invalid file name." });

    if (ontologyCache.TryGetValue(file, out var cached))
        return Results.Ok(cached);

    var dir = ResolveOntologyDir();
    if (dir == null)
        return Results.NotFound(new { error = "Ontology directory not found." });

    var path = Path.Combine(dir, file);
    if (!File.Exists(path))
        return Results.NotFound(new { error = $"File '{file}' not found." });

    try
    {
        logger.LogInformation("Parsing ontology: {Path}", path);
        var dto = parser.ParseFile(path);
        ontologyCache[file] = dto;
        return Results.Ok(dto);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to parse ontology {File}", file);
        return Results.BadRequest(new { error = $"Could not parse '{file}': {ex.Message}" });
    }
}).Produces<OntologyDto>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status400BadRequest)
  .Produces(StatusCodes.Status404NotFound);

// SPA fallback: any non-API route returns index.html so client-side routing works.
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
