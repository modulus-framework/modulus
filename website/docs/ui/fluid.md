---
title: "Liquid templates"
description: "Render pages and documents from Liquid templates with the Modulus components, tags and filters, using the Fluid package and the sample invoices app."
sidebar_position: 12
---

Liquid templates are for pages and documents that come from a template rather than from code: an invoice, a letter, a mail merge. The `Synvro.Modulus.AspNetCore.Fluid` package renders them with [Fluid](https://github.com/sebastienros/fluid) and gives them the Modulus components, tags and filters. The sample at `samples/Modulus.Samples.Fluid` (port 5103) lists and shows invoices this way.

## Set it up

```csharp title="Program.cs"
builder.Services.AddModulusHardening();
builder.Services.AddModulusObservability();

// The model types the templates may read. Only registered types expose their public properties.
builder.Services.AddSingleton(_ => new ModulusLiquid(
    new ModulusLiquidOptions().Register<Invoice>().Register<InvoiceLine>()));

var app = builder.Build();
app.UseModulusHardening();
app.MapModulusHealth();
```

`ModulusLiquid` is the renderer. It is safe to share, so register it as a singleton. Its methods are:

- `Load(path)` reads a template file and caches it.
- `Parse(source)` and `TryParseTemplate(source, out template, out error)` compile a template from a string.
- `Render(template, values)` returns the HTML of one template.
- `RenderPage(page, layout, values)` puts a page inside a layout, where the layout's `{{ content }}` marks the page body.

## Write a page

The sample's layout includes the Modulus stylesheet and scripts in its head with one tag, `{% modulus_head %}`, and marks where the page goes with `{{ content }}`. A page then fills that body:

```liquid title="Templates/invoice.liquid"
<header class="m-page-header">
    <p class="m-muted"><a href="/">Invoices</a></p>
    <h1>Invoice {{ invoice.Number }}</h1>
    <p>{{ invoice.ClientCompany }} · {{ invoice.ClientName }} · {{ invoice.ClientEmail }}</p>
    <p>{{ status | status_badge: tone }}</p>
</header>
<table class="m-liquid-table">
    <thead><tr><th>Item</th><th>Description</th><th class="m-num">Qty</th><th class="m-num">Cost</th><th class="m-num">Total</th></tr></thead>
    <tbody>
    {% for line in invoice.Lines %}
        <tr>
            <td>{{ line.Item }}</td>
            <td>{{ line.Description }}</td>
            <td class="m-num">{{ line.Qty }}</td>
            <td class="m-num">{{ line.Cost | money }}</td>
            <td class="m-num">{{ line.Total | money }}</td>
        </tr>
    {% endfor %}
    </tbody>
</table>
<p>{% icon "file-text" %} Reference <span>{% barcode invoice.Number %}</span></p>
```

The values come from your code. The sample has a small `Page` helper that renders a template with a dictionary of values, and the template reads `invoice` and `status` from it:

```csharp
return Page(liquid, templates, "invoice", "Invoice", new Dictionary<string, object?>
{
    ["invoice"] = invoice,
    ["status"] = StatusText(invoice.Status),
    ["tone"] = StatusTone(invoice.Status),
});
```

## Tags and filters

| Name | Kind | What it draws |
| --- | --- | --- |
| `modulus_head` | Tag | The Modulus stylesheets and scripts, with the right versioned URLs |
| `icon "name"` | Tag | An Ant Design icon by name |
| `barcode "value"` | Tag | A Code 128 barcode, as SVG |
| `status_badge: "success"` | Filter | A status pill; the argument is its tone, such as `success` |
| `money` | Filter | An amount with two decimals |

Every printed value is HTML-escaped, so a name with `<` or a quote in it cannot break the page. Only a `LiquidHtml` value is printed as it is, for the rare case where you already have trusted HTML.

## Model properties

A template can read a model's public properties only if the type is registered with `Register<T>()`. Anything else stays closed, which keeps a template from reaching data you did not mean to expose. The sample registers `Invoice` and `InvoiceLine` and nothing else.

## Documents

The sample also makes files from the same invoice data: its `/documents/invoices/{id}.pdf` and `.docx` routes build a PDF and a Word file with the `Modulus.Documents` package. Use the document package when the output must be a file, and a template page when it must be a web page.

## Limits

- The Liquid package is for rendered pages and documents. The interactive parts of the components (drawers, grids, live tables) need the MVC or Blazor flavour.
- Scaffolding with the CLI does not create Liquid templates yet. Copy the sample's `Templates` folder as a starting point.
