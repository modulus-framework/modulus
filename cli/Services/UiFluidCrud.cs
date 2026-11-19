using System.Text;

namespace Modulus.Cli.Services;

/// <summary>
/// The Liquid pages of a Fluid CRUD: a list and a form, written from the entity's fields. They live under <c>Templates/Shared/Crud</c>,
/// which the Fluid host never serves as pages: the generated endpoints render them, so a visitor reaches a page only through its
/// endpoint (and its sign-in, when the host has one).
/// </summary>
internal static class UiFluidCrud
{
    /// <summary>The list page, relative to <c>Templates/</c>.</summary>
    public static string ListPath(string route) => $"Shared/Crud/{route}/index.liquid";

    /// <summary>The create and edit page, relative to <c>Templates/</c>.</summary>
    public static string FormPath(string route) => $"Shared/Crud/{route}/form.liquid";

    /// <summary>The list: one column per field, the edit and delete actions per row, and paging.</summary>
    public static string ListPage(string entity, string plural, string route, IReadOnlyList<CrudField> fields)
    {
        var headers = new StringBuilder();
        var cells = new StringBuilder();
        foreach (var field in fields)
        {
            headers.Append($"<th class=\"ant-table-cell\">{field.Label}</th>");
            cells.Append($"<td class=\"ant-table-cell\">{{{{ row.{field.Camel} }}}}</td>");
        }

        return ListTemplate
            .Replace("__ENTITY__", entity)
            .Replace("__PLURAL__", plural)
            .Replace("__ROUTE__", route)
            .Replace("__LOWER__", entity.ToLowerInvariant())
            .Replace("__HEADERS__", headers.ToString())
            .Replace("__CELLS__", cells.ToString())
            .Replace("__COLUMNS__", (fields.Count + 1).ToString());
    }

    /// <summary>
    /// Returns <paramref name="program"/> (the Fluid host's Program.cs) with what a CRUD needs: the usings of its services and endpoints,
    /// <c>AddAntiforgery</c> and the client registration before the app is built, and the endpoints mapped before the page fallback.
    /// Each line is added only when it is missing, so a second run changes nothing.
    /// </summary>
    public static string EnsureProgram(string program, string uiNamespace, string plural)
    {
        var nl = program.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var result = program;

        var usings = new[] { $"using {uiNamespace}.Services;", $"using {uiNamespace}.Endpoints;" }
            .Where(line => !result.Contains(line, StringComparison.Ordinal))
            .ToList();
        if (usings.Count > 0)
            result = string.Join(nl, usings) + nl + result;

        const string build = "var app = builder.Build();";
        var buildAt = result.IndexOf(build, StringComparison.Ordinal);
        if (buildAt < 0)
            throw new InvalidOperationException("Program.cs has no `var app = builder.Build();`: add the CRUD's services and endpoints by hand.");
        var services = new List<string>();
        if (!result.Contains("AddAntiforgery(", StringComparison.Ordinal))
            services.Add("builder.Services.AddAntiforgery();");
        if (!result.Contains("AddModuleApiClients(", StringComparison.Ordinal))
            services.Add("builder.Services.AddModuleApiClients();");
        if (services.Count > 0)
        {
            var lineStart = result.LastIndexOf('\n', Math.Max(result.IndexOf(build, StringComparison.Ordinal) - 1, 0)) + 1;
            result = result.Insert(lineStart, string.Join(nl, services) + nl + nl);
        }

        var mapping = $"Map{plural}Crud(";
        if (!result.Contains(mapping, StringComparison.Ordinal))
        {
            if (!result.Contains("var templates = ", StringComparison.Ordinal))
                throw new InvalidOperationException("Program.cs has no `var templates = ...` (the Fluid host's page folder): map the CRUD endpoints by hand.");
            const string fallback = "app.MapFallback(";
            var fallbackAt = result.IndexOf(fallback, StringComparison.Ordinal);
            if (fallbackAt < 0)
                throw new InvalidOperationException("Program.cs has no `app.MapFallback(`: map the CRUD endpoints by hand.");
            var lineStart = result.LastIndexOf('\n', Math.Max(fallbackAt - 1, 0)) + 1;
            result = result.Insert(lineStart, $"app.{mapping}templates);" + nl + nl);
        }
        return result;
    }

    /// <summary>The create and edit form: one input per field, the errors above it, and the antiforgery token.</summary>
    public static string FormPage(string entity, string plural, string route, IReadOnlyList<CrudField> fields)
    {
        var inputs = new StringBuilder();
        foreach (var field in fields)
            inputs.Append(Input(field)).Append('\n');

        return FormTemplate
            .Replace("__ENTITY__", entity)
            .Replace("__PLURAL__", plural)
            .Replace("__ROUTE__", route)
            .Replace("__LOWER__", entity.ToLowerInvariant())
            .Replace("__INPUTS__", inputs.ToString());
    }

    /// <summary>
    /// A form partial for an entity's fields, with no page around it: the field rows only, reading the values from a
    /// <c>values</c> hash the page passes in. Used by <c>ui create-form-from-entity</c> on a Fluid host.
    /// </summary>
    public static string FormPartial(string entity, IReadOnlyList<CrudField> fields)
    {
        var inputs = new StringBuilder();
        foreach (var field in fields)
            inputs.Append(Input(field)).Append('\n');

        return $"{{% comment %}} {entity} form fields, generated by modulus ui create-form-from-entity. Pass a values hash to fill them. {{% endcomment %}}\n"
            + inputs;
    }

    // One form control. A checkbox sends "true" when ticked; a text area has no type; every other kind is an <input> of its type.
    private static string Input(CrudField field)
    {
        var id = $"f-{field.Camel}";
        var required = field.Required ? " required" : "";
        var label = $"<div class=\"ant-form-item-label\"><label for=\"{id}\">{field.Label}</label></div>";

        string control = field.Kind switch
        {
            "checkbox" => $"<input type=\"checkbox\" id=\"{id}\" name=\"{field.Camel}\" value=\"true\"{{% if values.{field.Camel} == 'true' %}} checked{{% endif %}} />",
            "textarea" => $"<textarea id=\"{id}\" name=\"{field.Camel}\" rows=\"3\" class=\"ant-input\"{required}>{{{{ values.{field.Camel} }}}}</textarea>",
            _ => $"<input id=\"{id}\" type=\"{field.InputType}\" name=\"{field.Camel}\" value=\"{{{{ values.{field.Camel} }}}}\" class=\"ant-input\"{required}{(field.Kind == "number" ? " step=\"any\"" : "")} />",
        };
        return $"<div class=\"ant-form-item\">{label}<div class=\"ant-form-item-control\">{control}</div></div>";
    }

    private const string ListTemplate = """
        {% comment %} __ENTITY__ list, generated by modulus ui create-crud: paging, a message after a save or delete, edit and delete per row. {% endcomment %}

        <div class="m-page-header">
            <div class="m-page-header-text"><div class="m-pretitle">__PLURAL__</div><h1 class="m-page-title">__PLURAL__</h1></div>
            <div class="m-page-actions"><a class="ant-btn ant-btn-primary" href="/__ROUTE__/new"><span>New __LOWER__</span></a></div>
        </div>

        {% if message and message != blank %}
        <div class="ant-alert ant-alert-success ant-alert-with-description m-mb-16" role="status">
            <div class="ant-alert-content"><div class="ant-alert-message">{{ message }}</div></div>
        </div>
        {% endif %}

        <div class="m-card">
            <div class="m-card-body m-card-body-flush">
                {% if rows.size == 0 %}
                <div class="m-empty">
                    <h3 class="m-empty-title">No __PLURAL__ yet</h3>
                    <p class="m-empty-subtitle">Create the first one.</p>
                    <a class="ant-btn ant-btn-primary" href="/__ROUTE__/new"><span>New __LOWER__</span></a>
                </div>
                {% else %}
                <div class="ant-table-wrapper m-table-flush">
                    <div class="ant-table"><div class="ant-table-container"><div class="ant-table-content">
                        <table style="table-layout:auto">
                            <thead class="ant-table-thead"><tr>__HEADERS__<th class="ant-table-cell"></th></tr></thead>
                            <tbody class="ant-table-tbody">
                                {% for row in rows %}
                                <tr class="ant-table-row">
                                    __CELLS__
                                    <td class="ant-table-cell">
                                        <form method="post" action="/__ROUTE__/{{ row.id }}/delete" class="m-flex">
                                            <input type="hidden" name="__RequestVerificationToken" value="{{ antiforgery_token }}" />
                                            <a class="ant-btn ant-btn-text ant-btn-sm" href="/__ROUTE__/{{ row.id }}/edit"><span>Edit</span></a>
                                            <button type="submit" class="ant-btn ant-btn-text ant-btn-sm ant-btn-dangerous"><span>Delete</span></button>
                                        </form>
                                    </td>
                                </tr>
                                {% endfor %}
                            </tbody>
                        </table>
                    </div></div></div>
                </div>
                {% endif %}
            </div>
            {% if has_prev or has_next %}
            <div class="m-card-footer m-flex m-between">
                <span class="m-muted">Page {{ page }}</span>
                <span class="m-flex">
                    {% if has_prev %}<a class="ant-btn ant-btn-default ant-btn-sm" href="/__ROUTE__?p={{ prev_page }}"><span>Previous</span></a>{% endif %}
                    {% if has_next %}<a class="ant-btn ant-btn-default ant-btn-sm" href="/__ROUTE__?p={{ next_page }}"><span>Next</span></a>{% endif %}
                </span>
            </div>
            {% endif %}
        </div>

        """;

    private const string FormTemplate = """
        {% comment %} __ENTITY__ form, generated by modulus ui create-crud: create and edit. Errors are listed above the fields. {% endcomment %}

        <div class="m-page-header">
            <div class="m-page-header-text"><div class="m-pretitle">__PLURAL__</div><h1 class="m-page-title">{{ title }}</h1></div>
            <div class="m-page-actions"><a class="ant-btn" href="/__ROUTE__"><span>Back to __PLURAL__</span></a></div>
        </div>

        {% if errors.size > 0 %}
        <div class="ant-alert ant-alert-error ant-alert-with-description m-mb-16" role="alert">
            <div class="ant-alert-content">
                <div class="ant-alert-message">Check the form</div>
                <ul class="ant-alert-description">
                    {% for error in errors %}<li>{{ error }}</li>{% endfor %}
                </ul>
            </div>
        </div>
        {% endif %}

        <div class="m-card">
            <div class="m-card-body">
                <form method="post" action="{{ action }}" class="ant-form ant-form-vertical" novalidate>
                    <input type="hidden" name="__RequestVerificationToken" value="{{ antiforgery_token }}" />
                    <div class="m-form-grid">
        __INPUTS__
                    </div>
                    <div class="m-form-actions m-mt-16">
                        <button type="submit" class="ant-btn ant-btn-primary"><span>Save __LOWER__</span></button>
                    </div>
                </form>
            </div>
        </div>

        """;
}
