using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Modulus.Identity.Tests;

/// <summary>Makes attestations and assertions the way an authenticator does (ES256, user verified, no attestation).</summary>
internal sealed class SoftwareAuthenticator
{
    public const string Origin = "https://erp.example.com";
    public const string RpId = "erp.example.com";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private uint _counter;

    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);

    public string Register(JsonElement options, string origin = Origin, byte flags = 0x45)
    {
        var challenge = Base64Url(options.GetProperty("challenge").GetString()!);
        var clientData = ClientData("webauthn.create", challenge, origin);
        var parameters = _key.ExportParameters(false);

        var cose = new CborWriter(CborConformanceMode.Ctap2Canonical);
        cose.WriteStartMap(5);
        cose.WriteInt32(1); cose.WriteInt32(2);
        cose.WriteInt32(3); cose.WriteInt32(-7);
        cose.WriteInt32(-1); cose.WriteInt32(1);
        cose.WriteInt32(-2); cose.WriteByteString(parameters.Q.X!);
        cose.WriteInt32(-3); cose.WriteByteString(parameters.Q.Y!);
        cose.WriteEndMap();

        var authData = new List<byte>();
        authData.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(RpId)));
        authData.Add(flags);
        authData.AddRange(Counter(_counter));
        authData.AddRange(new byte[16]);
        authData.Add((byte)(CredentialId.Length >> 8));
        authData.Add((byte)CredentialId.Length);
        authData.AddRange(CredentialId);
        authData.AddRange(cose.Encode());

        var attestation = new CborWriter(CborConformanceMode.Ctap2Canonical);
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt"); attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt"); attestation.WriteStartMap(0); attestation.WriteEndMap();
        attestation.WriteTextString("authData"); attestation.WriteByteString(authData.ToArray());
        attestation.WriteEndMap();

        return JsonSerializer.Serialize(new
        {
            id = Base64Url(CredentialId),
            rawId = Base64Url(CredentialId),
            type = "public-key",
            response = new { attestationObject = Base64Url(attestation.Encode()), clientDataJSON = Base64Url(clientData) },
        });
    }

    public string Assert(JsonElement options, Guid userHandle, string origin = Origin, byte flags = 0x05, uint? counter = null)
    {
        var challenge = Base64Url(options.GetProperty("challenge").GetString()!);
        var clientData = ClientData("webauthn.get", challenge, origin);
        _counter = counter ?? _counter + 1;

        var authData = new List<byte>();
        authData.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(RpId)));
        authData.Add(flags);
        authData.AddRange(Counter(_counter));
        var signed = authData.Concat(SHA256.HashData(clientData)).ToArray();
        var signature = _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return JsonSerializer.Serialize(new
        {
            id = Base64Url(CredentialId),
            rawId = Base64Url(CredentialId),
            type = "public-key",
            response = new
            {
                authenticatorData = Base64Url(authData.ToArray()),
                clientDataJSON = Base64Url(clientData),
                signature = Base64Url(signature),
                userHandle = Base64Url(userHandle.ToByteArray()),
            },
        });
    }

    private static byte[] Counter(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static byte[] ClientData(string type, byte[] challenge, string origin)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, challenge = Base64Url(challenge), origin, crossOrigin = false }));

    internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] Base64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
    }
}
