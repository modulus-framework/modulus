namespace Modulus.EntityFrameworkCore.DataProtection;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions.DataProtection;

/// <summary>
/// Applies transparent at-rest encryption to every <see cref="string"/> property
/// marked with <see cref="ProtectedPersonalDataAttribute"/>. Call from a
/// DbContext's <c>OnModelCreating</c>; <c>ModuleDbContext</c> does this automatically
/// when an <see cref="IPersonalDataProtector"/> is registered.
/// </summary>
public static class PersonalDataModelBuilderExtensions
{
    /// <summary>
    /// Walks the model and attaches an encrypting value converter to each string
    /// property carrying <see cref="ProtectedPersonalDataAttribute"/>. A single
    /// converter instance is shared across all such properties. Properties without a
    /// backing <see cref="PropertyInfo"/> (shadow/field-only) cannot carry the
    /// attribute and are skipped.
    /// </summary>
    /// <param name="modelBuilder">The model being configured.</param>
    /// <param name="protector">The protector used to encrypt and decrypt values.</param>
    public static ModelBuilder UseModulusPersonalDataEncryption(
        this ModelBuilder modelBuilder, IPersonalDataProtector protector)
    {
        var converter = new EncryptingConverter(protector);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType != typeof(string))
                    continue;
                if (property.PropertyInfo?.GetCustomAttribute<ProtectedPersonalDataAttribute>() is null)
                    continue;

                property.SetValueConverter(converter);
            }
        }

        return modelBuilder;
    }
}
