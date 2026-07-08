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

// Resolve the bundled ontology shipped with the project.
string? ResolveDefaultOntology()
{
    var configured = builder.Configuration["Ontology:DefaultPath"];
    var candidates = new[]
    {
        configured,
        Path.Combine(app.Environment.ContentRootPath, "..", "ontology", "Resource.owl"),
        Path.Combine(app.Environment.ContentRootPath, "ontology", "Resource.owl"),
        Path.Combine(AppContext.BaseDirectory, "ontology", "Resource.owl"),
    };
    foreach (var c in candidates)
        if (!string.IsNullOrWhiteSpace(c) && File.Exists(c))
            return Path.GetFullPath(c);
    return null;
}

// Cache the default parse — the bundled ontology never changes at runtime.
OntologyDto? defaultCache = null;

OntologyDto? GetDefault()
{
    if (defaultCache != null) return defaultCache;
    var path = ResolveDefaultOntology();
    if (path == null) { logger.LogWarning("Default ontology not found."); return null; }
    logger.LogInformation("Parsing default ontology: {Path}", path);
    defaultCache = parser.ParseFile(path);
    return defaultCache;
}

app.MapGet("/api/ontology/default", () =>
{
    var dto = GetDefault();
    return dto is null
        ? Results.NotFound(new { error = "Bundled ontology (ontology/Resource.owl) was not found." })
        : Results.Ok(dto);
}).Produces<OntologyDto>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/ontology/source", () =>
{
    var path = ResolveDefaultOntology();
    return path is null ? Results.NotFound() : Results.Text(File.ReadAllText(path), "application/xml");
});

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

// SPA fallback: any non-API route returns index.html so client-side routing works.
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
