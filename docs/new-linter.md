# Linter and Diagnostics

> Calcpad.Web only (web editor, desktop app, and VS Code extension). Not available in the standalone WPF desktop application for Windows.

Calcpad.Web checks your document as you write and flags problems before you ever run it.
Each diagnostic has a short, stable code (like `CPD-3301`) so you can look up exactly what it means.

Diagnostics appear:

- In **VS Code** — as squiggles in the editor and entries in the Problems panel.
- In the **desktop and web editors** — as colored marks (red for errors, yellow for warnings, blue for information) at the spot with the problem, and in the **Problems** panel.

## Diagnostic codes

Every diagnostic has a `CPD-` code.
The tables below group them by the kind of problem.

### Includes

| Code | Severity | Meaning |
|------|----------|---------|
| CPD-1101 | Error | Malformed `#include` statement |
| CPD-1102 | Error | Missing `#include` filename |

### Macro definitions

| Code | Severity | Meaning |
|------|----------|---------|
| CPD-2201 | Error | Duplicate macro definition |
| CPD-2202 | Error | Macro name must end with `$` |
| CPD-2203 | Error | Macro parameter must end with `$` |
| CPD-2204 | Error | Invalid macro name (must start with a letter) |
| CPD-2205 | Error | Malformed `#def` syntax |
| CPD-2206 | Error | Unmatched `#def` or `#end def` |
| CPD-2207 | Error | Nested macro definition not allowed |
| CPD-2208 | Error | Macro parameter must start with a letter |
| CPD-2209 | Warning | Macro definition inside a control block has no effect |
| CPD-2210 | Error | Invalid character in macro name |
| CPD-2211 | Error | Invalid character in macro parameter |
| CPD-2212 | Error | Duplicate macro parameter |

### Brackets and blocks

| Code | Severity | Meaning |
|------|----------|---------|
| CPD-3101 | Error | Unmatched opening parenthesis |
| CPD-3102 | Error | Unmatched closing parenthesis |
| CPD-3103 | Error | Unmatched opening square bracket |
| CPD-3104 | Error | Unmatched closing square bracket |
| CPD-3105 | Error | Unmatched opening curly brace or control block |
| CPD-3106 | Error | Unmatched closing curly brace |

### Naming

| Code | Severity | Meaning |
|------|----------|---------|
| CPD-3201 | Error | Invalid variable name (must start with a letter) |
| CPD-3202 | Error | Invalid function name |
| CPD-3203 | Error | Function name conflicts with a built-in function |
| CPD-3204 | Error | Variable name conflicts with a keyword |
| CPD-3205 | Error | Variable name conflicts with a built-in constant |
| CPD-3206 | Error | Function must have at least one parameter |

### Usage

| Code | Severity | Meaning |
|------|----------|---------|
| CPD-3301 | Error | Undefined variable |
| CPD-3302 | Error | Function called with the wrong number of parameters |
| CPD-3303 | Error | Undefined macro |
| CPD-3304 | Error | Macro called with the wrong number of parameters |
| CPD-3305 | Error | Undefined function |
| CPD-3306 | Warning | Invalid element access |
| CPD-3307 | Error | Too few parameters |
| CPD-3308 | Error | Too many parameters |
| CPD-3309 | Warning | Parameter type mismatch |
| CPD-3310 | Error | Undefined unit |
| CPD-3311 | Error | Empty parameter in a function call |
| CPD-3312 | Information | Unused variable |
| CPD-3313 | Error | Redefinition of existing function |

### Semantics

| Code | Severity | Meaning |
|------|----------|---------|
| CPD-3401 | Error | Invalid operator usage |
| CPD-3402 | Error | Unknown command name |
| CPD-3403 | Error | Unknown directive |
| CPD-3404 | Warning | Invalid assignment |
| CPD-3405 | Error | `#` directive not allowed inside a command block |
| CPD-3406 | Error | Invalid command syntax |
| CPD-3407 | Error | Incomplete expression |
| CPD-3408 | Error | Command variable mismatch |
| CPD-3409 | Error | Reassignment of a constant |
| CPD-3410 | Error | Outer-scope assignment (`←`) to an undefined variable |
| CPD-3411 | Warning | Invalid `paramType` value in a metadata comment |
| CPD-3412 | Warning | Invalid metadata-comment JSON |
| CPD-3413 | Error| Invalid #settings JSON |

### Formatting

| Code | Severity | Meaning |
|------|----------|---------|
| CPD-3601 | Warning | Invalid format specifier |

## Suppressing diagnostics (lint-ignore)

To silence specific diagnostics in a section — for example, a prototype variable you haven't wired up yet — wrap it in `LintIgnore` / `EndLintIgnore` markers and list the codes to ignore:

```text
'<!--{"LintIgnore": ["CPD-3301"]}-->
prototype_var = 5
'<!--{"EndLintIgnore": []}-->
```

Leave the list empty (`"LintIgnore": []`) to suppress *all* diagnostics inside the region.

These markers are one kind of [metadata comment](new-metadata-comments.md); the **Properties** panel tab can write them for you with a code picker.

## Choosing how much to show (VS Code)

The `calcpad.linter.minimumSeverity` setting (`error` | `warning` | `information`, default `information`) hides diagnostics below the level you choose — set it to `warning` to see only warnings and errors, or `error` for errors alone.

## See also

- [Using the VS Code Extension](new-vscode-extension.md) · [Using the Desktop App](new-desktop-app.md)
- [Programming](programming.md) · [Writing Math](writing-math.md)
