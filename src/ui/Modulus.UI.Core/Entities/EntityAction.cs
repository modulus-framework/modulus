namespace Modulus.UI;

/// <summary>Where an <see cref="EntityAction"/>'s htmx response lands.</summary>
public enum EntityActionTarget
{
    /// <summary>The shared modal (<c>#m-modal-container</c>); the response is the modal's content.</summary>
    Modal,

    /// <summary>The action's own table row, replaced by the response (a row fragment).</summary>
    Row,
}

/// <summary>
/// A per-row action one module contributes to another module's entity list (<c>Inventory</c> adds "Adjust stock" to
/// <c>Catalog.Product</c>). Registered with <c>ConfigureEntityUi</c> and rendered in a row by
/// <c>&lt;m-entity-actions entity="..." row-id="@row.Id" /&gt;</c>. Exactly one of <see cref="Url"/>,
/// <see cref="HxGet"/> or <see cref="HxPost"/> is set; <c>{id}</c> in it is replaced with the row's
/// URL-encoded id.
/// </summary>
public sealed class EntityAction
{
    /// <summary>Builds an action.</summary>
    /// <param name="id">Stable identifier (dedup/remove key), e.g. <c>Inventory.AdjustStock</c>.</param>
    /// <param name="label">Visible, already localised text.</param>
    /// <param name="url">Navigation target (renders a link).</param>
    /// <param name="hxGet">htmx GET url (renders a button).</param>
    /// <param name="hxPost">htmx POST url (renders a button).</param>
    /// <param name="target">Where an htmx response lands (default: the modal).</param>
    /// <param name="requiredPermission">Hidden unless the current user has it.</param>
    /// <param name="order">Sort key (ascending, ties by label).</param>
    /// <param name="cssClass">Button colour classes (default <c>btn-outline-secondary</c>).</param>
    public EntityAction(
        string id,
        string label,
        string? url = null,
        string? hxGet = null,
        string? hxPost = null,
        EntityActionTarget target = EntityActionTarget.Modal,
        string? requiredPermission = null,
        int order = 100,
        string? cssClass = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var set = new[] { url, hxGet, hxPost }.Count(v => !string.IsNullOrWhiteSpace(v));
        if (set != 1)
        {
            throw new ArgumentException($"Entity action '{id}' must define exactly one of url, hxGet or hxPost.", nameof(id));
        }

        Id = id;
        Label = label;
        Url = url;
        HxGet = hxGet;
        HxPost = hxPost;
        Target = target;
        RequiredPermission = requiredPermission;
        Order = order;
        CssClass = cssClass;
    }

    /// <summary>Stable identifier.</summary>
    public string Id { get; }

    /// <summary>Visible text.</summary>
    public string Label { get; }

    /// <summary>Navigation target, if a link.</summary>
    public string? Url { get; }

    /// <summary>htmx GET url, if a GET button.</summary>
    public string? HxGet { get; }

    /// <summary>htmx POST url, if a POST button.</summary>
    public string? HxPost { get; }

    /// <summary>Where an htmx response lands.</summary>
    public EntityActionTarget Target { get; }

    /// <summary>Permission the current user needs; null = everyone.</summary>
    public string? RequiredPermission { get; }

    /// <summary>Sort key (ascending).</summary>
    public int Order { get; }

    /// <summary>Button colour classes, or null for the default.</summary>
    public string? CssClass { get; }

    /// <summary>Replaces <c>{id}</c> in <paramref name="template"/> with the URL-encoded <paramref name="rowId"/>.</summary>
    internal static string Expand(string template, string rowId)
        => template.Replace("{id}", Uri.EscapeDataString(rowId), StringComparison.Ordinal);
}
