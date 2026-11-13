using MapDoc.Core.Models;

namespace MapDoc.Core.Validation;

public enum ValidationSeverity
{
    Valid = 0,
    Warning = 1,
    Error = 2
}

public enum ValidationCode
{
    BrokenEndpoint,
    FunctoidCycle,
    MissingFunctoidDefinition,
    UnconnectedFunctoidInput,
    MissingRequiredConfigParam,
    DanglingFunctoidOutput,
    DuplicateFunctoidPinInput,
    MultipleTargetMappings,
    ScalarTypeMismatch,
    StructuralTypeMismatch,
    IncompatibleObjectTypes,
    NullabilityMismatch,
    OptionalityMismatch,
    RepeatingToSingleCardinality
}

public sealed class ValidationDiagnostic
{
    public ValidationSeverity Severity { get; init; }
    public ValidationCode Code { get; init; }
    public string Message { get; init; } = string.Empty;
    public Guid? GridPageId { get; init; }
    public string GridPageName { get; init; } = string.Empty;
    public Guid? LinkId { get; init; }
    public Guid? FunctoidInstanceId { get; init; }
    public string? TargetPropertyPath { get; init; }
}

public sealed class MappingValidationReport
{
    public List<ValidationDiagnostic> Diagnostics { get; init; } = [];
    public Dictionary<Guid, ValidationSeverity> LinkSeverities { get; init; } = [];
    public Dictionary<Guid, List<ValidationDiagnostic>> LinkDiagnostics { get; init; } = [];
    public Dictionary<Guid, ValidationSeverity> FunctoidSeverities { get; init; } = [];
    public Dictionary<Guid, List<ValidationDiagnostic>> FunctoidDiagnostics { get; init; } = [];

    public int ErrorCount => Diagnostics.Count(d => d.Severity == ValidationSeverity.Error);
    public int WarningCount => Diagnostics.Count(d => d.Severity == ValidationSeverity.Warning);
    public bool IsClean => ErrorCount == 0 && WarningCount == 0;

    public ValidationSeverity GetLinkSeverity(Guid linkId) =>
        LinkSeverities.TryGetValue(linkId, out var sev) ? sev : ValidationSeverity.Valid;

    public ValidationSeverity GetFunctoidSeverity(Guid instanceId) =>
        FunctoidSeverities.TryGetValue(instanceId, out var sev) ? sev : ValidationSeverity.Valid;
}

public static class MappingValidator
{
    private sealed record EvaluatedSignal(
        PortTypeSpec TypeSpec,
        bool IsNullable,
        bool IsOptional,
        bool IsRepeating,
        string Label);

    public static MappingValidationReport Validate(
        MappingDefinition mapping,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog,
        IReadOnlyDictionary<Guid, FunctoidDefinition> functoidCatalog)
    {
        var report = new MappingValidationReport();
        var typeNames = typeCatalog.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Name);

        typeCatalog.TryGetValue(mapping.SourceTypeId, out var sourceType);
        typeCatalog.TryGetValue(mapping.TargetTypeId, out var targetType);

        // Track target property incoming link counts across ALL grid pages
        var targetPropertyLinks = new Dictionary<string, List<(MappingGridPage Page, MappingLink Link)>>(StringComparer.Ordinal);

        foreach (var page in mapping.GridPages.OrderBy(p => p.SortOrder))
        {
            ValidateGridPage(page, sourceType, targetType, typeCatalog, typeNames, functoidCatalog, targetPropertyLinks, report);
        }

        // Warn when multiple wires feed the exact same target property across the mapping
        foreach (var (targetPath, entries) in targetPropertyLinks)
        {
            if (entries.Count <= 1) continue;
            var resolved = PropertyPathResolver.Resolve(targetType, targetPath, typeCatalog);
            var label = resolved.IsValid ? resolved.DisplayPath : targetPath;

            foreach (var (page, link) in entries)
            {
                AddDiagnostic(report, new ValidationDiagnostic
                {
                    Severity = ValidationSeverity.Warning,
                    Code = ValidationCode.MultipleTargetMappings,
                    Message = $"Target property '{label}' receives {entries.Count} incoming connections without a combining functoid.",
                    GridPageId = page.Id,
                    GridPageName = page.Name,
                    LinkId = link.Id,
                    TargetPropertyPath = targetPath
                });
            }
        }

        return report;
    }

    /// <summary>
    /// Evaluates a prospective connection in real time (e.g., while dragging a wire over a candidate port).
    /// </summary>
    public static (ValidationSeverity Severity, List<string> Messages) PreviewCandidateLink(
        MappingDefinition mapping,
        MappingGridPage activePage,
        MappingLink candidateLink,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog,
        IReadOnlyDictionary<Guid, FunctoidDefinition> functoidCatalog)
    {
        // Clone page temporarily with candidate link added so full DAG inference + cycle detection runs accurately
        var simulatedMapping = new MappingDefinition
        {
            Id = mapping.Id,
            Name = mapping.Name,
            SourceTypeId = mapping.SourceTypeId,
            TargetTypeId = mapping.TargetTypeId,
            GridPages = mapping.GridPages.Select(p =>
            {
                var copy = new MappingGridPage
                {
                    Id = p.Id,
                    MappingId = p.MappingId,
                    Name = p.Name,
                    SortOrder = p.SortOrder,
                    FunctoidInstances = p.FunctoidInstances.ToList(),
                    Links = p.Links.ToList()
                };
                if (p.Id == activePage.Id)
                {
                    copy.Links.Add(candidateLink);
                }
                return copy;
            }).ToList()
        };

        var report = Validate(simulatedMapping, typeCatalog, functoidCatalog);
        var severity = report.GetLinkSeverity(candidateLink.Id);
        var messages = report.LinkDiagnostics.TryGetValue(candidateLink.Id, out var list)
            ? list.Select(d => d.Message).ToList()
            : [];

        return (severity, messages);
    }

    private static void ValidateGridPage(
        MappingGridPage page,
        TypeDefinition? sourceType,
        TypeDefinition? targetType,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog,
        IReadOnlyDictionary<Guid, string> typeNames,
        IReadOnlyDictionary<Guid, FunctoidDefinition> functoidCatalog,
        Dictionary<string, List<(MappingGridPage Page, MappingLink Link)>> targetPropertyLinks,
        MappingValidationReport report)
    {
        var instancesById = page.FunctoidInstances.ToDictionary(f => f.Id);

        // 1. Detect DAG cycles among functoid instances on this page
        var cycleNodes = DetectFunctoidCycles(page);
        foreach (var cycleNodeId in cycleNodes)
        {
            AddDiagnostic(report, new ValidationDiagnostic
            {
                Severity = ValidationSeverity.Error,
                Code = ValidationCode.FunctoidCycle,
                Message = "Functoid is part of a circular dependency cycle.",
                GridPageId = page.Id,
                GridPageName = page.Name,
                FunctoidInstanceId = cycleNodeId
            });
        }

        // Mark cycle links as Errors too
        foreach (var link in page.Links)
        {
            if (link.SourceFunctoidInstanceId is Guid sf &&
                link.TargetFunctoidInstanceId is Guid tf &&
                cycleNodes.Contains(sf) && cycleNodes.Contains(tf))
            {
                AddDiagnostic(report, new ValidationDiagnostic
                {
                    Severity = ValidationSeverity.Error,
                    Code = ValidationCode.FunctoidCycle,
                    Message = "Connection creates a circular dependency between functoids.",
                    GridPageId = page.Id,
                    GridPageName = page.Name,
                    LinkId = link.Id
                });
            }
        }

        // 2. Check functoid definitions, required inputs, config params, duplicate pin inputs, and dangling outputs
        var incomingByFunctoidPin = page.Links
            .Where(l => l.TargetFunctoidInstanceId.HasValue && l.TargetInputIndex.HasValue)
            .GroupBy(l => (FunctoidId: l.TargetFunctoidInstanceId!.Value, Pin: l.TargetInputIndex!.Value))
            .ToDictionary(g => g.Key, g => g.ToList());

        var outgoingByFunctoid = page.Links
            .Where(l => l.SourceFunctoidInstanceId.HasValue)
            .GroupBy(l => l.SourceFunctoidInstanceId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var ((fid, pin), pinLinks) in incomingByFunctoidPin)
        {
            if (pinLinks.Count > 1)
            {
                foreach (var dupLink in pinLinks)
                {
                    AddDiagnostic(report, new ValidationDiagnostic
                    {
                        Severity = ValidationSeverity.Error,
                        Code = ValidationCode.DuplicateFunctoidPinInput,
                        Message = $"Functoid input #{pin + 1} has multiple incoming connections ({pinLinks.Count}); each input pin accepts only one wire.",
                        GridPageId = page.Id,
                        GridPageName = page.Name,
                        LinkId = dupLink.Id,
                        FunctoidInstanceId = fid
                    });
                }
            }
        }

        foreach (var inst in page.FunctoidInstances)
        {
            var def = inst.FunctoidDefinition ?? (functoidCatalog.TryGetValue(inst.FunctoidDefinitionId, out var d) ? d : null);
            if (def is null)
            {
                AddDiagnostic(report, new ValidationDiagnostic
                {
                    Severity = ValidationSeverity.Error,
                    Code = ValidationCode.MissingFunctoidDefinition,
                    Message = "Referenced functoid definition no longer exists.",
                    GridPageId = page.Id,
                    GridPageName = page.Name,
                    FunctoidInstanceId = inst.Id
                });
                continue;
            }

            foreach (var inputSpec in def.Inputs)
            {
                if (!inputSpec.IsOptional && !incomingByFunctoidPin.ContainsKey((inst.Id, inputSpec.Index)))
                {
                    // Allow Coalesce / Equals with configured literal fallback when applicable
                    bool satisfiedByLiteral =
                        (def.PresetKey == "preset.coalesce" && inst.ConfigValues.TryGetValue("defaultLiteral", out var lit) && !string.IsNullOrEmpty(lit) && inputSpec.Index > 0)
                        || (def.PresetKey == "preset.equals" && inst.ConfigValues.TryGetValue("compareTo", out var cmp) && !string.IsNullOrEmpty(cmp) && inputSpec.Index == 1);

                    if (!satisfiedByLiteral)
                    {
                        AddDiagnostic(report, new ValidationDiagnostic
                        {
                            Severity = ValidationSeverity.Warning,
                            Code = ValidationCode.UnconnectedFunctoidInput,
                            Message = $"Functoid '{def.Name}' required input '{inputSpec.Name}' (#{inputSpec.Index + 1}) is not connected.",
                            GridPageId = page.Id,
                            GridPageName = page.Name,
                            FunctoidInstanceId = inst.Id
                        });
                    }
                }
            }

            foreach (var cfg in def.ConfigParams.Where(c => c.IsRequired))
            {
                var hasVal = inst.ConfigValues.TryGetValue(cfg.Key, out var v) && !string.IsNullOrWhiteSpace(v);
                var hasDefault = !string.IsNullOrWhiteSpace(cfg.DefaultValue);
                if (!hasVal && !hasDefault)
                {
                    AddDiagnostic(report, new ValidationDiagnostic
                    {
                        Severity = ValidationSeverity.Warning,
                        Code = ValidationCode.MissingRequiredConfigParam,
                        Message = $"Functoid '{def.Name}' requires configuration parameter '{cfg.Label}'.",
                        GridPageId = page.Id,
                        GridPageName = page.Name,
                        FunctoidInstanceId = inst.Id
                    });
                }
            }

            if (!outgoingByFunctoid.ContainsKey(inst.Id))
            {
                AddDiagnostic(report, new ValidationDiagnostic
                {
                    Severity = ValidationSeverity.Warning,
                    Code = ValidationCode.DanglingFunctoidOutput,
                    Message = $"Functoid '{def.Name}' output is not connected to any target.",
                    GridPageId = page.Id,
                    GridPageName = page.Name,
                    FunctoidInstanceId = inst.Id
                });
            }
        }

        // Memoized evaluation of functoid outputs (supports multi-hop DAG type inference)
        var evaluatedFunctoids = new Dictionary<Guid, EvaluatedSignal>();
        var visiting = new HashSet<Guid>();

        EvaluatedSignal? EvaluateFunctoidOutput(Guid instanceId)
        {
            if (evaluatedFunctoids.TryGetValue(instanceId, out var cached)) return cached;
            if (!instancesById.TryGetValue(instanceId, out var inst)) return null;
            if (!visiting.Add(instanceId))
            {
                return new EvaluatedSignal(PortTypeSpec.Any(), false, false, false, "cycle");
            }

            try
            {
                var def = inst.FunctoidDefinition ?? (functoidCatalog.TryGetValue(inst.FunctoidDefinitionId, out var d) ? d : null);
                if (def is null) return null;

                var incomingLinks = page.Links.Where(l => l.TargetFunctoidInstanceId == instanceId).ToList();
                bool anyRepeatingInput = false;

                foreach (var inLink in incomingLinks)
                {
                    var sig = EvaluateLinkSource(inLink);
                    if (sig is { IsRepeating: true })
                    {
                        anyRepeatingInput = true;
                    }
                }

                bool outRepeating = anyRepeatingInput && !def.AggregatesRepeatingInput;
                PortTypeSpec resolvedOutput = def.OutputType.Clone();

                switch (def.OutputInference)
                {
                    case OutputInferenceRule.SameAsInputPin:
                    {
                        var pinLink = incomingLinks.FirstOrDefault(l => l.TargetInputIndex == def.OutputInferencePinIndex)
                                      ?? incomingLinks.OrderBy(l => l.TargetInputIndex ?? 0).FirstOrDefault();
                        if (pinLink is not null)
                        {
                            var pinSig = EvaluateLinkSource(pinLink);
                            if (pinSig is not null)
                            {
                                resolvedOutput = pinSig.TypeSpec.Clone();
                            }
                        }
                        break;
                    }
                    case OutputInferenceRule.ElementOfArrayInputPin:
                    {
                        var pinLink = incomingLinks.FirstOrDefault(l => l.TargetInputIndex == def.OutputInferencePinIndex);
                        if (pinLink is not null)
                        {
                            var pinSig = EvaluateLinkSource(pinLink);
                            if (pinSig?.TypeSpec.SpecificType is { BaseKind: BaseTypeKind.Array, ElementType: { } elem })
                            {
                                resolvedOutput = PortTypeSpec.Of(elem);
                            }
                            else if (pinSig is not null)
                            {
                                resolvedOutput = pinSig.TypeSpec.Clone();
                            }
                        }
                        break;
                    }
                    case OutputInferenceRule.ArrayOfInputPin:
                    {
                        var pinLink = incomingLinks.FirstOrDefault(l => l.TargetInputIndex == def.OutputInferencePinIndex);
                        if (pinLink is not null)
                        {
                            var pinSig = EvaluateLinkSource(pinLink);
                            if (pinSig?.TypeSpec.SpecificType is { } innerType)
                            {
                                resolvedOutput = PortTypeSpec.Of(TypeDescriptor.ArrayOf(innerType));
                            }
                        }
                        break;
                    }
                    case OutputInferenceRule.FromConstantConfig:
                    {
                        resolvedOutput = PortTypeSpec.Of(ResolveConstantConfigType(inst));
                        break;
                    }
                }

                // Coalesce & Generators guarantee non-null / non-optional output; ValueMapping may produce optional
                bool outNullable = false;
                bool outOptional = def.PresetKey == "preset.valuemapping";

                var evaluated = new EvaluatedSignal(resolvedOutput, outNullable, outOptional, outRepeating, $"{def.Name}()");
                evaluatedFunctoids[instanceId] = evaluated;
                return evaluated;
            }
            finally
            {
                visiting.Remove(instanceId);
            }
        }

        EvaluatedSignal? EvaluateLinkSource(MappingLink link)
        {
            if (!string.IsNullOrWhiteSpace(link.SourcePropertyPath))
            {
                var resolved = PropertyPathResolver.Resolve(sourceType, link.SourcePropertyPath, typeCatalog);
                if (!resolved.IsValid) return null;
                return new EvaluatedSignal(
                    PortTypeSpec.Of(resolved.Type),
                    resolved.EffectiveIsNullable,
                    resolved.EffectiveIsOptional,
                    resolved.IsRepeating,
                    resolved.DisplayPath);
            }

            if (link.SourceFunctoidInstanceId is Guid fid)
            {
                return EvaluateFunctoidOutput(fid);
            }

            return null;
        }

        // 3. Validate each link's endpoints, type compatibility, nullability/optionality, and cardinality
        foreach (var link in page.Links)
        {
            // Ensure link has an initial entry in LinkSeverities
            report.LinkSeverities.TryAdd(link.Id, ValidationSeverity.Valid);

            EvaluatedSignal? sourceSignal = null;

            if (!string.IsNullOrWhiteSpace(link.SourcePropertyPath))
            {
                var srcResolved = PropertyPathResolver.Resolve(sourceType, link.SourcePropertyPath, typeCatalog);
                if (!srcResolved.IsValid)
                {
                    AddDiagnostic(report, new ValidationDiagnostic
                    {
                        Severity = ValidationSeverity.Error,
                        Code = ValidationCode.BrokenEndpoint,
                        Message = $"Source endpoint is broken: {srcResolved.ErrorMessage}",
                        GridPageId = page.Id,
                        GridPageName = page.Name,
                        LinkId = link.Id
                    });
                    continue;
                }
                sourceSignal = EvaluateLinkSource(link);
            }
            else if (link.SourceFunctoidInstanceId is Guid sfId)
            {
                if (!instancesById.ContainsKey(sfId))
                {
                    AddDiagnostic(report, new ValidationDiagnostic
                    {
                        Severity = ValidationSeverity.Error,
                        Code = ValidationCode.BrokenEndpoint,
                        Message = "Source functoid instance no longer exists on this grid page.",
                        GridPageId = page.Id,
                        GridPageName = page.Name,
                        LinkId = link.Id
                    });
                    continue;
                }
                sourceSignal = EvaluateFunctoidOutput(sfId);
            }

            if (sourceSignal is null) continue;

            // Case A: Target is a Target Type property
            if (!string.IsNullOrWhiteSpace(link.TargetPropertyPath))
            {
                var tgtResolved = PropertyPathResolver.Resolve(targetType, link.TargetPropertyPath, typeCatalog);
                if (!tgtResolved.IsValid)
                {
                    AddDiagnostic(report, new ValidationDiagnostic
                    {
                        Severity = ValidationSeverity.Error,
                        Code = ValidationCode.BrokenEndpoint,
                        Message = $"Target endpoint is broken: {tgtResolved.ErrorMessage}",
                        GridPageId = page.Id,
                        GridPageName = page.Name,
                        LinkId = link.Id
                    });
                    continue;
                }

                if (!targetPropertyLinks.TryGetValue(link.TargetPropertyPath, out var tList))
                {
                    tList = [];
                    targetPropertyLinks[link.TargetPropertyPath] = tList;
                }
                tList.Add((page, link));

                ValidateSignalToTargetSpec(
                    sourceSignal,
                    PortTypeSpec.Of(tgtResolved.Type),
                    targetLabel: tgtResolved.DisplayPath,
                    checkNullAndOptional: true,
                    targetIsNullable: tgtResolved.LeafIsNullable,
                    targetIsOptional: tgtResolved.LeafIsOptional,
                    page,
                    link,
                    typeNames,
                    report);

                // Repeating N -> Single 1 cardinality warning
                if (sourceSignal.IsRepeating && !tgtResolved.IsRepeating)
                {
                    AddDiagnostic(report, new ValidationDiagnostic
                    {
                        Severity = ValidationSeverity.Warning,
                        Code = ValidationCode.RepeatingToSingleCardinality,
                        Message = $"Repeating array item '{sourceSignal.Label}' (0..N) is mapped to single non-array property '{tgtResolved.DisplayPath}' (1) without an aggregating functoid (e.g., First, Join, Sum, Count).",
                        GridPageId = page.Id,
                        GridPageName = page.Name,
                        LinkId = link.Id,
                        TargetPropertyPath = link.TargetPropertyPath
                    });
                }
            }
            // Case B: Target is a Functoid Input pin
            else if (link.TargetFunctoidInstanceId is Guid tfId)
            {
                if (!instancesById.TryGetValue(tfId, out var targetInst))
                {
                    AddDiagnostic(report, new ValidationDiagnostic
                    {
                        Severity = ValidationSeverity.Error,
                        Code = ValidationCode.BrokenEndpoint,
                        Message = "Target functoid instance no longer exists on this grid page.",
                        GridPageId = page.Id,
                        GridPageName = page.Name,
                        LinkId = link.Id
                    });
                    continue;
                }

                var def = targetInst.FunctoidDefinition ?? (functoidCatalog.TryGetValue(targetInst.FunctoidDefinitionId, out var d) ? d : null);
                if (def is null) continue;

                var pinIdx = link.TargetInputIndex ?? 0;
                var inputSpec = def.GetInputSpec(pinIdx);
                if (inputSpec is null)
                {
                    AddDiagnostic(report, new ValidationDiagnostic
                    {
                        Severity = ValidationSeverity.Error,
                        Code = ValidationCode.BrokenEndpoint,
                        Message = $"Functoid '{def.Name}' does not have an input pin at index {pinIdx + 1}.",
                        GridPageId = page.Id,
                        GridPageName = page.Name,
                        LinkId = link.Id,
                        FunctoidInstanceId = tfId
                    });
                    continue;
                }

                ValidateSignalToTargetSpec(
                    sourceSignal,
                    inputSpec.TypeSpec,
                    targetLabel: $"{def.Name}.{inputSpec.Name}",
                    checkNullAndOptional: false,
                    targetIsNullable: true,
                    targetIsOptional: true,
                    page,
                    link,
                    typeNames,
                    report);
            }
        }
    }

    private static void ValidateSignalToTargetSpec(
        EvaluatedSignal source,
        PortTypeSpec targetSpec,
        string targetLabel,
        bool checkNullAndOptional,
        bool targetIsNullable,
        bool targetIsOptional,
        MappingGridPage page,
        MappingLink link,
        IReadOnlyDictionary<Guid, string> typeNames,
        MappingValidationReport report)
    {
        var srcSpec = source.TypeSpec;

        // If source itself is wildcard (e.g., Custom/Coalesce/IfThenElse before inputs are wired)
        if (srcSpec.Wildcard != PortWildcardKind.None)
        {
            if (targetSpec.Wildcard == PortWildcardKind.None && targetSpec.SpecificType is { } concreteTarget)
            {
                if (srcSpec.Wildcard == PortWildcardKind.AnyScalar && !concreteTarget.IsScalar)
                {
                    AddDiagnostic(report, new ValidationDiagnostic
                    {
                        Severity = ValidationSeverity.Error,
                        Code = ValidationCode.StructuralTypeMismatch,
                        Message = $"Cannot map scalar output '{source.Label}' ({srcSpec.FormatDisplay(typeNames)}) to non-scalar '{targetLabel}' ({concreteTarget.FormatDisplay(typeNames)}).",
                        GridPageId = page.Id,
                        GridPageName = page.Name,
                        LinkId = link.Id
                    });
                }
                else if (srcSpec.Wildcard == PortWildcardKind.AnyArray && concreteTarget.BaseKind != BaseTypeKind.Array)
                {
                    AddDiagnostic(report, new ValidationDiagnostic
                    {
                        Severity = ValidationSeverity.Error,
                        Code = ValidationCode.StructuralTypeMismatch,
                        Message = $"Cannot map array output '{source.Label}' to non-array '{targetLabel}' ({concreteTarget.FormatDisplay(typeNames)}).",
                        GridPageId = page.Id,
                        GridPageName = page.Name,
                        LinkId = link.Id
                    });
                }
                else if (srcSpec.Wildcard == PortWildcardKind.AnyObject && concreteTarget.BaseKind != BaseTypeKind.Object)
                {
                    AddDiagnostic(report, new ValidationDiagnostic
                    {
                        Severity = ValidationSeverity.Error,
                        Code = ValidationCode.StructuralTypeMismatch,
                        Message = $"Cannot map object output '{source.Label}' to non-object '{targetLabel}' ({concreteTarget.FormatDisplay(typeNames)}).",
                        GridPageId = page.Id,
                        GridPageName = page.Name,
                        LinkId = link.Id
                    });
                }
            }
            return;
        }

        var srcType = srcSpec.SpecificType!;

        // Check wildcard target ports on functoids
        if (targetSpec.Wildcard != PortWildcardKind.None)
        {
            switch (targetSpec.Wildcard)
            {
                case PortWildcardKind.Any:
                    return;
                case PortWildcardKind.AnyScalar:
                    if (!srcType.IsScalar)
                    {
                        AddDiagnostic(report, new ValidationDiagnostic
                        {
                            Severity = ValidationSeverity.Error,
                            Code = ValidationCode.StructuralTypeMismatch,
                            Message = $"'{targetLabel}' expects a scalar value, but '{source.Label}' is '{srcType.FormatDisplay(typeNames)}'.",
                            GridPageId = page.Id,
                            GridPageName = page.Name,
                            LinkId = link.Id
                        });
                    }
                    return;
                case PortWildcardKind.AnyArray:
                    if (srcType.BaseKind != BaseTypeKind.Array)
                    {
                        AddDiagnostic(report, new ValidationDiagnostic
                        {
                            Severity = ValidationSeverity.Error,
                            Code = ValidationCode.StructuralTypeMismatch,
                            Message = $"'{targetLabel}' expects an array, but '{source.Label}' is '{srcType.FormatDisplay(typeNames)}'.",
                            GridPageId = page.Id,
                            GridPageName = page.Name,
                            LinkId = link.Id
                        });
                    }
                    return;
                case PortWildcardKind.AnyObject:
                    if (srcType.BaseKind != BaseTypeKind.Object)
                    {
                        AddDiagnostic(report, new ValidationDiagnostic
                        {
                            Severity = ValidationSeverity.Error,
                            Code = ValidationCode.StructuralTypeMismatch,
                            Message = $"'{targetLabel}' expects an object, but '{source.Label}' is '{srcType.FormatDisplay(typeNames)}'.",
                            GridPageId = page.Id,
                            GridPageName = page.Name,
                            LinkId = link.Id
                        });
                    }
                    return;
            }
        }

        var tgtType = targetSpec.SpecificType!;
        var (compatSeverity, compatCode, compatReason) = EvaluateConcreteTypes(srcType, tgtType, typeNames);

        if (compatSeverity != ValidationSeverity.Valid)
        {
            AddDiagnostic(report, new ValidationDiagnostic
            {
                Severity = compatSeverity,
                Code = compatCode,
                Message = $"{source.Label} ({srcType.FormatDisplay(typeNames)}) → {targetLabel} ({tgtType.FormatDisplay(typeNames)}): {compatReason}",
                GridPageId = page.Id,
                GridPageName = page.Name,
                LinkId = link.Id,
                TargetPropertyPath = link.TargetPropertyPath
            });

            if (compatSeverity == ValidationSeverity.Error)
            {
                return;
            }
        }

        if (checkNullAndOptional)
        {
            if (source.IsNullable && !targetIsNullable)
            {
                AddDiagnostic(report, new ValidationDiagnostic
                {
                    Severity = ValidationSeverity.Warning,
                    Code = ValidationCode.NullabilityMismatch,
                    Message = $"Nullable source '{source.Label}' is mapped to non-nullable target '{targetLabel}'. Consider using a Coalesce functoid.",
                    GridPageId = page.Id,
                    GridPageName = page.Name,
                    LinkId = link.Id,
                    TargetPropertyPath = link.TargetPropertyPath
                });
            }

            if (source.IsOptional && !targetIsOptional)
            {
                AddDiagnostic(report, new ValidationDiagnostic
                {
                    Severity = ValidationSeverity.Warning,
                    Code = ValidationCode.OptionalityMismatch,
                    Message = $"Optional source '{source.Label}' is mapped to required target '{targetLabel}'.",
                    GridPageId = page.Id,
                    GridPageName = page.Name,
                    LinkId = link.Id,
                    TargetPropertyPath = link.TargetPropertyPath
                });
            }
        }
    }

    public static (ValidationSeverity Severity, ValidationCode Code, string Reason) EvaluateConcreteTypes(
        TypeDescriptor source,
        TypeDescriptor target,
        IReadOnlyDictionary<Guid, string>? typeNames = null)
    {
        // 1. Structural mismatches (Scalar <-> Object/Array, Object <-> Array) => ERROR
        if (source.IsScalar && !target.IsScalar)
        {
            return (ValidationSeverity.Error, ValidationCode.StructuralTypeMismatch,
                $"Cannot map scalar '{source.FormatDisplay(typeNames)}' directly to {target.BaseKind.ToString().ToLowerInvariant()} '{target.FormatDisplay(typeNames)}'.");
        }

        if (!source.IsScalar && target.IsScalar)
        {
            return (ValidationSeverity.Error, ValidationCode.StructuralTypeMismatch,
                $"Cannot map {source.BaseKind.ToString().ToLowerInvariant()} '{source.FormatDisplay(typeNames)}' directly to scalar '{target.FormatDisplay(typeNames)}'.");
        }

        if (source.BaseKind == BaseTypeKind.Object && target.BaseKind == BaseTypeKind.Array)
        {
            return (ValidationSeverity.Error, ValidationCode.StructuralTypeMismatch,
                "Cannot map an object directly to an array.");
        }

        if (source.BaseKind == BaseTypeKind.Array && target.BaseKind == BaseTypeKind.Object)
        {
            return (ValidationSeverity.Error, ValidationCode.StructuralTypeMismatch,
                "Cannot map an array directly to an object.");
        }

        // 2. Object -> Object
        if (source.BaseKind == BaseTypeKind.Object && target.BaseKind == BaseTypeKind.Object)
        {
            // Target is Untyped Object => accepts any Object
            if (target.ObjectTypeId is null)
            {
                return (ValidationSeverity.Valid, default, string.Empty);
            }

            // Same named catalog Type => Valid
            if (source.ObjectTypeId.HasValue && source.ObjectTypeId == target.ObjectTypeId)
            {
                return (ValidationSeverity.Valid, default, string.Empty);
            }

            return (ValidationSeverity.Error, ValidationCode.IncompatibleObjectTypes,
                $"Incompatible object types '{source.FormatDisplay(typeNames)}' and '{target.FormatDisplay(typeNames)}'. Map child properties individually or route through a converter functoid.");
        }

        // 3. Array -> Array (recursive element validation)
        if (source.BaseKind == BaseTypeKind.Array && target.BaseKind == BaseTypeKind.Array)
        {
            if (source.ElementType is null || target.ElementType is null)
            {
                return (ValidationSeverity.Valid, default, string.Empty);
            }

            var elemResult = EvaluateConcreteTypes(source.ElementType, target.ElementType, typeNames);
            if (elemResult.Severity != ValidationSeverity.Valid)
            {
                return (elemResult.Severity, elemResult.Code, $"Array element mismatch — {elemResult.Reason}");
            }

            if (source.ElementIsNullable && !target.ElementIsNullable)
            {
                return (ValidationSeverity.Warning, ValidationCode.NullabilityMismatch,
                    "Source array permits null elements, whereas target array requires non-null elements.");
            }

            return (ValidationSeverity.Valid, default, string.Empty);
        }

        // 4. Scalar -> Scalar
        if (source.Equals(target))
        {
            return (ValidationSeverity.Valid, default, string.Empty);
        }

        // Safe scalar widening rules
        if (IsSafeScalarWidening(source, target))
        {
            return (ValidationSeverity.Valid, default, string.Empty);
        }

        return (ValidationSeverity.Warning, ValidationCode.ScalarTypeMismatch,
            $"Scalar types differ ('{source.FormatDisplay(typeNames)}' vs '{target.FormatDisplay(typeNames)}'). Consider inserting a conversion functoid (e.g., ToString).");
    }

    public static bool IsSafeScalarWidening(TypeDescriptor source, TypeDescriptor target)
    {
        // Specific string format (uuid, date, email, etc.) widens safely to plain unformatted string
        if (source.BaseKind == BaseTypeKind.String &&
            target.BaseKind == BaseTypeKind.String &&
            source.StringSubtype != StringSubtype.None &&
            target.StringSubtype == StringSubtype.None)
        {
            return true;
        }

        if (source.BaseKind == BaseTypeKind.Number && target.BaseKind == BaseTypeKind.Number)
        {
            // Any specific number widens safely to generic Number(None)
            if (source.NumberSubtype != NumberSubtype.None && target.NumberSubtype == NumberSubtype.None)
            {
                return true;
            }

            return (source.NumberSubtype, target.NumberSubtype) switch
            {
                (NumberSubtype.Int32, NumberSubtype.Int64 or NumberSubtype.Float or NumberSubtype.Double or NumberSubtype.Decimal) => true,
                (NumberSubtype.Int64, NumberSubtype.Double or NumberSubtype.Decimal) => true,
                (NumberSubtype.Float, NumberSubtype.Double or NumberSubtype.Decimal) => true,
                _ => false
            };
        }

        return false;
    }

    private static TypeDescriptor ResolveConstantConfigType(MappingFunctoidInstance inst)
    {
        var raw = inst.ConfigValues.TryGetValue("type", out var t) ? t.Trim().ToLowerInvariant() : "string";
        return raw switch
        {
            "bool" or "boolean" => TypeDescriptor.ScalarBoolean(),
            "int" or "int32" or "integer" => TypeDescriptor.ScalarNumber(NumberSubtype.Int32),
            "long" or "int64" => TypeDescriptor.ScalarNumber(NumberSubtype.Int64),
            "float" => TypeDescriptor.ScalarNumber(NumberSubtype.Float),
            "double" => TypeDescriptor.ScalarNumber(NumberSubtype.Double),
            "decimal" => TypeDescriptor.ScalarNumber(NumberSubtype.Decimal),
            "number" => TypeDescriptor.ScalarNumber(NumberSubtype.None),
            "uuid" or "guid" => TypeDescriptor.ScalarString(StringSubtype.Uuid),
            "date" => TypeDescriptor.ScalarString(StringSubtype.Date),
            "time" => TypeDescriptor.ScalarString(StringSubtype.Time),
            "datetime" => TypeDescriptor.ScalarString(StringSubtype.DateTime),
            "uri" or "url" => TypeDescriptor.ScalarString(StringSubtype.Uri),
            "email" => TypeDescriptor.ScalarString(StringSubtype.Email),
            _ => TypeDescriptor.ScalarString()
        };
    }

    private static HashSet<Guid> DetectFunctoidCycles(MappingGridPage page)
    {
        var adj = new Dictionary<Guid, List<Guid>>();
        foreach (var f in page.FunctoidInstances)
        {
            adj[f.Id] = [];
        }

        foreach (var l in page.Links)
        {
            if (l.SourceFunctoidInstanceId is Guid src && l.TargetFunctoidInstanceId is Guid tgt && adj.ContainsKey(src) && adj.ContainsKey(tgt))
            {
                adj[src].Add(tgt);
            }
        }

        // 0 = unvisited, 1 = visiting, 2 = done
        var state = new Dictionary<Guid, int>();
        var inCycle = new HashSet<Guid>();
        var stack = new List<Guid>();

        void Dfs(Guid u)
        {
            state[u] = 1;
            stack.Add(u);

            foreach (var v in adj[u])
            {
                var st = state.GetValueOrDefault(v, 0);
                if (st == 0)
                {
                    Dfs(v);
                }
                else if (st == 1)
                {
                    var idx = stack.IndexOf(v);
                    for (int i = idx >= 0 ? idx : 0; i < stack.Count; i++)
                    {
                        inCycle.Add(stack[i]);
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[u] = 2;
        }

        foreach (var f in page.FunctoidInstances)
        {
            if (state.GetValueOrDefault(f.Id, 0) == 0)
            {
                Dfs(f.Id);
            }
        }

        return inCycle;
    }

    private static void AddDiagnostic(MappingValidationReport report, ValidationDiagnostic diag)
    {
        report.Diagnostics.Add(diag);

        if (diag.LinkId is Guid linkId)
        {
            if (!report.LinkDiagnostics.TryGetValue(linkId, out var list))
            {
                list = [];
                report.LinkDiagnostics[linkId] = list;
            }
            list.Add(diag);

            var existing = report.LinkSeverities.GetValueOrDefault(linkId, ValidationSeverity.Valid);
            if (diag.Severity > existing)
            {
                report.LinkSeverities[linkId] = diag.Severity;
            }
        }

        if (diag.FunctoidInstanceId is Guid fid)
        {
            if (!report.FunctoidDiagnostics.TryGetValue(fid, out var list))
            {
                list = [];
                report.FunctoidDiagnostics[fid] = list;
            }
            list.Add(diag);

            var existing = report.FunctoidSeverities.GetValueOrDefault(fid, ValidationSeverity.Valid);
            if (diag.Severity > existing)
            {
                report.FunctoidSeverities[fid] = diag.Severity;
            }
        }
    }
}
