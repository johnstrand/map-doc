using System.Text.Json.Serialization;

namespace MapDoc.Core.Models;

public enum BaseTypeKind
{
    String = 0,
    Boolean = 1,
    Number = 2,
    Array = 3,
    Object = 4
}

public enum StringSubtype
{
    None = 0,
    Uuid = 1,
    Date = 2,
    Time = 3,
    DateTime = 4,
    Duration = 5,
    Uri = 6,
    Email = 7,
    Base64 = 8,
    Custom = 9
}

public enum NumberSubtype
{
    None = 0,
    Int32 = 1,
    Int64 = 2,
    Float = 3,
    Double = 4,
    Decimal = 5
}

public sealed class TypeDescriptor : IEquatable<TypeDescriptor>
{
    public BaseTypeKind BaseKind { get; set; } = BaseTypeKind.String;
    public StringSubtype StringSubtype { get; set; } = StringSubtype.None;
    public string? CustomSubtype { get; set; }
    public NumberSubtype NumberSubtype { get; set; } = NumberSubtype.None;
    public Guid? ObjectTypeId { get; set; }
    public TypeDescriptor? ElementType { get; set; }
    public bool ElementIsNullable { get; set; }

    [JsonIgnore]
    public bool IsScalar => BaseKind is BaseTypeKind.String or BaseTypeKind.Boolean or BaseTypeKind.Number;

    public static TypeDescriptor ScalarString(StringSubtype subtype = StringSubtype.None, string? customSubtype = null) => new()
    {
        BaseKind = BaseTypeKind.String,
        StringSubtype = subtype,
        CustomSubtype = subtype == StringSubtype.Custom ? customSubtype : null
    };

    public static TypeDescriptor ScalarBoolean() => new()
    {
        BaseKind = BaseTypeKind.Boolean
    };

    public static TypeDescriptor ScalarNumber(NumberSubtype subtype = NumberSubtype.None) => new()
    {
        BaseKind = BaseTypeKind.Number,
        NumberSubtype = subtype
    };

    public static TypeDescriptor ObjectOf(Guid? objectTypeId = null) => new()
    {
        BaseKind = BaseTypeKind.Object,
        ObjectTypeId = objectTypeId
    };

    public static TypeDescriptor ArrayOf(TypeDescriptor elementType, bool elementIsNullable = false) => new()
    {
        BaseKind = BaseTypeKind.Array,
        ElementType = elementType.Clone(),
        ElementIsNullable = elementIsNullable
    };

    public Guid? GetLeafObjectTypeId() => BaseKind switch
    {
        BaseTypeKind.Object => ObjectTypeId,
        BaseTypeKind.Array => ElementType?.GetLeafObjectTypeId(),
        _ => null
    };

    public TypeDescriptor Clone() => new()
    {
        BaseKind = BaseKind,
        StringSubtype = StringSubtype,
        CustomSubtype = CustomSubtype,
        NumberSubtype = NumberSubtype,
        ObjectTypeId = ObjectTypeId,
        ElementType = ElementType?.Clone(),
        ElementIsNullable = ElementIsNullable
    };

    public string FormatDisplay(IReadOnlyDictionary<Guid, string>? typeNames = null)
    {
        return BaseKind switch
        {
            BaseTypeKind.String => StringSubtype switch
            {
                StringSubtype.None => "string",
                StringSubtype.Custom => string.IsNullOrWhiteSpace(CustomSubtype) ? "string (custom)" : $"string ({CustomSubtype})",
                _ => $"string ({FormatStringSubtype(StringSubtype)})"
            },
            BaseTypeKind.Boolean => "boolean",
            BaseTypeKind.Number => NumberSubtype == NumberSubtype.None
                ? "number"
                : $"number ({FormatNumberSubtype(NumberSubtype)})",
            BaseTypeKind.Object => ObjectTypeId is Guid id
                ? (typeNames != null && typeNames.TryGetValue(id, out var name) ? name : $"object ({id.ToString()[..8]})")
                : "object (untyped)",
            BaseTypeKind.Array => ElementType is null
                ? "array<any>"
                : $"{ElementType.FormatDisplay(typeNames)}{(ElementIsNullable ? "?" : "")}[]",
            _ => "unknown"
        };
    }

    public string FormatBadge(IReadOnlyDictionary<Guid, string>? typeNames = null)
    {
        return BaseKind switch
        {
            BaseTypeKind.String => StringSubtype switch
            {
                StringSubtype.None => "string",
                StringSubtype.Custom => string.IsNullOrWhiteSpace(CustomSubtype) ? "str:custom" : $"str:{CustomSubtype}",
                _ => $"str:{FormatStringSubtype(StringSubtype)}"
            },
            BaseTypeKind.Boolean => "bool",
            BaseTypeKind.Number => NumberSubtype == NumberSubtype.None
                ? "number"
                : FormatNumberSubtype(NumberSubtype),
            BaseTypeKind.Object => ObjectTypeId is Guid id
                ? (typeNames != null && typeNames.TryGetValue(id, out var name) ? name : "object")
                : "obj:any",
            BaseTypeKind.Array => ElementType is null
                ? "any[]"
                : $"{ElementType.FormatBadge(typeNames)}{(ElementIsNullable ? "?" : "")}[]",
            _ => "?"
        };
    }

    public static string FormatStringSubtype(StringSubtype subtype) => subtype switch
    {
        StringSubtype.Uuid => "uuid",
        StringSubtype.Date => "date",
        StringSubtype.Time => "time",
        StringSubtype.DateTime => "datetime",
        StringSubtype.Duration => "duration",
        StringSubtype.Uri => "uri",
        StringSubtype.Email => "email",
        StringSubtype.Base64 => "base64",
        StringSubtype.Custom => "custom",
        _ => ""
    };

    public static string FormatNumberSubtype(NumberSubtype subtype) => subtype switch
    {
        NumberSubtype.Int32 => "int32",
        NumberSubtype.Int64 => "int64",
        NumberSubtype.Float => "float",
        NumberSubtype.Double => "double",
        NumberSubtype.Decimal => "decimal",
        _ => "number"
    };

    public bool Equals(TypeDescriptor? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (BaseKind != other.BaseKind) return false;

        return BaseKind switch
        {
            BaseTypeKind.String => StringSubtype == other.StringSubtype &&
                                   (StringSubtype != StringSubtype.Custom ||
                                    string.Equals(CustomSubtype?.Trim(), other.CustomSubtype?.Trim(), StringComparison.OrdinalIgnoreCase)),
            BaseTypeKind.Boolean => true,
            BaseTypeKind.Number => NumberSubtype == other.NumberSubtype,
            BaseTypeKind.Object => ObjectTypeId == other.ObjectTypeId,
            BaseTypeKind.Array => ElementIsNullable == other.ElementIsNullable &&
                                  Equals(ElementType, other.ElementType),
            _ => false
        };
    }

    public override bool Equals(object? obj) => Equals(obj as TypeDescriptor);

    public override int GetHashCode() => HashCode.Combine(BaseKind, StringSubtype, NumberSubtype, ObjectTypeId, ElementIsNullable, ElementType);
}

public sealed class TypeDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<PropertyDefinition> Properties { get; set; } = [];

    [JsonIgnore]
    public string QualifiedName => string.IsNullOrWhiteSpace(Namespace) ? Name : $"{Namespace}.{Name}";
}

public sealed class PropertyDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerTypeId { get; set; }

    [JsonIgnore]
    public TypeDefinition? OwnerType { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TypeDescriptor Type { get; set; } = TypeDescriptor.ScalarString();

    public Guid? ReferencedTypeId { get; set; }

    [JsonIgnore]
    public TypeDefinition? ReferencedType { get; set; }

    public bool IsNullable { get; set; }
    public bool IsOptional { get; set; }
    public int SortOrder { get; set; }

    public void SyncReferencedType()
    {
        ReferencedTypeId = Type.GetLeafObjectTypeId();
    }
}
