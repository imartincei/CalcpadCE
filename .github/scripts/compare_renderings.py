"""Compare committed `*.html.stub` files to fresh Calcpad CLI renderings.

For every non-denylisted ``*.cpd`` under ``Examples/`` and ``Tests/`` this script
runs the Calcpad CLI with a fixed ``Settings.xml`` and
compares the resulting HTML against the committed ``*.html.stub`` sibling.

Two modes are supported:

* ``--write``: overwrite ``*.html.stub`` with the prettified CLI output.
    When an existing stub is present, this first reuses the verify-mode
    reconciliation against the committed stub so cross-platform last-digit noise
    and local ``file://`` paths are not written back. Brand-new stubs are still
    written from the raw rendering.
* ``--force``: only valid with ``--write``. Skip that reconciliation and use
    the raw rendered output instead before prettifying it.
* default (verify): pretty-print the rendered HTML and assert it matches the
    committed stub. Differences are printed as unified diffs and the script exits
    with a non-zero status.

Before comparing in verify mode, and before writing in default ``--write``
mode when a stub already exists, two reconciliations run on the rendered bytes
to absorb cross-platform noise that is not a real rendering change:

* ``_reconcile_numbers`` parses every floating-point literal (decimal,
  ``e``-notation, and ``×10<sup>…</sup>`` HTML form) outside HTML tags, aligns
  the committed and rendered number streams with ``difflib.SequenceMatcher``,
  and substitutes the rendered text with the committed text when the two values
  agree within the display precision implied by the printed digits, are close
  per ``math.isclose(rel_tol=NUMBER_REL_TOL, abs_tol=NUMBER_ABS_TOL)``, or are
  both effectively zero under those same tolerances.
* ``_reconcile_file_url`` rewrites the first ``href="file://…"`` link in the
    rendered HTML to match the committed stub, so local vs CI workspace paths do
    not show up as diffs.

Because reconciliation only substitutes aligned number literals that remain
equivalent under the configured tolerances, structural changes and real value
changes still flow through to the written or verified stub.

Stub files are normalised through ``BeautifulSoup.prettify`` plus a small
canonicaliser that strips trailing whitespace and collapses single-line leaf
elements to present a human-readable diff.
"""

import argparse
from concurrent.futures import ProcessPoolExecutor
import difflib
from fnmatch import fnmatch
from functools import partial
import logging
import math
import os
import re
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from bs4 import BeautifulSoup

from render_cpd import find_cli, render_many


GUIDANCE = (
    "Rendered output has changed. If the changes are intentional, run "
    "`python .github/scripts/compare_renderings.py --write` locally and commit the updated *.html.stub files "
    "so the PR diff shows the impact on rendered output. See DEVELOPER.md for details."
)
LEAF_TEXT_RE = re.compile(
    r"(?m)^(?P<indent>[ \t]*)<(?P<tag>[a-zA-Z][\w-]*)(?P<attrs>(?:\s+[^<>]*)?)>\n"
    r"(?P=indent)[ \t]+(?P<text>[^<>\n][^<>\n]*?)\n"
    r"(?P=indent)</(?P=tag)>$"
)
TRAILING_WHITESPACE_RE = re.compile(r"[ \t]+$", re.MULTILINE)
MAX_FULL_DIFF_FILES = 3
MAX_DIFF_LINES = 200
MAX_DIFF_LINE_CHARS = 500
REPO_ROOT = Path(__file__).resolve().parents[2]
CPD_ROOT = (REPO_ROOT / "Examples", REPO_ROOT / "Tests")
COMPARISON_SETTINGS = Path(__file__).with_name("CliSettingsComparison.xml")
# These can be disabled for testing the comparison algorithm.
# HW and SW floating-point arithemtics generate slightly different results at the last digits.
STUB_RENDER_ENV = {
    "DOTNET_EnableAVX2": "1",
    "DOTNET_EnableHWIntrinsic": "1",
}
NUMBER_REL_TOL = 1e-9
NUMBER_ABS_TOL = 1e-12
NUMBER_RE = re.compile(
    r"""
    (?<![\w.])
    (?P<text>
        (?P<sign>[+-]?)
        (?P<int_part>\d+)
        (?:\.(?P<frac>\d+))?
        (?:
            ×10\s*<sup>\s*(?P<html_exp>[+-]?\d+)\s*</sup>
            |
            [eE](?P<sci_exp>[+-]?\d+)
        )?
    )
    (?![\w.])
    """,
    re.VERBOSE,
)
FILE_URL_RE = re.compile(rb'href="file:/+[^"]+"')
DENYLIST_GLOBS = {
# Large precision errors (up to 1.0) due to accumulated rounding or numerical approximations
    "Examples/Engineering/Elastic Analysis/Deep Beam.cpd",
    "Examples/Engineering/Finite Elements/Flat Slab FEA Optimized.cpd",
    "Examples/Engineering/Finite Elements/Flat Slab FEA.cpd",
    "Examples/Engineering/Geometrical/Solution of a Triangle.cpd",
    "Examples/Engineering/Numerical/System of Nonlinear Equations.cpd",
    "Examples/Engineering/Special Math Functions/Special Math Functions.cpd",
# Random input data and thus non-deterministic rendering output
    "Examples/Engineering/Fractals/Sierpinski Christmas Tree.cpd",
    "Examples/Engineering/Numerical/Monte Carlo Pi.cpd",
# Animations whose renderings take several megabytes of storage space in Git
    "Examples/Engineering/Waves/Waves 2D on Water Interference*",
    "Examples/Engineering/Fractals/Mandelbrot Set.cpd",
    "Examples/Engineering/Pendulums/Elastic Damped Pendulum Animated.cpd",
# Just a helper include file, not a standalone rendering
    "**/svg_drawing.cpd",
}


@dataclass(frozen=True)
class NumberMatch:
    span: tuple[int, int]
    text: str
    value: float | None
    key: str
    display_tol: float


@dataclass(frozen=True)
class CompareResult:
    stub_path: Path
    status: str
    existing: bytes | None = None
    rendered: bytes | None = None


def _normalize_eol(content: bytes) -> bytes:
    return content.replace(b"\r\n", b"\n")


def _configure_render_logging() -> None:
    render_log = logging.getLogger("render_cpd")
    render_log.handlers.clear()
    render_log.setLevel(logging.INFO)
    render_log.propagate = False

    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(logging.Formatter("%(message)s"))
    render_log.addHandler(handler)


def _canonicalize_pretty_html(pretty: str) -> str:
    pretty = TRAILING_WHITESPACE_RE.sub("", pretty)
    pretty = LEAF_TEXT_RE.sub(
        lambda match: f"{match['indent']}<{match['tag']}{match['attrs']}>{match['text']}</{match['tag']}>",
        pretty,
    )
    return TRAILING_WHITESPACE_RE.sub("", pretty)


def _prettify(content: bytes) -> bytes:
    pretty = BeautifulSoup(content.decode("utf-8"), "html.parser").prettify()
    pretty = _canonicalize_pretty_html(pretty)
    return _normalize_eol(pretty.encode("utf-8"))


def _number_key(value: float | None, text: str) -> str:
    if value is None:
        return f"invalid:{text}"
    if not math.isfinite(value):
        return f"nonfinite:{text}"
    if value == 0.0:
        return "zero"

    mantissa, exponent = f"{abs(value):.8e}".split("e")
    sign = "-" if value < 0 else "+"
    return f"{sign}{mantissa}e{int(exponent):+d}"


def _inside_html_tag(content: str, index: int) -> bool:
    return content.rfind("<", 0, index) > content.rfind(">", 0, index)


def _iter_numbers(content: str) -> list[NumberMatch]:
    numbers: list[NumberMatch] = []
    for match in NUMBER_RE.finditer(content):
        start, end = match.span("text")
        if _inside_html_tag(content, start):
            continue

        frac = match.group("frac")
        exponent_text = match.group("html_exp") or match.group("sci_exp")
        int_part = match.group("int_part")
        if frac is None and exponent_text is None and int(int_part) != 0:
            continue

        text = match.group("text")
        mantissa_text = f"{match.group('sign')}{int_part}"
        if frac is not None:
            mantissa_text = f"{mantissa_text}.{frac}"

        try:
            exponent = int(exponent_text) if exponent_text is not None else 0
            value = float(mantissa_text) * (10.0 ** exponent)
            display_tol = 0.5 * (10.0 ** (exponent - len(frac or "")))
        except (OverflowError, ValueError):
            value = None
            display_tol = 0.0

        numbers.append(NumberMatch((start, end), text, value, _number_key(value, text), display_tol))

    return numbers


def _should_replace_number(committed: NumberMatch, rendered: NumberMatch, rel_tol: float, abs_tol: float) -> bool:
    if committed.value is None or rendered.value is None:
        return False
    if not math.isfinite(committed.value) or not math.isfinite(rendered.value):
        return False

    near_zero_abs_tol = max(rel_tol, abs_tol)
    if math.isclose(committed.value, 0.0, rel_tol=rel_tol, abs_tol=near_zero_abs_tol) and math.isclose(
        rendered.value, 0.0, rel_tol=rel_tol, abs_tol=near_zero_abs_tol
    ):
        return True

    difference = abs(committed.value - rendered.value)
    if difference <= committed.display_tol + rendered.display_tol:
        return True

    return math.isclose(committed.value, rendered.value, rel_tol=rel_tol, abs_tol=abs_tol)


def _reconcile_numbers(
    rendered: bytes,
    committed: bytes,
    *,
    rel_tol: float = NUMBER_REL_TOL,
    abs_tol: float = NUMBER_ABS_TOL,
) -> bytes:
    rendered_text = rendered.decode("utf-8")
    committed_text = committed.decode("utf-8")
    rendered_numbers = _iter_numbers(rendered_text)
    committed_numbers = _iter_numbers(committed_text)

    if not rendered_numbers or not committed_numbers:
        return rendered

    matcher = difflib.SequenceMatcher(
        a=[number.key for number in committed_numbers],
        b=[number.key for number in rendered_numbers],
        autojunk=False,
    )
    replacements: list[tuple[int, int, str]] = []

    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        if tag in {"delete", "insert"}:
            continue

        pair_count = i2 - i1 if tag == "equal" else min(i2 - i1, j2 - j1)
        for offset in range(pair_count):
            committed_number = committed_numbers[i1 + offset]
            rendered_number = rendered_numbers[j1 + offset]
            if _should_replace_number(committed_number, rendered_number, rel_tol, abs_tol):
                replacements.append((*rendered_number.span, committed_number.text))

    if not replacements:
        return rendered

    pieces: list[str] = []
    last_index = len(rendered_text)
    for start, end, replacement in reversed(replacements):
        pieces.append(rendered_text[end:last_index])
        pieces.append(replacement)
        last_index = start
    pieces.append(rendered_text[:last_index])
    reconciled = "".join(reversed(pieces))
    return reconciled.encode("utf-8")


def _reconcile_file_url(rendered: bytes, committed: bytes) -> bytes:
    rendered_match = FILE_URL_RE.search(rendered)
    committed_match = FILE_URL_RE.search(committed)
    if rendered_match is None or committed_match is None:
        return rendered

    return (
        rendered[: rendered_match.start()]
        + committed[committed_match.start() : committed_match.end()]
        + rendered[rendered_match.end() :]
    )


def _repo_rel(path: Path) -> str:
    return path.relative_to(REPO_ROOT).as_posix()


def _is_denylisted(path: Path) -> bool:
    rel = _repo_rel(path)
    return any(fnmatch(rel, pattern) for pattern in DENYLIST_GLOBS)


def _discover() -> list[Path]:
    return [
        path
        for path in sorted(
            (cpd_path for root in CPD_ROOT if root.exists() for cpd_path in root.rglob("*.cpd")),
            key=lambda item: item.as_posix(),
        )
        if not _is_denylisted(path)
    ]


def _stub_path(cpd_path: Path) -> Path:
    return cpd_path.with_suffix(".html.stub")


def _runtime_settings_path(cli_path: Path) -> Path:
    if os.name == "nt":
        return cli_path.parent / "Settings.xml"

    return Path.home() / "Documents" / ".config" / "calcpad" / "Settings.xml"


def _prepare_runtime_settings(cli_path: Path) -> tuple[Path, bytes | None]:
    if not COMPARISON_SETTINGS.is_file():
        raise FileNotFoundError(f"Comparison settings file not found: {COMPARISON_SETTINGS}")

    runtime_settings = _runtime_settings_path(cli_path)
    try:
        original = runtime_settings.read_bytes() if runtime_settings.exists() else None
        runtime_settings.parent.mkdir(parents=True, exist_ok=True)
        runtime_settings.write_bytes(COMPARISON_SETTINGS.read_bytes())
    except OSError as exc:
        raise RuntimeError(f"Cannot prepare runtime Settings.xml at {runtime_settings}: {exc}") from exc

    return runtime_settings, original


def _restore_runtime_settings(runtime_settings: Path, original: bytes | None) -> None:
    try:
        if original is None:
            if runtime_settings.exists():
                runtime_settings.unlink()
        else:
            runtime_settings.write_bytes(original)
    except OSError as exc:
        raise RuntimeError(f"Cannot restore runtime Settings.xml at {runtime_settings}: {exc}") from exc


def _read_normalized(path: Path) -> bytes:
    return _normalize_eol(path.read_bytes())


def _truncate_display_line(line: str) -> str:
    if len(line) <= MAX_DIFF_LINE_CHARS:
        return line

    truncated = len(line) - MAX_DIFF_LINE_CHARS
    return f"{line[:MAX_DIFF_LINE_CHARS]} ... [truncated {truncated} chars]"


def _print_diff(stub_path: Path, existing: bytes, rendered: bytes, truncate: bool) -> None:
    rel = _repo_rel(stub_path)
    diff_lines = list(
        difflib.unified_diff(
            existing.decode("utf-8", errors="replace").splitlines(),
            rendered.decode("utf-8", errors="replace").splitlines(),
            fromfile=f"{rel} (committed)",
            tofile=f"{rel} (rendered)",
            lineterm="",
        )
    )
    diff_lines = [_truncate_display_line(line) for line in diff_lines]

    print(f"\n=== {rel} ===")
    if not diff_lines:
        print("diff unavailable after normalization")
        return

    if truncate and len(diff_lines) > MAX_DIFF_LINES:
        print("\n".join(diff_lines[:MAX_DIFF_LINES]))
        print(f"... diff truncated after {MAX_DIFF_LINES} lines ...")
        return

    print("\n".join(diff_lines))


def _print_progress(action: str, stub_path: Path, elapsed_ms: int) -> None:
    print(f"{action:<10}{stub_path.name} [{elapsed_ms}ms]")


def compare_one_write(cpd_path: Path, rendered: bytes, *, force: bool = False) -> CompareResult:
    started = time.perf_counter()
    stub_path = _stub_path(cpd_path)
    existing = _read_normalized(stub_path) if stub_path.exists() else None

    if existing is not None and not force:
        rendered = _reconcile_numbers(rendered, existing)
        rendered = _reconcile_file_url(rendered, existing)

    pretty_rendered = _prettify(rendered)
    if existing == pretty_rendered:
        status = "unchanged"
    else:
        stub_path.write_bytes(pretty_rendered)
        status = "created" if existing is None else "written"

    elapsed_ms = round((time.perf_counter() - started) * 1000)
    _print_progress("Wrote", stub_path, elapsed_ms)
    return CompareResult(stub_path, status)


def compare_one_verify(cpd_path: Path, rendered: bytes) -> CompareResult:
    started = time.perf_counter()
    stub_path = _stub_path(cpd_path)
    if not stub_path.exists():
        elapsed_ms = round((time.perf_counter() - started) * 1000)
        _print_progress("Compared", stub_path, elapsed_ms)
        return CompareResult(stub_path, "missing")

    existing = _read_normalized(stub_path)
    rendered = _reconcile_numbers(rendered, existing)
    rendered = _reconcile_file_url(rendered, existing)
    pretty_rendered = _prettify(rendered)
    status = "unchanged"
    if existing != pretty_rendered:
        status = "mismatch"

    elapsed_ms = round((time.perf_counter() - started) * 1000)
    _print_progress("Compared", stub_path, elapsed_ms)
    return CompareResult(stub_path, status, existing, pretty_rendered)


def compare_many(
    rendered_by_cpd: dict[Path, bytes],
    compare_one: Callable[[Path, bytes], CompareResult],
    max_workers: int | None = None,
) -> list[CompareResult]:
    paths = list(rendered_by_cpd)
    if not paths:
        return []

    workers = max_workers or min(os.cpu_count() or 1, len(paths))
    fragments = [rendered_by_cpd[path] for path in paths]

    with ProcessPoolExecutor(max_workers=workers) as pool:
        return list(pool.map(compare_one, paths, fragments))


def _write_stubs(rendered_by_cpd: dict[Path, bytes], force: bool = False) -> int:
    started = time.perf_counter()
    results = compare_many(rendered_by_cpd, partial(compare_one_write, force=force))
    created = sum(result.status == "created" for result in results)
    unchanged = sum(result.status == "unchanged" for result in results)
    written = sum(result.status in {"created", "written"} for result in results)
    elapsed_ms = round((time.perf_counter() - started) * 1000)

    print(f"Wrote {written} stub(s); {unchanged} unchanged; {created} new. [{elapsed_ms}ms]")
    return 0


def _verify_stubs(rendered_by_cpd: dict[Path, bytes]) -> int:
    started = time.perf_counter()
    results = compare_many(rendered_by_cpd, compare_one_verify)
    elapsed_ms = round((time.perf_counter() - started) * 1000)

    missing = [result.stub_path for result in results if result.status == "missing"]
    mismatches = [
        (result.stub_path, result.existing, result.rendered)
        for result in results
        if result.status == "mismatch" and result.existing is not None and result.rendered is not None
    ]

    if not missing and not mismatches:
        print(f"OK: {len(rendered_by_cpd)} renderings match. [{elapsed_ms}ms]")
        return 0

    for stub_path in missing:
        print(f"ERROR: Missing stub: {_repo_rel(stub_path)}")

    truncate = len(mismatches) > MAX_FULL_DIFF_FILES
    if truncate:
        print(f"\n{len(mismatches)} stub file(s) differ; showing capped diffs.")

    for stub_path, existing, rendered in mismatches:
        _print_diff(stub_path, existing, rendered, truncate)

    print(f"\n{GUIDANCE}")
    print(f"Compared {len(rendered_by_cpd)} file(s). [{elapsed_ms}ms]")
    return 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Compare committed HTML stubs to fresh Calcpad renderings.")
    parser.add_argument("--write", action="store_true", help="Overwrite .html.stub files instead of verifying them.")
    parser.add_argument(
        "--force",
        action="store_true",
        help="With --write, skip number/file-URL reconciliation and use the raw rendered output.",
    )
    args = parser.parse_args(argv)

    if args.force and not args.write:
        parser.error("--force requires --write")

    _configure_render_logging()

    try:
        cli_path = find_cli(REPO_ROOT)
        cpd_paths = _discover()
        runtime_settings, original_settings = _prepare_runtime_settings(cli_path)
        try:
            rendered_by_cpd = render_many(cli_path, cpd_paths, extra_env=STUB_RENDER_ENV)
        finally:
            _restore_runtime_settings(runtime_settings, original_settings)
    except FileNotFoundError as exc:
        print(exc, file=sys.stderr)
        return 1
    except RuntimeError as exc:
        print(f"compare_renderings: {exc}", file=sys.stderr)
        return 1

    if args.write:
        return _write_stubs(rendered_by_cpd, force=args.force)
    return _verify_stubs(rendered_by_cpd)


if __name__ == "__main__":
    raise SystemExit(main())
