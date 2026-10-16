# Keyword Arguments

Functions and macros can declare **optional parameters** with default values, and can be called with **keyword arguments** of the form `name = value`.

## Optional parameters in functions

Give a parameter a default with `=` in the parameter list:

```calcpad
f(x; y = 0; z = 1kg) = x*y + z
```

Required parameters must come before optional ones.

## Calling a function with keyword arguments

```calcpad
f(x; y = 0; z = 1) = x*y + z

f(5; 3; 2)              'positional only
f(x = 5; z = 2; y = 3)  'all keyword, any order
f(5; z = 2)             'x = 5 positional, y = 0 default, z = 2 keyword
f(5)                    'y and z both take their defaults
```

Keyword arguments work with any custom function, including one without defaults.

## Optional parameters in macros

Macros use the `$` suffix on names and parameters.
A macro default is raw text that is substituted into the macro body:

```calcpad
#def calc$(a$; b$ = 10; c$ = 5)
    result = a$ + b$ + c$
#end def
```

## Calling a macro with keyword arguments

```calcpad
calc$(1; 2; 3)                'positional
calc$(a$ = 1; c$ = 7; b$ = 2) 'keyword, any order
calc$(1; c$ = 7)              'positional then keyword
calc$()                       'all defaults
```

Keyword arguments work with any macro, including one without defaults.

`calc$()` means "use every default" only when the macro has at least one optional parameter.
For a macro whose parameters are all required, an empty argument list still passes empty strings, as before.

## Rules

1. Required parameters must come before optional ones **in the definition**.
2. Positional arguments must come before keyword arguments **in a call**.
3. Each parameter can be supplied only once, duplicates are not allowed.
4. A missing required argument is an error.
5. A missing optional argument takes its default.

## Editor support

Autocomplete inserts calls with parameter-name placeholders, and hover documentation shows each default:

```text
y (default: 0)
z (default: 1kg)
```

A parameter without a default is marked `(required)` when the function or macro also has optional ones.

## Limitations

- Built-in functions (`sin`, `cos`, `sqrt`, …) take neither keyword arguments nor defaults.
