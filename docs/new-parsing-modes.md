# Parsing Modes

By default every line of a worksheet is Calcpad: a calculation, or a `'` comment.
Three directives change how the lines that follow are parsed:

| Directive | Lines are… |
| --------- | ---------- |
| `#html` | output as raw HTML |
| `#markdown` | rendered as Markdown |
| `#cpd` | parsed as Calcpad again (the default mode) |

## `#html`

Every line is written to the output exactly as typed, with no `'` prefix and no `<p>` wrapping, so multi-line `<style>`, `<script>` and `<pre>` blocks work:

```text
#html
<style>
    .note { border-left: 3px solid #888; padding-left: 8px; }
</style>
<div class="note">
    Loads follow EN 1991-1-1.
</div>
#end html
```

Opening tags get the source line attached, so click-to-source in the preview still jumps to the right line.

## `#markdown`

Consecutive lines are rendered together, so tables, nested lists, code blocks and multi-line paragraphs work.
Leading indentation is kept, and a blank line separates paragraphs:

```text
#markdown
## Load cases

| Case | Load  |
|------|-------|
| Dead | 5 kN  |
| Live | 3 kN  |

- Ultimate limit state
  - 1.35 G + 1.5 Q
#end markdown
```

Emphasis extras are enabled: `++inserted++`, `~sub~`, `^sup^`, `~~strike~~`, plus task lists and bare URLs.
Raw HTML inside Markdown passes through.

## `#cpd`

Switches back to Calcpad, for example to calculate inside an `#html` block:

```text
#html
<div class="note">
#cpd
A = 2m*3m
#end cpd
</div>
#end html
```

## `#end` forms and nesting

Like the [visibility directives](new-visibility-directives.md), each mode directive pushes the current mode and its own `#end` form pops back to it.
Only the `#end` that matches the mode in effect closes a block, so `#end markdown` inside `#html` is content, not a close.
Without an `#end`, a mode lasts until the next mode directive or the end of the file, so `#html` … `#cpd` also works.

Mode blocks nest inside `#if`, `#for` and the other Calcpad blocks, and the Calcpad block keeps working as long as the mode block is closed before it continues:

```text
#if x > 3
#html
<b>x is large</b>
#end html
#else
#markdown
*x is small*
#end markdown
#end if
```

## Optional condition

Each opener takes an optional condition, evaluated the same way `#if` evaluates one.
When it is false, the block is skipped up to its matching `#end`, including any blocks nested in it:

```text
#html showNotes == 1
<div class="note">Reviewer notes…</div>
#end html
```

The mode still changes when the condition is false, so the skipped lines are never parsed as Calcpad.
A `#cpd` block with a false condition skips its calculations.

## What is allowed inside

Inside a mode block the only reserved keywords are the three openers `#html`, `#markdown` and `#cpd`, the `#end` form of the mode in effect.
Every other line is content, including lines starting with `#`:

```text
#markdown
# Load cases     ← heading
#tag             ← content
#if x > 3        ← content, printed as written
#end markdown    ← closes the block
```

So `#if`, `#for`, `#hide`, `#md` and the rest do not run inside `#html` or `#markdown`.
They do work around a mode block, just not in it:

```text
#if x > 3
#markdown
**x is large**
#end markdown
#end if
```

If you need to use output control keywords such as `#pre` and `#post` inside an `#html` or `#markdown` block, switch to `#cpd` mode first:

```text
#markdown
Content
#cpd
#pre
#end cpd
Pre content
#cpd
#show
#end cpd
More content
#end markdown
```

Macros work in every mode because they are expanded before the worksheet is parsed.
`#def`, `#include` and macro calls can all appear inside an `#html` or `#markdown` block:

```text
#def note$(text$) = <div class="note">text$</div>
#html
note$(Check deflection at midspan.)
#end html
```

A macro can also switch mode itself: a multi-line macro containing `#html` switches mode wherever it is called.
This works in the output, but is not recommended unless the entire HTML/Markdown block is contained in the macro.
The editor colours HTML and Markdown from the directives written in the file before macros are expanded, so declaring `#html` in a macro and `#end html` in code after the macro is called breaks syntax highlighting.

## Editor support

- HTML and Markdown content is coloured with the editor's HTML and Markdown grammars, and the linter ignores it.
- The formatting hotkeys (bold, headings, lists, paragraph, line break) insert raw HTML or Markdown in the block's format, with no `'` comment quotes.
- Toggle comment and paste as comment use `<!-- … -->`.
- Format Document re-indents directive lines but leaves content lines untouched.

## Links

A bare URL is turned into a link only by `#markdown`.
In `#html` write the `<a>` yourself.
In Calcpad mode use an `'<a href="…">` comment or `#md on` with `[text](url)`.

Links are always confirmed before anything happens, so the URL/filepath can be reviewed before proceeding.

| Scheme | On click |
| ------ | -------- |
| `http`, `https` | Prompts with the URL, then opens in the browser |
| `file` | Prompts with the path, then opens it with the system default |
| `#anchor` | Scrolls to that element in the preview, with no prompt |
| anything else | Ignored, since the app uses URLs for navigation |

The worksheet template styles headings, paragraphs, and Calcpad's own output, but not the rest of what Markdown produces.
Blockquotes, fenced code, rules, task lists, and table borders come out as browser defaults.
Style them in an `#html` block when a document needs them.
The CSS snippets contain some helpful starting points.

Link targets do not currently support project/library paths, so only absolute paths will work.

## `#markdown` vs `#md on`

`#md on` is a lighter toggle that stays in Calcpad mode: calculations still run, and only `'` comment lines are rendered as Markdown, one line at a time.
Use `#markdown` for prose, tables and lists that span several lines, and `#md on` to format comments between calculations.
