namespace Modulus.Branding;

using Microsoft.AspNetCore.Builder;
using Modulus.Core.Abstractions.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Settings;
using Modulus.Storage;

/// <summary>What an upload stored, and where it is served.</summary>
public sealed record BrandingAssetResponse(string Slot, string Scope, string ContentType, long Size, string Url);

/// <summary>
/// Branding images over HTTP. Uploads and removals are authorized: a company's images need <c>settings:manage</c>, the installation-wide
/// images need <c>settings:global:manage</c> (Critical), and each runs on its own route so the permission is fixed by the route, not a request
/// parameter. The image itself is served to anyone (the sign-in page shows it before anyone is signed in), and the bytes are opened only for
/// a key this application issued.
/// </summary>
public static class BrandingEndpoints
{
    /// <summary>Maps the branding routes under <paramref name="prefix"/> (default <c>/api/branding</c>).</summary>
    public static RouteGroupBuilder MapModulusBranding(this IEndpointRouteBuilder endpoints, string prefix = "/api/branding")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix);

        // Anyone may read the effective image for their company (or the installation when none is signed in).
        group.MapGet("/{slot}", (string slot, HttpContext http, ISettingManager settings, IBrandingAssetStore store, CancellationToken ct) => Answer(async () =>
        {
            if (!TryParse(slot, out var parsed))
                return Results.NotFound();

            var key = await settings.GetOrNullAsync(ApplicationSettingsCatalog.AssetSettingName(parsed), ct).ConfigureAwait(false);
            if (!BrandingAssetStore.IsIssuedKey(key))
                return Results.NotFound();

            // The stored type came from the file's bytes; nosniff stops a browser guessing another one.
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var stream = await store.OpenAsync(key!, ct).ConfigureAwait(false);
            return Results.Stream(stream, BrandingAssetStore.ContentTypeOfKey(key!));
        }))
            // The same reviewed opening the Loosen extension makes: the reason is recorded for the startup guard and the audit fingerprint.
            .WithMetadata(new LoosenedAttribute("The sign-in page shows the installation logo and background before anyone is signed in; branding images are public by design"))
            .AllowAnonymous();

        group.MapPost("/{slot}", (string slot, HttpRequest request, IBrandingAssetStore store, ISettingAdministration settings, ISettingManager manager, ICurrentTenant tenant, CancellationToken ct)
                => Upload(prefix, slot, request, SettingScope.Tenant, store, settings, manager, tenant, ct))
            .RequireAuthorization(SettingsAdministrationEndpoints.ManagePermission)
            .DisableAntiforgery();

        group.MapPost("/platform/{slot}", (string slot, HttpRequest request, IBrandingAssetStore store, ISettingAdministration settings, ISettingManager manager, ICurrentTenant tenant, CancellationToken ct)
                => Upload(prefix, slot, request, SettingScope.Global, store, settings, manager, tenant, ct))
            .RequireAuthorization(SettingsAdministrationEndpoints.GlobalManagePermission)
            .DisableAntiforgery();

        group.MapDelete("/{slot}", (string slot, IBrandingAssetStore store, ISettingAdministration settings, ISettingManager manager, CancellationToken ct)
                => Restore(slot, SettingScope.Tenant, store, settings, manager, ct))
            .RequireAuthorization(SettingsAdministrationEndpoints.ManagePermission);

        group.MapDelete("/platform/{slot}", (string slot, IBrandingAssetStore store, ISettingAdministration settings, ISettingManager manager, CancellationToken ct)
                => Restore(slot, SettingScope.Global, store, settings, manager, ct))
            .RequireAuthorization(SettingsAdministrationEndpoints.GlobalManagePermission);

        return group;
    }

    private static async Task<IResult> Upload(
        string prefix,
        string slot,
        HttpRequest request,
        SettingScope scope,
        IBrandingAssetStore store,
        ISettingAdministration settings,
        ISettingManager manager,
        ICurrentTenant tenant,
        CancellationToken ct)
        => await Answer(async () =>
        {
            if (!TryParse(slot, out var parsed))
                return Results.NotFound();

            // A company image needs a company in scope; refusing here means no file is written that could not be recorded.
            if (scope == SettingScope.Tenant && tenant.TenantId is null)
                throw new ValidationException(["A company image needs a company in scope."]);

            var tenantId = scope == SettingScope.Global ? null : tenant.TenantId;
            // The file is read here, after authorization, not bound as a parameter (see UploadRequest).
            var file = await UploadRequest.RequireFileAsync(request, ct).ConfigureAwait(false);
            await using var content = file.OpenReadStream();
            var asset = await store.UploadAsync(parsed, tenantId, content, ct).ConfigureAwait(false);

            var name = ApplicationSettingsCatalog.AssetSettingName(parsed);
            var previous = await manager.ResolveAsync(name, ct).ConfigureAwait(false);
            try
            {
                await settings.SetAsync(name, asset.Key, scope, ct).ConfigureAwait(false);
            }
            catch
            {
                // The value was not recorded, so the new file would be unreachable. Remove it rather than leave an orphan.
                await store.RemoveAsync(asset, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            // The replaced image is removed only when this scope owned it. An image inherited from the installation is left alone.
            if (OwnedBy(previous, scope) && BrandingAssetStore.IsIssuedKey(previous.Value))
                await store.RemoveAsync(new BrandingAsset(previous.Value!, "", 0), ct).ConfigureAwait(false);

            var response = new BrandingAssetResponse(parsed.ToString(), scope.ToString(), asset.ContentType, asset.Length, $"{prefix}/{parsed}");
            return Results.Created($"{prefix}/{parsed}", response);
        });

    private static async Task<IResult> Restore(
        string slot,
        SettingScope scope,
        IBrandingAssetStore store,
        ISettingAdministration settings,
        ISettingManager manager,
        CancellationToken ct)
        => await Answer(async () =>
        {
            if (!TryParse(slot, out var parsed))
                return Results.NotFound();

            var name = ApplicationSettingsCatalog.AssetSettingName(parsed);
            var current = await manager.ResolveAsync(name, ct).ConfigureAwait(false);

            // Restoring removes this scope's image and its value. An image set at another scope is not this scope's to remove, so nothing happens.
            if (!OwnedBy(current, scope))
                return Results.NoContent();

            await settings.RemoveAsync(name, scope, ct).ConfigureAwait(false);
            if (BrandingAssetStore.IsIssuedKey(current.Value))
                await store.RemoveAsync(new BrandingAsset(current.Value!, "", 0), ct).ConfigureAwait(false);
            return Results.NoContent();
        });

    private static bool OwnedBy(SettingResolution resolution, SettingScope scope)
        => resolution.Source == (scope == SettingScope.Global ? SettingSource.Global : SettingSource.Tenant) && resolution.Value is not null;

    private static bool TryParse(string slot, out BrandingSlot parsed)
        => Enum.TryParse(slot, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);

    private static async Task<IResult> Answer(Func<Task<IResult>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (ValidationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ex.Errors.ToArray() });
        }
        catch (NotFoundException)
        {
            return Results.NotFound();
        }
        catch (UnauthorizedException)
        {
            return Results.Unauthorized();
        }
    }
}
