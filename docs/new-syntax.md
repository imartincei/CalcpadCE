# Visibility & Output-Mode Directives

> Staging doc for the directive-conditions / `#end` / `#pre`+`#post` print-semantics change. Fold
> this into `quick-reference.md`, `Setup/AI/Work/CALCPAD_LANGUAGE_REFERENCE_FOR_CLAUDE.md`,
> `.claude/skills/calcpad-generator/reference/syntax-reference.md`, `new-pdf-export.md`, and
> `new-metadata-comments.md` when those get their next real update.

Print visibility used to be a separate, web-only mechanism: wrapping content in
`'<!--{"NoPrintStart": true}-->` / `'<!--{"NoPrintEnd": true}-->` HTML comments, stripped from the
source before conversion when exporting to PDF. That mechanism is gone. Visibility is now handled
entirely by Core's directive system (`#hide`/`#show`/`#pre`/`#post`), which also gained an optional
condition argument and matching `#end` forms.

## `#pre` / `#post` now mean print, not calculation phase

`#pre` and `#post` used to be tied to the WPF `#input`/`#pause` calculate/don't-calculate phases.
In Calcpad.Web every parse always calculates, so that distinction was dead weight. They're rebound
to print output instead:

- `#pre` — show on screen, hide when printing/exporting to PDF.
- `#post` — show everywhere for now (preview and PDF alike). It's kept distinct from `#show`
  because a later `#UI` mode is planned to hide `#post` content in that mode while still printing
  it — not yet wired up, so today it behaves like `#show`.

```text
#pre
'These lines are visible in the preview but excluded from the PDF.
debug_x = 5
debug_y = debug_x + 1
#end pre
'This always prints.
```

The web backend sets this via the request's existing `forPrint` flag, which now maps directly to
`ExpressionParser.ForPrint` instead of stripping `NoPrintStart`/`NoPrintEnd` regions beforehand.

## Optional condition argument

`#hide`, `#show`, `#pre`, `#post`, `#val`, `#equ`, `#noc`, `#varsub`, `#nosub`, and `#novar` each
optionally take a trailing condition, evaluated the same way `#if` evaluates one:

```text
#hide x == 5
'Hidden only when x equals 5.
```

- No condition → the directive always applies (same as before).
- A condition that's true → the directive applies.
- A condition that's false → the directive is a no-op; the *current* state is left as-is.
- A bad expression records an error and leaves state unchanged.
- A `#hide`/`#show`/etc. inside a false `#if` branch no longer takes effect (previously it did,
  regardless of the branch).

## `#end` forms restore prior state

Every directive in these three groups can be closed with `#end <directive>`, which pops back to
whatever state was in effect before the matching opener (or the default state if there was none)
— rather than setting a fixed value the way the bare directive does:

- Visibility: `#end hide`, `#end show`, `#end pre`, `#end post`
- Output mode: `#end val`, `#end equ`, `#end noc`
- Substitution: `#end varsub`, `#end nosub`, `#end novar`

This lets a macro change state temporarily without leaking it to the caller:

```text
#pre
'On screen only.
#hide
'Hidden.
#end hide
'Back to "on screen only" (#pre's state), not the document default.
#end pre
'Back to the default (shown everywhere).
```

`#end hide` (etc.) with nothing open falls back to the default state — visible, equations, and
both variable names and substituted values.
