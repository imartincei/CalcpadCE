# Visibility & Output-Mode Directives

## `#pre` / `#post`

`#pre` and `#post` used to be tied to the WPF calculate/don't-calculate phases.
In Calcpad.Web, changes to the input form calculates the document, so these directives were re-purposed for a similar usage.

- `#pre` — shows on the input form, hides when printing/exporting to the report PDF.
- `#post` — shows in the report, but not the input form.

```text
#pre
'These lines are visible in the input form but excluded from the report PDF.
debug_x = 5
debug_y = debug_x + 1
#end pre
'This always prints.
```

## Optional condition argument

`#hide`, `#show`, `#pre`, `#post`, `#val`, `#equ`, `#noc`, `#varsub`, `#nosub`, and `#novar` each optionally take a trailing condition, evaluated the same way `#if` evaluates one:

```text
#hide x == 5
'Hidden only when x equals 5.
```

- No condition → the directive always applies.
- A bad expression records an error and leaves state unchanged.

## `#end` forms restore prior state

Every directive in these three groups can be closed with `#end <directive>`, which pops back to whatever state was in effect before the matching opener (or the default state if there was none):

- Visibility: `#end hide`, `#end show`, `#end pre`, `#end post`
- Output mode: `#end val`, `#end equ`, `#end noc`
- Substitution: `#end varsub`, `#end nosub`, `#end novar`

This lets a macro change a state temporarily without leaking it to the caller:

```text
#pre
'On screen only.
#hide
'Hidden.
#end hide
'Back to #pre state.
#end pre
'Back to the default #show state.
```
