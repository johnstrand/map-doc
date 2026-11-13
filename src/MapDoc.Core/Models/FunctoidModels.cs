namespace MapDoc.Core.Models;

public enum FunctoidCategory
{
    Conversion = 0,
    String = 1,
    Math = 2,
    Logical = 3,
    Generators = 4,
    Collection = 5,
    Custom = 6
}

public enum PortWildcardKind
{
    None = 0,
    AnyScalar = 1,
    Any = 2,
    AnyArray = 3,
    AnyObject = 4
}

public enum OutputInferenceRule
{
    Fixed = 0,
    SameAsInputPin = 1,
    ElementOfArrayInputPin = 2,
    ArrayOfInputPin = 3,
    FromConstantConfig = 4
}

public sealed class PortTypeSpec
{
    public PortWildcardKind Wildcard { get; set; } = PortWildcardKind.None;
    public TypeDescriptor? SpecificType { get; set; }

    public static PortTypeSpec Any() => new() { Wildcard = PortWildcardKind.Any };
    public static PortTypeSpec AnyScalar() => new() { Wildcard = PortWildcardKind.AnyScalar };
    public static PortTypeSpec AnyArray() => new() { Wildcard = PortWildcardKind.AnyArray };
    public static PortTypeSpec AnyObject() => new() { Wildcard = PortWildcardKind.AnyObject };
    public static PortTypeSpec Of(TypeDescriptor type) => new() { Wildcard = PortWildcardKind.None, SpecificType = type.Clone() };

    public PortTypeSpec Clone() => new()
    {
        Wildcard = Wildcard,
        SpecificType = SpecificType?.Clone()
    };

    public string FormatDisplay(IReadOnlyDictionary<Guid, string>? typeNames = null) => Wildcard switch
    {
        PortWildcardKind.Any => "any",
        PortWildcardKind.AnyScalar => "any scalar",
        PortWildcardKind.AnyArray => "array<any>",
        PortWildcardKind.AnyObject => "object<any>",
        _ => SpecificType?.FormatDisplay(typeNames) ?? "any"
    };
}

public sealed class FunctoidInputSpec
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PortTypeSpec TypeSpec { get; set; } = PortTypeSpec.Any();
    public bool IsOptional { get; set; }

    public FunctoidInputSpec Clone() => new()
    {
        Index = Index,
        Name = Name,
        Description = Description,
        TypeSpec = TypeSpec.Clone(),
        IsOptional = IsOptional
    };
}

public sealed class FunctoidConfigParamSpec
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Placeholder { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsRequired { get; set; }

    public FunctoidConfigParamSpec Clone() => new()
    {
        Key = Key,
        Label = Label,
        Placeholder = Placeholder,
        DefaultValue = DefaultValue,
        Description = Description,
        IsRequired = IsRequired
    };
}

public sealed class FunctoidDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? PresetKey { get; set; }
    public bool IsPreset { get; set; }
    public string Name { get; set; } = string.Empty;
    public FunctoidCategory Category { get; set; } = FunctoidCategory.Custom;
    public string Symbol { get; set; } = "fx";
    public string Description { get; set; } = string.Empty;

    public List<FunctoidInputSpec> Inputs { get; set; } = [];
    public bool IsVariadic { get; set; }
    public string VariadicInputName { get; set; } = "value";
    public PortTypeSpec? VariadicInputType { get; set; }

    public PortTypeSpec OutputType { get; set; } = PortTypeSpec.AnyScalar();
    public OutputInferenceRule OutputInference { get; set; } = OutputInferenceRule.Fixed;
    public int OutputInferencePinIndex { get; set; }
    public bool AggregatesRepeatingInput { get; set; }

    public List<FunctoidConfigParamSpec> ConfigParams { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public FunctoidInputSpec? GetInputSpec(int pinIndex)
    {
        if (pinIndex < 0) return null;
        if (pinIndex < Inputs.Count) return Inputs[pinIndex];
        if (!IsVariadic) return null;

        return new FunctoidInputSpec
        {
            Index = pinIndex,
            Name = $"{VariadicInputName}{pinIndex + 1}",
            Description = $"Variadic {VariadicInputName} #{pinIndex + 1}",
            TypeSpec = (VariadicInputType ?? PortTypeSpec.AnyScalar()).Clone(),
            IsOptional = true
        };
    }
}
