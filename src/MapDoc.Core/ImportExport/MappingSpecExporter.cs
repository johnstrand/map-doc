using System.Net;
using System.Text;
using System.Text.Json;
using MapDoc.Core.Models;
using MapDoc.Core.Validation;

namespace MapDoc.Core.ImportExport;

public static class MappingSpecExporter
{
    public static string ExportMarkdown(
        MappingDefinition mapping,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog,
        IReadOnlyDictionary<Guid, FunctoidDefinition> functoidCatalog)
    {
        var sb = new StringBuilder();
        var typeNames = typeCatalog.ToDictionary(k => k.Key, v => v.Value.Name);
        typeCatalog.TryGetValue(mapping.SourceTypeId, out var sourceType);
        typeCatalog.TryGetValue(mapping.TargetTypeId, out var targetType);

        var report = MappingValidator.Validate(mapping, typeCatalog, functoidCatalog);

        sb.AppendLine($"# Transformation Specification: {mapping.QualifiedName}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(mapping.Description))
        {
            sb.AppendLine(mapping.Description);
            sb.AppendLine();
        }

        sb.AppendLine("## Overview");
        sb.AppendLine();
        sb.AppendLine("| Attribute | Value |");
        sb.AppendLine("| :--- | :--- |");
        sb.AppendLine($"| **Mapping** | `{mapping.QualifiedName}` |");
        sb.AppendLine($"| **Source Type** | `{sourceType?.QualifiedName ?? "(missing)"}` |");
        sb.AppendLine($"| **Target Type** | `{targetType?.QualifiedName ?? "(missing)"}` |");
        sb.AppendLine($"| **Grid Pages** | {mapping.GridPages.Count} |");
        sb.AppendLine($"| **Validation Status** | {report.ErrorCount} Error(s), {report.WarningCount} Warning(s) |");
        sb.AppendLine($"| **Last Updated (UTC)** | {mapping.UpdatedAtUtc:yyyy-MM-dd HH:mm:ss} |");
        sb.AppendLine();

        foreach (var page in mapping.GridPages.OrderBy(p => p.SortOrder))
        {
            sb.AppendLine($"## Grid Page: {page.Name}");
            sb.AppendLine();

            var rows = BuildTransformationRows(page, sourceType, targetType, typeCatalog, typeNames, functoidCatalog, report);
            if (rows.Count == 0)
            {
                sb.AppendLine("_No property mappings defined on this grid page._");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine("| Source(s) | Transformation / Functoid Chain | Target Property | Target Type | Status | Comments |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");

            foreach (var row in rows)
            {
                sb.AppendLine($"| {EscapeMd(row.Sources)} | {EscapeMd(row.Transform)} | `{EscapeMd(row.TargetPath)}` | `{EscapeMd(row.TargetType)}` | {row.StatusBadge} | {EscapeMd(row.Comments)} |");
            }
            sb.AppendLine();

            if (page.FunctoidInstances.Count > 0)
            {
                sb.AppendLine($"### Functoid Instances ({page.Name})");
                sb.AppendLine();
                sb.AppendLine("| Functoid | Category | Configuration | Comment |");
                sb.AppendLine("| :--- | :--- | :--- | :--- |");

                foreach (var inst in page.FunctoidInstances)
                {
                    var def = inst.FunctoidDefinition ?? (functoidCatalog.TryGetValue(inst.FunctoidDefinitionId, out var d) ? d : null);
                    var fname = def?.Name ?? "(unknown)";
                    var cat = def?.Category.ToString() ?? "-";
                    var cfg = inst.ConfigValues.Count == 0
                        ? "—"
                        : string.Join("; ", inst.ConfigValues.Where(kv => !string.IsNullOrEmpty(kv.Value)).Select(kv => $"{kv.Key}=\"{kv.Value}\""));
                    var comment = string.IsNullOrWhiteSpace(inst.Comment) ? "—" : inst.Comment;
                    sb.AppendLine($"| `{EscapeMd(fname)}` | {cat} | {EscapeMd(string.IsNullOrEmpty(cfg) ? "—" : cfg)} | {EscapeMd(comment)} |");
                }
                sb.AppendLine();
            }
        }

        if (report.Diagnostics.Count > 0)
        {
            sb.AppendLine("## Validation Diagnostics");
            sb.AppendLine();
            sb.AppendLine("| Severity | Page | Diagnostic |");
            sb.AppendLine("| :--- | :--- | :--- |");
            foreach (var d in report.Diagnostics)
            {
                var badge = d.Severity == ValidationSeverity.Error ? "ERROR" : "WARNING";
                sb.AppendLine($"| **{badge}** | {EscapeMd(d.GridPageName)} | {EscapeMd(d.Message)} |");
            }
            sb.AppendLine();
        }

        if (sourceType is not null)
        {
            AppendTypeSchemaMarkdown(sb, "Source Schema", sourceType, typeNames);
        }

        if (targetType is not null && targetType.Id != sourceType?.Id)
        {
            AppendTypeSchemaMarkdown(sb, "Target Schema", targetType, typeNames);
        }

        return sb.ToString();
    }

    public static string ExportHtml(
        MappingDefinition mapping,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog,
        IReadOnlyDictionary<Guid, FunctoidDefinition> functoidCatalog)
    {
        var typeNames = typeCatalog.ToDictionary(k => k.Key, v => v.Value.Name);
        typeCatalog.TryGetValue(mapping.SourceTypeId, out var sourceType);
        typeCatalog.TryGetValue(mapping.TargetTypeId, out var targetType);
        var report = MappingValidator.Validate(mapping, typeCatalog, functoidCatalog);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\" />");
        sb.AppendLine($"<title>{H(mapping.QualifiedName)} — Transformation Specification</title>");
        sb.AppendLine("""
<style>
  :root { --fg: #1e293b; --muted: #64748b; --border: #cbd5e1; --bg: #ffffff; --card: #f8fafc; --ok: #059669; --warn: #d97706; --err: #dc2626; --accent: #2563eb; }
  body { font-family: 'Segoe UI', Inter, system-ui, sans-serif; color: var(--fg); background: var(--bg); max-width: 1120px; margin: 2rem auto; padding: 0 1.5rem; line-height: 1.5; }
  h1 { margin-bottom: 0.25rem; font-size: 1.75rem; }
  h2 { margin-top: 2rem; border-bottom: 2px solid var(--border); padding-bottom: 0.35rem; font-size: 1.25rem; }
  h3 { margin-top: 1.25rem; font-size: 1.05rem; color: #334155; }
  .meta { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 0.75rem; background: var(--card); border: 1px solid var(--border); border-radius: 8px; padding: 1rem; margin: 1rem 0; }
  .meta div strong { display: block; font-size: 0.75rem; text-transform: uppercase; color: var(--muted); }
  table { width: 100%; border-collapse: collapse; margin: 0.75rem 0 1.5rem; font-size: 0.9rem; }
  th, td { border: 1px solid var(--border); padding: 0.5rem 0.65rem; text-align: left; vertical-align: top; }
  th { background: var(--card); font-weight: 600; }
  code { background: #f1f5f9; padding: 0.1rem 0.35rem; border-radius: 4px; font-size: 0.85em; }
  .badge { display: inline-block; padding: 0.1rem 0.45rem; border-radius: 999px; font-size: 0.75rem; font-weight: 600; }
  .b-ok { background: #d1fae5; color: #065f46; }
  .b-warn { background: #fef3c7; color: #92400e; }
  .b-err { background: #fee2e2; color: #991b1b; }
  @media print { body { margin: 0; max-width: none; } }
</style>
</head><body>
""");

        sb.AppendLine($"<h1>{H(mapping.QualifiedName)}</h1>");
        if (!string.IsNullOrWhiteSpace(mapping.Description))
        {
            sb.AppendLine($"<p>{H(mapping.Description)}</p>");
        }

        sb.AppendLine("<div class=\"meta\">");
        sb.AppendLine($"<div><strong>Source Type</strong><code>{H(sourceType?.QualifiedName ?? "(missing)")}</code></div>");
        sb.AppendLine($"<div><strong>Target Type</strong><code>{H(targetType?.QualifiedName ?? "(missing)")}</code></div>");
        sb.AppendLine($"<div><strong>Validation</strong><span>{report.ErrorCount} Error(s), {report.WarningCount} Warning(s)</span></div>");
        sb.AppendLine($"<div><strong>Updated (UTC)</strong><span>{mapping.UpdatedAtUtc:yyyy-MM-dd HH:mm}</span></div>");
        sb.AppendLine("</div>");

        foreach (var page in mapping.GridPages.OrderBy(p => p.SortOrder))
        {
            sb.AppendLine($"<h2>Grid Page: {H(page.Name)}</h2>");
            var rows = BuildTransformationRows(page, sourceType, targetType, typeCatalog, typeNames, functoidCatalog, report);
            if (rows.Count == 0)
            {
                sb.AppendLine("<p><em>No property mappings defined on this grid page.</em></p>");
                continue;
            }

            sb.AppendLine("<table><thead><tr><th>Source(s)</th><th>Transformation</th><th>Target Property</th><th>Target Type</th><th>Status</th><th>Comments</th></tr></thead><tbody>");
            foreach (var r in rows)
            {
                var badgeCss = r.Severity switch
                {
                    ValidationSeverity.Error => "b-err",
                    ValidationSeverity.Warning => "b-warn",
                    _ => "b-ok"
                };
                sb.AppendLine($"<tr><td><code>{H(r.Sources)}</code></td><td>{H(r.Transform)}</td><td><code>{H(r.TargetPath)}</code></td><td><code>{H(r.TargetType)}</code></td><td><span class=\"badge {badgeCss}\">{H(r.StatusBadge)}</span></td><td>{H(r.Comments)}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        if (report.Diagnostics.Count > 0)
        {
            sb.AppendLine("<h2>Validation Diagnostics</h2><table><thead><tr><th>Severity</th><th>Page</th><th>Message</th></tr></thead><tbody>");
            foreach (var d in report.Diagnostics)
            {
                var css = d.Severity == ValidationSeverity.Error ? "b-err" : "b-warn";
                sb.AppendLine($"<tr><td><span class=\"badge {css}\">{d.Severity}</span></td><td>{H(d.GridPageName)}</td><td>{H(d.Message)}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    public static string GenerateJsonSchemaPreview(
        TypeDefinition type,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", "https://json-schema.org/draft/2020-12/schema");
            writer.WriteString("title", type.Name);
            if (!string.IsNullOrWhiteSpace(type.Description))
            {
                writer.WriteString("description", type.Description);
            }
            writer.WriteString("type", "object");

            WritePropertiesAndRequired(writer, type, typeCatalog, [type.Id], depth: 0);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WritePropertiesAndRequired(
        Utf8JsonWriter writer,
        TypeDefinition type,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog,
        HashSet<Guid> visited,
        int depth)
    {
        var ordered = type.Properties.OrderBy(p => p.SortOrder).ToList();
        var required = ordered.Where(p => !p.IsOptional).Select(p => p.Name).ToList();

        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        foreach (var p in ordered)
        {
            writer.WritePropertyName(p.Name);
            WriteDescriptorSchema(writer, p.Type, p.IsNullable, p.Description, typeCatalog, visited, depth + 1);
        }
        writer.WriteEndObject();

        if (required.Count > 0)
        {
            writer.WritePropertyName("required");
            writer.WriteStartArray();
            foreach (var r in required) writer.WriteStringValue(r);
            writer.WriteEndArray();
        }
    }

    private static void WriteDescriptorSchema(
        Utf8JsonWriter writer,
        TypeDescriptor desc,
        bool isNullable,
        string description,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog,
        HashSet<Guid> visited,
        int depth)
    {
        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(description))
        {
            writer.WriteString("description", description);
        }

        void WriteType(string jsonType)
        {
            if (isNullable)
            {
                writer.WritePropertyName("type");
                writer.WriteStartArray();
                writer.WriteStringValue(jsonType);
                writer.WriteStringValue("null");
                writer.WriteEndArray();
            }
            else
            {
                writer.WriteString("type", jsonType);
            }
        }

        switch (desc.BaseKind)
        {
            case BaseTypeKind.Boolean:
                WriteType("boolean");
                break;

            case BaseTypeKind.Number:
                if (desc.NumberSubtype is NumberSubtype.Int32 or NumberSubtype.Int64)
                {
                    WriteType("integer");
                    writer.WriteString("format", desc.NumberSubtype == NumberSubtype.Int64 ? "int64" : "int32");
                }
                else
                {
                    WriteType("number");
                    if (desc.NumberSubtype != NumberSubtype.None)
                    {
                        writer.WriteString("format", TypeDescriptor.FormatNumberSubtype(desc.NumberSubtype));
                    }
                }
                break;

            case BaseTypeKind.String:
                WriteType("string");
                if (desc.StringSubtype != StringSubtype.None)
                {
                    var fmt = desc.StringSubtype switch
                    {
                        StringSubtype.DateTime => "date-time",
                        StringSubtype.Custom => desc.CustomSubtype ?? "custom",
                        _ => TypeDescriptor.FormatStringSubtype(desc.StringSubtype)
                    };
                    writer.WriteString("format", fmt);
                }
                break;

            case BaseTypeKind.Array:
                WriteType("array");
                writer.WritePropertyName("items");
                if (desc.ElementType is not null)
                {
                    WriteDescriptorSchema(writer, desc.ElementType, desc.ElementIsNullable, string.Empty, typeCatalog, visited, depth + 1);
                }
                else
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                }
                break;

            case BaseTypeKind.Object:
                if (desc.ObjectTypeId is Guid objId && typeCatalog.TryGetValue(objId, out var refType))
                {
                    if (visited.Contains(objId) || depth > 5)
                    {
                        writer.WriteString("$comment", $"Recursive reference to {refType.QualifiedName}");
                        WriteType("object");
                    }
                    else
                    {
                        WriteType("object");
                        var nextVisited = new HashSet<Guid>(visited) { objId };
                        WritePropertiesAndRequired(writer, refType, typeCatalog, nextVisited, depth + 1);
                    }
                }
                else
                {
                    WriteType("object");
                }
                break;
        }

        writer.WriteEndObject();
    }

    private sealed record SpecRow(
        string Sources,
        string Transform,
        string TargetPath,
        string TargetType,
        ValidationSeverity Severity,
        string StatusBadge,
        string Comments);

    private static List<SpecRow> BuildTransformationRows(
        MappingGridPage page,
        TypeDefinition? sourceType,
        TypeDefinition? targetType,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog,
        IReadOnlyDictionary<Guid, string> typeNames,
        IReadOnlyDictionary<Guid, FunctoidDefinition> functoidCatalog,
        MappingValidationReport report)
    {
        var rows = new List<SpecRow>();
        var functoidMap = page.FunctoidInstances.ToDictionary(f => f.Id);

        foreach (var link in page.Links.Where(l => !string.IsNullOrWhiteSpace(l.TargetPropertyPath)))
        {
            var tgt = PropertyPathResolver.Resolve(targetType, link.TargetPropertyPath, typeCatalog);
            var tgtPath = tgt.IsValid ? tgt.DisplayPath : link.TargetPropertyPath ?? "(missing)";
            var tgtType = tgt.IsValid
                ? $"{tgt.Type.FormatDisplay(typeNames)}{(tgt.LeafIsNullable ? " | null" : "")}{(tgt.LeafIsOptional ? " (optional)" : " (required)")}"
                : "(unresolved)";

            var sources = new List<string>();
            var comments = new List<string>();
            if (!string.IsNullOrWhiteSpace(link.Comment))
            {
                comments.Add(link.Comment);
            }

            string transformDesc;
            var sev = report.GetLinkSeverity(link.Id);

            if (!string.IsNullOrWhiteSpace(link.SourcePropertyPath))
            {
                var src = PropertyPathResolver.Resolve(sourceType, link.SourcePropertyPath, typeCatalog);
                sources.Add(src.IsValid ? $"{src.DisplayPath} ({src.Type.FormatDisplay(typeNames)})" : link.SourcePropertyPath!);
                transformDesc = "Direct Copy";
            }
            else if (link.SourceFunctoidInstanceId is Guid sfId)
            {
                transformDesc = DescribeUpstreamFunctoidChain(sfId, page, functoidMap, sourceType, typeCatalog, typeNames, functoidCatalog, sources, comments, new HashSet<Guid>());
                var fSev = report.GetFunctoidSeverity(sfId);
                if (fSev > sev) sev = fSev;
            }
            else
            {
                transformDesc = "—";
            }

            var status = sev switch
            {
                ValidationSeverity.Error => "Error",
                ValidationSeverity.Warning => "Warning",
                _ => "Valid"
            };

            rows.Add(new SpecRow(
                Sources: sources.Count > 0 ? string.Join(", ", sources.Distinct()) : "(0-input generator)",
                Transform: transformDesc,
                TargetPath: tgtPath,
                TargetType: tgtType,
                Severity: sev,
                StatusBadge: status,
                Comments: comments.Count > 0 ? string.Join(" | ", comments) : "—"));
        }

        return rows;
    }

    private static string DescribeUpstreamFunctoidChain(
        Guid instanceId,
        MappingGridPage page,
        Dictionary<Guid, MappingFunctoidInstance> functoidMap,
        TypeDefinition? sourceType,
        IReadOnlyDictionary<Guid, TypeDefinition> typeCatalog,
        IReadOnlyDictionary<Guid, string> typeNames,
        IReadOnlyDictionary<Guid, FunctoidDefinition> functoidCatalog,
        List<string> collectedSources,
        List<string> collectedComments,
        HashSet<Guid> visited)
    {
        if (!visited.Add(instanceId)) return "(cycle)";
        if (!functoidMap.TryGetValue(instanceId, out var inst)) return "(missing functoid)";

        var def = inst.FunctoidDefinition ?? (functoidCatalog.TryGetValue(inst.FunctoidDefinitionId, out var d) ? d : null);
        var fname = def?.Name ?? "Functoid";

        if (!string.IsNullOrWhiteSpace(inst.Comment))
        {
            collectedComments.Add($"[{fname}] {inst.Comment}");
        }

        var args = new List<string>();
        var incoming = page.Links
            .Where(l => l.TargetFunctoidInstanceId == instanceId)
            .OrderBy(l => l.TargetInputIndex ?? 0)
            .ToList();

        foreach (var inLink in incoming)
        {
            if (!string.IsNullOrWhiteSpace(inLink.Comment))
            {
                collectedComments.Add(inLink.Comment);
            }

            if (!string.IsNullOrWhiteSpace(inLink.SourcePropertyPath))
            {
                var src = PropertyPathResolver.Resolve(sourceType, inLink.SourcePropertyPath, typeCatalog);
                var label = src.IsValid ? src.DisplayPath : inLink.SourcePropertyPath!;
                if (src.IsValid)
                {
                    collectedSources.Add($"{src.DisplayPath} ({src.Type.FormatDisplay(typeNames)})");
                }
                args.Add(label);
            }
            else if (inLink.SourceFunctoidInstanceId is Guid upId)
            {
                args.Add(DescribeUpstreamFunctoidChain(upId, page, functoidMap, sourceType, typeCatalog, typeNames, functoidCatalog, collectedSources, collectedComments, visited));
            }
        }

        foreach (var (k, v) in inst.ConfigValues)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                args.Add($"{k}: \"{v}\"");
            }
        }

        visited.Remove(instanceId);
        return $"{fname}({string.Join(", ", args)})";
    }

    private static void AppendTypeSchemaMarkdown(
        StringBuilder sb,
        string heading,
        TypeDefinition type,
        IReadOnlyDictionary<Guid, string> typeNames)
    {
        sb.AppendLine($"## {heading}: `{type.QualifiedName}`");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(type.Description))
        {
            sb.AppendLine(type.Description);
            sb.AppendLine();
        }

        sb.AppendLine("| Property | Type | Nullable | Optional | Description |");
        sb.AppendLine("| :--- | :--- | :---: | :---: | :--- |");
        foreach (var p in type.Properties.OrderBy(p => p.SortOrder))
        {
            sb.AppendLine($"| `{EscapeMd(p.Name)}` | `{EscapeMd(p.Type.FormatDisplay(typeNames))}` | {(p.IsNullable ? "Yes" : "No")} | {(p.IsOptional ? "Yes" : "No")} | {EscapeMd(string.IsNullOrWhiteSpace(p.Description) ? "—" : p.Description)} |");
        }
        sb.AppendLine();
    }

    private static string EscapeMd(string s) =>
        s.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");

    private static string H(string s) => WebUtility.HtmlEncode(s);
}
