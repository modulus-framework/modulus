using System.ComponentModel.DataAnnotations;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>Element type of the <c>m-line-items</c> probe page's collection.</summary>
public sealed class InvoiceLine
{
    [Required, Display(Name = "SKU")]
    public string? Sku { get; set; }

    [Range(1, 100)]
    public int Quantity { get; set; } = 1;
}
