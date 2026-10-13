---
sidebar_position: 9
---

# Known Limitations

These are the limits of the UI as it ships today. Plan around them before you build on the framework.

## Look and feel

- **The base is Ant Design Blazor 1.6, which ships Ant Design 4 styling.** Modulus adds a refresh layer (6 px radius, softer shadows, Ant Design 5 menu and table styling). The result looks like modern Ant Design Pro, but it is not pixel-identical to Ant Design 5.
- **Dark-mode brand colour is generated.** The stylesheet `antd-dark-brand.css` repeats the Ant Design declarations that use the blue, with your brand's dark palette. It is generated from the pinned Ant Design version, and a drift check fails when it no longer matches.
- **Native pickers in MVC.** `app-date-picker` and `app-range-picker` use the browser's date and time inputs inside Ant Design's chrome, so the calendar popup follows the browser, not Ant Design.
- **Some components are Modulus components**, not Ant Design Blazor ones: the colour picker, float buttons, QR code and tour. They draw the Ant Design 5 markup, styled by `modulus.css`, and may differ in detail from Ant Design 5.

## MVC and Razor Pages

- **Interactive widgets need Alpine.js.** Cascader, tree select, transfer, tree, calendar, carousel, anchor, mentions, splitter, colour picker and tour run on small Alpine components. Their form values post through hidden inputs, so the form still submits without script, but the widget itself does not work without it.
- **Icons reuse Ant Design Blazor's icon store through reflection**, because the store is internal. A unit test fails if an Ant Design upgrade moves it.

## Business fields and plugins

- **Rich text** (`app-rich-text`) covers formatting, lists, links and tables only. Use `app-editor` for images. The HTML of both must still be sanitized on the server with `RichText.Sanitize`.
- **Phone numbers** are checked for shape (a `+`, a calling code, 8 to 15 digits), not against each country's numbering plan.
- **Tile maps** load their tiles from `tile.openstreetmap.org`, whose usage policy does not cover heavy production traffic. They need network access and, for real use, your own tile server.
- **Plugin libraries are third-party.** The editor, maps, vector map, PDF viewer, uploader, cropper and code editor wrap Tiptap, MapLibre, jsVectorMap, pdf.js, Uppy, Cropper.js and CodeMirror. They load from `Synvro.Modulus.Ui.Assets` on first use.

## Localization

Ant Design's built-in strings, dates and numbers follow the request culture. The sample pages stay in English, so a translated app needs its own text for its pages.

## Authorization and sign-in

- **Menus** use `RequiredPolicy`. Pages and endpoints need their own authorization attributes.
- **The samples sign in against in-memory sample users.** There is no user store, registration or password reset behind the sample auth pages. The generated apps use the identity module, which provides these, but the in-memory stores must be replaced before production (see [Authentication](./authentication)).
- **External providers** sign users in with the OpenID Connect code flow. The sign-in was checked end to end in development against a local issuer, but not against Keycloak, Authentik or the other providers, and it is not in the automated test suite. Test the provider you use end to end before production.

## Samples and tests

- **The browser gates** (`tests/browser`) run against the samples on ports 5100 to 5103, in CI. Run them locally with the samples started.
- **Unit tests that pin named cultures** (`en-US`, `de-DE`) need the ICU globalization library, so they do not run with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.
