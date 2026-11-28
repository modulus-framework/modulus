---
title: "CLI and the admin starter"
description: "The modulus ui commands were removed in Modulus CLI 4.0.0. Version 3.1.0 still has them."
sidebar_position: 13
---

Modulus CLI 4.0.0 creates APIs only. It no longer has the `modulus ui` commands (pages, CRUD screens, the admin starter, auth pages, charts and themes),
and `modulus app` no longer creates a web project.

You can still add Modulus UI to a web project by hand: see [Getting started](./getting-started.md).

## Using the old commands

CLI 3.1.0 is the last version with the UI commands:

```bash
dotnet tool update -g Synvro.Modulus.Cli --version 3.1.0 --allow-downgrade
```

To keep both versions, install 3.1.0 as a local tool in the web app's folder:

```bash
dotnet new tool-manifest
dotnet tool install Synvro.Modulus.Cli --version 3.1.0
dotnet modulus ui add-component --list
```

CLI 3.1.0 tells you that a newer version exists. Set `MODULUS_NO_UPDATE_CHECK=1` to turn the notice off.
