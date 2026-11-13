using System.Text.Json.Serialization;

namespace MapDoc.Core.Models;

public sealed class MappingDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public Guid SourceTypeId { get; set; }
    public TypeDefinition? SourceType { get; set; }

    public Guid TargetTypeId { get; set; }
    public TypeDefinition? TargetType { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<MappingGridPage> GridPages { get; set; } = [];

    [JsonIgnore]
    public string QualifiedName => string.IsNullOrWhiteSpace(Namespace) ? Name : $"{Namespace}.{Name}";
}

public sealed class MappingGridPage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MappingId { get; set; }

    [JsonIgnore]
    public MappingDefinition? Mapping { get; set; }

    public string Name { get; set; } = "Main";
    public int SortOrder { get; set; }

    public List<MappingFunctoidInstance> FunctoidInstances { get; set; } = [];
    public List<MappingLink> Links { get; set; } = [];
}

public sealed class MappingFunctoidInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GridPageId { get; set; }

    [JsonIgnore]
    public MappingGridPage? GridPage { get; set; }

    public Guid FunctoidDefinitionId { get; set; }
    public FunctoidDefinition? FunctoidDefinition { get; set; }

    public double X { get; set; } = 180;
    public double Y { get; set; } = 80;
    public string Comment { get; set; } = string.Empty;
    public string? LabelOverride { get; set; }
    public Dictionary<string, string> ConfigValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MappingLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GridPageId { get; set; }

    [JsonIgnore]
    public MappingGridPage? GridPage { get; set; }

    public string? SourcePropertyPath { get; set; }
    public Guid? SourceFunctoidInstanceId { get; set; }

    public string? TargetPropertyPath { get; set; }
    public Guid? TargetFunctoidInstanceId { get; set; }
    public int? TargetInputIndex { get; set; }

    public string Comment { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsSourceProperty => !string.IsNullOrWhiteSpace(SourcePropertyPath);

    [JsonIgnore]
    public bool IsTargetProperty => !string.IsNullOrWhiteSpace(TargetPropertyPath);
}
