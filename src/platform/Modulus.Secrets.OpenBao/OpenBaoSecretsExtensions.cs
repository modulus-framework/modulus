namespace Modulus.Secrets.OpenBao;

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Secrets;
using Modulus.Secrets;
using Modulus.Platform.Http;

/// <summary>Registers OpenBao as the secret provider of Modulus.</summary>
public static class OpenBaoSecretsExtensions
{
    private const string ReadClient = "modulus-openbao-read";
    private const string WriteClient = "modulus-openbao-write";
    private const string HealthClient = "modulus-openbao-health";

    /// <summary>
    /// Registers the runtime read path: <see cref="ISecretStore"/> (cached) backed by OpenBao KV v2 with the
    /// <c>Secrets:OpenBao:Auth</c> identity. Also registers the OpenBao health check. Options are validated at startup.
    /// </summary>
    public static IServiceCollection AddOpenBaoSecretStore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModulusSecrets(configuration);
        AddOptions(services, configuration);

        services.AddModulusHttpClient(ReadClient)
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client))
            .ConfigurePrimaryHttpMessageHandler(sp => CreateHandler(sp.GetRequiredService<IOptions<OpenBaoOptions>>().Value));

        services.AddHttpClient(HealthClient)
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client))
            .ConfigurePrimaryHttpMessageHandler(sp => CreateHandler(sp.GetRequiredService<IOptions<OpenBaoOptions>>().Value));

        services.Replace(ServiceDescriptor.Singleton<ISecretBackend>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<OpenBaoOptions>>().Value;
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var tokens = new OpenBaoTokenProvider(
                factory.CreateClient(ReadClient),
                options.Auth,
                sp.GetService<TimeProvider>() ?? TimeProvider.System,
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("Modulus.Secrets.OpenBao"));
            return new OpenBaoSecretStore(new OpenBaoKvClient(factory.CreateClient(ReadClient), tokens, options));
        }));

        services.AddSingleton<IModuleHealthCheck>(sp => new OpenBaoHealthCheck(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HealthClient)));
        return services;
    }

    /// <summary>
    /// Registers the provisioning write path: <see cref="ISecretWriter"/> bound to the <c>Secrets:OpenBao:Writer</c>
    /// identity. Refused unless <c>Secrets:RuntimeReadOnly</c> is set to <c>false</c>, so a runtime host can never hold
    /// a write-capable identity by mistake. Writes are not retried, because a retried check-and-set could report a false
    /// conflict.
    /// </summary>
    public static IServiceCollection AddOpenBaoSecretWriter(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.GetValue("Secrets:RuntimeReadOnly", defaultValue: true))
            throw new InvalidOperationException(
                "Secrets:RuntimeReadOnly is true, so this host must not register a secret writer. Set it to false only on the provisioning host.");

        services.AddModulusSecrets(configuration);
        AddOptions(services, configuration);
        services.AddSingleton<IValidateOptions<OpenBaoOptions>, WriterRequiredValidator>();

        services.AddHttpClient(WriteClient)
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client))
            .ConfigurePrimaryHttpMessageHandler(sp => CreateHandler(sp.GetRequiredService<IOptions<OpenBaoOptions>>().Value));

        services.AddSingleton<ISecretWriter>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<OpenBaoOptions>>().Value;
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var identity = options.Writer ?? throw new InvalidOperationException("Secrets:OpenBao:Writer is required.");
            var tokens = new OpenBaoTokenProvider(
                factory.CreateClient(WriteClient),
                identity,
                sp.GetService<TimeProvider>() ?? TimeProvider.System,
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("Modulus.Secrets.OpenBao.Writer"));
            return new OpenBaoSecretWriter(new OpenBaoKvClient(factory.CreateClient(WriteClient), tokens, options));
        });
        return services;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OpenBaoOptions>()
            .Bind(configuration.GetSection(OpenBaoOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OpenBaoOptions>, OpenBaoOptionsValidator>();
    }

    private static void ConfigureClient(IServiceProvider sp, HttpClient client)
    {
        var options = sp.GetRequiredService<IOptions<OpenBaoOptions>>().Value;
        client.BaseAddress = new Uri(options.Address.TrimEnd('/') + "/");
        client.Timeout = options.RequestTimeout;
        if (!string.IsNullOrWhiteSpace(options.Namespace))
            client.DefaultRequestHeaders.Add("X-Vault-Namespace", options.Namespace);
    }

    /// <summary>
    /// The primary handler. With <see cref="OpenBaoOptions.CaCertificatePath"/> set, the server chain is judged only against
    /// that CA (never the system store), and the host name must still match.
    /// </summary>
    internal static HttpMessageHandler CreateHandler(OpenBaoOptions options)
    {
        var handler = new HttpClientHandler();
        if (string.IsNullOrWhiteSpace(options.CaCertificatePath))
            return handler;

#if NET9_0_OR_GREATER
        var ca = X509CertificateLoader.LoadCertificateFromFile(options.CaCertificatePath);
#else
#pragma warning disable SYSLIB0057 // X509CertificateLoader is .NET 9+; this branch serves net8.0.
        var ca = new X509Certificate2(options.CaCertificatePath);
#pragma warning restore SYSLIB0057
#endif
        handler.ServerCertificateCustomValidationCallback = (_, certificate, chain, errors) =>
            ValidateAgainstCa(certificate, chain, errors, ca);
        return handler;
    }

    private static bool ValidateAgainstCa(X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors errors, X509Certificate2 ca)
    {
        if (certificate is null || chain is null || (errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
            return false;

        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Clear();
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        return chain.Build(certificate);
    }

    internal sealed class OpenBaoOptionsValidator : IValidateOptions<OpenBaoOptions>
    {
        public ValidateOptionsResult Validate(string? name, OpenBaoOptions options)
            => options.Validate() is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
    }

    internal sealed class WriterRequiredValidator : IValidateOptions<OpenBaoOptions>
    {
        public ValidateOptionsResult Validate(string? name, OpenBaoOptions options)
            => options.Writer is null
                ? ValidateOptionsResult.Fail("Secrets:OpenBao:Writer is required on the provisioning host.")
                : ValidateOptionsResult.Success;
    }
}
