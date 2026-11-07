---
title: "Forms and business fields"
description: "Validated forms, the business fields for ERP-style screens (money, phone, lookup, and more), the schema-driven form and the rules the server shares with the browser."
sidebar_position: 8
---

Forms in Modulus are ordinary forms. In MVC they post normally, and they still work with script turned off. In Blazor they are components with two-way binding. Both use the same field types, and both check the same rules on the server, so the server accepts exactly what the browser accepts.

## Validated forms

- In Blazor, the form is bound to a model, and `DataAnnotations` or your own validation shows the message next to the field. `DrawerForm` and `ModalForm` submit asynchronously and close only when `OnFinish` returns success.
- In MVC, `app-form-item asp-for="Name"` shows the label, the input and the validation message for a model property. The post goes to the controller, and a failed check returns the form with the messages.

The tutorials build a validated customer form in both flavours. See [Step 9 of the Blazor tutorial](./tutorial-blazor.md#step-9-data-the-model-the-form-input-and-a-store) and the form partial in [Build an app with MVC](./tutorial-mvc.md).

## Business fields

These are for ERP-style forms. Each posts a plain, culture-invariant value, so the server reads one format regardless of the language of the page.

| Field | Blazor | MVC tag helper | Posts |
| --- | --- | --- | --- |
| Money | `AppMoneyInput` | `app-money-input` | A grouped amount with a currency picker; the currency's decimals are enforced |
| Quantity | `AppQuantityInput` | `app-quantity-input` | A number with a unit of measure and that unit's precision |
| Duration | `AppDurationInput` | `app-duration-input` | Minutes. Accepts `1:30`, `1.5` or `90m` |
| Number range | `AppNumberRange` | `app-number-range` | Two numbers, from and to |
| Phone | `AppPhoneInput` | `app-phone-input` | A country picker and a national number, shown as `+1 202 555-0111` |
| Masked input | `AppMaskedInput` | `app-masked-input` | A value that follows a pattern, such as `99-9999999` |
| One-time code | `AppOtpInput` | `app-otp-input` | One box per character; paste and SMS autofill spread over the boxes |
| Tags | `AppTagInput` | `app-tag-input` | A list of free-form tags; Enter or a separator adds one, Backspace removes the last. Posts one value per tag, so a `List<string>` binds back |
| Lookup | `AppLookup` | `app-lookup` | A many-to-one field with type-ahead and a "Search more" dialog |
| Status bar | `AppStatusBar` | `app-status-bar` | Workflow stages, with the finished ones folded |
| Signature | `AppSignaturePad` | `app-signature-pad` | A PNG data URL |
| Rich text | `AppRichText` | `app-rich-text` | HTML from a small editor with a table toolbar |

Two notes on the rules:

- **Phone numbers** are checked for shape: a `+`, a calling code, and 8 to 15 digits. They are not checked against each country's numbering plan.
- **Rich text** (`AppRichText` and the editor in [Plugins](./plugins.md)) must be sanitized on the server with `RichText.Sanitize` before you store or show it.

## Lookups

`AppLookup` has two sources.

- **Local items.** Pass the list in code; the field filters it as the person types.
- **A server search.** The field calls `GET` on a search address with `q` for the text and `max` for the number of results, and the server answers with a JSON array of `value`, `label`, `description` and `fields`. The address is set with the field's search href.

## Creatable selects

`AppCreatableSelect` (MVC: `app-creatable-select`) is a select with an "Add item" row at the bottom of the list. The person types a name, picks a tag colour, and presses Add; the new option joins the list and is selected.

- The server checks the new option with `GridSchema.AddOption`: it must be unique, trimmed, and at most 50 characters.
- `OnAdd` (Blazor) or `add-href` (MVC) saves the option before it is added, so it survives a reload.
- For real records, `OnAddNew` or `add-modal` replace the inline row with an "Add new" button that opens a create form instead. The form's save answers `Response.HxOptionAdded(list, option)`, and the new record is selected.

## Schema-driven forms

When a form's fields come from data, describe them as a `FormSchema` and draw it with `AppSchemaForm` (MVC: `app-schema-form`).

- A `FormSchema` is read from JSON with `FormSchema.Parse`, or built in code.
- The field types are text, multi-line text, number, money, date, choice and switch.
- The server runs the same rules as the browser with `Validate` and stores the result of `Normalize`. Use both when you save a schema-driven form, so the stored value is the checked one.

The rules live in `Modulus.Ui.Abstractions.Forms`: `NumberText`, `DurationText`, `Currency`, `UnitOfMeasure`, `InputMask`, `PhoneNumber`, `LookupSearch`, `RichText`, `SignatureImage` and `FormSchema`. Call them from your own code when a field does not come from the schema.

## Editing grids

For many rows at once, use the editable grid, which checks each cell as it is edited. See [Pro components](./pro.md#editable-grid).
