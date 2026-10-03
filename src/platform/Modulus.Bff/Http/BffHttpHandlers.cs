namespace Modulus.Bff.Http;

using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Modulus.Bff.Tokens;

/// <summary>
/// Attaches the caller's access token to outgoing calls made while serving a BFF request:
/// the web session's server-side token (refreshed before expiry; on a <c>401</c> refreshed once
/// and the call replayed, except a gRPC call, whose body is a stream) or the mobile/partner caller's own bearer token. Calls made outside a
/// BFF request (in a host without a default client), or by an anonymous caller, go out without a token.
/// </summary>
public sealed class UserAccessTokenHandler(IHttpContextAccessor accessor, IBffAccessTokenService tokens, IBffClientContext clientContext) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = accessor.HttpContext;
        if (context is null || clientContext.Name is not { } name || clientContext.Kind is not { } kind || request.Headers.Authorization is not null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var token = await tokens.GetAccessTokenAsync(context, name, ct: cancellationToken).ConfigureAwait(false);
        if (token is null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // A gRPC body is a stream the caller is still writing (buffering it would stall a streaming call), so a gRPC
        // call is never replayed; it still gets a token refreshed ahead of expiry.
        var replayable = kind == BffClientKind.Web && !IsGrpc(request);
        if (replayable && request.Content is not null)
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || !replayable)
            return response;

        // The token may have been revoked or the clocks skewed: refresh once and replay.
        var refreshed = await tokens.GetAccessTokenAsync(context, name, forceRefresh: true, cancellationToken).ConfigureAwait(false);
        if (refreshed is null || refreshed == token)
            return response;

        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsGrpc(HttpRequestMessage request)
        => request.Content?.Headers.ContentType?.MediaType?.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>Stamps <c>X-Client-App: {client}</c> so upstream services can tell which front end called.</summary>
public sealed class BffClientHeaderHandler(IBffClientContext clientContext) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (clientContext.Name is { } client && !request.Headers.Contains(BffDefaults.ClientAppHeader))
            request.Headers.TryAddWithoutValidation(BffDefaults.ClientAppHeader, client);

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Forwards the company the caller selected (<see cref="BffOptions.TenantHeader"/>, or the <c>tid</c> claim) to the
/// upstream service, so an aggregator's calls act in the same company as the request that triggered them. The
/// upstream still checks membership; the BFF never picks a company on its own.
/// </summary>
public sealed class BffTenantHeaderHandler(IHttpContextAccessor accessor, IOptions<BffOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var header = options.Value.TenantHeader;
        if (!request.Headers.Contains(header) && BffTenant.Selected(accessor.HttpContext, header) is { } tenant)
            request.Headers.TryAddWithoutValidation(header, tenant);

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>The company a BFF request acts in.</summary>
internal static class BffTenant
{
    /// <summary>The <c>tid</c> claim (a token pinned to one company), else the selecting header, else null.</summary>
    public static string? Selected(HttpContext? context, string header)
    {
        if (context is null)
            return null;
        if (context.User.FindFirst("tid")?.Value is { Length: > 0 } pinned)
            return pinned;
        var value = context.Request.Headers[header].ToString();
        return value.Length > 0 ? value : null;
    }
}
