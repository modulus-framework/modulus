---
title: "Plugins"
description: "The rich-text editor, maps, PDF viewer, spreadsheet, uploads, image cropper, code editor, media player and sortable lists, each as a Blazor component and an MVC tag helper."
sidebar_position: 9
---

Plugins are components for jobs a plain form cannot do: a rich-text editor, a map, a PDF viewer, a spreadsheet, uploads. Each one is a Blazor component and an MVC tag helper, over one shared renderer, so a page looks and posts the same way in both.

Third-party libraries load only on the pages that use them. `modulus-plugins.js` fetches a library the first time a page needs it, so a page with no editor does not download one.

| Plugin | Blazor | MVC tag helper | What it does |
| --- | --- | --- | --- |
| Rich-text editor | `AppEditor` | `app-editor` | WYSIWYG editor on Tiptap. The HTML is cleaned with `RichText.Sanitize(images: true)`. |
| Map | `AppMap` | `app-map` | Tile map on MapLibre GL JS, with markers and popups |
| Vector map | `AppVectorMap` | `app-vector-map` | Choropleth map on jsVectorMap, with hover values and a legend |
| PDF viewer | `AppPdf` | `app-pdf` | pdf.js viewer: one page at a time, zoom, and a download link |
| Spreadsheet | `AppSpreadsheet` | `app-spreadsheet` | Excel-style multi-sheet grid with formulas, on Univer. Posts its sheets as JSON. |
| Uploader | `AppUploader` | `app-uploader` | A drop area on Uppy. Each file uploads as soon as it is dropped, with previews, progress and limits. |
| Image cropper | `AppImageCropper` | `app-image-cropper` | Cropper.js, with aspect-ratio buttons. The crop goes to a form field as JSON. |
| Code editor | `AppCodeEditor` | `app-code-editor` | CodeMirror 6, with highlighting, folding and search for JavaScript, TypeScript, JSON, CSS and HTML |
| Media player | `AppMediaPlayer` | `app-media-player` | Video and audio, with custom controls, a poster and captions |
| Sortable list | `AppSortable` | `app-sortable` | Reorder a list, drag between lists, with handles and keyboard moves. Posts the new order. |
| Workflow designer | `AppFlowEditor` | `app-flow-editor` | An n8n style canvas for AI and automation flows: drag nodes, draw connections, undo, show run status. See [Workflow designer](#workflow-designer). |
| Lightbox | `AppLightbox` | `app-lightbox` | Full-size image viewer |
| Barcode | `AppBarcode` | `app-barcode` | Code 128 and EAN-13, drawn as SVG |

## Workflow designer

A canvas of nodes you drag and wire together, for AI agents, RAG pipelines, approval flows and ETL jobs. The workflow is a `FlowGraph` (nodes, connections, each node's settings), which saves as JSON and runs on the built-in `FlowRunner`.

```razor
<AppFlowEditor @bind-Graph="_graph" OnOpen="id => _open = _graph.Node(id)" Height="600" />
```

```html
<form method="post" asp-antiforgery="true" action="/flows/save">
  <app-flow-editor asp-for="Workflow" height="600" />
  <button type="submit">Save</button>
</form>
```

In MVC and Razor Pages the graph posts as JSON in one field: read it with `FlowGraph.TryParse`.

### Running a workflow

```csharp
var runner = new FlowRunner()
    .OnEach("ai.chat", async (ctx, item, ct) => item.With("reply", await model.CompleteAsync(ctx.Setting("prompt", item), ct)))
    .OnEach("action.email", async (ctx, item, ct) => { await mail.SendAsync(ctx.Setting("to", item), ctx.Setting("subject", item), ct); return item; });

var result = await runner.RunAsync(graph, [FlowItem.Of(("message", text))], new FlowRunOptions { OnStep = step => Task.CompletedTask });
var statuses = result.ApplyTo(graph); // hand this to the editor to show each node's status
```

The built-in nodes (triggers, if, switch, merge, wait, set fields, filter) need no handler. Expressions use `{{path}}` and conditions such as `customer.tier == "gold"`; see the Workflow designer section of the documentation. The library has no model, mail or HTTP clients: your app supplies them, and loop nodes are not run. The palette comes from a `FlowCatalog`; `FlowCatalog.Default` has triggers (manual, webhook, schedule, chat), AI (chat model, agent, classifier, extractor, embeddings, vector search, human approval), logic (if, switch, merge, loop, wait), data (HTTP, set fields, code, database, filter) and actions (email, chat message, respond). Pass your own catalog for your node kinds.

| You can | How |
| --- | --- |
| Add a node | click it in the palette, drag it onto the canvas, or search and press Enter |
| Connect | drag from an output dot to a node; let go on empty canvas and the next node you pick is wired to it |
| Move, select, delete | drag (snaps to a grid), Shift-click for several, Delete |
| Undo and redo | Ctrl+Z, Ctrl+Shift+Z or the toolbar |
| Pan and zoom | drag the background, Ctrl/Cmd and the wheel, pinch, or the toolbar; the wheel pans once the editor has focus |
| Use the keyboard | Tab to a node, Enter opens it, arrows move it, **C** then Enter on another node connects them, **U** disconnects, **D** disables, Ctrl+D duplicates |
| Open a node's settings | double-click or Enter raises `m-flow-open` (`OnOpen` in Blazor); `UpdateNodeAsync` or `modulusFlow.updateNode` writes the settings back without redrawing |
| Show a run | `SetStatusAsync` or `modulusFlow.setStatus(editor, id, 'running' / 'success' / 'error' / 'waiting', note)` draws a ring and an icon on each node |
| Check it | `graph.Validate(catalog)` finds unknown node kinds, broken connections, a missing trigger, nodes no trigger reaches and loops; `graph.ExecutionOrder(catalog)` gives a valid run order |

`modulus-flow.js` (about 11 KB gzipped) and its stylesheet load only on a page that has a designer. The designer draws and edits workflows; `FlowRunner` runs them. See the "Workflow designer" sample.

## Notes on use

- **Upload.** `app-upload` also takes `action` (upload with per-file progress), `max-size`, `max-count` and `list-type="picture"`.
- **Forms.** Plugins that edit content post a hidden form field. The editor and the code editor post their text; the cropper posts JSON; the spreadsheet posts its sheets as JSON. Read the value on the server like any other form field.
- **Sanitize HTML.** The rich-text editor and the `AppRichText` field both produce HTML. Clean it with `RichText.Sanitize` before you store or show it.
- **Print.** The spreadsheet prints from its ribbon, with Ctrl+P, or from any button with `data-m-print="{id}"`. The document editor works the same way.
- **Themes.** The spreadsheet follows the light and dark theme.

## Maps need a tile server for real use

The map plugins load tiles from `tile.openstreetmap.org`. Its usage policy does not cover heavy production traffic, so they need network access and, for real use, your own tile server.

## Liquid templates

A Liquid template can draw the same icons, barcodes and status pills without any plugin. See [Liquid templates](./fluid.md).
