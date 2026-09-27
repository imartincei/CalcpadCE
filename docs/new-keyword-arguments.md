# Keyword Arguments

Functions and macros can declare **optional parameters** with default values, and can be called with
**keyword arguments** of the form `name = value`.

## Optional parameters in functions

Give a parameter a default with `=` in the parameter list:

```text
f(x; y = 0; z = 1kg) = x*y + z
```

Required parameters must come before optional ones. A default is stored as source text and evaluated
at **call time** in the global scope, so `y = a` picks up whatever `a` holds when the call runs.

## Calling a function with keyword arguments

```text
f(x; y = 0; z = 1) = x*y + z

f(5; 3; 2)              'positional only
f(x = 5; z = 2; y = 3)  'all keyword, any order
f(5; z = 2)             'x = 5 positional, y = 0 default, z = 2 keyword
f(5)                    'y and z both take their defaults
```

The call is rewritten to a plain positional call before it is evaluated, so `f(5; z = 2)` becomes
`f(5; 0; 2)`.

## Optional parameters in macros

Macros use the `$` suffix on names and parameters. A macro default is raw text that is substituted
into the macro body:

```text
#def calc$(a$; b$ = 10; c$ = 5)
    result = a$ + b$ + c$
#end def
```

## Calling a macro with keyword arguments

```text
calc$(1; 2; 3)                'positional
calc$(a$ = 1; c$ = 7; b$ = 2) 'keyword, any order
calc$(1; c$ = 7)              'positional then keyword
calc$()                       'all defaults
```

`m$()` means "use every default" only when the macro has at least one optional parameter. For a
macro whose parameters are all required, an empty argument list still passes empty strings, as
before.

## Rules

1. Required parameters must come before optional ones **in the definition**.
2. Positional arguments must come before keyword arguments **in a call**.
3. Each parameter can be supplied only once — a duplicate is an error.
4. A missing required argument is an error.
5. A missing optional argument takes its default.
6. Defaults cannot reference other parameters; each one is resolved on its own.

## Linter diagnostics

| Code | Applies to | Condition |
|------|------------|-----------|
| `CPD-2213` | Macro definition | Required parameter after an optional parameter |
| `CPD-3215` | Function definition | Required parameter after an optional parameter |
| `CPD-3314` | Macro call | Unknown keyword argument name |
| `CPD-3315` | Function call | Unknown keyword argument name |

## Editor support

Autocomplete inserts calls with parameter-name placeholders, and hover documentation shows each
default:

```text
y (default: 0)
z (default: 1kg)
```

A parameter without a default is marked `(required)` when the function or macro also has optional
ones.

## Limitations

- Built-in functions (`sin`, `cos`, `sqrt`, …) take neither keyword arguments nor defaults.
- A default cannot reference another parameter.
- String variable expansion is not applied to function default expressions.
- Command-block functions (`$Inline`, `$Block`, `$While`) accept optional parameters in their
  definition but receive only positional arguments internally.
