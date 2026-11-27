# Validate every worksheet before calling it done

Do not eyeball a worksheet and declare it correct.
The linter drives the real Calcpad engine, so it resolves `#include` chains and reports macro arity, undefined variables and unbalanced blocks.
Where to run it depends on your host: `check_worksheet` in the app, `tools/server/calcpad-server.sh lint` in the repo.

The linter does not catch runtime calculation errors such as inconsistent units.
Those appear when the worksheet runs: `check_worksheet` and `render_worksheet` report them in the app; in the repo, render the report.

For the full list of linter codes, read the `new-linter` page.
