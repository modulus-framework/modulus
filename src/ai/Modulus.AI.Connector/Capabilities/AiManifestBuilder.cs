namespace Modulus.AI.Connector.Capabilities;

using System.Security.Cryptography;
using System.Text.Json;
using Modulus.AI.Connector.Contract;

/// <summary>
/// Builds the manifest once: capabilities (all read-only) with their input schemas, required permissions and output
/// fields, and resource types with their fields, deep-link templates and title fields. Secret fields are left out;
/// every other field carries its platform class. The fingerprint changes exactly when the manifest does, so a deploy
/// that changes nothing the platform sees needs no new manifest approval.
/// </summary>
internal sealed class AiManifestBuilder(AiCapabilityRegistry registry, IOptions<ModulusAiConnectorOptions> options)
{
    private readonly Lazy<ConnectorManifest> _manifest = new(() => Build(registry, options.Value));

    /// <summary>The manifest.</summary>
    public ConnectorManifest Manifest => _manifest.Value;

    private static ConnectorManifest Build(AiCapabilityRegistry registry, ModulusAiConnectorOptions options)
    {
        var capabilities = registry.Capabilities
            .Select(c => new ManifestCapability(
                c.Name,
                c.Description,
                ReadOnly: true,
                c.InputSchema,
                c.RequiredPermission is null ? [] : [c.RequiredPermission],
                c.ResourceType,
                Fields(c.Fields, options)))
            .ToList();

        var resources = registry.Resources
            .Select(r => new ManifestResourceType(
                r.ResourceType,
                r.Description,
                r.DeepLink,
                r.TitleField is null ? null : JsonNamingPolicy.CamelCase.ConvertName(r.TitleField),
                registry.TryGetIndexed(r.ResourceType, out _),
                Fields(AiFieldCatalog.For(r.ItemType), options)))
            .ToList();

        var unsigned = new ConnectorManifest(
            options.ContractVersion, options.AppType, options.AppName, string.Empty, capabilities, resources);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(unsigned, ConnectorJson.Options);
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes))[..32];
        return unsigned with { Fingerprint = fingerprint };
    }

    private static List<ManifestField> Fields(IReadOnlyList<AiField> fields, ModulusAiConnectorOptions options)
        => [.. fields
            .Where(f => !f.IsSecret)
            .Select(f => new ManifestField(f.Name, f.Type, f.ClassOr(options.DefaultClassification)))];
}
