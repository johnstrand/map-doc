using MapDoc.Core.Models;

namespace MapDoc.Core.Validation;

public sealed class ResolvedPropertyEndpoint
{
    public bool IsValid { get; init; }
    public string PropertyPath { get; init; } = string.Empty;
    public string DisplayPath { get; init; } = string.Empty;
    public TypeDescriptor Type { get; init; } = TypeDescriptor.ScalarString();
    public bool LeafIsNullable { get; init; }
    public bool LeafIsOptional { get; init; }
    public bool EffectiveIsNullable { get; init; }
    public bool EffectiveIsOptional { get; init; }
    public int ArrayDepth { get; init; }
    public PropertyDefinition? LeafProperty { get; init; }
    public string? ErrorMessage { get; init; }

    public bool IsRepeating => ArrayDepth > 0;
}

public sealed class MapperTreeNode
{
    public string Path { get; init; } = string.Empty;
    public string DisplayPath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public TypeDescriptor Type { get; init; } = TypeDescriptor.ScalarString();
    public string TypeBadge { get; init; } = string.Empty;
    public string TypeFullDisplay { get; init; } = string.Empty;
    public bool IsNullable { get; init; }
    public bool IsOptional { get; init; }
    public int Depth { get; init; }
    public int ArrayDepth { get; init; }
    public bool CanExpand { get; init; }
    public bool IsExpanded { get; init; }
    public bool IsRecursiveCycle { get; init; }
    public bool IsSyntheticArrayItem { get; init; }
    public PropertyDefinition? Property { get; init; }
    public List<MapperTreeNode> Children { get; init; } = [];
}

public static class PropertyPathResolver
{
    public const string ArrayItemToken = "$item";

    public static ResolvedPropertyEndpoint Resolve(
        TypeDefinition? rootType,
        string? propertyPath,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog)
    {
        if (rootType is null)
        {
            return new ResolvedPropertyEndpoint
            {
                IsValid = false,
                PropertyPath = propertyPath ?? string.Empty,
                DisplayPath = "(missing type)",
                ErrorMessage = "Root type definition could not be found."
            };
        }

        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            return new ResolvedPropertyEndpoint
            {
                IsValid = false,
                PropertyPath = string.Empty,
                DisplayPath = "(empty path)",
                ErrorMessage = "Property path is empty."
            };
        }

        var segments = propertyPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return new ResolvedPropertyEndpoint
            {
                IsValid = false,
                PropertyPath = propertyPath,
                DisplayPath = "(invalid path)",
                ErrorMessage = "Property path has no segments."
            };
        }

        TypeDefinition? currentContainerType = rootType;
        TypeDescriptor? currentDescriptor = null;
        PropertyDefinition? leafProperty = null;
        bool leafNullable = false;
        bool leafOptional = false;
        bool effectiveNullable = false;
        bool effectiveOptional = false;
        int arrayDepth = 0;
        var displayParts = new List<string>();

        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];

            if (string.Equals(seg, ArrayItemToken, StringComparison.Ordinal))
            {
                if (currentDescriptor is null || currentDescriptor.BaseKind != BaseTypeKind.Array || currentDescriptor.ElementType is null)
                {
                    return new ResolvedPropertyEndpoint
                    {
                        IsValid = false,
                        PropertyPath = propertyPath,
                        DisplayPath = displayParts.Count > 0 ? string.Join(".", displayParts) + ".?" : "(broken item path)",
                        ErrorMessage = $"Segment '{ArrayItemToken}' encountered on a non-array property."
                    };
                }

                arrayDepth++;
                leafNullable = currentDescriptor.ElementIsNullable;
                leafOptional = false;
                effectiveNullable |= leafNullable;
                currentDescriptor = currentDescriptor.ElementType;

                if (displayParts.Count > 0)
                {
                    displayParts[^1] = displayParts[^1] + "[]";
                }
                else
                {
                    displayParts.Add("[]");
                }

                if (currentDescriptor.BaseKind == BaseTypeKind.Object && currentDescriptor.ObjectTypeId is Guid elemObjId)
                {
                    typeCatalog.TryGetValue(elemObjId, out currentContainerType);
                }
                else
                {
                    currentContainerType = null;
                }

                continue;
            }

            if (!Guid.TryParse(seg, out var propId))
            {
                return new ResolvedPropertyEndpoint
                {
                    IsValid = false,
                    PropertyPath = propertyPath,
                    DisplayPath = "(invalid segment)",
                    ErrorMessage = $"Segment '{seg}' is not a valid property identifier."
                };
            }

            // If previous descriptor was Array<Object(T)>, stepping directly to a property ID implicitly traverses the 1D array element!
            if (i > 0 && currentDescriptor is not null)
            {
                if (currentDescriptor.BaseKind == BaseTypeKind.Array &&
                    currentDescriptor.ElementType is { BaseKind: BaseTypeKind.Object, ObjectTypeId: Guid arrObjId })
                {
                    arrayDepth++;
                    effectiveNullable |= currentDescriptor.ElementIsNullable;
                    if (displayParts.Count > 0)
                    {
                        displayParts[^1] = displayParts[^1] + "[]";
                    }
                    typeCatalog.TryGetValue(arrObjId, out currentContainerType);
                }
                else if (currentDescriptor.BaseKind == BaseTypeKind.Object && currentDescriptor.ObjectTypeId is Guid objId)
                {
                    typeCatalog.TryGetValue(objId, out currentContainerType);
                }
                else
                {
                    currentContainerType = null;
                }
            }

            if (currentContainerType is null)
            {
                return new ResolvedPropertyEndpoint
                {
                    IsValid = false,
                    PropertyPath = propertyPath,
                    DisplayPath = displayParts.Count > 0 ? string.Join(".", displayParts) + ".(missing)" : "(missing)",
                    ErrorMessage = "Cannot traverse into child property because container object type is missing or not an object."
                };
            }

            var prop = currentContainerType.Properties.FirstOrDefault(p => p.Id == propId);
            if (prop is null)
            {
                return new ResolvedPropertyEndpoint
                {
                    IsValid = false,
                    PropertyPath = propertyPath,
                    DisplayPath = displayParts.Count > 0 ? string.Join(".", displayParts) + ".(deleted)" : "(deleted property)",
                    ErrorMessage = $"Property '{propId}' no longer exists on type '{currentContainerType.Name}'."
                };
            }

            leafProperty = prop;
            currentDescriptor = prop.Type;
            leafNullable = prop.IsNullable;
            leafOptional = prop.IsOptional;
            effectiveNullable |= prop.IsNullable;
            effectiveOptional |= prop.IsOptional;
            displayParts.Add(prop.Name);
        }

        if (currentDescriptor is null)
        {
            return new ResolvedPropertyEndpoint
            {
                IsValid = false,
                PropertyPath = propertyPath,
                DisplayPath = "(unresolved)",
                ErrorMessage = "Failed to resolve property descriptor."
            };
        }

        return new ResolvedPropertyEndpoint
        {
            IsValid = true,
            PropertyPath = propertyPath,
            DisplayPath = string.Join(".", displayParts),
            Type = currentDescriptor.Clone(),
            LeafIsNullable = leafNullable,
            LeafIsOptional = leafOptional,
            EffectiveIsNullable = effectiveNullable,
            EffectiveIsOptional = effectiveOptional,
            ArrayDepth = arrayDepth,
            LeafProperty = leafProperty
        };
    }

    public static List<MapperTreeNode> BuildTree(
        TypeDefinition rootType,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog,
        IReadOnlySet<string> expandedPaths)
    {
        var typeNames = typeCatalog.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Name);
        var ancestorTypes = new HashSet<Guid> { rootType.Id };

        return BuildPropertiesNodes(
            rootType.Properties.OrderBy(p => p.SortOrder).ThenBy(p => p.Name),
            parentPath: string.Empty,
            parentDisplayPath: string.Empty,
            depth: 0,
            arrayDepth: 0,
            ancestorTypes,
            typeCatalog,
            typeNames,
            expandedPaths);
    }

    public static List<MapperTreeNode> FlattenVisible(IEnumerable<MapperTreeNode> roots)
    {
        var result = new List<MapperTreeNode>();
        void Visit(IEnumerable<MapperTreeNode> nodes)
        {
            foreach (var n in nodes)
            {
                result.Add(n);
                if (n.IsExpanded && n.Children.Count > 0)
                {
                    Visit(n.Children);
                }
            }
        }
        Visit(roots);
        return result;
    }

    /// <summary>
    /// Finds either the exact visible tree node for a property path, or its deepest currently-visible collapsed ancestor
    /// (for BizTalk-style collapsed ancestor roll-up lines).
    /// </summary>
    public static (string? VisiblePath, bool IsRolledUpAncestor) FindVisibleOrAncestorPath(
        string propertyPath,
        IReadOnlySet<string> visiblePaths)
    {
        if (visiblePaths.Contains(propertyPath))
        {
            return (propertyPath, false);
        }

        var current = propertyPath;
        while (true)
        {
            var slashIdx = current.LastIndexOf('/');
            if (slashIdx <= 0) break;
            current = current[..slashIdx];
            if (visiblePaths.Contains(current))
            {
                return (current, true);
            }
        }

        return (null, false);
    }

    public static HashSet<string> GetAncestorPathsToExpand(IEnumerable<string> propertyPaths)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in propertyPaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var current = path;
            while (true)
            {
                var slashIdx = current.LastIndexOf('/');
                if (slashIdx <= 0) break;
                current = current[..slashIdx];
                set.Add(current);
            }
        }
        return set;
    }

    private static List<MapperTreeNode> BuildPropertiesNodes(
        IEnumerable<PropertyDefinition> properties,
        string parentPath,
        string parentDisplayPath,
        int depth,
        int arrayDepth,
        HashSet<Guid> ancestorTypes,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog,
        IReadOnlyDictionary<Guid, string> typeNames,
        IReadOnlySet<string> expandedPaths)
    {
        var nodes = new List<MapperTreeNode>();

        foreach (var prop in properties)
        {
            var path = string.IsNullOrEmpty(parentPath) ? prop.Id.ToString() : $"{parentPath}/{prop.Id}";
            var displayPath = string.IsNullOrEmpty(parentDisplayPath) ? prop.Name : $"{parentDisplayPath}.{prop.Name}";

            var (canExpand, isCycle, referencedObjType) = InspectExpandability(prop.Type, ancestorTypes, typeCatalog);
            bool isExpanded = canExpand && expandedPaths.Contains(path);
            var children = new List<MapperTreeNode>();

            if (isExpanded && depth < 32)
            {
                children = BuildExpandedChildren(
                    prop.Type,
                    path,
                    displayPath,
                    depth + 1,
                    arrayDepth,
                    ancestorTypes,
                    referencedObjType,
                    typeCatalog,
                    typeNames,
                    expandedPaths);
            }

            nodes.Add(new MapperTreeNode
            {
                Path = path,
                DisplayPath = displayPath,
                Name = prop.Name,
                Description = prop.Description,
                Type = prop.Type.Clone(),
                TypeBadge = prop.Type.FormatBadge(typeNames),
                TypeFullDisplay = prop.Type.FormatDisplay(typeNames),
                IsNullable = prop.IsNullable,
                IsOptional = prop.IsOptional,
                Depth = depth,
                ArrayDepth = arrayDepth,
                CanExpand = canExpand,
                IsExpanded = isExpanded,
                IsRecursiveCycle = isCycle,
                Property = prop,
                Children = children
            });
        }

        return nodes;
    }

    private static (bool CanExpand, bool IsCycle, TypeDefinition? ReferencedObjectType) InspectExpandability(
        TypeDescriptor descriptor,
        HashSet<Guid> ancestorTypes,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog)
    {
        if (descriptor.BaseKind == BaseTypeKind.Object && descriptor.ObjectTypeId is Guid objId)
        {
            if (typeCatalog.TryGetValue(objId, out var objType) && objType.Properties.Count > 0)
            {
                return (true, ancestorTypes.Contains(objId), objType);
            }
            return (false, false, null);
        }

        if (descriptor.BaseKind == BaseTypeKind.Array && descriptor.ElementType is not null)
        {
            if (descriptor.ElementType.BaseKind == BaseTypeKind.Object && descriptor.ElementType.ObjectTypeId is Guid elemObjId)
            {
                if (typeCatalog.TryGetValue(elemObjId, out var elemObjType) && elemObjType.Properties.Count > 0)
                {
                    return (true, ancestorTypes.Contains(elemObjId), elemObjType);
                }
                return (false, false, null);
            }

            // Array of scalars or nested Array<Array<...>> expands to a synthetic [item] node
            return (true, false, null);
        }

        return (false, false, null);
    }

    private static List<MapperTreeNode> BuildExpandedChildren(
        TypeDescriptor descriptor,
        string currentPath,
        string currentDisplayPath,
        int childDepth,
        int currentArrayDepth,
        HashSet<Guid> ancestorTypes,
        TypeDefinition? referencedObjectType,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog,
        IReadOnlyDictionary<Guid, string> typeNames,
        IReadOnlySet<string> expandedPaths)
    {
        if (descriptor.BaseKind == BaseTypeKind.Object && referencedObjectType is not null)
        {
            var nextAncestors = new HashSet<Guid>(ancestorTypes) { referencedObjectType.Id };
            return BuildPropertiesNodes(
                referencedObjectType.Properties.OrderBy(p => p.SortOrder).ThenBy(p => p.Name),
                currentPath,
                currentDisplayPath,
                childDepth,
                currentArrayDepth,
                nextAncestors,
                typeCatalog,
                typeNames,
                expandedPaths);
        }

        if (descriptor.BaseKind == BaseTypeKind.Array && descriptor.ElementType is not null)
        {
            // Direct Array<Object(T)> -> render T's properties directly under the array node (Lines[].Sku)
            if (descriptor.ElementType.BaseKind == BaseTypeKind.Object && referencedObjectType is not null)
            {
                var nextAncestors = new HashSet<Guid>(ancestorTypes) { referencedObjectType.Id };
                return BuildPropertiesNodes(
                    referencedObjectType.Properties.OrderBy(p => p.SortOrder).ThenBy(p => p.Name),
                    currentPath,
                    $"{currentDisplayPath}[]",
                    childDepth,
                    currentArrayDepth + 1,
                    nextAncestors,
                    typeCatalog,
                    typeNames,
                    expandedPaths);
            }

            // Array of Scalar or nested Array<Array<...>> -> render synthetic [item] node
            var itemPath = $"{currentPath}/{ArrayItemToken}";
            var itemDisplayPath = $"{currentDisplayPath}[]";
            var elemType = descriptor.ElementType;
            var (itemCanExpand, itemIsCycle, itemRefObj) = InspectExpandability(elemType, ancestorTypes, typeCatalog);
            bool itemExpanded = itemCanExpand && expandedPaths.Contains(itemPath);

            var grandChildren = itemExpanded && childDepth < 32
                ? BuildExpandedChildren(
                    elemType,
                    itemPath,
                    itemDisplayPath,
                    childDepth + 1,
                    currentArrayDepth + 1,
                    ancestorTypes,
                    itemRefObj,
                    typeCatalog,
                    typeNames,
                    expandedPaths)
                : [];

            return
            [
                new MapperTreeNode
                {
                    Path = itemPath,
                    DisplayPath = itemDisplayPath,
                    Name = "[item]",
                    Description = "Element of parent array.",
                    Type = elemType.Clone(),
                    TypeBadge = elemType.FormatBadge(typeNames),
                    TypeFullDisplay = elemType.FormatDisplay(typeNames),
                    IsNullable = descriptor.ElementIsNullable,
                    IsOptional = false,
                    Depth = childDepth,
                    ArrayDepth = currentArrayDepth + 1,
                    CanExpand = itemCanExpand,
                    IsExpanded = itemExpanded,
                    IsRecursiveCycle = itemIsCycle,
                    IsSyntheticArrayItem = true,
                    Children = grandChildren
                }
            ];
        }

        return [];
    }
}
