using System.Text.Json;
using System.Text.RegularExpressions;
using MapDoc.Core.Models;

namespace MapDoc.Core.ImportExport;

public sealed class JsonImportResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string DetectedMode { get; init; } = string.Empty;
    public List<TypeDefinition> GeneratedTypes { get; init; } = [];
    public TypeDefinition? RootType => GeneratedTypes.FirstOrDefault();
}

public static partial class JsonTypeImporter
{
    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex UuidRegex();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"^\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?$")]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}[Tt ]\d{2}:\d{2}:\d{2}")]
    private static partial Regex DateTimeRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    public static JsonImportResult ImportFromJsonOrSchema(
        string jsonText,
        string rootTypeName,
        string targetNamespace = "")
    {
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return new JsonImportResult { Success = false, ErrorMessage = "Input JSON cannot be empty." };
        }

        if (string.IsNullOrWhiteSpace(rootTypeName))
        {
            rootTypeName = "ImportedType";
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonText, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException ex)
        {
            return new JsonImportResult
            {
                Success = false,
                ErrorMessage = $"Invalid JSON syntax: {ex.Message}"
            };
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (IsLikelyJsonSchema(root))
            {
                return ImportJsonSchema(root, ToPascalIdentifier(rootTypeName), targetNamespace.Trim());
            }

            return ImportSampleJson(root, ToPascalIdentifier(rootTypeName), targetNamespace.Trim());
        }
    }

    private static bool IsLikelyJsonSchema(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (root.TryGetProperty("$schema", out _) || root.TryGetProperty("$defs", out _) || root.TryGetProperty("definitions", out _))
        {
            return true;
        }

        if (root.TryGetProperty("type", out var typeProp) &&
            typeProp.ValueKind == JsonValueKind.String &&
            typeProp.GetString() is "object" or "array" &&
            (root.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object ||
             root.TryGetProperty("items", out _)))
        {
            return true;
        }

        return false;
    }

    private static JsonImportResult ImportJsonSchema(JsonElement root, string rootTypeName, string ns)
    {
        var generated = new List<TypeDefinition>();
        var defsByName = new Dictionary<string, TypeDefinition>(StringComparer.OrdinalIgnoreCase);
        var rawDefs = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var defsContainerName in new[] { "$defs", "definitions" })
        {
            if (root.TryGetProperty(defsContainerName, out var defsEl) && defsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var defProp in defsEl.EnumerateObject())
                {
                    rawDefs[defProp.Name] = defProp.Value;
                    var typeDef = new TypeDefinition
                    {
                        Id = Guid.NewGuid(),
                        Name = ToPascalIdentifier(defProp.Name),
                        Namespace = ns,
                        Description = GetString(defProp.Value, "description")
                    };
                    defsByName[defProp.Name] = typeDef;
                }
            }
        }

        var rootTitle = GetString(root, "title");
        var effectiveRootName = string.IsNullOrWhiteSpace(rootTitle) ? rootTypeName : ToPascalIdentifier(rootTitle);
        var rootDef = new TypeDefinition
        {
            Id = Guid.NewGuid(),
            Name = effectiveRootName,
            Namespace = ns,
            Description = GetString(root, "description")
        };
        generated.Add(rootDef);

        foreach (var (_, defType) in defsByName)
        {
            generated.Add(defType);
        }

        // Populate defined types first
        foreach (var (rawKey, rawSchema) in rawDefs)
        {
            PopulateSchemaObjectProperties(rawSchema, defsByName[rawKey], defsByName, generated, ns);
        }

        // If root schema is an array of objects, unwrap items as the root object schema
        var targetRootSchema = root;
        if (GetSchemaTypes(root).Contains("array") && root.TryGetProperty("items", out var rootItems) && rootItems.ValueKind == JsonValueKind.Object)
        {
            targetRootSchema = rootItems;
        }

        PopulateSchemaObjectProperties(targetRootSchema, rootDef, defsByName, generated, ns);

        return new JsonImportResult
        {
            Success = true,
            DetectedMode = "JSON Schema",
            GeneratedTypes = generated
        };
    }

    private static void PopulateSchemaObjectProperties(
        JsonElement schemaObj,
        TypeDefinition targetType,
        Dictionary<string, TypeDefinition> defsByName,
        List<TypeDefinition> generated,
        string ns)
    {
        var requiredSet = new HashSet<string>(StringComparer.Ordinal);
        if (schemaObj.TryGetProperty("required", out var reqArr) && reqArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in reqArr.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                {
                    requiredSet.Add(s);
                }
            }
        }

        if (!schemaObj.TryGetProperty("properties", out var propsEl) || propsEl.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        int order = 0;
        foreach (var prop in propsEl.EnumerateObject())
        {
            var (descriptor, isNullable) = ResolveSchemaPropertyDescriptor(
                prop.Value,
                suggestedName: $"{targetType.Name}{ToPascalIdentifier(prop.Name)}",
                rootType: targetType,
                defsByName,
                generated,
                ns);

            var propDef = new PropertyDefinition
            {
                Id = Guid.NewGuid(),
                OwnerTypeId = targetType.Id,
                Name = prop.Name,
                Description = GetString(prop.Value, "description"),
                Type = descriptor,
                IsNullable = isNullable,
                IsOptional = requiredSet.Count > 0 && !requiredSet.Contains(prop.Name),
                SortOrder = order++
            };
            propDef.SyncReferencedType();
            targetType.Properties.Add(propDef);
        }
    }

    private static (TypeDescriptor Descriptor, bool IsNullable) ResolveSchemaPropertyDescriptor(
        JsonElement propSchema,
        string suggestedName,
        TypeDefinition rootType,
        Dictionary<string, TypeDefinition> defsByName,
        List<TypeDefinition> generated,
        string ns)
    {
        if (propSchema.ValueKind != JsonValueKind.Object)
        {
            return (TypeDescriptor.ScalarString(), false);
        }

        if (propSchema.TryGetProperty("$ref", out var refEl) && refEl.ValueKind == JsonValueKind.String)
        {
            var refStr = refEl.GetString() ?? string.Empty;
            if (refStr == "#")
            {
                return (TypeDescriptor.ObjectOf(rootType.Id), false);
            }
            var slash = refStr.LastIndexOf('/');
            var defName = slash >= 0 ? refStr[(slash + 1)..] : refStr;
            if (defsByName.TryGetValue(defName, out var refType))
            {
                return (TypeDescriptor.ObjectOf(refType.Id), false);
            }
        }

        // Handle anyOf / oneOf with { "type": "null" }
        foreach (var unionKey in new[] { "anyOf", "oneOf" })
        {
            if (propSchema.TryGetProperty(unionKey, out var unionArr) && unionArr.ValueKind == JsonValueKind.Array)
            {
                bool hasNull = false;
                JsonElement? nonNullBranch = null;
                foreach (var branch in unionArr.EnumerateArray())
                {
                    var bTypes = GetSchemaTypes(branch);
                    if (bTypes.Count == 1 && bTypes[0] == "null")
                    {
                        hasNull = true;
                    }
                    else if (nonNullBranch is null)
                    {
                        nonNullBranch = branch;
                    }
                }

                if (nonNullBranch.HasValue)
                {
                    var (innerDesc, innerNull) = ResolveSchemaPropertyDescriptor(nonNullBranch.Value, suggestedName, rootType, defsByName, generated, ns);
                    return (innerDesc, hasNull || innerNull);
                }
            }
        }

        var types = GetSchemaTypes(propSchema);
        bool isNullable = types.Contains("null") ||
                          (propSchema.TryGetProperty("nullable", out var nProp) && nProp.ValueKind == JsonValueKind.True);

        var primaryType = types.FirstOrDefault(t => t != "null") ?? "string";
        var format = GetString(propSchema, "format").ToLowerInvariant();

        switch (primaryType)
        {
            case "boolean":
                return (TypeDescriptor.ScalarBoolean(), isNullable);

            case "integer":
            {
                var sub = format switch
                {
                    "int64" => NumberSubtype.Int64,
                    _ => NumberSubtype.Int32
                };
                return (TypeDescriptor.ScalarNumber(sub), isNullable);
            }

            case "number":
            {
                var sub = format switch
                {
                    "int32" => NumberSubtype.Int32,
                    "int64" => NumberSubtype.Int64,
                    "float" => NumberSubtype.Float,
                    "double" => NumberSubtype.Double,
                    "decimal" => NumberSubtype.Decimal,
                    _ => NumberSubtype.Decimal
                };
                return (TypeDescriptor.ScalarNumber(sub), isNullable);
            }

            case "array":
            {
                if (propSchema.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Object)
                {
                    var itemTypeName = suggestedName.EndsWith("s", StringComparison.OrdinalIgnoreCase) && suggestedName.Length > 2
                        ? suggestedName[..^1]
                        : $"{suggestedName}Item";
                    var (elemDesc, elemNull) = ResolveSchemaPropertyDescriptor(itemsEl, itemTypeName, rootType, defsByName, generated, ns);
                    return (TypeDescriptor.ArrayOf(elemDesc, elemNull), isNullable);
                }
                return (TypeDescriptor.ArrayOf(TypeDescriptor.ScalarString()), isNullable);
            }

            case "object":
            {
                if (propSchema.TryGetProperty("properties", out var nestedProps) && nestedProps.ValueKind == JsonValueKind.Object)
                {
                    var nestedType = new TypeDefinition
                    {
                        Id = Guid.NewGuid(),
                        Name = EnsureUniqueName(suggestedName, generated),
                        Namespace = ns,
                        Description = GetString(propSchema, "description")
                    };
                    generated.Add(nestedType);
                    PopulateSchemaObjectProperties(propSchema, nestedType, defsByName, generated, ns);
                    return (TypeDescriptor.ObjectOf(nestedType.Id), isNullable);
                }
                return (TypeDescriptor.ObjectOf(null), isNullable);
            }

            default:
            {
                var strSub = format switch
                {
                    "uuid" or "guid" => StringSubtype.Uuid,
                    "date" => StringSubtype.Date,
                    "time" => StringSubtype.Time,
                    "date-time" or "datetime" => StringSubtype.DateTime,
                    "duration" => StringSubtype.Duration,
                    "uri" or "url" or "iri" => StringSubtype.Uri,
                    "email" or "idn-email" => StringSubtype.Email,
                    "byte" or "base64" => StringSubtype.Base64,
                    "" => StringSubtype.None,
                    _ => StringSubtype.Custom
                };
                return (TypeDescriptor.ScalarString(strSub, strSub == StringSubtype.Custom ? format : null), isNullable);
            }
        }
    }

    private static JsonImportResult ImportSampleJson(JsonElement root, string rootTypeName, string ns)
    {
        JsonElement targetObj = root;
        if (root.ValueKind == JsonValueKind.Array)
        {
            var firstObj = root.EnumerateArray().FirstOrDefault(e => e.ValueKind == JsonValueKind.Object);
            if (firstObj.ValueKind != JsonValueKind.Object)
            {
                return new JsonImportResult
                {
                    Success = false,
                    ErrorMessage = "Sample JSON must be a JSON object or an array containing at least one JSON object."
                };
            }
            targetObj = firstObj;
        }
        else if (root.ValueKind != JsonValueKind.Object)
        {
            return new JsonImportResult
            {
                Success = false,
                ErrorMessage = "Sample JSON root must be an object or array of objects."
            };
        }

        var generated = new List<TypeDefinition>();
        InferObjectFromSample(targetObj, rootTypeName, ns, generated);

        return new JsonImportResult
        {
            Success = true,
            DetectedMode = "Sample JSON Payload",
            GeneratedTypes = generated
        };
    }

    private static TypeDefinition InferObjectFromSample(
        JsonElement objEl,
        string typeName,
        string ns,
        List<TypeDefinition> generated)
    {
        var typeDef = new TypeDefinition
        {
            Id = Guid.NewGuid(),
            Name = EnsureUniqueName(ToPascalIdentifier(typeName), generated),
            Namespace = ns,
            Description = $"Inferred from sample JSON ({DateTime.UtcNow:yyyy-MM-dd})."
        };
        generated.Add(typeDef);

        int order = 0;
        foreach (var prop in objEl.EnumerateObject())
        {
            var (desc, isNull) = InferDescriptorFromSampleValue(prop.Value, $"{typeDef.Name}{ToPascalIdentifier(prop.Name)}", ns, generated);
            var propDef = new PropertyDefinition
            {
                Id = Guid.NewGuid(),
                OwnerTypeId = typeDef.Id,
                Name = prop.Name,
                Type = desc,
                IsNullable = isNull,
                IsOptional = false,
                SortOrder = order++
            };
            propDef.SyncReferencedType();
            typeDef.Properties.Add(propDef);
        }

        return typeDef;
    }

    private static (TypeDescriptor Descriptor, bool IsNullable) InferDescriptorFromSampleValue(
        JsonElement val,
        string suggestedTypeName,
        string ns,
        List<TypeDefinition> generated)
    {
        switch (val.ValueKind)
        {
            case JsonValueKind.Null:
                return (TypeDescriptor.ScalarString(), true);

            case JsonValueKind.True:
            case JsonValueKind.False:
                return (TypeDescriptor.ScalarBoolean(), false);

            case JsonValueKind.Number:
                if (val.TryGetInt32(out _))
                    return (TypeDescriptor.ScalarNumber(NumberSubtype.Int32), false);
                if (val.TryGetInt64(out _))
                    return (TypeDescriptor.ScalarNumber(NumberSubtype.Int64), false);
                return (TypeDescriptor.ScalarNumber(NumberSubtype.Decimal), false);

            case JsonValueKind.String:
                return (InferStringSubtype(val.GetString() ?? string.Empty), false);

            case JsonValueKind.Object:
            {
                bool hasProps = val.EnumerateObject().Any();
                if (!hasProps)
                {
                    return (TypeDescriptor.ObjectOf(null), false);
                }
                var childType = InferObjectFromSample(val, suggestedTypeName, ns, generated);
                return (TypeDescriptor.ObjectOf(childType.Id), false);
            }

            case JsonValueKind.Array:
            {
                var items = val.EnumerateArray().ToList();
                var nonNull = items.FirstOrDefault(i => i.ValueKind != JsonValueKind.Null);
                bool hasNull = items.Any(i => i.ValueKind == JsonValueKind.Null);

                if (items.Count == 0 || nonNull.ValueKind == JsonValueKind.Undefined)
                {
                    return (TypeDescriptor.ArrayOf(TypeDescriptor.ScalarString(), hasNull), false);
                }

                var singularName = suggestedTypeName.EndsWith("s", StringComparison.OrdinalIgnoreCase) && suggestedTypeName.Length > 2
                    ? suggestedTypeName[..^1]
                    : $"{suggestedTypeName}Item";

                if (nonNull.ValueKind == JsonValueKind.Object)
                {
                    // Merge all sample objects in the array so optional properties on later elements are captured too!
                    var mergedType = InferMergedArrayObjectFromSamples(
                        items.Where(i => i.ValueKind == JsonValueKind.Object).ToList(),
                        singularName,
                        ns,
                        generated);
                    return (TypeDescriptor.ArrayOf(TypeDescriptor.ObjectOf(mergedType.Id), hasNull), false);
                }

                var (elemDesc, _) = InferDescriptorFromSampleValue(nonNull, singularName, ns, generated);
                return (TypeDescriptor.ArrayOf(elemDesc, hasNull), false);
            }

            default:
                return (TypeDescriptor.ScalarString(), false);
        }
    }

    private static TypeDefinition InferMergedArrayObjectFromSamples(
        List<JsonElement> sampleObjects,
        string typeName,
        string ns,
        List<TypeDefinition> generated)
    {
        var typeDef = new TypeDefinition
        {
            Id = Guid.NewGuid(),
            Name = EnsureUniqueName(ToPascalIdentifier(typeName), generated),
            Namespace = ns,
            Description = $"Inferred from {sampleObjects.Count} sample array item(s)."
        };
        generated.Add(typeDef);

        var seenProps = new Dictionary<string, (List<JsonElement> Values, int Count)>(StringComparer.Ordinal);
        var orderedNames = new List<string>();

        foreach (var obj in sampleObjects)
        {
            foreach (var p in obj.EnumerateObject())
            {
                if (!seenProps.TryGetValue(p.Name, out var entry))
                {
                    entry = ([], 0);
                    orderedNames.Add(p.Name);
                }
                entry.Values.Add(p.Value);
                seenProps[p.Name] = (entry.Values, entry.Count + 1);
            }
        }

        int order = 0;
        foreach (var name in orderedNames)
        {
            var (values, count) = seenProps[name];
            var nonNull = values.FirstOrDefault(v => v.ValueKind != JsonValueKind.Null);
            bool anyNull = values.Any(v => v.ValueKind == JsonValueKind.Null);
            bool isOptional = count < sampleObjects.Count;

            var sampleVal = nonNull.ValueKind == JsonValueKind.Undefined ? values[0] : nonNull;
            var (desc, inferredNull) = InferDescriptorFromSampleValue(sampleVal, $"{typeDef.Name}{ToPascalIdentifier(name)}", ns, generated);

            var propDef = new PropertyDefinition
            {
                Id = Guid.NewGuid(),
                OwnerTypeId = typeDef.Id,
                Name = name,
                Type = desc,
                IsNullable = anyNull || inferredNull,
                IsOptional = isOptional,
                SortOrder = order++
            };
            propDef.SyncReferencedType();
            typeDef.Properties.Add(propDef);
        }

        return typeDef;
    }

    private static TypeDescriptor InferStringSubtype(string sample)
    {
        if (string.IsNullOrWhiteSpace(sample)) return TypeDescriptor.ScalarString();
        if (UuidRegex().IsMatch(sample)) return TypeDescriptor.ScalarString(StringSubtype.Uuid);
        if (DateTimeRegex().IsMatch(sample)) return TypeDescriptor.ScalarString(StringSubtype.DateTime);
        if (DateRegex().IsMatch(sample)) return TypeDescriptor.ScalarString(StringSubtype.Date);
        if (TimeRegex().IsMatch(sample)) return TypeDescriptor.ScalarString(StringSubtype.Time);
        if (EmailRegex().IsMatch(sample)) return TypeDescriptor.ScalarString(StringSubtype.Email);
        if (Uri.TryCreate(sample, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return TypeDescriptor.ScalarString(StringSubtype.Uri);

        return TypeDescriptor.ScalarString();
    }

    private static List<string> GetSchemaTypes(JsonElement schema)
    {
        var list = new List<string>();
        if (schema.ValueKind != JsonValueKind.Object) return list;
        if (!schema.TryGetProperty("type", out var t)) return list;

        if (t.ValueKind == JsonValueKind.String && t.GetString() is { } s)
        {
            list.Add(s.ToLowerInvariant());
        }
        else if (t.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in t.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && el.GetString() is { } es)
                {
                    list.Add(es.ToLowerInvariant());
                }
            }
        }
        return list;
    }

    private static string GetString(JsonElement obj, string propName)
    {
        if (obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(propName, out var el) &&
            el.ValueKind == JsonValueKind.String)
        {
            return el.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static string EnsureUniqueName(string desired, List<TypeDefinition> existing)
    {
        var candidate = desired;
        int suffix = 2;
        while (existing.Any(t => string.Equals(t.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{desired}{suffix++}";
        }
        return candidate;
    }

    public static string ToPascalIdentifier(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "GeneratedType";
        var parts = Regex.Split(raw.Trim(), @"[^a-zA-Z0-9]+").Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return "GeneratedType";
        var joined = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        return char.IsDigit(joined[0]) ? $"T{joined}" : joined;
    }
}
