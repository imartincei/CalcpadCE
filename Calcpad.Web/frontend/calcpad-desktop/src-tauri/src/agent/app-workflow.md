# Working inside the CalcpadCE app

The app's `calcpad` MCP server gives you the user's open worksheets.
Work through it, not through the file system: the editor holds unsaved changes the files on disk don't have.

| Tool | Use |
| --- | --- |
| `calcpad_reference` | Language and app docs for the installed version. No arguments lists the pages; `topic` reads one; `section` reads one heading of a long page. |
| `list_open_documents` | Which worksheets are open, which is active, which have unsaved changes. |
| `get_document` | A worksheet's current text. Opens it first if needed. |
| `get_selection` | What the user has selected; "this" in a request usually means the selection. |
| `propose_edit` | The only way to change or create a worksheet. |
| `check_worksheet` | Lint and run a worksheet now; returns `linter` and `runtime` entries in one list. |
| `render_worksheet` | The rendered report as plain text, with calculation errors. |
| `get_diagnostics` | What the **Problems** panel currently shows. Cheaper than `check_worksheet`, but its `runtime` entries only reflect the last preview. |

## Steps

1. Read the context: `get_selection` and `get_document` for the active worksheet, `list_open_documents` for related ones.
2. Call `calcpad_reference` with a `topic` for any syntax the rules above don't cover.
3. Look at the user's other open worksheets for their conventions (headings, macros, units, check style) and match them.
4. Change worksheets with `propose_edit`:
   - Prefer small `edits` on existing worksheets; each `oldText` must match exactly once.
   - Send the full `text` only for a new worksheet (give a path that doesn't exist yet) or a rewrite.
   - The user may reject the edit. If so, ask what to change rather than resending it.
5. After an edit is accepted, call `check_worksheet` and fix everything with error severity.
6. Offer `render_worksheet` and review the text as **Reviewing the report** describes.

## Limits

- Never write `.cpd` files with your own file tools; those edits skip the user's review and the app's change tracking.
- In Plan mode `propose_edit` is refused: present the plan instead.
- In the automatic modes you can't run commands or write other files.
- Line numbers from every tool are 1-based, like the editor.
