namespace Modulus.UI;

/// <summary>
/// Describes a prebuilt UI module for CLI resolution and the <c>/_ui/modules</c>
/// endpoint. Mirrors the <c>module.json</c> manifest shipped with each UI package.
/// </summary>
/// <param name="Id">Stable module id (e.g. <c>Modulus.Identity</c>).</param>
/// <param name="Name">Short display name (e.g. <c>Identity</c>).</param>
/// <param name="Version">Module version.</param>
/// <param name="Dependencies">Ids of modules that must be installed first.</param>
/// <param name="Features">Feature areas provided (e.g. Users, Roles, Tenants).</param>
public sealed record ModuleManifest(
    string Id,
    string Name,
    string Version,
    string[] Dependencies,
    string[] Features);
