namespace Modulus.Testing;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

/// <summary>
/// Points one host's HTTP clients at another host's in-memory
/// <see cref="Microsoft.AspNetCore.TestHost.TestServer"/>, so a two-process
/// app — the <c>webapp+api</c> split's Web host calling the API host over
/// HTTP — can be exercised end to end without a network port: the paired
/// factory's typed/named clients send every request to the API factory's
/// TestServer handler instead of the network.
/// </summary>
/// <remarks>
/// <para>
/// Pairing replaces the <b>primary</b> handler of every
/// <see cref="System.Net.Http.HttpClient"/> the host builds through
/// <c>IHttpClientFactory</c>; the rest of each pipeline (resilience,
/// correlation, token relay) stays exactly as the host registered it — an
/// outage of the API therefore surfaces as it would in production: retried,
/// then failed.
/// </para>
/// <para>
/// Call <see cref="PairedWith{TWeb, TApi}"/> before the web factory's host is
/// built (before the first <c>CreateClient()</c>): it returns a derived
/// factory whose host carries the pairing; use the returned factory and
/// dispose it, not the original.
/// </para>
/// </remarks>
public static class TestServerPairing
{
    /// <summary>
    /// Returns a factory whose HTTP clients send their requests to
    /// <paramref name="apiFactory"/>'s TestServer. The API factory must be
    /// created (its host starts lazily on first use) and disposed after the
    /// paired web factory.
    /// </summary>
    public static WebApplicationFactory<TWeb> PairedWith<TWeb, TApi>(
        this WebApplicationFactory<TWeb> webFactory,
        WebApplicationFactory<TApi> apiFactory)
        where TWeb : class
        where TApi : class
    {
        ArgumentNullException.ThrowIfNull(webFactory);
        ArgumentNullException.ThrowIfNull(apiFactory);

        return webFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                    handlerBuilder.PrimaryHandler = apiFactory.Server.CreateHandler()))));
    }
}
