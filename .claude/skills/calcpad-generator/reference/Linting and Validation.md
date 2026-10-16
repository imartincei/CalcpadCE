## Validate every .cpd file before calling it done

Do not eyeball a `.cpd` file and declare it correct. Run the linter — it drives the real Calcpad engine, so it resolves `#include` chains and reports actual macro arity, undefined variables and unbalanced blocks.

```
python tools/server/cpdlint.py "<file or folder>" ...
```

Run it from the repo root. Exit code is `1` when anything of error severity was reported, `2` when the server could not start. Useful flags:

| Flag | Effect |
|---|---|
| `--info` | Include information diagnostics (unused variables). Noisy — custom units from included files report as unused. |
| `--server PATH` | Use a specific `Calcpad.Server.exe`. |
| `--url http://127.0.0.1:PORT` | Reuse a running server instead of launching one. |
| `--no-engine` | Convention checks only, no server. |
| `--ignore CODE` | Suppress a diagnostic code. Repeatable. |

The script launches the newest `Calcpad.Server.exe` under `Calcpad.Web/backend/bin/` (build the backend first if there is none), posts each file to `POST /api/calcpad/lint`, and shuts the server down afterwards. The API is documented in `Calcpad.Web\backend\API_SCHEMA.md`. Line and column numbers from the API are zero-based; the script converts them to editor numbering. `/convert` is the exception — its error lines are already 1-based. To render and review the report itself, see `Reviewing HTML Output.md`.

## Convention rules the engine cannot catch

These are the `CPW-*` codes. Each one is silent at run time, which is exactly why they are worth checking here instead.

| Code | Mistake |
|---|---|
| `CPW-DOLLAR` | A `$` inside quoted comment text. Calcpad treats `$` as a macro marker, so naming a macro in full inside a comment can confuse the parser. Write the bare name. A macro call after a label (`'Label -'x = m$(a)`) is fine. |
| `CPW-EXPRARG` | An arithmetic expression passed as a macro argument. Calcpad does not reliably evaluate inline math there. Assign it to a named variable first and pass the variable. |
| `CPW-LIBPATH` | The file uses `{library}` without declaring `#LibraryPath`. |

## Mistakes that keep recurring

1. **Semicolons in macro parameters text.** The most common silent breakage. Reach for "and".
2. **Passing expressions to macros.** Write `V_n = n_b*R_n` then `check$(V_n; V_a)`, never
   `check$(n_b*R_n; V_a)`.
3. **Assuming a module's `#local` includes propagate.** They do not. A file calling
   `myMacro$` must include `module.cpd` and `referenceModule.cpd`
   itself, even though `module.cpd` includes them for its own compilation.
4. **Non-ASCII macro names or parameters.** Macro names and `#def` parameter names are
   ASCII-only. Regular variables may use Greek, `°`, `ø`/`Ø` and subscripts.
