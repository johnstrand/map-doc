using System.Text;
using System.Text.Json;
using MapDoc.Core.ImportExport;
using MapDoc.Data;
using MapDoc.Data.Seeding;
using MapDoc.Data.Services;
using MapDoc.Migrations.Sqlite;
using MapDoc.Migrations.SqlServer;
using MapDoc.Web.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var dbProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";

builder.Services.AddDbContextFactory<MapDocDbContext>(options =>
{
    if (string.Equals(dbProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        var connStr = builder.Configuration.GetConnectionString("SqlServer")
                      ?? builder.Configuration.GetConnectionString("DefaultConnection")
                      ?? throw new InvalidOperationException("SQL Server connection string ('ConnectionStrings:SqlServer') is required when Database:Provider is SqlServer.");

        options.UseSqlServer(connStr, sql =>
        {
            sql.MigrationsAssembly(typeof(SqlServerMigrationsMarker).Assembly.FullName);
            sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(15), errorNumbersToAdd: null);
        });
    }
    else
    {
        var connStr = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=mapdoc.db";
        options.UseSqlite(connStr, sqlite =>
        {
            sqlite.MigrationsAssembly(typeof(SqliteMigrationsMarker).Assembly.FullName);
            sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        });
    }
});

builder.Services.AddScoped<CatalogService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(hub =>
    {
        hub.MaximumReceiveMessageSize = 8 * 1024 * 1024; // 8 MB for large JSON Schema / bundle imports
    });

var app = builder.Build();

// Apply migrations & seed preset functoids (+ demo showcase in Development) unless running under EF design-time tooling
if (!EF.IsDesignTime)
{
    var dbFactory = app.Services.GetRequiredService<IDbContextFactory<MapDocDbContext>>();
    var autoMigrate = app.Configuration.GetValue("Database:AutoMigrate", true);
    var seedDemo = app.Configuration.GetValue("Database:SeedDemoData", app.Environment.IsDevelopment());
    await DatabaseInitializer.InitializeAsync(dbFactory, autoMigrate, seedDemo);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();

// Export endpoints for Markdown spec, printable HTML spec, and portable .mapdoc.json bundle
app.MapGet("/api/export/mappings/{id:guid}/markdown", async (Guid id, CatalogService catalog, CancellationToken ct) =>
{
    var mapping = await catalog.GetMappingByIdAsync(id, ct);
    if (mapping is null) return Results.NotFound();

    var types = await catalog.GetTypeCatalogDictionaryAsync(ct);
    var functoids = await catalog.GetFunctoidCatalogDictionaryAsync(ct);
    var md = MappingSpecExporter.ExportMarkdown(mapping, types, functoids);
    var safeName = SanitizeFileName(mapping.Name) + ".spec.md";

    return Results.File(Encoding.UTF8.GetBytes(md), "text/markdown; charset=utf-8", safeName);
});

app.MapGet("/api/export/mappings/{id:guid}/html", async (Guid id, bool? download, CatalogService catalog, CancellationToken ct) =>
{
    var mapping = await catalog.GetMappingByIdAsync(id, ct);
    if (mapping is null) return Results.NotFound();

    var types = await catalog.GetTypeCatalogDictionaryAsync(ct);
    var functoids = await catalog.GetFunctoidCatalogDictionaryAsync(ct);
    var html = MappingSpecExporter.ExportHtml(mapping, types, functoids);

    if (download == true)
    {
        var safeName = SanitizeFileName(mapping.Name) + ".spec.html";
        return Results.File(Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", safeName);
    }

    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/api/export/bundle", async (CatalogService catalog, CancellationToken ct) =>
{
    var bundle = await catalog.ExportBundleAsync(ct);
    var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });
    return Results.File(
        Encoding.UTF8.GetBytes(json),
        "application/json; charset=utf-8",
        $"mapdoc-catalog-{DateTime.UtcNow:yyyyMMdd-HHmm}.mapdoc.json");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string SanitizeFileName(string name)
{
    var invalid = Path.GetInvalidFileNameChars();
    var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    return string.IsNullOrWhiteSpace(cleaned) ? "mapping" : cleaned;
}
