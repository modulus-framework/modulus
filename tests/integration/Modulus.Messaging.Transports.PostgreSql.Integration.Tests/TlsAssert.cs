using System.Security.Authentication;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

internal static class TlsAssert
{
    /// <summary>True when the exception or one of its inner exceptions is a TLS certificate failure.</summary>
    public static bool IsCertificateFailure(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
            {
                return true;
            }
        }

        return false;
    }
}
