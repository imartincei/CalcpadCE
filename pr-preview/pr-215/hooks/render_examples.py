"""MkDocs hook: renders .cpd examples with the Calcpad CLI and injects
virtual category pages into the docs build.

Which examples are built and in what order is controlled by docs/examples.yml.
Each top-level key becomes an examples/<slug>.md page showing source code
alongside the rendered HTML output in a two-column grid.
"""

import importlib.util
import logging
import os
import re
import shutil
import sys
import tempfile
from pathlib import Path

import urllib.parse

import mkdocs.structure.files as mkdocs_files
from pygments import highlight as _pyg_highlight
from pygments.formatters import HtmlFormatter


_REPO_ROOT = Path(__file__).resolve().parents[2]
_SCRIPTS_DIR = _REPO_ROOT / ".github" / "scripts"
if str(_SCRIPTS_DIR) not in sys.path:
    sys.path.insert(0, str(_SCRIPTS_DIR))

from render_cpd import find_cli, render_many

log = logging.getLogger("mkdocs")
render_log = logging.getLogger("render_cpd")

_temp_dir: str | None = None


# Load the sibling calcpad_lexer.py module by path. MkDocs loads hooks as
# free-standing files (not a package), so a normal `import` does not resolve.
def _load_calcpad_lexer():
    lexer_path = Path(__file__).parent / "calcpad_lexer.py"
    spec = importlib.util.spec_from_file_location("calcpad_lexer", lexer_path)
    module = importlib.util.module_from_spec(spec)
    sys.modules["calcpad_lexer"] = module
    spec.loader.exec_module(module)
    return module


_calcpad_lexer_mod = _load_calcpad_lexer()
_CALCPAD_LEXER = _calcpad_lexer_mod.CalcpadLexer(stripnl=False)
# wrapcode=True wraps highlighted output in <pre><code>…</code></pre>, which is
# what Material's copy-button JS looks for to attach itself.
_CALCPAD_FORMATTER = HtmlFormatter(nowrap=False, wrapcode=True, cssclass="highlight")
# Make ```calcpad / ```cpd fenced blocks in narrative .md pages work too.
_calcpad_lexer_mod.register()


# ---------------------------------------------------------------------------
# Hook entry points
# ---------------------------------------------------------------------------


def on_config(config, **kwargs):
    """Inject the Examples nav entries from docs/examples.yml."""
    repo_root = Path(config["config_file_path"]).parent
    examples = _load_examples(repo_root)

    for group_name, categories in examples.items():
        cat_nav = [
            {f"{_display_name(cat)} ({len(stems)})": f"examples/{_slugify(cat)}.md"}
            for cat, stems in categories.items()
        ]
        config["nav"].append({group_name: cat_nav})
    return config


def on_files(files, config, **kwargs):
    """Generate category pages and inject them as virtual MkDocs files."""
    global _temp_dir

    render_log.handlers.clear()
    render_log.setLevel(log.getEffectiveLevel())
    if log.handlers:
        for handler in log.handlers:
            render_log.addHandler(handler)
        render_log.propagate = False
    else:
        render_log.propagate = True

    repo_root = Path(config["config_file_path"]).parent
    examples_root = repo_root / "Examples"
    examples = _load_examples(repo_root)
    group_folders = _group_folders(repo_root)

    try:
        cli_path = find_cli(repo_root)
    except FileNotFoundError as exc:
        raise SystemExit(f"render_examples: {exc}") from exc

    # Resolve every listed entry to a concrete .cpd path (fail fast on missing files)
    all_cpd: list[tuple[str, Path]] = []
    for group_name, categories in examples.items():
        folder = group_folders[group_name]
        for category, stems in categories.items():
            for stem in stems:
                cpd_file = examples_root / folder / category / f"{stem}.cpd"
                if not cpd_file.exists():
                    raise SystemExit(
                        f"render_examples: Listed example not found on disk:\n"
                        f"  {cpd_file}\n"
                        f"Either add the file or remove the entry from docs/examples.yml."
                    )
                all_cpd.append((category, cpd_file))

    # Render all fragments in parallel
    labels = {cpd_file: f"{cat}/{cpd_file.stem}" for cat, cpd_file in all_cpd}
    try:
        fragments = {
            cpd_file: fragment.decode("utf-8")
            for cpd_file, fragment in render_many(
                cli_path,
                [cpd_file for _, cpd_file in all_cpd],
                label_for=labels.__getitem__,
            ).items()
        }
    except RuntimeError as exc:
        raise SystemExit(f"render_examples: {exc}") from exc

    # Write generated .md files to a temp directory so MkDocs can read them
    _temp_dir = tempfile.mkdtemp(prefix="calcpad-examples-")
    examples_temp = Path(_temp_dir) / "examples"
    examples_temp.mkdir()

    all_files = list(files)

    group_folders = _group_folders(repo_root)
    for group_name, categories in examples.items():
        folder = group_folders[group_name]
        for category, stems in categories.items():
            cpd_files = [examples_root / folder / category / f"{stem}.cpd" for stem in stems]
            content = _render_category_page(category, cpd_files, fragments)
            slug = _slugify(category)
            dest = examples_temp / f"{slug}.md"
            dest.write_text(content, encoding="utf-8")

            virtual = mkdocs_files.File(
                path=f"examples/{slug}.md",
                src_dir=_temp_dir,
                dest_dir=config["site_dir"],
                use_directory_urls=config["use_directory_urls"],
            )
            all_files.append(virtual)

    # Collect all assets to serve, keyed by their virtual path under examples/.
    # Two different "anchors" are needed because URLs in the rendered HTML differ:
    #   - Category-local files (e.g. ./Models/bolt_and_nut.glb) are referenced
    #     relative to the category dir → serve at examples/<rel-from-cat-dir>
    #   - Shared images (../../Images/..., stripped to Images/...) are referenced
    #     relative to Examples/ → serve at examples/<rel-from-examples-root>
    _ASSET_SUFFIXES = {".js", ".css", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".txt", ".glb"}
    assets_to_serve: dict[str, Path] = {}  # path_key → source file

    for group_name, categories in examples.items():
        folder = group_folders[group_name]
        for category in categories:
            cat_dir = examples_root / folder / category
            for asset in cat_dir.rglob("*"):
                if asset.is_file() and asset.suffix.lower() in _ASSET_SUFFIXES:
                    assets_to_serve.setdefault(asset.relative_to(cat_dir).as_posix(), asset)

    images_src = examples_root / "Images"
    if images_src.is_dir():
        for asset in images_src.rglob("*"):
            if asset.is_file():
                assets_to_serve.setdefault(asset.relative_to(examples_root).as_posix(), asset)

    assets_temp = Path(_temp_dir) / "assets"
    assets_examples = assets_temp / "examples"
    assets_examples.mkdir(parents=True)
    for path_key, asset in assets_to_serve.items():
        dest = assets_examples / path_key
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(asset, dest)
        all_files.append(mkdocs_files.File(
            path=f"examples/{path_key}",
            src_dir=str(assets_temp),
            dest_dir=config["site_dir"],
            use_directory_urls=False,
        ))

    return mkdocs_files.Files(all_files)


def on_post_build(config, **kwargs):
    _cleanup_temp()


def on_build_error(error, **kwargs):
    _cleanup_temp()


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------


def _cleanup_temp():
    global _temp_dir
    if _temp_dir and os.path.exists(_temp_dir):
        shutil.rmtree(_temp_dir, ignore_errors=True)
        _temp_dir = None


def _slugify(text: str) -> str:
    """Convert a category name to a URL-safe slug."""
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def _display_name(name: str) -> str:
    """Convert a filesystem-safe name to a display name.

    Replaces the safe separator ' - ' with ': ' so that folder names like
    'Reinforced Concrete - Punching' render as 'Reinforced Concrete: Punching'.
    """
    return name.replace(" - ", ": ")


def _load_examples(repo_root: Path) -> dict[str, dict[str, list[str]]]:
    """Read docs/examples.yml and return {group: {category: [stem, ...]}}."""
    yaml_path = repo_root / "docs" / "examples.yml"
    if not yaml_path.exists():
        raise SystemExit(
            f"render_examples: docs/examples.yml not found."
        )

    # Minimal hand-rolled YAML parser — avoids a hard PyYAML import.
    # Format (3 levels):
    #   GroupName:
    #     CategoryName:
    #       - stem
    examples: dict[str, dict[str, list[str]]] = {}
    current_group: str | None = None
    current_category: str | None = None
    for lineno, raw in enumerate(yaml_path.read_text(encoding="utf-8").splitlines(), 1):
        line = raw.rstrip()
        if not line or line.lstrip().startswith("#"):
            continue
        if line.startswith("    - "):          # 4-space list item → stem
            if current_category is None:
                raise SystemExit(f"render_examples: examples.yml line {lineno}: list item before any category key")
            examples[current_group][current_category].append(line[6:])
        elif line.startswith("  ") and line.endswith(":"):  # 2-space category key
            if current_group is None:
                raise SystemExit(f"render_examples: examples.yml line {lineno}: category before any group key")
            current_category = line.strip()[:-1]
            examples[current_group][current_category] = []
        elif not line.startswith(" ") and line.endswith(":"):  # top-level group key
            current_group = line[:-1]
            current_category = None
            examples[current_group] = {}
        else:
            raise SystemExit(f"render_examples: examples.yml line {lineno}: unexpected format: {line!r}")

    return examples


def _group_folders(repo_root: Path) -> dict[str, str]:
    """Return a mapping from group display name to its folder inside Examples/.

    The mapping is derived from the docs/examples.yml group names by matching
    them against actual sub-folders of Examples/.
    """
    examples_root = repo_root / "Examples"
    available = {d.name for d in examples_root.iterdir() if d.is_dir()}

    # Try an explicit well-known mapping first; fall back to matching by name.
    known: dict[str, str] = {
        "Structural Engineering Examples": "Structural",
        "Other Examples": "Engineering",
    }
    result: dict[str, str] = {}
    for group_name in _load_examples(repo_root):
        if group_name in known and known[group_name] in available:
            result[group_name] = known[group_name]
        else:
            # Fall back: look for a folder whose name appears in the group display name
            match = next((f for f in available if f.lower() in group_name.lower()), None)
            if match is None:
                raise SystemExit(
                    f"render_examples: Cannot map group '{group_name}' to a folder in Examples/.\n"
                    f"Available folders: {sorted(available)}"
                )
            result[group_name] = match
    return result


def _strip_headings(fragment: str) -> str:
    """Replace <h1>–<h6> tags with divs so they don't appear in the MkDocs TOC."""
    fragment = re.sub(r"<(h[1-6])([ >])", r'<div class="calcpad-\1"\2', fragment)
    fragment = re.sub(r"</(h[1-6])>", r"</div>", fragment)
    return fragment


# Matches the start of a leading Calcpad text comment whose body is an HTML
# comment, e.g. `'<!-- Some narrative markdown.` (single-line or opening line
# of a multi-line block). Used to lift a description from the top of a `.cpd`
# file into the rendered docs page above the example.
_DESC_OPEN_RE = re.compile(r"^'<!--\s*(.*?)\s*$")
# Matches a continuation line inside the description block (Calcpad text
# comment marker `'` followed by content). The closing `-->` may appear on
# any line.
_DESC_CONT_RE = re.compile(r"^'\s?(.*?)\s*$")


def _extract_first_line_desc(source: str) -> tuple[str, str]:
    """Return (description_markdown, source_without_description_block) if the
    source begins with a leading Calcpad text-comment HTML-comment block,
    otherwise ("", source).

    Supports both a single-line form and a multi-line form where each
    subsequent line starts with `'` and the block is closed with `-->`:

        '<!-- First sentence.
        'Second sentence.
        'Third sentence. -->
    """
    lines = source.splitlines(keepends=True)
    if not lines:
        return "", source
    open_match = _DESC_OPEN_RE.match(lines[0].rstrip("\r\n"))
    if not open_match:
        return "", source

    def _strip_close(text: str) -> tuple[str, bool]:
        if text.endswith("-->"):
            return text[:-3].rstrip(), True
        return text, False

    first_body, closed = _strip_close(open_match.group(1))
    collected: list[str] = []
    if first_body:
        collected.append(first_body)
    if closed:
        return "\n".join(collected), "".join(lines[1:])

    for i in range(1, len(lines)):
        cont_match = _DESC_CONT_RE.match(lines[i].rstrip("\r\n"))
        if not cont_match:
            # Malformed block — bail out and leave source untouched.
            return "", source
        body, closed = _strip_close(cont_match.group(1))
        if body:
            collected.append(body)
        if closed:
            return "\n".join(collected), "".join(lines[i + 1 :])

    # No closing `-->` found — treat as not a description block.
    return "", source


def _first_paragraph(text: str) -> str:
    """Return the first non-empty paragraph of a markdown string as a single
    line, with simple Markdown decorations removed. Suitable for use as an
    HTML <meta name="description"> value.
    """
    paragraph: list[str] = []
    for raw in text.splitlines():
        line = raw.strip()
        if not paragraph:
            if not line or line.startswith("#") or line.startswith("---"):
                continue
            paragraph.append(line)
        else:
            if not line:
                break
            paragraph.append(line)
    joined = " ".join(paragraph)
    joined = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", joined)
    joined = re.sub(r"[*_`]+", "", joined)
    return joined.strip()


def _render_category_page(category: str, cpd_files: list[Path], fragments: dict[Path, str]) -> str:
    """Build the Markdown content for one category page from pre-rendered fragments.

    The example grid is emitted as a raw HTML block (no markdown="block" attribute)
    so that Python-Markdown / md_in_html passes it through verbatim.  This prevents
    the md_in_html preprocessor from HTML-escaping '>' characters inside <script>
    tag bodies (which would break inline JavaScript such as arrow functions).
    The source code is HTML-escaped in Python before being written into <pre><code>.

    Optional narrative content for SEO / readability:
      - `<category-dir>/_intro.md` → emitted between the H1 and the first example;
        its first paragraph also becomes the page meta description.
      - First line of each `.cpd` file matching `'<!-- ... -->` → stripped from the
        highlighted source and emitted as Markdown between the H2 and the example
        grid. The HTML comment in the rendered fragment is harmless (invisible
        in browsers) and is left in place.
    """
    cat_dir = cpd_files[0].parent if cpd_files else None
    intro_path = cat_dir / "_intro.md" if cat_dir else None
    intro_text = (
        intro_path.read_text(encoding="utf-8")
        if intro_path is not None and intro_path.is_file()
        else ""
    )

    front_matter = ["---", "search:", "  exclude: true"]
    if intro_text:
        description = (
            _first_paragraph(intro_text).replace("\\", "\\\\").replace('"', "'")
        )
        if description:
            front_matter.append(f'description: "{description}"')
    front_matter.append("---\n")

    lines = ["\n".join(front_matter), f"# {_display_name(category)}\n"]

    if intro_text:
        lines.append(intro_text.rstrip() + "\n")

    for cpd_file in cpd_files:
        name = re.sub(
            r"\s*\bAnimated\b",
            ' <span title="Animated" aria-label="Animated">🎬</span>',
            cpd_file.stem,
        )
        lines.append(f"\n## {name}\n")

        source = cpd_file.read_text(encoding="utf-8", errors="replace")
        desc_md, source = _extract_first_line_desc(source)
        if desc_md:
            lines.append(desc_md + "\n")

        fragment = _strip_headings(fragments[cpd_file])
        highlighted_source = _pyg_highlight(
            source.rstrip(), _CALCPAD_LEXER, _CALCPAD_FORMATTER
        )

        # No markdown="block" on any div here — the outer <div class="example-grid">
        # has no markdown attribute so md_in_html passes the entire block through
        # unchanged (including blank lines and raw <script> content inside it).
        lines.append('<div class="example-grid">')
        lines.append('<figure>')
        lines.append(f'<figcaption>Code:</figcaption>')
        lines.append(highlighted_source)
        lines.append('</figure>')
        lines.append('<figure class="example-output">')
        lines.append(f'<figcaption>Rendered Output:</figcaption>')
        lines.append(fragment)
        lines.append('</figure>')
        lines.append('</div>\n')

    # Footer: link to the category folder on GitHub
    if cpd_files:
        cat_path = cpd_files[0].parent
        parts = cat_path.parts
        try:
            idx = next(i for i, p in enumerate(parts) if p == "Examples")
            rel = "/".join(urllib.parse.quote(p, safe="") for p in parts[idx + 1:])
        except StopIteration:
            rel = urllib.parse.quote(cat_path.name, safe="")
        github_url = f"https://github.com/imartincei/CalcpadCE/tree/main/Examples/{rel}"
        lines.append(f"\nSpotted an error? [Edit]({github_url}) these examples.\n")

    return "\n".join(lines)
