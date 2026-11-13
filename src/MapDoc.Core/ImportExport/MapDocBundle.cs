using MapDoc.Core.Models;

namespace MapDoc.Core.ImportExport;

public sealed class MapDocBundle
{
    public string Format { get; set; } = "MapDoc.CatalogBundle";
    public int Version { get; set; } = 1;
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;

    public List<TypeDefinition> Types { get; set; } = [];
    public List<FunctoidDefinition> CustomFunctoids { get; set; } = [];
    public List<MappingDefinition> Mappings { get; set; } = [];
}
