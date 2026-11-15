using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

/// <summary>A self-signed certificate for localhost, created per test fixture, so no certificate is committed.</summary>
internal sealed class TlsTestCertificate
{
    private TlsTestCertificate(X509Certificate2 certificate, string certificatePem, string privateKeyPem)
    {
        Certificate = certificate;
        CertificatePem = certificatePem;
        PrivateKeyPem = privateKeyPem;
    }

    public X509Certificate2 Certificate { get; }

    /// <summary>The certificate in PEM form, also the trust root for a client that trusts it.</summary>
    public string CertificatePem { get; }

    /// <summary>The private key in PKCS#8 PEM form.</summary>
    public string PrivateKeyPem { get; }

    public static TlsTestCertificate Create()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, critical: false));

        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        return new TlsTestCertificate(
            certificate,
            certificate.ExportCertificatePem(),
            rsa.ExportPkcs8PrivateKeyPem());
    }
}
