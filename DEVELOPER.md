# Developer Documentation for CalcpadCE

## Adding a New Example `.cpd` to the Documentation

Put new `.cpd` examples in a suitable directory under `Examples/`.
If you want to add a new category, add a directory under `Examples/Engineering/` or `Examples/Structural/` with an `_intro.md` file, explaining what the new category is about.

You must generate an HTML stub of the example used for automatic testing in continuous integration by running `python .github\scripts\compare_renderings.py --write`.
See [Automatic Rendered Output Validation](#ci-automatic-rendered-output-validation).
If the example produces random output (which should be avoided), add it to the denylist in `.github\scripts\compare_renderings.py`.
Then, it won't be used for quality assurance.

Add an entry to `docs/examples.yml` to let the example show up in the documentation.
The entry's name must match the `.cpd` filename without the extension.
The name will also be used as the title of the example.

Describe in a few words what the example does, and add this text as an HTML comment to the first line of the `.cpd` file.
The text will be displayed above the example and the HTML comment will be stripped prior to rendering.
Latex is possible by enclosing it with `$...$`.
Please note the `'` at the beginning:

`'<!-- ... -->`

The output is automatically rendered and displayed side-by-side with the CalcpadCE code.
See [Building the Documentation](#building-the-documentation) how to generate a preview locally.

## CI: Automatic Rendered Output Validation

When filing a PR, the GitHub Actions check if any code change leads to a change in the rendered CalcpadCE output.
This is done by rendering the CalcpadCE example and test worksheets under `Examples/` and `Test/` and comparing them to the HTML stubs, saved as siblings alongside the `.cpd` files.

The CI build will fail if a difference is detected and the HTML stub isn't updated accordingly.
Then, a unified diff is printed in the CI log showing the change in question.

If the change is intentional, the HTML stub needs to be regenerated to reflect the updated output.
Run this script with the `--write` argument.
It will regenerate all stubs which need an update.

```bash
pip install beautifulsoup4
python .github\scripts\compare_renderings.py --write
```

Now, when filing the PR, the diff will not only show the code changes but also any resulting changes to the rendered output.

### Run the CI Check Locally

Before filing the PR, you can run the script locally without regenerating anything to check if there are any unexpected changes to the rendered output.
Any change will be printed to the console.

```bash
python .github\scripts\compare_renderings.py
```

### Implementation Details

The HTML stubs are prettified before being stored and compared to generate human-readable diffs.
This also prevents false alarms if only whitespace changes occur, or for example if just the order of HTML attributes change.
Furthermore, any decimals in the rendered output are compared with tolerance, because the last digits of floating-point numbers vary when running on different platforms (AVX2 CPU extensions of different architectures/CPU manufacturers behave slightly different at the edge of precision).

Also, any values are only re-written if they exceed the tolerance to not clutter the PR diff with changes in the last digits of some decimals.
If you want to override this behavior (for whatever reason) and overwrite all decimals, run the script with `--write --force`.

## Building the Documentation

.NET SDK 10, Python and NPM need to be installed.

Install the following Python/NPM dependencies and build Calcpad.Cli:

```pwsh
pip install mkdocs
npm install --no-save --package-lock=false --prefix . mathjax@4 @mathjax/mathjax-newcm-font@4
cp -r node_modules/mathjax/* docs/javascripts/mathjax/
md -f docs/javascripts/mathjax/output/fonts/mathjax-newcm
cp -r node_modules/@mathjax/mathjax-newcm-font/* docs/javascripts/mathjax/output/fonts/mathjax-newcm/
dotnet build Calcpad.Cli
```

The example `.cpd` files are rendered via a hook when the documentation is built.
The hook calls CalcpadCE CLI for each example and spits out an HTML stub (without headers/body).

Generation can be started by invoking this command.
A local webserver will spawn to serve the rendered documentation:

`mkdocs serve`

## Customizing the `#UI` Datagrid

A `#UI` datagrid is a [jspreadsheet CE](https://github.com/jspreadsheet/ce) 5.0.4 widget
hydrated in the preview by `UiPreviewScript`. The `"style"` class of the `#UI` directive lands on
the outer `.calcpad-ui-datagrid` container only — everything inside the grid is the library's
markup, so restyling it means writing CSS against the library's own classes.

That CSS belongs in **`Calcpad.Web/backend/UiAssets/calcpad-datagrid.css`**.

### Why that file and not `template.html`

`BundledUiAssets.GetHeadMarkup()` inlines the grid assets into `<head>` immediately before
`</head>` (see the `enableUi` block in `Calcpad.Web/backend/Services/CalcpadService.cs`), while
the template's own `<style>` block sits at the *top* of `<head>`. Rules in `template.html`
therefore lose to jspreadsheet's at equal specificity — which is why the preview CSS in there
carries so many `!important`s.

`calcpad-datagrid.css` is inlined **last**, after both `jsuites.min.css` and
`jspreadsheet.min.css`, so plain rules win on source order alone. It is also optional: if it
ever goes missing from a deployment the grids still render. That is unlike the four library
files, whose absence disables datagrids entirely (`BundledUiAssets` logs a warning and returns
no head markup rather than emitting a grid container with no library behind it).

### Selectors

jspreadsheet CE 5.0.4 exposes one custom property, `--jss-border-color`; everything else is
plain rules.

| Part | Selector |
|------|----------|
| Whole widget | `.jss_container`, `.jss_spreadsheet` |
| Scroll box | `.jss_content` |
| Cells | `.jss_worksheet > tbody > tr > td` |
| Column headers | `.jss_worksheet > thead > tr > td` |
| Row headers | `.jss_worksheet > tbody > tr > td:first-child` |
| Corner / select-all | `.jss_corner`, `.jss_selectall` |
| Context menu | `.jcontextmenu`, `.jcontextmenu div` |
| CE footer link | `.jss_about` |

### Things to know when writing rules

- **Dark mode** is `class="dark-theme"` on the `<body>` of the preview document, so a themed
  rule is scoped `.dark-theme .jss_worksheet > tbody > tr > td { … }`. The file already carries a
  full dark palette — `#1e1e1e` page, `#252526` surfaces, `#3e3e42` borders, `#d4d4d4` text,
  `#4fc1ff` selection — matching the template's. jspreadsheet ships light only, so every colour
  it sets has to be restated; extend those rules rather than starting a second palette.
- **Column widths are inline.** The script sets `width: 80` per column, plus `tableWidth` and
  `tableHeight`, on the worksheet definition in `UiPreviewScript.cs`. Those become inline
  `<col>`/element styles, which CSS cannot override without `!important` — change them in
  `UiPreviewScript.cs` instead.
- **No HTML tags in the file, not even inside a comment.** The whole stylesheet is inlined into
  `<head>` ahead of the real tags, and `extractBodyHtml` looks for the first `<head>`/`<body>`
  it finds.

### Rebuilding

The head markup is cached per server process, so a changed stylesheet needs a restart.

```sh
dotnet build Calcpad.Web/backend/Calcpad.Server.csproj
```

`UiAssets/**` is both `Content` (copied to the output directory) and an `EmbeddedResource`, and
`BundledUiAssets.Load` prefers the filesystem copy in `<output>/UiAssets/`. For a quick
iteration you can edit that copy directly and restart the server — no rebuild — but the edit
must go back into `Calcpad.Web/backend/UiAssets/` to survive.

Then re-stage the sidecar for whichever host you are testing:

```sh
cd Calcpad.Web/frontend/calcpad-desktop && ./stage-sidecar.sh   # desktop app
cd Calcpad.Web/frontend/vscode-calcpad && npm run sync-server   # VS Code extension
```

## Creating a Release

Releasing is automated via GitHub Actions.
The workflow is as follows:

Items marked with 🫵, require an action by you.

- 🫵 Push a tag in the format `vX.Y.Z`.
- The artifacts for all platforms are built.
- A release draft is created on the repo's Releases page.
- A workflow is triggered to create a PR updating the version on the website.
- 🫵 Edit the draft and the automatically generated release notes. Remove all release notes lines:
  - starting with `chore:`, `CI:`, `docs:`
  - refactorings, which are not relevant from the user's perspective
  - fixes which addressed only unreleased code
- 🫵 Click on "Publish Release".
- A workflow is triggered to create a PR in the winget repo updating the version.
- 🫵 Write an announcement on GitHub Discussions.
