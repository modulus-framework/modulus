namespace Modulus.Storage;

/// <summary>
/// A presigned browser upload: POST <see cref="Fields"/> as <c>multipart/form-data</c> to <see cref="Url"/>, with the file as the
/// last form field. The storage checks the signature and the size and content-type conditions, so a client can upload only the
/// file the policy describes, at most the size it names, before the policy expires.
/// </summary>
public sealed record PresignedPost(string Url, IReadOnlyDictionary<string, string> Fields);
