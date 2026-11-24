# ERP

The project was generated using
the [Clean.Architecture.Solution.Template](https://github.com/jasontaylordev/CleanArchitecture) version 11.0.0.

## Prerequisites

- The [.NET SDK](https://dotnet.microsoft.com/download) for `net10.0` (`global.json` sets the minimum version)
- [Node.js](https://nodejs.org/) (LTS), if the solution includes an Angular or React client
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) or [Podman](https://podman.io/), if the solution
  uses SQL Server or PostgreSQL. Aspire runs the database in a container, for local development and the functional
  tests.
- The [Aspire CLI](https://aspire.dev/get-started/install-cli/) (recommended)

Alternatively, open the solution in the included [Dev Container](#dev-container), which has everything installed.

## Build

Run `dotnet build` to build the solution.

## Run

To run the application:

Using the [Aspire CLI](https://aspire.dev/get-started/install-cli/) (recommended):

```bash
aspire run
```

Or using the .NET CLI:

```bash
dotnet run --project ./src/AppHost
```

The Aspire dashboard will open automatically, showing the application URLs and logs.

## Code Styles & Formatting

The template includes [EditorConfig](https://editorconfig.org/) support to help maintain consistent coding styles for
multiple developers working on the same project across various editors and IDEs. The **.editorconfig** file defines the
coding styles applicable to this solution.

## Code Scaffolding

The template includes support to scaffold new commands and queries.

Start in the `./src/Application/` folder.

Create a new command:

```
dotnet new ca-usecase --name CreateTodoList --feature-name TodoLists --usecase-type command --return-type int
```

Create a new query:

```
dotnet new ca-usecase -n GetTodos -fn TodoLists -ut query -rt TodosVm
```

If you encounter the error *"No templates or subcommands found matching: 'ca-usecase'."*, install the template and try
again:

```bash
dotnet new install Clean.Architecture.Solution.Template::11.0.0
```

## Test

The solution contains unit, integration, and functional tests.

To run the tests:

```bash
dotnet test
```

## Dev Container

The `.devcontainer` folder configures a [Dev Container](https://containers.dev/) with the .NET SDK, the Aspire CLI,
Docker and (for Angular or React solutions) Node.js. In VS Code, install the Dev Containers extension and select
**Reopen in Container**, or open the repository in GitHub Codespaces.
See [.devcontainer/README.md](.devcontainer/README.md) for details, including the certificate warning you may see in
your browser.

## Deploy

To deploy the solution to Azure, see
the [deployment guide](https://cleanarchitecture.jasontaylor.dev/docs/getting-started/deployment/).

## Architectural Decisions

Key design decisions are documented as [Architecture Decision Records](docs/decisions/).

## Help

To learn more about the template go to the [project website](https://cleanarchitecture.jasontaylor.dev). Here you can
find additional guidance, request new features, report a bug, and discuss the template with other users.
## Third-party notices

- **QuestPDF** (invoice PDFs) is used under the QuestPDF Community licence (`QuestPDF.Settings.License = LicenseType.Community`;
  free for organisations below the revenue threshold of the licence, otherwise a commercial licence is required).
  The default font, Lato, ships inside the QuestPDF package (SIL Open Font Licence 1.1).
- **Noto Sans** Regular and Bold (`src/Infrastructure/Documents/Fonts/NotoSans-*.ttf`) are embedded as the fallback font for
  glyphs Lato lacks (emoji, CJK, Arabic, symbols). Copyright 2022 The Noto Project Authors, licensed under the
  SIL Open Font License 1.1; the licence text is in `src/Infrastructure/Documents/Fonts/OFL.txt`.
  Source: `@expo-google-fonts/noto-sans` 0.4.2 (npm). Characters missing in both fonts render as the font's missing-glyph box
  (`ThrowOnMissingTextGlyphs` is off), so a character can never make PDF generation fail.

## Invoice numbering and default series

- Every invoice number has the same 16-character form for all modes (paper, e-Fatura, e-Arşiv): `{3-char prefix}{yyyy}{9-digit sequence}`,
  e.g. `ABC2026000000001`. The prefix is 3 characters of `A-Z0-9`.
- A prefix is unique per tenant across modes, kind groups and branches (`409 prefix_taken`); only the year differs between
  rows of one series (unique index `IX_document_series_prefix_year` on tenant + prefix + year).
- When a series has no row for the invoice's year, issuing finds or creates the same-prefix row for that year (sequence from 1).
- When a branch has no default **paper** series for a kind group, issuing provisions one lazily (like `DefaultWarehouseService`).
  Prefixes are deterministic: the tenant's default branch gets `SAT` (sales) and `ALS` (purchase); other branches get `S` / `A`
  plus two base-36 characters derived from the branch id, probing the next candidate when taken. e-Fatura / e-Arşiv series are
  never provisioned automatically (their prefixes are registered with GİB), so they still return `409 series_missing`.
