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

## Creating a Release

Releasing is automated via GitHub Actions.
The tag you push decides which workflow runs:

| Tag | Workflow | Result |
| --- | --- | --- |
| `vX.Y.Z` | `stable-release-build.yml` | Stable release, marked "Latest" |
| `vX.Y.Z-<suffix>`, e.g. `v8.0.0-beta.1` or `v8.0.0-rc.2` | `pre-release-build.yml` | Pre-release, **not** marked "Latest" |

Both build the same artifacts via `build-all.yml`: the Windows portable zip, the Linux desktop packages (`.deb`, `.rpm`, `.pkg.tar.zst`), and the VS Code extension.

Items marked with 🫵, require an action by you.

### Stable Release

- 🫵 Push a tag in the format `vX.Y.Z`.
- The artifacts for all platforms are built.
- A release draft is created on the repo's Releases page.
- A workflow is triggered to create a PR updating the version on the website.
- 🫵 Edit the draft and the automatically generated release notes. Remove all release notes lines:
  - starting with `chore:`, `CI:`, `docs:`
  - refactorings, which are not relevant from the user's perspective
  - fixes which addressed only unreleased code
- 🫵 Click on "Publish Release".
- 🫵 Run the `Publish to WinGet` workflow manually, passing the release tag.
  It is manual until a Windows installer ships: see `winget-publish.yml`.
- 🫵 Write an announcement on GitHub Discussions.

### Pre-Release

Same build, but three deliberate differences from the stable flow:

- **Marked as a pre-release, and not marked "Latest".**
  The Releases page keeps pointing normal users at the newest stable version.
- **No generated release notes.**
  The commit-derived list is noise in the body of a test build, so the release text is only the download table from `.github/pre-release-notes-template.md`.
  Add comments to draft manually as needed.
- **No website bump and no WinGet submission.**

The steps:
- 🫵 Push a tag in the format `vX.Y.Z-<suffix>`, for example `v8.0.0-beta.1`.
- The artifacts for all platforms are built.
- A pre-release draft is created on the repo's Releases page.
- 🫵 Edit the draft to say what is being tested and what feedback you want.
- 🫵 Click on "Publish Release".

In most cases, there is no announcement step on Github: a pre-release is published for testers and is not broadcast.
Pre-releases should be posted on Discord, and major pre-releases can also be posted in a Github Discussion.
