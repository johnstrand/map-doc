using System.Text.Json;
using MapDoc.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MapDoc.Data;

public sealed class MapDocDbContext(DbContextOptions<MapDocDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public DbSet<TypeDefinition> Types => Set<TypeDefinition>();
    public DbSet<PropertyDefinition> Properties => Set<PropertyDefinition>();
    public DbSet<FunctoidDefinition> Functoids => Set<FunctoidDefinition>();
    public DbSet<MappingDefinition> Mappings => Set<MappingDefinition>();
    public DbSet<MappingGridPage> GridPages => Set<MappingGridPage>();
    public DbSet<MappingFunctoidInstance> FunctoidInstances => Set<MappingFunctoidInstance>();
    public DbSet<MappingLink> Links => Set<MappingLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ==================== TypeDefinition ====================
        modelBuilder.Entity<TypeDefinition>(b =>
        {
            b.ToTable("Types");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Namespace).HasMaxLength(200);
            b.Property(x => x.Tags).HasMaxLength(500);
            b.Property(x => x.Description).HasMaxLength(4000);
            b.HasIndex(x => new { x.Namespace, x.Name });
        });

        // ==================== PropertyDefinition ====================
        var typeDescConverter = new ValueConverter<TypeDescriptor, string>(
            v => JsonSerializer.Serialize(v, JsonOpts),
            v => JsonSerializer.Deserialize<TypeDescriptor>(v, JsonOpts) ?? TypeDescriptor.ScalarString());

        var typeDescComparer = new ValueComparer<TypeDescriptor>(
            (a, b) => JsonSerializer.Serialize(a, JsonOpts) == JsonSerializer.Serialize(b, JsonOpts),
            v => JsonSerializer.Serialize(v, JsonOpts).GetHashCode(),
            v => v.Clone());

        modelBuilder.Entity<PropertyDefinition>(b =>
        {
            b.ToTable("Properties");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasMaxLength(2000);
            b.Property(x => x.Type)
                .HasConversion(typeDescConverter)
                .Metadata.SetValueComparer(typeDescComparer);

            b.HasOne(x => x.OwnerType)
                .WithMany(t => t.Properties)
                .HasForeignKey(x => x.OwnerTypeId)
                .OnDelete(DeleteBehavior.Cascade);

            // NoAction avoids SQL Server Error 1785 (self-reference / multiple cascade paths)
            b.HasOne(x => x.ReferencedType)
                .WithMany()
                .HasForeignKey(x => x.ReferencedTypeId)
                .OnDelete(DeleteBehavior.NoAction);

            b.HasIndex(x => x.OwnerTypeId);
            b.HasIndex(x => x.ReferencedTypeId);
        });

        // ==================== FunctoidDefinition ====================
        var inputsConverter = JsonValueConverter<List<FunctoidInputSpec>>(() => []);
        var inputsComparer = JsonValueComparer<List<FunctoidInputSpec>>(() => []);
        var portSpecConverter = JsonValueConverter<PortTypeSpec>(PortTypeSpec.AnyScalar);
        var portSpecComparer = JsonValueComparer<PortTypeSpec>(PortTypeSpec.AnyScalar);
        var nullablePortSpecConverter = NullableJsonValueConverter<PortTypeSpec>();
        var nullablePortSpecComparer = NullableJsonValueComparer<PortTypeSpec>();
        var configSpecsConverter = JsonValueConverter<List<FunctoidConfigParamSpec>>(() => []);
        var configSpecsComparer = JsonValueComparer<List<FunctoidConfigParamSpec>>(() => []);

        modelBuilder.Entity<FunctoidDefinition>(b =>
        {
            b.ToTable("Functoids");
            b.HasKey(x => x.Id);
            b.Property(x => x.PresetKey).HasMaxLength(120);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Symbol).HasMaxLength(24);
            b.Property(x => x.Description).HasMaxLength(4000);
            b.Property(x => x.VariadicInputName).HasMaxLength(100);

            b.Property(x => x.Inputs).HasConversion(inputsConverter).Metadata.SetValueComparer(inputsComparer);
            b.Property(x => x.VariadicInputType).HasConversion(nullablePortSpecConverter).Metadata.SetValueComparer(nullablePortSpecComparer);
            b.Property(x => x.OutputType).HasConversion(portSpecConverter).Metadata.SetValueComparer(portSpecComparer);
            b.Property(x => x.ConfigParams).HasConversion(configSpecsConverter).Metadata.SetValueComparer(configSpecsComparer);

            b.HasIndex(x => x.PresetKey);
            b.HasIndex(x => new { x.Category, x.Name });
        });

        // ==================== MappingDefinition ====================
        modelBuilder.Entity<MappingDefinition>(b =>
        {
            b.ToTable("Mappings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Namespace).HasMaxLength(200);
            b.Property(x => x.Tags).HasMaxLength(500);
            b.Property(x => x.Description).HasMaxLength(4000);

            b.HasOne(x => x.SourceType)
                .WithMany()
                .HasForeignKey(x => x.SourceTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.TargetType)
                .WithMany()
                .HasForeignKey(x => x.TargetTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(x => x.SourceTypeId);
            b.HasIndex(x => x.TargetTypeId);
        });

        // ==================== MappingGridPage ====================
        modelBuilder.Entity<MappingGridPage>(b =>
        {
            b.ToTable("MappingGridPages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();

            b.HasOne(x => x.Mapping)
                .WithMany(m => m.GridPages)
                .HasForeignKey(x => x.MappingId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => x.MappingId);
        });

        // ==================== MappingFunctoidInstance ====================
        var dictConverter = new ValueConverter<Dictionary<string, string>, string>(
            v => JsonSerializer.Serialize(v, JsonOpts),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOpts) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var dictComparer = new ValueComparer<Dictionary<string, string>>(
            (a, b) => JsonSerializer.Serialize(a, JsonOpts) == JsonSerializer.Serialize(b, JsonOpts),
            v => JsonSerializer.Serialize(v, JsonOpts).GetHashCode(),
            v => v == null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(v, StringComparer.OrdinalIgnoreCase));

        modelBuilder.Entity<MappingFunctoidInstance>(b =>
        {
            b.ToTable("MappingFunctoidInstances");
            b.HasKey(x => x.Id);
            b.Property(x => x.Comment).HasMaxLength(4000);
            b.Property(x => x.LabelOverride).HasMaxLength(200);
            b.Property(x => x.ConfigValues).HasConversion(dictConverter).Metadata.SetValueComparer(dictComparer);

            b.HasOne(x => x.GridPage)
                .WithMany(p => p.FunctoidInstances)
                .HasForeignKey(x => x.GridPageId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.FunctoidDefinition)
                .WithMany()
                .HasForeignKey(x => x.FunctoidDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(x => x.GridPageId);
            b.HasIndex(x => x.FunctoidDefinitionId);
        });

        // ==================== MappingLink ====================
        modelBuilder.Entity<MappingLink>(b =>
        {
            b.ToTable("MappingLinks");
            b.HasKey(x => x.Id);
            b.Property(x => x.SourcePropertyPath).HasMaxLength(1500);
            b.Property(x => x.TargetPropertyPath).HasMaxLength(1500);
            b.Property(x => x.Comment).HasMaxLength(4000);

            b.HasOne(x => x.GridPage)
                .WithMany(p => p.Links)
                .HasForeignKey(x => x.GridPageId)
                .OnDelete(DeleteBehavior.Cascade);

            // NoAction avoids SQL Server Error 1785 since both Links and FunctoidInstances cascade from GridPage
            b.HasOne<MappingFunctoidInstance>()
                .WithMany()
                .HasForeignKey(x => x.SourceFunctoidInstanceId)
                .OnDelete(DeleteBehavior.NoAction);

            b.HasOne<MappingFunctoidInstance>()
                .WithMany()
                .HasForeignKey(x => x.TargetFunctoidInstanceId)
                .OnDelete(DeleteBehavior.NoAction);

            b.HasIndex(x => x.GridPageId);
            b.HasIndex(x => x.SourceFunctoidInstanceId);
            b.HasIndex(x => x.TargetFunctoidInstanceId);
        });
    }

    private static ValueConverter<T, string> JsonValueConverter<T>(Func<T> fallback) =>
        new(
            v => JsonSerializer.Serialize(v, JsonOpts),
            v => JsonSerializer.Deserialize<T>(v, JsonOpts) ?? fallback());

    private static ValueComparer<T> JsonValueComparer<T>(Func<T> fallback) =>
        new(
            (a, b) => JsonSerializer.Serialize(a, JsonOpts) == JsonSerializer.Serialize(b, JsonOpts),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, JsonOpts), JsonOpts) ?? fallback());

    private static ValueConverter<T?, string?> NullableJsonValueConverter<T>() where T : class =>
        new(
            v => v == null ? null : JsonSerializer.Serialize(v, JsonOpts),
            v => string.IsNullOrWhiteSpace(v) ? null : JsonSerializer.Deserialize<T>(v, JsonOpts));

    private static ValueComparer<T?> NullableJsonValueComparer<T>() where T : class =>
        new(
            (a, b) => JsonSerializer.Serialize(a, JsonOpts) == JsonSerializer.Serialize(b, JsonOpts),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonOpts).GetHashCode(),
            v => v == null ? null : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, JsonOpts), JsonOpts));
}
