using MapDoc.Core.ImportExport;
using MapDoc.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MapDoc.Data.Services;

public sealed record DashboardSummary(
    int TypeCount,
    int CustomFunctoidCount,
    int PresetFunctoidCount,
    int MappingCount,
    List<string> Namespaces);

public sealed record DeletionImpact(
    bool CanDelete,
    List<string> BlockingReasons);

public sealed class CatalogService(IDbContextFactory<MapDocDbContext> dbFactory)
{
    public async Task<DashboardSummary> GetDashboardSummaryAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var typeCount = await db.Types.CountAsync(ct);
        var customCount = await db.Functoids.CountAsync(f => !f.IsPreset, ct);
        var presetCount = await db.Functoids.CountAsync(f => f.IsPreset, ct);
        var mappingCount = await db.Mappings.CountAsync(ct);

        var typeNs = await db.Types.Where(t => t.Namespace != "").Select(t => t.Namespace).Distinct().ToListAsync(ct);
        var mapNs = await db.Mappings.Where(m => m.Namespace != "").Select(m => m.Namespace).Distinct().ToListAsync(ct);
        var namespaces = typeNs.Union(mapNs, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        return new DashboardSummary(typeCount, customCount, presetCount, mappingCount, namespaces);
    }

    // ==================== TYPES ====================

    public async Task<List<TypeDefinition>> GetTypesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var types = await db.Types
            .AsNoTracking()
            .Include(t => t.Properties)
            .OrderBy(t => t.Namespace)
            .ThenBy(t => t.Name)
            .ToListAsync(ct);

        foreach (var t in types)
        {
            t.Properties = t.Properties.OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToList();
        }
        return types;
    }

    public async Task<Dictionary<Guid, TypeDefinition>> GetTypeCatalogDictionaryAsync(CancellationToken ct = default)
    {
        var list = await GetTypesAsync(ct);
        return list.ToDictionary(t => t.Id);
    }

    public async Task<TypeDefinition?> GetTypeByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var t = await db.Types
            .AsNoTracking()
            .Include(x => x.Properties)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (t is not null)
        {
            t.Properties = t.Properties.OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToList();
        }
        return t;
    }

    public async Task<TypeDefinition> SaveTypeAsync(TypeDefinition incoming, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        for (int i = 0; i < incoming.Properties.Count; i++)
        {
            var p = incoming.Properties[i];
            if (p.Id == Guid.Empty) p.Id = Guid.NewGuid();
            p.OwnerTypeId = incoming.Id;
            p.SortOrder = i;
            p.SyncReferencedType();
        }

        var existing = await db.Types
            .Include(t => t.Properties)
            .FirstOrDefaultAsync(t => t.Id == incoming.Id, ct);

        if (existing is null)
        {
            if (incoming.Id == Guid.Empty) incoming.Id = Guid.NewGuid();
            var headerProps = incoming.Properties.ToList();
            incoming.Properties = [];
            incoming.CreatedAtUtc = now;
            incoming.UpdatedAtUtc = now;

            // Insert Type first so self-referential properties (ReferencedTypeId == incoming.Id) satisfy FK on SQL Server & SQLite
            db.Types.Add(incoming);
            await db.SaveChangesAsync(ct);

            foreach (var p in headerProps)
            {
                p.OwnerTypeId = incoming.Id;
                p.SyncReferencedType();
                db.Properties.Add(p);
            }
            await db.SaveChangesAsync(ct);
            incoming.Properties = headerProps;
            return incoming;
        }

        existing.Name = incoming.Name.Trim();
        existing.Namespace = incoming.Namespace.Trim();
        existing.Tags = incoming.Tags.Trim();
        existing.Description = incoming.Description.Trim();
        existing.UpdatedAtUtc = now;

        var incomingById = incoming.Properties.ToDictionary(p => p.Id);

        foreach (var oldProp in existing.Properties.Where(op => !incomingById.ContainsKey(op.Id)).ToList())
        {
            db.Properties.Remove(oldProp);
        }

        foreach (var inProp in incoming.Properties)
        {
            var match = existing.Properties.FirstOrDefault(ep => ep.Id == inProp.Id);
            if (match is null)
            {
                inProp.OwnerTypeId = existing.Id;
                inProp.SyncReferencedType();
                db.Properties.Add(inProp);
            }
            else
            {
                match.Name = inProp.Name.Trim();
                match.Description = inProp.Description.Trim();
                match.Type = inProp.Type.Clone();
                match.IsNullable = inProp.IsNullable;
                match.IsOptional = inProp.IsOptional;
                match.SortOrder = inProp.SortOrder;
                match.SyncReferencedType();
            }
        }

        await db.SaveChangesAsync(ct);
        return (await GetTypeByIdAsync(existing.Id, ct))!;
    }

    public async Task SaveImportedTypesAsync(IReadOnlyList<TypeDefinition> importedTypes, CancellationToken ct = default)
    {
        if (importedTypes.Count == 0) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;
        var allProps = new List<PropertyDefinition>();

        foreach (var t in importedTypes)
        {
            if (t.Id == Guid.Empty) t.Id = Guid.NewGuid();
            var props = t.Properties.ToList();
            t.Properties = [];
            t.CreatedAtUtc = now;
            t.UpdatedAtUtc = now;
            db.Types.Add(t);

            for (int i = 0; i < props.Count; i++)
            {
                var p = props[i];
                if (p.Id == Guid.Empty) p.Id = Guid.NewGuid();
                p.OwnerTypeId = t.Id;
                p.SortOrder = i;
                p.SyncReferencedType();
                allProps.Add(p);
            }
        }

        // Phase 1: insert all TypeDefinition rows so mutually recursive / self-referential FKs exist
        await db.SaveChangesAsync(ct);

        // Phase 2: insert all PropertyDefinition rows
        db.Properties.AddRange(allProps);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    public async Task<DeletionImpact> GetTypeDeletionImpactAsync(Guid typeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var reasons = new List<string>();

        var usedInMappings = await db.Mappings
            .Where(m => m.SourceTypeId == typeId || m.TargetTypeId == typeId)
            .Select(m => m.Name)
            .ToListAsync(ct);

        foreach (var mName in usedInMappings)
        {
            reasons.Add($"Used as Source or Target schema in mapping '{mName}'.");
        }

        var referencedByTypes = await db.Properties
            .Where(p => p.ReferencedTypeId == typeId && p.OwnerTypeId != typeId)
            .Select(p => new { OwnerName = p.OwnerType!.Name, PropName = p.Name })
            .ToListAsync(ct);

        foreach (var r in referencedByTypes)
        {
            reasons.Add($"Referenced by property '{r.OwnerName}.{r.PropName}'.");
        }

        return new DeletionImpact(reasons.Count == 0, reasons);
    }

    public async Task<(bool Deleted, List<string> BlockingReasons)> DeleteTypeAsync(Guid typeId, CancellationToken ct = default)
    {
        var impact = await GetTypeDeletionImpactAsync(typeId, ct);
        if (!impact.CanDelete)
        {
            return (false, impact.BlockingReasons);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var selfProps = await db.Properties.Where(p => p.OwnerTypeId == typeId).ToListAsync(ct);
        // Clear self-referential FK before deleting type + properties on SQL Server
        foreach (var sp in selfProps.Where(p => p.ReferencedTypeId == typeId))
        {
            sp.ReferencedTypeId = null;
        }
        if (selfProps.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            db.Properties.RemoveRange(selfProps);
        }

        var t = await db.Types.FirstOrDefaultAsync(x => x.Id == typeId, ct);
        if (t is not null)
        {
            db.Types.Remove(t);
            await db.SaveChangesAsync(ct);
        }

        return (true, []);
    }

    // ==================== FUNCTOIDS ====================

    public async Task<List<FunctoidDefinition>> GetFunctoidsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Functoids
            .AsNoTracking()
            .OrderBy(f => f.Category)
            .ThenByDescending(f => f.IsPreset)
            .ThenBy(f => f.Name)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, FunctoidDefinition>> GetFunctoidCatalogDictionaryAsync(CancellationToken ct = default)
    {
        var list = await GetFunctoidsAsync(ct);
        return list.ToDictionary(f => f.Id);
    }

    public async Task<FunctoidDefinition> SaveCustomFunctoidAsync(FunctoidDefinition incoming, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        for (int i = 0; i < incoming.Inputs.Count; i++)
        {
            incoming.Inputs[i].Index = i;
        }

        var existing = await db.Functoids.FirstOrDefaultAsync(f => f.Id == incoming.Id, ct);
        if (existing is null)
        {
            if (incoming.Id == Guid.Empty) incoming.Id = Guid.NewGuid();
            incoming.IsPreset = false;
            incoming.PresetKey = null;
            incoming.CreatedAtUtc = now;
            incoming.UpdatedAtUtc = now;
            db.Functoids.Add(incoming);
            await db.SaveChangesAsync(ct);
            return incoming;
        }

        if (existing.IsPreset)
        {
            throw new InvalidOperationException("Built-in preset functoids cannot be modified.");
        }

        existing.Name = incoming.Name.Trim();
        existing.Category = incoming.Category;
        existing.Symbol = string.IsNullOrWhiteSpace(incoming.Symbol) ? "fx" : incoming.Symbol.Trim();
        existing.Description = incoming.Description.Trim();
        existing.Inputs = incoming.Inputs.Select(i => i.Clone()).ToList();
        existing.IsVariadic = incoming.IsVariadic;
        existing.VariadicInputName = incoming.VariadicInputName;
        existing.VariadicInputType = incoming.VariadicInputType?.Clone();
        existing.OutputType = incoming.OutputType.Clone();
        existing.OutputInference = incoming.OutputInference;
        existing.OutputInferencePinIndex = incoming.OutputInferencePinIndex;
        existing.AggregatesRepeatingInput = incoming.AggregatesRepeatingInput;
        existing.ConfigParams = incoming.ConfigParams.Select(c => c.Clone()).ToList();
        existing.UpdatedAtUtc = now;

        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<(bool Deleted, List<string> BlockingReasons)> DeleteCustomFunctoidAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Functoids.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (existing is null) return (true, []);
        if (existing.IsPreset)
        {
            return (false, ["Built-in preset functoids cannot be deleted."]);
        }

        var usages = await db.FunctoidInstances
            .Where(fi => fi.FunctoidDefinitionId == id)
            .Select(fi => new { MappingName = fi.GridPage!.Mapping!.Name, PageName = fi.GridPage.Name })
            .Distinct()
            .ToListAsync(ct);

        if (usages.Count > 0)
        {
            return (false, usages.Select(u => $"Used in mapping '{u.MappingName}' (page '{u.PageName}').").ToList());
        }

        db.Functoids.Remove(existing);
        await db.SaveChangesAsync(ct);
        return (true, []);
    }

    // ==================== MAPPINGS ====================

    public async Task<List<MappingDefinition>> GetMappingsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Mappings
            .AsNoTracking()
            .AsSplitQuery()
            .Include(m => m.SourceType)
            .Include(m => m.TargetType)
            .Include(m => m.GridPages)
                .ThenInclude(p => p.FunctoidInstances)
            .Include(m => m.GridPages)
                .ThenInclude(p => p.Links)
            .OrderBy(m => m.Namespace)
            .ThenBy(m => m.Name)
            .ToListAsync(ct);
    }

    public async Task<MappingDefinition?> GetMappingByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var mapping = await db.Mappings
            .AsNoTracking()
            .AsSplitQuery()
            .Include(m => m.SourceType)
                .ThenInclude(t => t!.Properties)
            .Include(m => m.TargetType)
                .ThenInclude(t => t!.Properties)
            .Include(m => m.GridPages)
                .ThenInclude(p => p.FunctoidInstances)
                    .ThenInclude(fi => fi.FunctoidDefinition)
            .Include(m => m.GridPages)
                .ThenInclude(p => p.Links)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (mapping is not null)
        {
            mapping.GridPages = mapping.GridPages.OrderBy(p => p.SortOrder).ToList();
        }
        return mapping;
    }

    public async Task<MappingDefinition> CreateMappingAsync(
        string name,
        string ns,
        string description,
        Guid sourceTypeId,
        Guid targetTypeId,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var mapping = new MappingDefinition
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Namespace = ns.Trim(),
            Description = description.Trim(),
            SourceTypeId = sourceTypeId,
            TargetTypeId = targetTypeId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            GridPages =
            [
                new MappingGridPage
                {
                    Id = Guid.NewGuid(),
                    Name = "Main",
                    SortOrder = 0
                }
            ]
        };

        db.Mappings.Add(mapping);
        await db.SaveChangesAsync(ct);
        return (await GetMappingByIdAsync(mapping.Id, ct))!;
    }

    public async Task SaveMappingGraphAsync(MappingDefinition incoming, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existing = await db.Mappings
            .AsSplitQuery()
            .Include(m => m.GridPages)
                .ThenInclude(p => p.FunctoidInstances)
            .Include(m => m.GridPages)
                .ThenInclude(p => p.Links)
            .FirstOrDefaultAsync(m => m.Id == incoming.Id, ct)
            ?? throw new InvalidOperationException($"Mapping '{incoming.Id}' was not found.");

        existing.Name = incoming.Name.Trim();
        existing.Namespace = incoming.Namespace.Trim();
        existing.Tags = incoming.Tags.Trim();
        existing.Description = incoming.Description.Trim();
        existing.SourceTypeId = incoming.SourceTypeId;
        existing.TargetTypeId = incoming.TargetTypeId;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        if (incoming.GridPages.Count == 0)
        {
            incoming.GridPages.Add(new MappingGridPage
            {
                Id = Guid.NewGuid(),
                MappingId = existing.Id,
                Name = "Main",
                SortOrder = 0
            });
        }

        var incomingPageIds = incoming.GridPages.Select(p => p.Id).ToHashSet();

        // Remove deleted pages (Links first -> FunctoidInstances -> GridPage for SQL Server NoAction FKs)
        var removedPages = existing.GridPages.Where(ep => !incomingPageIds.Contains(ep.Id)).ToList();
        foreach (var rp in removedPages)
        {
            db.Links.RemoveRange(rp.Links);
            await db.SaveChangesAsync(ct);
            db.FunctoidInstances.RemoveRange(rp.FunctoidInstances);
            await db.SaveChangesAsync(ct);
            db.GridPages.Remove(rp);
        }

        for (int pIdx = 0; pIdx < incoming.GridPages.Count; pIdx++)
        {
            var inPage = incoming.GridPages[pIdx];
            inPage.SortOrder = pIdx;
            var exPage = existing.GridPages.FirstOrDefault(ep => ep.Id == inPage.Id);

            if (exPage is null)
            {
                var newPage = new MappingGridPage
                {
                    Id = inPage.Id == Guid.Empty ? Guid.NewGuid() : inPage.Id,
                    MappingId = existing.Id,
                    Name = inPage.Name.Trim(),
                    SortOrder = pIdx
                };
                db.GridPages.Add(newPage);
                await db.SaveChangesAsync(ct);

                foreach (var fi in inPage.FunctoidInstances)
                {
                    db.FunctoidInstances.Add(new MappingFunctoidInstance
                    {
                        Id = fi.Id == Guid.Empty ? Guid.NewGuid() : fi.Id,
                        GridPageId = newPage.Id,
                        FunctoidDefinitionId = fi.FunctoidDefinitionId,
                        X = fi.X,
                        Y = fi.Y,
                        Comment = fi.Comment,
                        LabelOverride = fi.LabelOverride,
                        ConfigValues = new Dictionary<string, string>(fi.ConfigValues, StringComparer.OrdinalIgnoreCase)
                    });
                }
                await db.SaveChangesAsync(ct);

                foreach (var ln in inPage.Links)
                {
                    db.Links.Add(new MappingLink
                    {
                        Id = ln.Id == Guid.Empty ? Guid.NewGuid() : ln.Id,
                        GridPageId = newPage.Id,
                        SourcePropertyPath = ln.SourcePropertyPath,
                        SourceFunctoidInstanceId = ln.SourceFunctoidInstanceId,
                        TargetPropertyPath = ln.TargetPropertyPath,
                        TargetFunctoidInstanceId = ln.TargetFunctoidInstanceId,
                        TargetInputIndex = ln.TargetInputIndex,
                        Comment = ln.Comment
                    });
                }
                await db.SaveChangesAsync(ct);
                continue;
            }

            exPage.Name = inPage.Name.Trim();
            exPage.SortOrder = pIdx;

            var inLinkIds = inPage.Links.Select(l => l.Id).ToHashSet();
            var inFuncIds = inPage.FunctoidInstances.Select(f => f.Id).ToHashSet();

            // 1. Remove deleted links first (before removing functoid instances they might reference)
            var removedLinks = exPage.Links.Where(el => !inLinkIds.Contains(el.Id)).ToList();
            if (removedLinks.Count > 0)
            {
                db.Links.RemoveRange(removedLinks);
                await db.SaveChangesAsync(ct);
            }

            // 2. Remove deleted functoid instances
            var removedFuncs = exPage.FunctoidInstances.Where(ef => !inFuncIds.Contains(ef.Id)).ToList();
            if (removedFuncs.Count > 0)
            {
                db.FunctoidInstances.RemoveRange(removedFuncs);
                await db.SaveChangesAsync(ct);
            }

            // 3. Upsert functoid instances before adding new links that reference them
            foreach (var inFi in inPage.FunctoidInstances)
            {
                var exFi = exPage.FunctoidInstances.FirstOrDefault(f => f.Id == inFi.Id);
                if (exFi is null)
                {
                    db.FunctoidInstances.Add(new MappingFunctoidInstance
                    {
                        Id = inFi.Id == Guid.Empty ? Guid.NewGuid() : inFi.Id,
                        GridPageId = exPage.Id,
                        FunctoidDefinitionId = inFi.FunctoidDefinitionId,
                        X = inFi.X,
                        Y = inFi.Y,
                        Comment = inFi.Comment,
                        LabelOverride = inFi.LabelOverride,
                        ConfigValues = new Dictionary<string, string>(inFi.ConfigValues, StringComparer.OrdinalIgnoreCase)
                    });
                }
                else
                {
                    exFi.FunctoidDefinitionId = inFi.FunctoidDefinitionId;
                    exFi.X = inFi.X;
                    exFi.Y = inFi.Y;
                    exFi.Comment = inFi.Comment;
                    exFi.LabelOverride = inFi.LabelOverride;
                    exFi.ConfigValues = new Dictionary<string, string>(inFi.ConfigValues, StringComparer.OrdinalIgnoreCase);
                }
            }
            await db.SaveChangesAsync(ct);

            // 4. Upsert links
            foreach (var inLn in inPage.Links)
            {
                var exLn = exPage.Links.FirstOrDefault(l => l.Id == inLn.Id);
                if (exLn is null)
                {
                    db.Links.Add(new MappingLink
                    {
                        Id = inLn.Id == Guid.Empty ? Guid.NewGuid() : inLn.Id,
                        GridPageId = exPage.Id,
                        SourcePropertyPath = inLn.SourcePropertyPath,
                        SourceFunctoidInstanceId = inLn.SourceFunctoidInstanceId,
                        TargetPropertyPath = inLn.TargetPropertyPath,
                        TargetFunctoidInstanceId = inLn.TargetFunctoidInstanceId,
                        TargetInputIndex = inLn.TargetInputIndex,
                        Comment = inLn.Comment
                    });
                }
                else
                {
                    exLn.SourcePropertyPath = inLn.SourcePropertyPath;
                    exLn.SourceFunctoidInstanceId = inLn.SourceFunctoidInstanceId;
                    exLn.TargetPropertyPath = inLn.TargetPropertyPath;
                    exLn.TargetFunctoidInstanceId = inLn.TargetFunctoidInstanceId;
                    exLn.TargetInputIndex = inLn.TargetInputIndex;
                    exLn.Comment = inLn.Comment;
                }
            }
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task DeleteMappingAsync(Guid mappingId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var mapping = await db.Mappings
            .AsSplitQuery()
            .Include(m => m.GridPages)
                .ThenInclude(p => p.FunctoidInstances)
            .Include(m => m.GridPages)
                .ThenInclude(p => p.Links)
            .FirstOrDefaultAsync(m => m.Id == mappingId, ct);

        if (mapping is null) return;

        foreach (var page in mapping.GridPages)
        {
            db.Links.RemoveRange(page.Links);
        }
        await db.SaveChangesAsync(ct);

        foreach (var page in mapping.GridPages)
        {
            db.FunctoidInstances.RemoveRange(page.FunctoidInstances);
        }
        await db.SaveChangesAsync(ct);

        db.GridPages.RemoveRange(mapping.GridPages);
        db.Mappings.Remove(mapping);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    // ==================== PORTABLE BUNDLE EXPORT / IMPORT ====================

    public async Task<MapDocBundle> ExportBundleAsync(CancellationToken ct = default)
    {
        var types = await GetTypesAsync(ct);
        var functoids = (await GetFunctoidsAsync(ct)).Where(f => !f.IsPreset).ToList();
        var mappings = await GetMappingsAsync(ct);

        // Detach navigation properties on exported mappings so JSON stays concise & normalized
        foreach (var m in mappings)
        {
            m.SourceType = null;
            m.TargetType = null;
            foreach (var p in m.GridPages)
            {
                foreach (var fi in p.FunctoidInstances)
                {
                    fi.FunctoidDefinition = null;
                }
            }
        }

        return new MapDocBundle
        {
            ExportedAtUtc = DateTime.UtcNow,
            Types = types,
            CustomFunctoids = functoids,
            Mappings = mappings
        };
    }

    public async Task<(int ImportedTypes, int ImportedFunctoids, int ImportedMappings)> ImportBundleAsync(
        MapDocBundle bundle,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existingTypeIds = (await db.Types.Select(t => t.Id).ToListAsync(ct)).ToHashSet();
        var existingFunctoidIds = (await db.Functoids.Select(f => f.Id).ToListAsync(ct)).ToHashSet();
        var existingMappingIds = (await db.Mappings.Select(m => m.Id).ToListAsync(ct)).ToHashSet();

        int addedTypes = 0, addedFunctoids = 0, addedMappings = 0;
        var deferredProps = new List<PropertyDefinition>();

        foreach (var t in bundle.Types.Where(t => !existingTypeIds.Contains(t.Id)))
        {
            var props = t.Properties.ToList();
            t.Properties = [];
            db.Types.Add(t);
            addedTypes++;

            foreach (var p in props)
            {
                p.OwnerTypeId = t.Id;
                p.SyncReferencedType();
                deferredProps.Add(p);
            }
        }
        await db.SaveChangesAsync(ct);

        if (deferredProps.Count > 0)
        {
            db.Properties.AddRange(deferredProps);
            await db.SaveChangesAsync(ct);
        }

        foreach (var cf in bundle.CustomFunctoids.Where(f => !existingFunctoidIds.Contains(f.Id)))
        {
            cf.IsPreset = false;
            cf.PresetKey = null;
            db.Functoids.Add(cf);
            addedFunctoids++;
        }
        await db.SaveChangesAsync(ct);

        foreach (var m in bundle.Mappings.Where(m => !existingMappingIds.Contains(m.Id)))
        {
            m.SourceType = null;
            m.TargetType = null;
            var pages = m.GridPages.ToList();
            m.GridPages = [];
            db.Mappings.Add(m);
            await db.SaveChangesAsync(ct);

            foreach (var page in pages)
            {
                var funcs = page.FunctoidInstances.ToList();
                var links = page.Links.ToList();
                page.MappingId = m.Id;
                page.FunctoidInstances = [];
                page.Links = [];
                db.GridPages.Add(page);
                await db.SaveChangesAsync(ct);

                foreach (var fi in funcs)
                {
                    fi.GridPageId = page.Id;
                    fi.FunctoidDefinition = null;
                    db.FunctoidInstances.Add(fi);
                }
                await db.SaveChangesAsync(ct);

                foreach (var ln in links)
                {
                    ln.GridPageId = page.Id;
                    db.Links.Add(ln);
                }
                await db.SaveChangesAsync(ct);
            }

            addedMappings++;
        }

        await tx.CommitAsync(ct);
        return (addedTypes, addedFunctoids, addedMappings);
    }
}
