namespace Modulus.EntityFrameworkCore.DataProtection;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Modulus.Core.Abstractions.DataProtection;

/// <summary>
/// EF Core value converter that encrypts a string property on the way to the database
/// and decrypts it on the way back, via an <see cref="IPersonalDataProtector"/>. EF
/// invokes converters only for non-null values, so <c>null</c> is stored as-is.
/// Applied by <see cref="PersonalDataModelBuilderExtensions.UseModulusPersonalDataEncryption"/>.
/// </summary>
internal sealed class EncryptingConverter(IPersonalDataProtector protector)
    : ValueConverter<string, string>(
        plaintext => protector.Protect(plaintext),
        ciphertext => protector.Unprotect(ciphertext));
