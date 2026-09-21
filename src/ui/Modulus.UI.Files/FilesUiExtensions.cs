namespace Modulus.UI.Files;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Localization;

/// <summary>
/// Registers the Files UI: Razor Pages services, the
/// <see cref="FilesUiModule"/> navigation sidecar, options bound from the
/// <c>FilesUi</c> section, the <c>files:manage</c> permission declaration,
/// and startup seeding of the <c>Modulus.Files</c> localizer resource. The
/// host must still call <c>MapRazorPages()</c> (or
/// <see cref="MapModulusFilesUi"/>) so the RCL pages get endpoints.
/// </summary>
public static class FilesUiExtensions
{
    public static IServiceCollection AddModulusFilesUi(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = services.AddOptions<FilesUiOptions>();
        if (configuration is not null)
            options.Bind(configuration.GetSection(FilesUiOptions.SectionName));

        var pages = services.AddRazorPages();
        services.AddUiModule<FilesUiModule>();
        services.AddPermissions("Files", registry => registry.Add(
            FilesUiPermissions.Manage,
            "Upload, download, and delete files."));

        // Folder convention needs the value at registration time, so read it
        // eagerly; the bound options remain the runtime source of truth.
        // IConfigurationSection.Get<T>() returns null (not a default-
        // constructed instance) when the section is absent — the common case
        // for a host that hasn't touched FilesUi:RequirePermission — so the
        // fallback to the compiled-in permission must happen here, not only
        // on the options class's property initializer.
        var requirePermission = configuration
            ?.GetSection(FilesUiOptions.SectionName)
            .Get<FilesUiOptions>()?.RequirePermission
            ?? FilesUiPermissions.Manage;
        if (!string.IsNullOrWhiteSpace(requirePermission))
            pages.AddRazorPagesOptions(o => o.Conventions.AuthorizeFolder("/Files", requirePermission));

        services.AddSingleton<IStartupFilter, FilesUiStartupFilter>();
        return services;
    }

    /// <summary>Maps Razor Pages endpoints (hosts the RCL file pages).</summary>
    public static IEndpointRouteBuilder MapModulusFilesUi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRazorPages();
        return endpoints;
    }

    private sealed class FilesUiStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            var store = app.ApplicationServices.GetService(typeof(ILocalizationStore)) as ILocalizationStore;
            if (store is not null)
                FilesUiLocalization.Seed(store);

            next(app);
        };
    }
}
