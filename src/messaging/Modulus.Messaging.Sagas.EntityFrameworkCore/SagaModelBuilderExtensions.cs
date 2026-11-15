using Microsoft.EntityFrameworkCore;

namespace Modulus.Messaging.Sagas.EntityFrameworkCore;

public static class SagaModelBuilderExtensions
{
    /// <summary>Maps <see cref="SagaRecord"/> into the DbContext. Call it from <c>OnModelCreating</c>.</summary>
    public static ModelBuilder ApplyModulusSagas(this ModelBuilder modelBuilder, string tableName = "messaging_sagas")
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        modelBuilder.Entity<SagaRecord>(b =>
        {
            b.ToTable(tableName);
            b.HasKey(r => new { r.SagaType, r.CorrelationId });
            b.Property(r => r.SagaType).HasMaxLength(512).IsRequired();
            b.Property(r => r.Data).IsRequired();
        });

        return modelBuilder;
    }
}
