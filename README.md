# MapDoc

A **.NET 10 Blazor** application for visually designing, validating, and documenting data transformations in the style of Microsoft BizTalk Mapper.

## Features

- **Rich Schema & Type Studio (`/types`)**:
  - Define reusable types organized by **Namespaces** and **Tags**.
  - Base types: `string`, `boolean`, `number`, `array`, `object` with OpenAPI-grade subtypes (`uuid`, `date`, `time`, `datetime`, `duration`, `uri`, `email`, `base64`, custom formats; `int32`, `int64`, `float`, `double`, `decimal`).
  - Recursive **Arrays** (arrays of formatted scalars, arrays of objects, multidimensional arrays) and **Object** references (including **self-referential** and mutually recursive schemas, plus untyped objects).
  - `Nullable` and `Optional` flags on every property.
  - **JSON & JSON Schema Importer**: Infer types (including nested child object types) from sample JSON payloads or Draft 2020-12 JSON Schemas.
- **Preset & Custom Functoids (`/functoids`)**:
  - 40 built-in **Preset Functoids** across Conversion (`ToString`, `ToInt32`, `ToDecimal`, `ParseDate`, `FormatDate`, ...), String (`Concat`, `Substring`, `Split`, ...), Math (`Add`, `Multiply`, `Round`, ...), Logical (`Equals`, `IfThenElse`, `ValueMapping`, `Coalesce`, ...), 0-input Generators (`Constant`, `UtcNow`, `Today`, `NewUuid`), and Collection aggregators (`Count`, `First`, `Last`, `Join`, `Sum`, `ToArray`).
  - Define reusable **Custom Functoids** with `0..N` or variadic typed/wildcard inputs, `1` output, optional literal configuration fields, and array-aggregation behavior.
- **BizTalk-Style Visual Mapper Studio (`/mappings/{id}`)**:
  - **3-Pane Split Stage**: Collapsible Source Type tree (left), interactive 60fps SVG Bézier wire & Functoid canvas (center), and Target Type tree (right).
  - **Grid Pages**: Organize large transformations across multiple named tabs sharing the same Source and Target trees.
  - **Collapsed Ancestor Roll-up**: Collapsing a tree container automatically rolls child connections up to the visible parent row as dashed indicator wires.
  - **Comments & Annotations**: Attach rich documentation comments to **any property-to-property wire** and **any functoid instance**, plus 1-click *"Insert Functoid on Wire"*.
  - **Static Type & Cardinality Validation**:
    - **Valid**: Identical types and safe scalar widening (`uuid` → `string`, `int32` → `decimal`/`double`).
    - **Warning**: Cross-scalar or narrowing conversions (`int32` → `string`, `datetime` → `date`), `Nullable`/`Optional` → required/non-nullable targets, repeating array item (`N`) → single scalar (`1`) without an aggregator functoid, or multiple wires targeting the same property.
    - **Error**: `Object`/`Array` ↔ `Scalar`, `Object` ↔ `Array`, incompatible named `Object` types, or functoid DAG cycles.
- **Documentation & Catalog Export**:
  - Export transformations to **GitHub-Flavored Markdown (`.md`)** or **Printable HTML Specs**.
  - Export and import portable **`.mapdoc.json`** catalog bundles.

## Running Locally (SQLite)

```powershell
dotnet run --project src/MapDoc.Web
```

In `Development`, MapDoc automatically applies SQLite migrations (`mapdoc.dev.db`), seeds the 40 preset functoids, and populates a demo `OrderToCash.SalesOrder_To_SalesInvoice` transformation across two Grid Pages.

## Running Automated Tests

```powershell
dotnet test
```

## Deploying with Azure SQL Server

In `Production` (`ASPNETCORE_ENVIRONMENT=Production`), `appsettings.Production.json` switches `Database:Provider` to `SqlServer` and disables demo sample seeding (only Preset Functoids are seeded).

Provide your Azure SQL connection string via configuration or Azure App Service settings (supports Managed Identity via `Authentication=Active Directory Default`):

```json
{
  "Database": {
    "Provider": "SqlServer",
    "AutoMigrate": true,
    "SeedDemoData": false
  },
  "ConnectionStrings": {
    "SqlServer": "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=MapDocDb;Authentication=Active Directory Default;Encrypt=True;"
  }
}
```

### Generating New EF Core Migrations (Dual Provider)

```powershell
# SQLite (Development)
$env:Database__Provider='Sqlite'
dotnet tool run dotnet-ef migrations add <MigrationName> --project src/MapDoc.Migrations.Sqlite --startup-project src/MapDoc.Web

# SQL Server (Azure Production)
$env:Database__Provider='SqlServer'
dotnet tool run dotnet-ef migrations add <MigrationName> --project src/MapDoc.Migrations.SqlServer --startup-project src/MapDoc.Web
```
