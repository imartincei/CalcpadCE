# Linter and Diagnostics

CalcpadCE checks your document as you write and flags problems before you ever run it.
Each diagnostic has a short, stable code (like `CPD-3301`) so you can look up exactly what it means.

Diagnostics appear as squiggles in the editor and entries in the Problems panel.

## Where diagnostics come from

- **As you type**, CalcpadCE checks every line without calculating, including `#if` branches and loops that won't run. This catches syntax errors, unmatched brackets and blocks, wrong argument counts and unknown functions.
- **After you run the document** (the preview runs it), its errors appear in the editor too, on the lines where they happened. These are the problems only a calculation finds, such as inconsistent units, mismatched matrix sizes or a solver with no solution.
- If you edit the document after a run, its unit and value errors stay on unchanged lines until the next run. Errors on lines you changed are removed straight away.

While an `#include` or macro has an error, only that error and the checks that don't need the expanded text are shown. Fix it first to see the rest.

## Diagnostic codes

Every diagnostic has a `CPD-` code.
The tables below group them by the kind of problem.

### Includes

| Code | Severity | Meaning |
| ---- | -------- | ------- |
| CPD-1101 | Error | Malformed `#include` statement |
| CPD-1102 | Error | Missing `#include` filename |
| CPD-1103 | Warning | Deprecated `#include` input values |
| CPD-1104 | Error | Include file not found |
| CPD-1105 | Error | Circular `#include` |
| CPD-1106 | Error | Invalid `#ProjectPath` or `#LibraryPath` |

### Macro definitions

| Code | Severity | Meaning |
| ---- | -------- | ------- |
| CPD-2200 | Error | Macro or include error |
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
| CPD-2213 | Error | Required parameter after optional parameter |

### Brackets and blocks

| Code | Severity | Meaning |
| ---- | -------- | ------- |
| CPD-3101 | Error | Unmatched opening parenthesis |
| CPD-3102 | Error | Unmatched closing parenthesis |
| CPD-3103 | Error | Unmatched opening square bracket |
| CPD-3104 | Error | Unmatched closing square bracket |
| CPD-3105 | Error | Unmatched opening curly brace or control block |
| CPD-3106 | Error | Unmatched closing curly brace |

### Naming

| Code | Severity | Meaning |
| ---- | -------- | ------- |
| CPD-3201 | Error | Invalid variable name (must start with a letter) |
| CPD-3202 | Error | Invalid function name |
| CPD-3203 | Error | Function name conflicts with a built-in function |
| CPD-3204 | Error | Variable name conflicts with a keyword |
| CPD-3205 | Error | Variable name conflicts with a built-in constant |
| CPD-3206 | Error | Function must have at least one parameter |
| CPD-3207 | Error | Required parameter after optional parameter in function definition |

### Usage

| Code | Severity | Meaning |
| ---- | -------- | ------- |
| CPD-3301 | Error | Undefined variable |
| CPD-3302 | Error | Function called with the wrong number of parameters |
| CPD-3303 | Error | Undefined macro |
| CPD-3304 | Error | Macro called with the wrong number of parameters |
| CPD-3305 | Error | Undefined function |
| CPD-3307 | Error | Too few parameters |
| CPD-3308 | Error | Too many parameters |
| CPD-3310 | Error | Undefined unit |
| CPD-3311 | Error | Empty parameter in a function call |
| CPD-3312 | Information | Unused variable |
| CPD-3313 | Warning | Redefinition of existing function |
| CPD-3314 | Error | Unknown keyword argument in a macro call |
| CPD-3315 | Error | Unknown keyword argument in a function call |
| CPD-3316 | Error | Duplicate keyword argument in a macro call |
| CPD-3317 | Error | Circular macro reference |

### Semantics

| Code | Severity | Meaning |
| ---- | -------- | ------- |
| CPD-3401 | Error | Invalid operator usage |
| CPD-3402 | Error | Unknown command name |
| CPD-3403 | Error | Unknown directive |
| CPD-3404 | Error | Invalid assignment |
| CPD-3405 | Error | `#` directive not allowed inside a command block |
| CPD-3406 | Error | Invalid command syntax |
| CPD-3407 | Error | Incomplete expression |
| CPD-3408 | Error | Command variable mismatch |
| CPD-3409 | Error | Reassignment of a constant |
| CPD-3410 | Error | Outer-scope assignment (`←`) to an undefined variable |
| CPD-3411 | Warning | Invalid `paramType` value in a metadata comment |
| CPD-3412 | Warning | Invalid metadata-comment JSON |
| CPD-3413 | Error | Invalid #settings JSON (an unknown key is a warning) |
| CPD-3414 | Warning | Invalid PDF settings in a metadata comment |
| CPD-3415 | Error | Invalid `#UI` format (an unknown property is a warning) |
| CPD-3416 | Warning | `uiOverrides` metadata comment not at the end of the file |
| CPD-3417 | Warning | Duplicate `uiOverrides` metadata comment |
| CPD-3418 | Warning | `uiOverrides` sharing a comment with another key |
| CPD-3419 | Warning | Deprecated stored input value |
| CPD-3420 | Warning | Deprecated `#input` keyword |
| CPD-3421 | Warning | `fileInfo` metadata comment not on the last line |

### Formatting

| Code | Severity | Meaning |
| ---- | -------- | ------- |
| CPD-3601 | Warning | Invalid format specifier |

### Calculation errors (CPD-5xxx)

Reported by Core when it checks or runs the document. Errors that match a check above reuse its code.

| Code | Kind | Meaning |
| ---- | ---- | ------- |
| CPD-5101 | Syntax | Cannot evaluate literal |
| CPD-5102 | Syntax | Expression is empty |
| CPD-5103 | Syntax | Missing operand |
| CPD-5104 | Syntax | Cannot evaluate as type |
| CPD-5105 | Syntax | Invalid expression (stack leak) |
| CPD-5106 | Syntax | Invalid expression (empty stack) |
| CPD-5107 | Syntax | Undefined input field |
| CPD-5108 | Syntax | Cannot parse number |
| CPD-5109 | Syntax | More than one assignment |
| CPD-5110 | Syntax | Unexpected delimiter |
| CPD-5111 | Syntax | Missing delimiter in function definition |
| CPD-5112 | Syntax | Duplicate keyword argument in a function call |
| CPD-5113 | Syntax | Missing item in a $Plot or $Map command |
| CPD-5120 | Syntax | More than one delimiter in a plot command |
| CPD-5114 | Syntax | Bracket mismatch |
| CPD-5115 | Syntax | Function definition inside a solver block |
| CPD-5116 | Syntax | Missing file name |
| CPD-5117 | Syntax | Invalid data type |
| CPD-5118 | Syntax | Missing matrix index |
| CPD-5119 | Syntax | Invalid number of indexes |
| CPD-5121 | Syntax | Invalid `#for` header |
| CPD-5201 | Structure | Condition cannot be empty |
| CPD-5202 | Structure | Duplicate #else |
| CPD-5203 | Structure | #else if after #else |
| CPD-5204 | Structure | Blocks nested too deeply |
| CPD-5301 | Names | Cannot evaluate as a function |
| CPD-5302 | Names | Cannot evaluate as a function or operator |
| CPD-5303 | Names | Cannot redefine existing units |
| CPD-5304 | Names | Right side must be constant |
| CPD-5305 | Names | Recursion not allowed in function definition |
| CPD-5306 | Names | Circular function reference |
| CPD-5401 | Values | Power must be unitless |
| CPD-5402 | Values | Right side does not evaluate to units |
| CPD-5403 | Values | Inconsistent units |
| CPD-5404 | Values | Invalid units for function |
| CPD-5405 | Values | Root index must be unitless |
| CPD-5406 | Values | Root index cannot be complex |
| CPD-5407 | Values | Root index must be an integer greater than 1 |
| CPD-5408 | Values | Factorial argument out of range |
| CPD-5409 | Values | Factorial argument must be unitless |
| CPD-5410 | Values | Factorial argument must be a positive integer |
| CPD-5411 | Values | Factorial argument cannot be complex |
| CPD-5412 | Values | Remainder denominator must be unitless |
| CPD-5413 | Values | Both values must be integers |
| CPD-5414 | Values | Calculated units inconsistent with target units |
| CPD-5415 | Values | Inconsistent units for assigned value |
| CPD-5416 | Values | Inconsistent units for assigned value |
| CPD-5417 | Values | Result is not a real number |
| CPD-5418 | Values | Argument out of range |
| CPD-5419 | Values | Condition cannot be complex |
| CPD-5420 | Values | Condition result is invalid |
| CPD-5421 | Values | Plot limits are identical |
| CPD-5422 | Values | Inconsistent units in operation |
| CPD-5423 | Values | Units raised to a complex power |
| CPD-5424 | Values | Cannot evaluate remainder |
| CPD-5501 | Values | Index out of range |
| CPD-5502 | Values | Cannot assign vector to scalar |
| CPD-5503 | Values | Value must be scalar |
| CPD-5504 | Values | Value must be real |
| CPD-5505 | Values | Value must be a positive integer |
| CPD-5506 | Values | Value must be a matrix |
| CPD-5507 | Values | Value must be a vector |
| CPD-5508 | Values | Step cannot be zero |
| CPD-5509 | Values | Vector size limit exceeded |
| CPD-5510 | Values | Cross product needs 2 or 3 element vectors |
| CPD-5511 | Values | Function not defined in complex mode |
| CPD-5512 | Values | Matrix dimensions do not match |
| CPD-5513 | Values | Matrix must be square |
| CPD-5514 | Values | Lp norm needs p >= 1 |
| CPD-5515 | Values | Matrix must be symmetric |
| CPD-5516 | Values | Matrix is not positive definite |
| CPD-5517 | Values | Matrix is singular |
| CPD-5518 | Values | Matrix is singular or close to singular |
| CPD-5519 | Values | Vectors and matrices not supported in complex mode |
| CPD-5520 | Values | Cannot interpolate with a non-scalar value |
| CPD-5521 | Values | Jacobi iteration failed |
| CPD-5522 | Values | Invalid operand |
| CPD-5523 | Values | Invalid argument |
| CPD-5524 | Values | Argument is not an eigenvalue |
| CPD-5525 | Values | QL algorithm failed to converge |
| CPD-5526 | Values | Matrix size limit exceeded |
| CPD-5527 | Values | Embedded data size limit exceeded |
| CPD-5528 | Values | Datagrid size limit exceeded |
| CPD-5529 | Values | Matrix needs at least as many rows as columns |
| CPD-5530 | Values | Variable is not a matrix |
| CPD-5531 | Values | Value must be a high-performance matrix |
| CPD-5532 | Values | Value must be a high-performance vector |
| CPD-5533 | Values | FFT input needs one or two rows |
| CPD-5534 | Values | Matrix is ill-conditioned |
| CPD-5535 | Values | PCG solver failed to converge |
| CPD-5536 | Values | SVD failed to converge |
| CPD-5537 | Values | Complex value outside of complex mode |
| CPD-5601 | Values | Cannot evaluate the function for the solver variable |
| CPD-5602 | Values | Function not defined for the solver variable |
| CPD-5603 | Values | Solver limits out of range |
| CPD-5604 | Values | No solution |
| CPD-5701 | Values | Cannot write PNG file |
| CPD-5702 | Values | Cannot write SVG file |
| CPD-5703 | Values | Cannot convert PNG to Base64 |
| CPD-5704 | Values | File not found |
| CPD-5705 | Values | Path not found |
| CPD-5706 | Values | File format not supported |
| CPD-5707 | Values | Cannot read or write data file |
| CPD-5900 | Values | Unexpected error |
| CPD-5901 | Values | Interrupted by user |
| CPD-5902 | Values | Calculations not active |
| CPD-5903 | Values | Possibly infinite loop |
| CPD-5904 | Values | Too many errors |
| CPD-5905 | Values | Iteration limit exceeded |
| CPD-5906 | Values | Output too long |

## Suppressing diagnostics (lint-ignore)

To silence specific diagnostics in a section, wrap it in `LintIgnore` / `EndLintIgnore` markers and list the codes to ignore:

```text
'<!--{"LintIgnore": ["CPD-3301"]}-->
prototype_var = 5
'<!--{"EndLintIgnore": []}-->
```

Leave the list empty (`"LintIgnore": []`) to suppress *all* diagnostics inside the region.

These markers are one kind of [metadata comment](new-metadata-comments.md); the **Properties** panel tab can write them for you with a code picker.

## Choosing how much to show

The [**Minimum Severity**](new-settings.md#linter) setting hides diagnostics below the level you choose.
Set it to `warning` to see only warnings and errors, or `error` for errors alone.

## See also

- [Using the VS Code Extension](new-vscode-extension.md) · [Using the Desktop App](new-desktop-app.md)
- [Programming](programming.md) · [Writing Math](writing-math.md)
