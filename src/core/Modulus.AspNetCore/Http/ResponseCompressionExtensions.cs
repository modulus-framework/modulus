namespace Modulus.AspNetCore.Http;

using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Binds from the <c>ResponseCompression</c> configuration section.</summary>
public sealed class ModulusResponseCompressionOptions
{
    public const string SectionName = "ResponseCompression";

    /// <summary>Turns compression on. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Compress responses sent over HTTPS. Off by default: compressing a response that reflects attacker-controlled
    /// input next to a secret enables BREACH-style attacks. Turn it on only when responses carry no such mix, or
    /// leave compression to the TLS-terminating proxy.
    /// </summary>
    public bool EnableForHttps { get; set; }

    /// <summary>Media types compressed in addition to ASP.NET Core's defaults (<c>application/json</c> is already among them).</summary>
    public string[] AdditionalMimeTypes { get; set; } = ["application/problem+json", "application/grpc-web+proto"];
}

/// <summary>Brotli and gzip response compression for the API.</summary>
public static class ResponseCompressionExtensions
{
    /// <summary>Registers response compression (Brotli, then gzip, at the fastest level) from the <c>ResponseCompression</c> section.</summary>
    public static IServiceCollection AddModulusResponseCompression(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ModulusResponseCompressionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(ModulusResponseCompressionOptions.SectionName)
            .Get<ModulusResponseCompressionOptions>() ?? new ModulusResponseCompressionOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        if (!options.Enabled)
            return services;

        services.AddResponseCompression(o =>
        {
            o.EnableForHttps = options.EnableForHttps;
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
            o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(options.AdditionalMimeTypes);
        });
        // Fastest, not Optimal: API responses are compressed per request, so CPU per byte saved matters more than ratio.
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        return services;
    }

    /// <summary>Adds the compression middleware when it is enabled. Place it before anything that writes bodies.</summary>
    public static IApplicationBuilder UseModulusResponseCompression(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.ApplicationServices.GetService<ModulusResponseCompressionOptions>();
        return options is { Enabled: true } ? app.UseResponseCompression() : app;
    }
}
