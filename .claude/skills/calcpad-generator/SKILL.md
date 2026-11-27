---
name: calcpad-generator
description: Write, edit and review CalcpadCE worksheets (.cpd files) in the CalcpadCE repo. Use whenever creating, changing or checking Calcpad code. Covers syntax, units, macros, report conventions, and linting and rendering with the repo's tools.
---

You are an expert CalcpadCE developer. You write worksheets a senior engineer who does not know Calcpad can review: unit-aware, hierarchically structured, every value cited, every check explicit.

## Language reference

Read the page rather than guessing a function signature or directive: `docs/<topic>.md` at the repo root, or `https://imartincei.github.io/CalcpadCE/<topic>.html` outside the repo.

Start with `quick-reference` (the whole language on one page).
Go to a topical page for depth: `writing-math`, `operators`, `functions`, `units`, `variables`, `programming` (control flow, macros, includes), `vectors`, `matrices`, `iterative-procedures`, `numerical-methods`, `plotting`, `reporting`, `results`, `working-with-files`, `new-parsing-modes`, `new-visibility-directives`, `new-includes`, `new-keyword-arguments`, `new-metadata-comments`, `new-ui-mode`, `new-linter`, `migration-guide`.

## Non-negotiables

1. Units on every dimensioned quantity; `|` sets output units, and only a unit may follow it: `L|mm`, never `L|mm*2`.
2. `;` separates function and macro arguments, never `,`.
3. `#hide` / `#end hide` around hidden content.
4. Headings for structure, as HTML in `'` comments: `'<h1>Title</h1>`, then `'<h2>Section</h2>` and below (`#`, `##` inside a `#markdown` block).
5. No `e` notation: write `5*10^-6`, not `5e-6`, in code and in `#UI` values.
6. `^` for unit powers: `m^2`, `kg^-1`, not `m²`.
7. Direct values (`L = 10m`) unless the user asks for a fill-in form; only then mark inputs with `#UI` (below).

## Text, headings and markup

Everything outside quotes is math.

- **One line of text:** `'text` is a paragraph and `'<h2>Section</h2>` a heading. The closing quote is optional at the end of a line, and `''` escapes a quote. A `"` comment also works (`"Title` renders as `<h1>`), but use `'` for all text.
- **Text beside math:** a comment labels an expression (`'Span -'L = 6m`), and separates two expressions on one line (`'a = 'a = 3m', b = 'b = 2*a`).
- **Several lines of text:** use `#markdown` ... `#end markdown` or `#html` ... `#end html`, not a stack of `'` lines. Inside a block, lines are text and `#if`, `#hide` and the like don't run; open `#cpd` ... `#end cpd` inside it to calculate.
- **Values inside markup** (SVG, HTML attributes): a block can't do this. Use `'` lines with the values between quotes, between `#val` and `#equ`, so `w` inserts as `120`, not `w = 120`: `'<rect width="'w'"/>`.

## Input forms (`#UI`)

`#UI` marks an input; in Input mode it becomes a control and the worksheet recalculates when the user changes it.
The right-hand side must be a plain value, and an inline comment labels the control:

```
#UI 'Span, 'L = 6m
#UI 'Load, 'w = 25kN/m
M = w*L^2/8
```

Read `new-ui-mode` for drop-downs, grids, `#pre` / `#post` and the other options.

## Common mistakes

The linter can't catch the first few; the rest fail silently or with a confusing error.

- **Semicolons in macro parameter text.** The text splits into extra arguments; the most common silent breakage. Write "and".
- **Expressions as macro arguments.** Calcpad doesn't reliably evaluate inline math there. Assign first: `V_n = n_b*R_n` then `check$(V_n; V_a)`, never `check$(n_b*R_n; V_a)`.
- **`$` in quoted comment text.** Calcpad reads it as a macro marker. Write the bare macro name. A macro call after a label (`'Label -'x = m$(a)`) is fine.
- **Assuming a module's `#local` includes propagate.** They do not; a file calling a macro must include every module the macro needs.
- **Non-ASCII macro names or `#def` parameters.** ASCII only. Regular variables may use Greek, `°`, `ø`/`Ø` and subscripts.
- **`=` vs comparison.** `=` assigns; compare with `≡`.
- **Interval bounds without units.** In `$Plot`, `$Root`, `$Area` and the other `$` methods, bounds must match the variable's units: `$Plot{f(x) @ x = 0m : L}`.
- **`|` inside `$Plot`.** There it separates parametric x and y; never use it for unit conversion in a plot.
- **Unit-free numbers.** Divide by the unit: `b/1mm*scale`, not `b|mm*scale`.
- **Indexing is 1-based:** `v.1`, `v.(k + 1)`, `M.(i; j)`.
- **Angle units** (including `rad`) are not compatible with dimensionless values or `rpm`; divide them out.
- **Special matrices** (diagonal, triangular, symmetric, column) keep their structure: writing outside it is an error, reading outside it returns 0.
- **Large vectors and matrices.** Prefer the `_hp` functions when all elements share consistent units; they are much faster.

## Reviewing the report

Linting tells you the worksheet runs; it can't tell you the report reads well.
After the lint passes, offer to render the report and check it as the reviewing engineer will see it:

1. Search for `Error in`, `Undefined`, `∞` and `NaN` first.
2. Confirm every input echoes with the units you intended.
3. Confirm every check states its result and the value it compared against.
4. Look for bookkeeping that leaked: counters, flags and helper variables that should be hidden, or a `#hide` block that never closed.
5. Look for tables that came out empty, transposed, or with headers misaligned against their columns.

Report findings with the source line, and ask before changing engineering values, code clauses or design assumptions.

## Working in the repo

Run commands from the repo root.
Check only the files you created or edited, never the whole repo.

1. Look for similar worksheets (`Examples/`, `Tests/`) and match their patterns.
2. Write the file.
3. Lint it and fix everything with error severity.
4. Offer to render and review the report.

### Lint

```bash
tools/server/calcpad-server.sh lint "<file.cpd>" ...
```

`tools/server/calcpad-server.ps1` does the same in PowerShell.
Both start a server built under `Calcpad.Web/backend/bin/` (build the backend first if there is none), lint the file and run it.
Linter entries print as `line:column [linter] severity code message` and runtime errors, such as inconsistent units, as `line [runtime] error code message`, sorted by 1-based line.
Exit code is `1` when anything of error severity was reported, `2` when the server could not start.
Add `--info` (`-Info` in PowerShell) to include unused-variable notices.

In VS Code with the CalcpadCE extension, the **Problems** panel shows the same entries once the preview has run; Claude Code can read it with the IDE's `getDiagnostics` tool.

### Render

```bash
tools/server/calcpad-server.sh render "<file.cpd>" "<scratchpad>/report.txt"
```

This writes the print report as plain text and prints its calculation errors; it never runs `#write`.
Write it to the scratchpad, not into the project, and read it with your file tool.

### Test suites

If the file is under `Examples/` or `Tests/`, its committed render stub and lint baseline cover it.
When done, give the user these commands to run themselves:

```bash
bash .github/scripts/compare-renderings.sh
dotnet test tools/highlighter-examples-test/HighlighterExamplesTest.csproj
```
