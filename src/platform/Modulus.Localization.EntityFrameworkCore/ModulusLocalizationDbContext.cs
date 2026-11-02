namespace Modulus.Localization.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// The translation table. Derive the application's context from it and register it with
/// <c>AddModulusEfLocalizationStore&lt;TContext&gt;()</c>.
/// </summary>
public class ModulusLocalizationDbContext : DbContext
{
    /// <summary>The translation table name.</summary>
    public const string TranslationTable = "modulus_translations";

    /// <summary>Creates the context.</summary>
    protected ModulusLocalizationDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>One row per resource, culture and key.</summary>
    public DbSet<TranslationRow> Translations => Set<TranslationRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TranslationRow>(b =>
        {
            b.ToTable(TranslationTable);
            b.HasKey(t => new { t.Resource, t.Culture, t.Key });
            b.Property(t => t.Resource).HasMaxLength(200);
            b.Property(t => t.Culture).HasMaxLength(35);
            b.Property(t => t.Key).HasMaxLength(300);
            b.Property(t => t.Value).HasMaxLength(4000).IsRequired();
        });
    }
}

/// <summary>A translation row. Culture is stored lower-cased, as the store reads it.</summary>
public sealed class TranslationRow
{
    public string Resource { get; set; } = "";

    public string Culture { get; set; } = "";

    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}
