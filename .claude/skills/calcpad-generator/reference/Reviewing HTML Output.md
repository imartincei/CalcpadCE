## Why render the report

`cpdlint.py` catches some errors, but it cannot tell you the report is readable and there are no runtime errors. Rendering the HTML is the only way to see what the reviewing engineer will actually see, and it catches a class of defect the linter is blind to:

- Runtime calculation errors. These do **not** fail the lint — they render as `class="err"` spans inside the report, e.g. `Error in "b = a + 2kip" on line [3]: Inconsistent units`.
- Bookkeeping that leaked into the report — a `#hide` block that never closed, or one closed with `#show` inside an include so the state leaked to the caller.
- Values that computed but display wrong: unintended units, absurd magnitudes, `∞`, `Undefined`, a number rounded to `0`.
- Tables that came out empty, transposed, or with headers misaligned against their columns.

Offer this to the user after the lint passes.

## The endpoint

`POST /api/calcpad/convert` takes Calcpad source and returns `text/html`. Full request schema is in `Calcpad.Web\backend\API_SCHEMA.md`. The fields that matter here:

| Field | Use |
|---|---|
| `content` | The source text |
| `sourceFilePath` | Absolute path, so relative `#include` and `#read` resolve against the file's folder. Always pass it. |
| `forPrint` | `true` for report layout — what a reviewer sees. Hides `#pre`. |
| `includeLineAnchors` | **Set `true`.** See the gotcha below. |
| `write` | Leave `false` so rendering never fires `#write`/`#append` and rewrite the user's data. |

Server launch, port discovery and the `X-Calcpad-Token` header are already solved in
`tools/server/cpdlint.py`'s `CalcpadServer` context manager — import it rather than re-implementing.

## Two behaviours the schema does not state

Both established by testing against the shipped server, not read off the docs:

1. **`X-Calcpad-Errors` is only populated when `includeLineAnchors` is `true`.** With
   `includeLineAnchors: false` the header is present but empty, and calculation errors reach you only as inline `class="err"` markup. Requesting a print-layout render without line anchors will silently report zero errors on a file that has them.
2. **`/convert`'s error `sourceLine` is 1-based.** The schema's "line and column numbers are zero-based" note covers `/lint`, `/highlight` and `/definitions`. Do not add 1 to a `/convert` error line — `cpdlint.py` does that for lint results, and copying the habit here points every error one line past its cause.

## Render

```python
"""Render a .cpd file to HTML via POST /api/calcpad/convert."""
import json, os, sys, urllib.parse, urllib.request

sys.path.insert(0, os.path.join("tools", "server"))
from cpdlint import CalcpadServer, find_server, read_text

def render(server, path):
    body = {
        "content": read_text(path),
        "sourceFilePath": os.path.abspath(path),
        "forPrint": True,
        "includeLineAnchors": True,   # required for X-Calcpad-Errors
        "write": False,
    }
    req = urllib.request.Request(
        server.url + "/api/calcpad/convert",
        data=json.dumps(body).encode("utf-8"),
        headers=server._headers(), method="POST",
    )
    with urllib.request.urlopen(req, timeout=180) as resp:
        html = resp.read().decode("utf-8")
        raw = resp.headers.get("X-Calcpad-Errors")
    errors = json.loads(urllib.parse.unquote(raw)) if raw else []
    return html, errors

if __name__ == "__main__":
    src, out = sys.argv[1], sys.argv[2]
    with CalcpadServer(exe=find_server()) as srv:
        html, errors = render(srv, src)
    with open(out, "w", encoding="utf-8") as fh:
        fh.write(html)
    for e in errors:
        print(f"  line {e['sourceLine']}: [{e['source']}] {e['message']}")
    print(f"{out}  ({len(html)} bytes, {len(errors)} error(s))")
```

Run it from the repo root. Write the HTML to the scratchpad, not into the project.

## Strip it before reading it

**Do not read the raw HTML.** A report is ~1.7 MB and about 99% of that is the inlined MathJax bundle and stylesheet — roughly 820 KB of `<script>` and 850 KB of `<style>` around a payload measured in hundreds of bytes. Drop those two elements and a typical report becomes a few hundred bytes of plain text you can read in full.

```python
"""Strip a Calcpad HTML report down to reviewable text."""
import html as _html, io, re, sys

s = io.open(sys.argv[1], encoding="utf-8").read()
s = re.sub(r"<(script|style)\b.*?</\1>", "", s, flags=re.S | re.I)
s = re.sub(r"<!--.*?-->", "", s, flags=re.S)
s = re.sub(r"</t[dh]\s*>", " | ", s, flags=re.I)
s = re.sub(r"<br\s*/?>", "\n", s, flags=re.I)
s = re.sub(r"</(p|div|tr|h[1-6]|li)\s*>", "\n", s, flags=re.I)
s = re.sub(r"<[^>]+>", "", s)
s = _html.unescape(s)
s = re.sub(r"[ \t]+", " ", s)
s = re.sub(r"\n\s*\n+", "\n", s)
io.open(sys.argv[2], "w", encoding="utf-8").write(s.strip())
```

Write the text to a file and read that. Do **not** `print()` it — Calcpad output is dense with Greek letters, vector arrows and math symbols, and stdout on this machine is `cp1252`, so printing raises `UnicodeEncodeError` on the first `⃗` or `Ω`. Every read and write needs an explicit `encoding="utf-8"`.

A stripped report reads like this, and the leaked `hideOutput = 1` line is exactly the kind of finding the linter cannot produce:

```
Input Vector
⃗v1 = [1; 2; 3] = [1 2 3]
Result Vector
⃗v2 = vector ( len ( ⃗v1 )  )  = [0 0 0]
Calculations showing first iteration
⃗v21 = ⃗v11 · 2 = 1 · 2 = 2
hideOutput = 1
```

## What to check in the stripped text

1. Search for `Error in`, `Undefined`, `∞` and `NaN` first.
2. Confirm every input echoes with the units you intended.

Report findings to the user with the source line, and fix or ask before changing engineering content.
