using MapDoc.Core.Models;
using MapDoc.Core.Presets;
using MapDoc.Core.Validation;

namespace MapDoc.Tests;

public class MappingValidatorTests
{
    private static readonly IReadOnlyDictionary<Guid, FunctoidDefinition> Functoids =
        PresetFunctoids.All.ToDictionary(f => f.Id);

    private static FunctoidDefinition Preset(string key) =>
        PresetFunctoids.All.First(f => f.PresetKey == key);

    [Fact]
    public void ExactMatch_And_SafeWidening_AreValid()
    {
        var (src, pUuid, pInt) = CreateTwoPropType("Src",
            ("id", TypeDescriptor.ScalarString(StringSubtype.Uuid)),
            ("count", TypeDescriptor.ScalarNumber(NumberSubtype.Int32)));

        var (tgt, pPlainStr, pDec) = CreateTwoPropType("Tgt",
            ("externalRef", TypeDescriptor.ScalarString(StringSubtype.None)),
            ("amount", TypeDescriptor.ScalarNumber(NumberSubtype.Decimal)));

        var mapping = SinglePageMapping(src, tgt,
            new MappingLink { SourcePropertyPath = pUuid.Id.ToString(), TargetPropertyPath = pPlainStr.Id.ToString() },
            new MappingLink { SourcePropertyPath = pInt.Id.ToString(), TargetPropertyPath = pDec.Id.ToString() });

        var catalog = new Dictionary<Guid, TypeDefinition> { [src.Id] = src, [tgt.Id] = tgt };
        var report = MappingValidator.Validate(mapping, catalog, Functoids);

        Assert.True(report.IsClean);
        Assert.Equal(0, report.ErrorCount);
        Assert.Equal(0, report.WarningCount);
    }

    [Fact]
    public void DifferentScalars_Warn_UntilRoutedThroughToString()
    {
        var (src, pNum, _) = CreateTwoPropType("Src",
            ("seq", TypeDescriptor.ScalarNumber(NumberSubtype.Int32)),
            ("unused", TypeDescriptor.ScalarBoolean()));

        var (tgt, pStr, _) = CreateTwoPropType("Tgt",
            ("code", TypeDescriptor.ScalarString()),
            ("unused", TypeDescriptor.ScalarBoolean()));

        var catalog = new Dictionary<Guid, TypeDefinition> { [src.Id] = src, [tgt.Id] = tgt };

        // Direct int32 -> string => Warning
        var direct = SinglePageMapping(src, tgt,
            new MappingLink { SourcePropertyPath = pNum.Id.ToString(), TargetPropertyPath = pStr.Id.ToString() });

        var directReport = MappingValidator.Validate(direct, catalog, Functoids);
        Assert.Equal(0, directReport.ErrorCount);
        Assert.Equal(1, directReport.WarningCount);
        Assert.Contains(directReport.Diagnostics, d => d.Code == ValidationCode.ScalarTypeMismatch);

        // Routed through ToString => Valid!
        var toStringDef = Preset("preset.tostring");
        var fnInst = new MappingFunctoidInstance { Id = Guid.NewGuid(), FunctoidDefinitionId = toStringDef.Id };
        var converted = SinglePageMapping(src, tgt,
            [fnInst],
            new MappingLink { SourcePropertyPath = pNum.Id.ToString(), TargetFunctoidInstanceId = fnInst.Id, TargetInputIndex = 0 },
            new MappingLink { SourceFunctoidInstanceId = fnInst.Id, TargetPropertyPath = pStr.Id.ToString() });

        var convertedReport = MappingValidator.Validate(converted, catalog, Functoids);
        Assert.True(convertedReport.IsClean);
    }

    [Fact]
    public void StructuralMismatches_ObjectArrayScalar_ProduceErrors()
    {
        var addrType = new TypeDefinition { Id = Guid.NewGuid(), Name = "Address" };
        var otherObjType = new TypeDefinition { Id = Guid.NewGuid(), Name = "OtherEntity" };

        var pScalar = new PropertyDefinition { Id = Guid.NewGuid(), Name = "name", Type = TypeDescriptor.ScalarString() };
        var pArray = new PropertyDefinition { Id = Guid.NewGuid(), Name = "tags", Type = TypeDescriptor.ArrayOf(TypeDescriptor.ScalarString()) };
        var pObjA = new PropertyDefinition { Id = Guid.NewGuid(), Name = "addr", Type = TypeDescriptor.ObjectOf(addrType.Id) };

        var src = new TypeDefinition { Id = Guid.NewGuid(), Name = "Src", Properties = [pScalar, pArray, pObjA] };

        var tScalar = new PropertyDefinition { Id = Guid.NewGuid(), Name = "title", Type = TypeDescriptor.ScalarString() };
        var tArray = new PropertyDefinition { Id = Guid.NewGuid(), Name = "items", Type = TypeDescriptor.ArrayOf(TypeDescriptor.ScalarString()) };
        var tObjB = new PropertyDefinition { Id = Guid.NewGuid(), Name = "other", Type = TypeDescriptor.ObjectOf(otherObjType.Id) };

        var tgt = new TypeDefinition { Id = Guid.NewGuid(), Name = "Tgt", Properties = [tScalar, tArray, tObjB] };

        var l1 = new MappingLink { Id = Guid.NewGuid(), SourcePropertyPath = pObjA.Id.ToString(), TargetPropertyPath = tScalar.Id.ToString() }; // Object -> Scalar
        var l2 = new MappingLink { Id = Guid.NewGuid(), SourcePropertyPath = pScalar.Id.ToString(), TargetPropertyPath = tArray.Id.ToString() }; // Scalar -> Array
        var l3 = new MappingLink { Id = Guid.NewGuid(), SourcePropertyPath = pObjA.Id.ToString(), TargetPropertyPath = tArray.Id.ToString() };  // Object -> Array
        var l4 = new MappingLink { Id = Guid.NewGuid(), SourcePropertyPath = pObjA.Id.ToString(), TargetPropertyPath = tObjB.Id.ToString() };   // Object(A) -> Object(B)

        var mapping = SinglePageMapping(src, tgt, l1, l2, l3, l4);
        var catalog = new Dictionary<Guid, TypeDefinition>
        {
            [addrType.Id] = addrType,
            [otherObjType.Id] = otherObjType,
            [src.Id] = src,
            [tgt.Id] = tgt
        };

        var report = MappingValidator.Validate(mapping, catalog, Functoids);
        Assert.Equal(ValidationSeverity.Error, report.GetLinkSeverity(l1.Id));
        Assert.Equal(ValidationSeverity.Error, report.GetLinkSeverity(l2.Id));
        Assert.Equal(ValidationSeverity.Error, report.GetLinkSeverity(l3.Id));
        Assert.Equal(ValidationSeverity.Error, report.GetLinkSeverity(l4.Id));
    }

    [Fact]
    public void RecursiveArrayValidation_AppliesElementRules()
    {
        var (r1, _, _) = MappingValidator.EvaluateConcreteTypes(
            TypeDescriptor.ArrayOf(TypeDescriptor.ScalarString(StringSubtype.Uuid)),
            TypeDescriptor.ArrayOf(TypeDescriptor.ScalarString(StringSubtype.None)));
        Assert.Equal(ValidationSeverity.Valid, r1);

        var (r2, code2, _) = MappingValidator.EvaluateConcreteTypes(
            TypeDescriptor.ArrayOf(TypeDescriptor.ScalarNumber(NumberSubtype.Int32)),
            TypeDescriptor.ArrayOf(TypeDescriptor.ScalarString()));
        Assert.Equal(ValidationSeverity.Warning, r2);
        Assert.Equal(ValidationCode.ScalarTypeMismatch, code2);

        var (r3, code3, _) = MappingValidator.EvaluateConcreteTypes(
            TypeDescriptor.ArrayOf(TypeDescriptor.ObjectOf(Guid.NewGuid())),
            TypeDescriptor.ArrayOf(TypeDescriptor.ScalarString()));
        Assert.Equal(ValidationSeverity.Error, r3);
        Assert.Equal(ValidationCode.StructuralTypeMismatch, code3);
    }

    [Fact]
    public void NullableSource_ToRequiredTarget_Warns_UnlessCoalesced()
    {
        var pPhone = new PropertyDefinition { Id = Guid.NewGuid(), Name = "phone", Type = TypeDescriptor.ScalarString(), IsNullable = true };
        var src = new TypeDefinition { Id = Guid.NewGuid(), Name = "Src", Properties = [pPhone] };

        var tPhone = new PropertyDefinition { Id = Guid.NewGuid(), Name = "contactPhone", Type = TypeDescriptor.ScalarString(), IsNullable = false };
        var tgt = new TypeDefinition { Id = Guid.NewGuid(), Name = "Tgt", Properties = [tPhone] };
        var catalog = new Dictionary<Guid, TypeDefinition> { [src.Id] = src, [tgt.Id] = tgt };

        var direct = SinglePageMapping(src, tgt,
            new MappingLink { SourcePropertyPath = pPhone.Id.ToString(), TargetPropertyPath = tPhone.Id.ToString() });
        var directReport = MappingValidator.Validate(direct, catalog, Functoids);
        Assert.Contains(directReport.Diagnostics, d => d.Code == ValidationCode.NullabilityMismatch);

        var coalesceDef = Preset("preset.coalesce");
        var fn = new MappingFunctoidInstance
        {
            Id = Guid.NewGuid(),
            FunctoidDefinitionId = coalesceDef.Id,
            ConfigValues = new(StringComparer.OrdinalIgnoreCase) { ["defaultLiteral"] = "N/A" }
        };
        var fixedMap = SinglePageMapping(src, tgt,
            [fn],
            new MappingLink { SourcePropertyPath = pPhone.Id.ToString(), TargetFunctoidInstanceId = fn.Id, TargetInputIndex = 0 },
            new MappingLink { SourceFunctoidInstanceId = fn.Id, TargetPropertyPath = tPhone.Id.ToString() });

        var fixedReport = MappingValidator.Validate(fixedMap, catalog, Functoids);
        Assert.True(fixedReport.IsClean);
    }

    [Fact]
    public void RepeatingArrayChild_ToSingleScalar_Warns_UntilAggregated()
    {
        var pSku = new PropertyDefinition { Id = Guid.NewGuid(), Name = "sku", Type = TypeDescriptor.ScalarString() };
        var lineType = new TypeDefinition { Id = Guid.NewGuid(), Name = "Line", Properties = [pSku] };

        var pLines = new PropertyDefinition { Id = Guid.NewGuid(), Name = "lines", Type = TypeDescriptor.ArrayOf(TypeDescriptor.ObjectOf(lineType.Id)) };
        var pHeaderCurr = new PropertyDefinition { Id = Guid.NewGuid(), Name = "currency", Type = TypeDescriptor.ScalarString() };
        var src = new TypeDefinition { Id = Guid.NewGuid(), Name = "Order", Properties = [pLines, pHeaderCurr] };

        var tItemSku = new PropertyDefinition { Id = Guid.NewGuid(), Name = "partNo", Type = TypeDescriptor.ScalarString() };
        var tItemCurr = new PropertyDefinition { Id = Guid.NewGuid(), Name = "curr", Type = TypeDescriptor.ScalarString() };
        var invLineType = new TypeDefinition { Id = Guid.NewGuid(), Name = "InvLine", Properties = [tItemSku, tItemCurr] };

        var tItems = new PropertyDefinition { Id = Guid.NewGuid(), Name = "items", Type = TypeDescriptor.ArrayOf(TypeDescriptor.ObjectOf(invLineType.Id)) };
        var tPrimarySku = new PropertyDefinition { Id = Guid.NewGuid(), Name = "primarySku", Type = TypeDescriptor.ScalarString() };
        var tgt = new TypeDefinition { Id = Guid.NewGuid(), Name = "Invoice", Properties = [tItems, tPrimarySku] };

        var catalog = new Dictionary<Guid, TypeDefinition>
        {
            [lineType.Id] = lineType,
            [src.Id] = src,
            [invLineType.Id] = invLineType,
            [tgt.Id] = tgt
        };

        var linkNtoN = new MappingLink { Id = Guid.NewGuid(), SourcePropertyPath = $"{pLines.Id}/{pSku.Id}", TargetPropertyPath = $"{tItems.Id}/{tItemSku.Id}" };
        var linkBroadcast = new MappingLink { Id = Guid.NewGuid(), SourcePropertyPath = pHeaderCurr.Id.ToString(), TargetPropertyPath = $"{tItems.Id}/{tItemCurr.Id}" };
        var linkNto1 = new MappingLink { Id = Guid.NewGuid(), SourcePropertyPath = $"{pLines.Id}/{pSku.Id}", TargetPropertyPath = tPrimarySku.Id.ToString() };

        var map = SinglePageMapping(src, tgt, linkNtoN, linkBroadcast, linkNto1);
        var report = MappingValidator.Validate(map, catalog, Functoids);

        Assert.Equal(ValidationSeverity.Valid, report.GetLinkSeverity(linkNtoN.Id));
        Assert.Equal(ValidationSeverity.Valid, report.GetLinkSeverity(linkBroadcast.Id));
        Assert.Equal(ValidationSeverity.Warning, report.GetLinkSeverity(linkNto1.Id));

        // Route N -> 1 through First() aggregator => Valid!
        var firstFn = new MappingFunctoidInstance { Id = Guid.NewGuid(), FunctoidDefinitionId = Preset("preset.first").Id };
        var aggMap = SinglePageMapping(src, tgt,
            [firstFn],
            new MappingLink { SourcePropertyPath = $"{pLines.Id}/{pSku.Id}", TargetFunctoidInstanceId = firstFn.Id, TargetInputIndex = 0 },
            new MappingLink { SourceFunctoidInstanceId = firstFn.Id, TargetPropertyPath = tPrimarySku.Id.ToString() });

        var aggReport = MappingValidator.Validate(aggMap, catalog, Functoids);
        Assert.True(aggReport.IsClean);
    }

    [Fact]
    public void FunctoidCycle_IsDetectedAsError()
    {
        var trimDef = Preset("preset.trim");
        var f1 = new MappingFunctoidInstance { Id = Guid.NewGuid(), FunctoidDefinitionId = trimDef.Id };
        var f2 = new MappingFunctoidInstance { Id = Guid.NewGuid(), FunctoidDefinitionId = trimDef.Id };

        var (src, _, _) = CreateTwoPropType("S", ("a", TypeDescriptor.ScalarString()), ("b", TypeDescriptor.ScalarString()));
        var (tgt, _, _) = CreateTwoPropType("T", ("x", TypeDescriptor.ScalarString()), ("y", TypeDescriptor.ScalarString()));

        var map = SinglePageMapping(src, tgt,
            [f1, f2],
            new MappingLink { SourceFunctoidInstanceId = f1.Id, TargetFunctoidInstanceId = f2.Id, TargetInputIndex = 0 },
            new MappingLink { SourceFunctoidInstanceId = f2.Id, TargetFunctoidInstanceId = f1.Id, TargetInputIndex = 0 });

        var report = MappingValidator.Validate(map, new Dictionary<Guid, TypeDefinition> { [src.Id] = src, [tgt.Id] = tgt }, Functoids);
        Assert.Contains(report.Diagnostics, d => d.Code == ValidationCode.FunctoidCycle && d.Severity == ValidationSeverity.Error);
    }

    private static (TypeDefinition Type, PropertyDefinition P1, PropertyDefinition P2) CreateTwoPropType(
        string name,
        (string Name, TypeDescriptor Td) p1Spec,
        (string Name, TypeDescriptor Td) p2Spec)
    {
        var p1 = new PropertyDefinition { Id = Guid.NewGuid(), Name = p1Spec.Name, Type = p1Spec.Td, SortOrder = 0 };
        var p2 = new PropertyDefinition { Id = Guid.NewGuid(), Name = p2Spec.Name, Type = p2Spec.Td, SortOrder = 1 };
        var t = new TypeDefinition { Id = Guid.NewGuid(), Name = name, Properties = [p1, p2] };
        return (t, p1, p2);
    }

    private static MappingDefinition SinglePageMapping(TypeDefinition src, TypeDefinition tgt, params MappingLink[] links) =>
        SinglePageMapping(src, tgt, [], links);

    private static MappingDefinition SinglePageMapping(
        TypeDefinition src,
        TypeDefinition tgt,
        List<MappingFunctoidInstance> functoids,
        params MappingLink[] links) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "TestMap",
            SourceTypeId = src.Id,
            TargetTypeId = tgt.Id,
            GridPages =
            [
                new MappingGridPage
                {
                    Id = Guid.NewGuid(),
                    Name = "Main",
                    FunctoidInstances = functoids,
                    Links = links.ToList()
                }
            ]
        };
}
