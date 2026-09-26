"""Builds the font-overlay comparison page for "Font Rendering Tests.cpd".

Renders the worksheet through the Calcpad server's /api/calcpad/convert endpoint,
which returns a self-contained page built from template.html with the bundled
@font-face rules already embedded, then stacks two tinted copies of the rendered
body so candidate serif fonts can be compared glyph by glyph.
"""

import argparse
import base64
import json
import struct
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
CPD = HERE / "Font Rendering Tests.cpd"
OUT = HERE / "Font Rendering Tests.overlay.html"

# Extra fonts to embed, so families that are not installed system-wide (Georgia Pro,
# say) can still be selected. Every font file in the directory is embedded as a data
# URL; the family name and weight/style come from the filename.
DEFAULT_FONTS_DIR = Path("/home/isaiahm/Documents/CalcpadCETesting/Fonts")
FONT_SUFFIXES = (".ttf", ".otf", ".woff2", ".woff")
FONT_MIME = {".ttf": ("font/ttf", "truetype"), ".otf": ("font/otf", "opentype"),
             ".woff2": ("font/woff2", "woff2"), ".woff": ("font/woff", "woff")}
WIDTHS = {
    1: "ultra-condensed", 2: "extra-condensed", 3: "condensed", 4: "semi-condensed",
    5: "normal", 6: "semi-expanded", 7: "expanded", 8: "extra-expanded",
    9: "ultra-expanded",
}
WEIGHTS = {
    "thin": 100, "hairline": 100, "extralight": 200, "ultralight": 200, "light": 300,
    "regular": 400, "normal": 400, "book": 400, "medium": 500, "semibold": 600,
    "demibold": 600, "bold": 700, "extrabold": 800, "ultrabold": 800, "black": 900,
    "heavy": 900,
}

FONT_CHOICES = [
    ("DejaVu Serif Condensed", "DejaVu Serif Condensed — bundled, current default"),
    ("Georgia", "Georgia — the metric target"),
    ("Gelasio", "Gelasio — the previous default (system install only)"),
    ("Century Schoolbook", "Century Schoolbook"),
    ("Times New Roman", "Times New Roman"),
    ("Cambria", "Cambria"),
    ("Charter", "Charter"),
    ("Liberation Serif", "Liberation Serif"),
    ("DejaVu Serif", "DejaVu Serif — full width"),
    ("Noto Serif", "Noto Serif"),
    ("serif", "serif — browser default"),
]

SETTINGS = {
    "math": {
        "decimals": 2,
        "degrees": 0,
        "isComplex": False,
        "substitute": True,
        "formatEquations": True,
    },
    "units": "m",
}

OVERLAY_CSS = """
/* ---- font overlay harness ---- */
body.overlay-host { margin: 0; }

#fontbar {
    position: sticky;
    top: 0;
    z-index: 10;
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 10px 18px;
    padding: 8px 14px;
    background: #fff;
    border-bottom: 1px solid #ccc;
    font: 12px 'Segoe UI', Helvetica, sans-serif;
}

#fontbar .pick { display: flex; align-items: center; gap: 6px; }
#fontbar .swatch { width: 11px; height: 11px; border-radius: 2px; }
#fontbar .swatch.a { background: #d00; }
#fontbar .swatch.b { background: #0a0; }
#fontbar select { font: inherit; padding: 2px 4px; text-align: left; background: #fff; }
#fontbar .note { color: #777; margin-left: auto; }

#stage {
    position: relative;
    isolation: isolate;
    margin: 0 0 4em 1.5em;
    max-width: 190mm;
}

.layer {
    position: absolute;
    top: 0;
    left: 0;
    width: 100%;
}

.layer .eq,
.layer input[type="text"],
.layer table.matrix,
.layer .eq small var,
.layer .eq small i,
.layer .nary {
    font-family: var(--eq-font), 'Century Schoolbook', 'Times New Roman', Times, serif;
    font-stretch: var(--eq-stretch, normal);
}

.layer-a, .layer-a * {
    color: #d00 !important;
    border-color: #d00 !important;
    background: transparent !important;
    background-color: transparent !important;
}

.layer-b, .layer-b * {
    color: #0a0 !important;
    border-color: #0a0 !important;
    background: transparent !important;
    background-color: transparent !important;
}

.layer-b { mix-blend-mode: multiply; }

/* The .value pseudo-element underline and input shadows only add noise here. */
.layer .value:after { display: none !important; }
.layer input, .layer select { box-shadow: none !important; }

#stage.solo-a .layer-b, #stage.solo-b .layer-a { visibility: hidden; }
#stage.solo-a .layer-a, #stage.solo-b .layer-b { mix-blend-mode: normal; }
"""

OVERLAY_JS = """
(function () {
    var stage = document.getElementById('stage');
    var a = document.getElementById('layer-a');
    var b = document.getElementById('layer-b');

    function bind(selectId, layer) {
        var sel = document.getElementById(selectId);
        var apply = function () {
            // Values are "Family" or "Family|width", so a family shipping more than one
            // width can be picked by width as well as by name.
            var parts = sel.value.split('|');
            layer.style.setProperty('--eq-font', "'" + parts[0] + "'");
            layer.style.setProperty('--eq-stretch', parts[1] || 'normal');
        };
        sel.addEventListener('change', apply);
        apply();
    }

    bind('font-a', a);
    bind('font-b', b);

    document.getElementById('mode').addEventListener('change', function (e) {
        stage.className = e.target.value;
    });

    // The stage is only as tall as its absolutely positioned layers make it.
    function resize() { stage.style.height = Math.max(a.offsetHeight, b.offsetHeight) + 'px'; }
    resize();
    window.addEventListener('resize', resize);
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(resize);
})();
"""


def render(url: str) -> str:
    payload = json.dumps({
        "content": CPD.read_text(encoding="utf-8"),
        "settings": SETTINGS,
        "sourceFilePath": str(CPD),
        "forPrint": True,
        "includeLineAnchors": False,
    }).encode("utf-8")
    request = urllib.request.Request(
        f"{url.rstrip('/')}/api/calcpad/convert",
        data=payload,
        headers={"Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            html = response.read().decode("utf-8")
            errors = response.headers.get("X-Calcpad-Errors")
    except urllib.error.URLError as exc:
        raise SystemExit(
            f"Could not reach the Calcpad server at {url}: {exc}\n"
            "Start it with: dotnet run --project Calcpad.Web/backend"
        ) from exc

    if errors and errors not in ("[]", "%5B%5D"):
        print(f"note: server reported errors: {urllib.parse.unquote(errors)}", file=sys.stderr)
    return html


def family_name(name: str) -> str:
    """"GeorgiaPro" -> "Georgia Pro"; "CAMBRIAB" -> "Cambriab"."""
    spaced = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", name).strip()
    return " ".join(w if w[:1].isupper() and w[1:].islower() else w.capitalize()
                    for w in spaced.split())


def meta_from_filename(stem: str) -> tuple[str, int, str, str]:
    """Last-resort guess when the font's own tables cannot be read (woff/woff2)."""
    family, _, suffix = stem.partition("-")
    style = "italic" if re.search(r"italic|oblique", suffix, re.IGNORECASE) else "normal"
    face = re.sub(r"italic|oblique", "", suffix, flags=re.IGNORECASE)
    return family_name(family), WEIGHTS.get(face.replace(" ", "").lower(), 400), style, "normal"


def meta_from_tables(data: bytes) -> tuple[str, int, str, str]:
    """Read family, weight, style and width straight out of the font's OS/2 and name
    tables. Filenames lie — Windows ships Cambria italic as CAMBRIAI.TTF, and DejaVu
    calls its semi-condensed faces "Condensed"."""
    num = struct.unpack(">H", data[4:6])[0]
    tables = {}
    for i in range(num):
        rec = 12 + 16 * i
        offset, length = struct.unpack(">II", data[rec + 8:rec + 16])
        tables[data[rec:rec + 4].decode("latin-1")] = (offset, length)

    os2 = tables["OS/2"][0]
    weight, width = struct.unpack(">HH", data[os2 + 4:os2 + 8])
    italic = bool(struct.unpack(">H", data[os2 + 62:os2 + 64])[0] & 0x1)

    # Name ID 16 (typographic family) groups width and weight variants under one name;
    # ID 1 splits them, which is what puts "DejaVu Serif Condensed" in a family of its own.
    name = tables["name"][0]
    count, str_offset = struct.unpack(">HH", data[name + 2:name + 6])
    found: dict[int, str] = {}
    for i in range(count):
        rec = name + 6 + 12 * i
        platform, _, _, name_id, length, offset = struct.unpack(">HHHHHH", data[rec:rec + 12])
        if name_id not in (1, 16):
            continue
        raw = data[name + str_offset + offset:name + str_offset + offset + length]
        try:
            # Platform 0 (Unicode) and 3 (Windows) are both UTF-16BE; 1 (Mac) is single byte.
            found.setdefault(name_id, raw.decode("latin-1" if platform == 1 else "utf-16-be"))
        except UnicodeDecodeError:
            continue

    family = (found.get(16) or found.get(1) or "").strip()
    if not family:
        raise ValueError("no usable family name")
    return family, weight, "italic" if italic else "normal", WIDTHS.get(width, "normal")


def read_font_meta(path: Path) -> tuple[str, int, str, str]:
    if path.suffix.lower() in (".ttf", ".otf"):
        try:
            return meta_from_tables(path.read_bytes())
        except (KeyError, ValueError, struct.error, IndexError):
            pass
    return meta_from_filename(path.stem)


def scan_fonts(fonts_dir: Path) -> tuple[str, list[tuple[str, str]]]:
    """Embed every font in fonts_dir as @font-face rules, grouped into families.

    Faces are keyed by (family, width), so a family that ships more than one width —
    DejaVu Serif carries both normal and semi-condensed under one typographic name —
    becomes one picker entry per width rather than collapsing into whichever loaded last.
    """
    if not fonts_dir.is_dir():
        print(f"note: no font directory at {fonts_dir}; "
              "only bundled and system fonts will be offered.", file=sys.stderr)
        return "", []

    rules: list[str] = []
    variants: dict[tuple[str, str], int] = {}
    for path in sorted(fonts_dir.iterdir()):
        if path.suffix.lower() not in FONT_SUFFIXES:
            continue
        family, weight, style, stretch = read_font_meta(path)
        mime, fmt = FONT_MIME[path.suffix.lower()]
        url = f"data:{mime};base64," + base64.b64encode(path.read_bytes()).decode("ascii")
        rules.append(
            f'@font-face{{font-family:"{family}";src:url({url}) format("{fmt}");'
            f"font-weight:{weight};font-style:{style};font-stretch:{stretch};}}"
        )
        key = (family, stretch)
        variants[key] = variants.get(key, 0) + 1

    choices = []
    for (family, stretch), n in sorted(variants.items()):
        faces = f"{n} face{'s' if n > 1 else ''}"
        if stretch == "normal":
            choices.append((family, f"{family} — {faces} from {fonts_dir.name}"))
        else:
            choices.append((f"{family}|{stretch}",
                            f"{family} {stretch} — {faces} from {fonts_dir.name}"))
    if choices:
        print(f"Embedded {len(rules)} face(s) from {fonts_dir}: "
              + ", ".join(label.split(" — ")[0] for _, label in choices))
    return "".join(rules), choices


def options_markup(choices: list[tuple[str, str]], default: str) -> str:
    out = []
    for value, label in choices:
        selected = " selected" if value == default else ""
        out.append(f'<option value="{value}"{selected}>{label}</option>')
    return "".join(out)


def build(html: str, fonts_dir: Path) -> str:
    head_end = html.lower().index("</head>")
    # The template CSS mentions "<body>" in a comment, so only search past </head>.
    body_open = re.search(r"<body[^>]*>", html[head_end:], re.IGNORECASE)
    body_end = html.lower().rindex("</body>")
    if body_open is None:
        raise SystemExit("Rendered page has no <body> tag.")

    head = html[:head_end]
    body = html[head_end + body_open.end():body_end]

    extra_faces, embedded = scan_fonts(fonts_dir)
    embedded_values = {value for value, _ in embedded}
    choices = embedded + [c for c in FONT_CHOICES if c[0] not in embedded_values]
    default_b = embedded[0][0] if embedded else "Georgia"

    bar = (
        '<div id="fontbar">'
        '<span class="pick"><span class="swatch a"></span>Layer A'
        f'<select id="font-a">{options_markup(choices, "DejaVu Serif Condensed")}</select></span>'
        '<span class="pick"><span class="swatch b"></span>Layer B'
        f'<select id="font-b">{options_markup(choices, default_b)}</select></span>'
        '<span class="pick">Show'
        '<select id="mode">'
        '<option value="">Overlay</option>'
        '<option value="solo-a">A only</option>'
        '<option value="solo-b">B only</option>'
        "</select></span>"
        '<span class="note">Matching glyphs multiply to near-black; '
        "differences show as red or green fringes. Radical signs are SVG background "
        "images and stay black.</span>"
        "</div>"
    )

    return (
        f"{head}<style>{extra_faces}{OVERLAY_CSS}</style></head>\n"
        f'<body class="overlay-host">\n{bar}\n'
        f'<div id="stage">\n'
        f'<div class="layer layer-a" id="layer-a">{body}</div>\n'
        f'<div class="layer layer-b" id="layer-b">{body}</div>\n'
        f"</div>\n<script>{OVERLAY_JS}</script>\n</body></html>\n"
    )


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://localhost:9420",
                        help="Calcpad server base URL "
                             "(9420 is what scripts/restart-dev-server.sh uses).")
    parser.add_argument("--out", type=Path, default=OUT, help="Output HTML path.")
    parser.add_argument(
        "--fonts-dir",
        type=Path,
        default=DEFAULT_FONTS_DIR,
        help="Directory of font files to embed as extra picker options "
             f"(default: {DEFAULT_FONTS_DIR}). Anything embedded from here is baked "
             "into the output page, so keep that page out of git when the fonts are "
             "not redistributable.",
    )
    args = parser.parse_args()

    page = build(render(args.url), args.fonts_dir)
    args.out.write_text(page, encoding="utf-8")
    print(f"Wrote {args.out} ({len(page) / 1024:.0f} KB)")


if __name__ == "__main__":
    main()
