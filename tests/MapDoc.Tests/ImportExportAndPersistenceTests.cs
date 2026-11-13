using MapDoc.Core.ImportExport;
using MapDoc.Core.Models;
using MapDoc.Core.Validation;
using MapDoc.Data;
using MapDoc.Data.Seeding;
using MapDoc.Data.Services;
using MapDoc.Migrations.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MapDoc.Tests;

public class ImportExportAndPersistenceTests
{
    [Fact]
    public void SampleJsonImporter_InfersSubtypes_NestedObjects_AndMergedArrayItems()
    {
        const string sample = """
        {
          "orderId": "550e8400-e29b-41d4-a716-446655440000",
          "placedAt": "2026-11-12T10:15:30Z",
          "buyerEmail": "customer@example.com",
          "total": 149.95,
          "isPaid": true,
          "shipping": {
            "city": "Berlin"
          },
          "lines": [
            { "sku": "A-100", "qty": 2 },
            { "sku": "B-200", "qty": 1, "promoCode": "FALL10" }
          ]
        }
        """;

        var result = JsonTypeImporter.ImportFromJsonOrSchema(sample, "WebOrder", "Commerce");
        Assert.True(result.Success);
        Assert.Equal("Sample JSON Payload", result.DetectedMode);
        Assert.Equal(3, result.GeneratedTypes.Count); // WebOrder, WebOrderShipping, WebOrderLine

        var root = result.RootType!;
        Assert.Equal("Commerce.WebOrder", root.QualifiedName);
        Assert.Equal(StringSubtype.Uuid, root.Properties.Single(p => p.Name == "orderId").Type.StringSubtype);
        Assert.Equal(StringSubtype.DateTime, root.Properties.Single(p => p.Name == "placedAt").Type.StringSubtype);
        Assert.Equal(StringSubtype.Email, root.Properties.Single(p => p.Name == "buyerEmail").Type.StringSubtype);
        Assert.Equal(NumberSubtype.Decimal, root.Properties.Single(p => p.Name == "total").Type.NumberSubtype);

        var lineType = result.GeneratedTypes.Single(t => t.Name == "WebOrderLine");
        Assert.False(lineType.Properties.Single(p => p.Name == "sku").IsOptional);
        Assert.True(lineType.Properties.Single(p => p.Name == "promoCode").IsOptional);
    }

    [Fact]
    public void JsonSchemaImporter_HandlesDefs_Refs_Formats_AndNullableUnions()
    {
        const string schema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "title": "InvoicePayload",
          "type": "object",
          "required": ["invoiceId", "billingAddress"],
          "properties": {
            "invoiceId": { "type": "string", "format": "uuid" },
            "dueDate": { "type": ["string", "null"], "format": "date" },
            "billingAddress": { "$ref": "#/$defs/PostalAddress" }
          },
          "$defs": {
            "PostalAddress": {
              "type": "object",
              "required": ["street"],
              "properties": {
                "street": { "type": "string" }
              }
            }
          }
        }
        """;

        var res = JsonTypeImporter.ImportFromJsonOrSchema(schema, "FallbackName", "ERP");
        Assert.True(res.Success);
        Assert.Equal("JSON Schema", res.DetectedMode);

        var root = res.RootType!;
        Assert.Equal("InvoicePayload", root.Name);

        var invId = root.Properties.Single(p => p.Name == "invoiceId");
        Assert.False(invId.IsOptional);
        Assert.Equal(StringSubtype.Uuid, invId.Type.StringSubtype);

        var due = root.Properties.Single(p => p.Name == "dueDate");
        Assert.True(due.IsNullable);
        Assert.True(due.IsOptional);
        Assert.Equal(StringSubtype.Date, due.Type.StringSubtype);

        var addr = root.Properties.Single(p => p.Name == "billingAddress");
        Assert.Equal(BaseTypeKind.Object, addr.Type.BaseKind);
        Assert.NotNull(addr.Type.ObjectTypeId);
    }

    [Fact]
    public async Task SqliteMigrations_DemoSeed_MarkdownExport_AndBundleRoundtrip_Succeed()
    {
        await using var conn1 = new SqliteConnection("Data Source=:memory:");
        await conn1.OpenAsync();
        var factory1 = new TestDbContextFactory(conn1);

        // 1. Run migrations + seed demo data
        await DatabaseInitializer.InitializeAsync(factory1, autoMigrate: true, seedDemoData: true);

        var catalog1 = new CatalogService(factory1);
        var summary = await catalog1.GetDashboardSummaryAsync();
        Assert.Equal(8, summary.TypeCount);
        Assert.Equal(1, summary.CustomFunctoidCount);
        Assert.True(summary.PresetFunctoidCount >= 35);
        Assert.Equal(1, summary.MappingCount);

        // 2. Load demo mapping, validate, and export Markdown & HTML specs
        var mappings = await catalog1.GetMappingsAsync();
        var demoMapping = await catalog1.GetMappingByIdAsync(mappings[0].Id);
        Assert.NotNull(demoMapping);
        Assert.Equal(2, demoMapping.GridPages.Count);

        var typeDict = await catalog1.GetTypeCatalogDictionaryAsync();
        var fnDict = await catalog1.GetFunctoidCatalogDictionaryAsync();

        var report = MappingValidator.Validate(demoMapping, typeDict, fnDict);
        Assert.Equal(0, report.ErrorCount);
        Assert.Equal(1, report.WarningCount); // Intentional CreatedUtc(datetime) -> InvoiceDate(date) demo warning

        var md = MappingSpecExporter.ExportMarkdown(demoMapping, typeDict, fnDict);
        Assert.Contains("# Transformation Specification: OrderToCash.SalesOrder_To_SalesInvoice", md);
        Assert.Contains("Header & Customer", md);
        Assert.Contains("Line Items", md);

        var html = MappingSpecExporter.ExportHtml(demoMapping, typeDict, fnDict);
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("SalesOrder_To_SalesInvoice", html);

        // 3. Export bundle & import into a fresh SQLite DB (with only preset functoids seeded)
        var bundle = await catalog1.ExportBundleAsync();

        await using var conn2 = new SqliteConnection("Data Source=:memory:");
        await conn2.OpenAsync();
        var factory2 = new TestDbContextFactory(conn2);
        await DatabaseInitializer.InitializeAsync(factory2, autoMigrate: true, seedDemoData: false);

        var catalog2 = new CatalogService(factory2);
        var (addedTypes, addedFns, addedMaps) = await catalog2.ImportBundleAsync(bundle);
        Assert.Equal(8, addedTypes);
        Assert.Equal(1, addedFns);
        Assert.Equal(1, addedMaps);
    }

    private sealed class TestDbContextFactory(SqliteConnection connection) : IDbContextFactory<MapDocDbContext>
    {
        public MapDocDbContext CreateDbContext()
        {
            var opts = new DbContextOptionsBuilder<MapDocDbContext>()
                .UseSqlite(connection, b =>
                {
                    b.MigrationsAssembly(typeof(SqliteMigrationsMarker).Assembly.FullName);
                    b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                })
                .Options;
            return new MapDocDbContext(opts);
        }
    }
}
