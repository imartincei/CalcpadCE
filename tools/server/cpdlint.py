#!/usr/bin/env python3
"""cpdlint - lint Calcpad (.cpd) files.

Two layers of checking:

1. The real Calcpad engine. Launches Calcpad.Server.exe and posts each file to
   POST /api/calcpad/lint, so includes resolve and macro arity, undefined
   variables, unbalanced blocks and the rest come back as CPD-xxxx diagnostics.
   This is authoritative - it is the same analyzer the editor uses.

2. Workspace conventions the engine has no opinion about, recorded in
   CLAUDE.md. These are silent at runtime, which is exactly why they
   need a linter.

Usage
-----
    python tools/server/cpdlint.py <path> [<path> ...] [options]

    <path>              .cpd file, or a directory to scan recursively.
    --info              Include "information" diagnostics (unused variables and
                        such). Off by default because it is noisy.
    --ignore CODE       Suppress a diagnostic code. Repeatable.
    --no-default-ignore Do not apply the built-in ignore list.
    --no-conventions    Engine diagnostics only, skip layer 2.
    --no-engine         Convention checks only, do not launch the server.
    --server PATH       Path to Calcpad.Server.exe. Defaults to the newest build
                        under Calcpad.Web/backend/bin.
    --url URL           Use an already-running server instead of launching one,
                        e.g. http://127.0.0.1:50062
    --token TOKEN       Value for the X-Calcpad-Token header, when the server
                        was launched with CALCPAD_API_TOKEN set.
    --library DIR       Workspace root. Defaults to the repo root.
    -q, --quiet         Print only the summary line.

Exit code is 1 if anything of "error" severity was reported, otherwise 0.

Convention rules (layer 2)
--------------------------
CPW-DOLLAR    A "$" inside quoted comment text. Calcpad treats "$" as a macro
              marker, so naming a macro in full inside a comment can confuse
              the parser. Write the bare name without the trailing marker.
              Macro definition bodies are skipped.

CPW-EXPRARG   A macro argument that is an arithmetic expression. Calcpad does
              not reliably evaluate inline math in macro arguments. Assign it
              to a named variable first and pass that variable.
CPW-LIBPATH   The file uses "{library}" without declaring "#LibraryPath".
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

DEF_RE = re.compile(r"^\s*#def\s+([A-Za-z_][A-Za-z0-9_]*)\$\s*(\((.*?)\))?")
CALL_RE = re.compile(r"(?<![A-Za-z0-9_])([A-Za-z_][A-Za-z0-9_]*)\$\(")

# Operators that make an argument an expression rather than a bare term.
# A leading minus and the "*" inside a unit like kip*ft are not flagged.
EXPR_OP_RE = re.compile(r"(?<=[A-Za-z0-9_)\]])\s*[-+/^]\s*(?=[A-Za-z0-9_(\[])")
MULT_RE = re.compile(r"(?<=[A-Za-z0-9_)\]])\s*\*\s*(?=[A-Za-z0-9_(\[])")
NUM_UNIT_RE = re.compile(r"^[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?\s*[A-Za-z_%°′″][A-Za-z0-9_^*/°′″]*$")
# Two words separated only by a space is prose passed to a text macro, not math.
PROSE_RE = re.compile(r"[A-Za-z0-9]\s+[A-Za-z0-9]")


class Finding:
    __slots__ = ("path", "line", "column", "code", "severity", "message")

    def __init__(self, path, line, column, code, severity, message):
        self.path = path
        self.line = line
        self.column = column
        self.code = code
        self.severity = severity
        self.message = message

    @property
    def is_error(self):
        return self.severity == "error"

    def __str__(self):
        col = f":{self.column}" if self.column else ""
        return f"{self.path}:{self.line}{col}: {self.severity}: {self.code}: {self.message}"


# --------------------------------------------------------------------------
# file helpers
# --------------------------------------------------------------------------

def read_text(path):
    for enc in ("utf-8-sig", "utf-8", "cp1252"):
        try:
            with open(path, "r", encoding=enc) as fh:
                return fh.read()
        except UnicodeDecodeError:
            continue
    raise OSError(f"cannot decode {path}")


def iter_cpd(paths):
    seen = set()
    for p in paths:
        if os.path.isdir(p):
            for root, dirs, files in os.walk(p):
                dirs[:] = [d for d in dirs if not d.startswith(".")]
                for name in sorted(files):
                    if name.lower().endswith(".cpd"):
                        full = os.path.abspath(os.path.join(root, name))
                        if full not in seen:
                            seen.add(full)
                            yield full
        elif p.lower().endswith(".cpd"):
            full = os.path.abspath(p)
            if full not in seen:
                seen.add(full)
                yield full


# --------------------------------------------------------------------------
# layer 1 - the Calcpad engine
# --------------------------------------------------------------------------

class CalcpadServer:
    """Launches Calcpad.Server.exe, or attaches to one already running."""

    def __init__(self, exe=None, url=None, token=None):
        self.exe = exe
        self.url = url.rstrip("/") if url else None
        self.token = token or os.environ.get("CALCPAD_API_TOKEN")
        self.proc = None
        self.port_file = None

    def __enter__(self):
        if self.url:
            return self
        if not self.exe or not os.path.isfile(self.exe):
            raise OSError(f"Calcpad.Server.exe not found at {self.exe!r}. Pass --server or --url")
        self.exe = os.path.abspath(self.exe)

        fd, self.port_file = tempfile.mkstemp(prefix="cpdlint-port-", suffix=".txt")
        os.close(fd)
        os.remove(self.port_file)

        self.proc = subprocess.Popen(
            [
                self.exe,
                "--port-file", self.port_file,
                "--parent-pid", str(os.getpid()),
                "--no-exit-on-stdin-close",
            ],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            cwd=os.path.dirname(self.exe),
        )

        deadline = time.time() + 60
        while time.time() < deadline:
            if self.proc.poll() is not None:
                raise OSError(f"Calcpad.Server exited with code {self.proc.returncode}")
            if os.path.exists(self.port_file):
                text = ""
                try:
                    with open(self.port_file, "r", encoding="utf-8") as fh:
                        text = fh.read().strip()
                except OSError:
                    pass
                if text:
                    self.url = text.splitlines()[0].strip().rstrip("/")
                    break
            time.sleep(0.1)
        else:
            raise OSError("timed out waiting for Calcpad.Server to report its port")

        self._wait_healthy()
        return self

    def _wait_healthy(self):
        deadline = time.time() + 30
        while time.time() < deadline:
            try:
                self._get("/api/calcpad/health")
                return
            except Exception:
                time.sleep(0.15)
        raise OSError(f"Calcpad.Server at {self.url} never became healthy")

    def __exit__(self, *exc):
        if self.proc and self.proc.poll() is None:
            self.proc.terminate()
            try:
                self.proc.wait(timeout=10)
            except subprocess.TimeoutExpired:
                self.proc.kill()
        if self.port_file and os.path.exists(self.port_file):
            try:
                os.remove(self.port_file)
            except OSError:
                pass
        return False

    def _headers(self):
        h = {"Content-Type": "application/json"}
        if self.token:
            h["X-Calcpad-Token"] = self.token
        return h

    def _get(self, route):
        req = urllib.request.Request(self.url + route, headers=self._headers())
        with urllib.request.urlopen(req, timeout=10) as resp:
            return json.loads(resp.read().decode("utf-8"))

    def lint(self, path):
        payload = json.dumps({"content": read_text(path), "sourceFilePath": path}).encode("utf-8")
        req = urllib.request.Request(
            self.url + "/api/calcpad/lint", data=payload, headers=self._headers(), method="POST"
        )
        with urllib.request.urlopen(req, timeout=180) as resp:
            return json.loads(resp.read().decode("utf-8"))


def engine_findings(server, path):
    try:
        result = server.lint(path)
    except urllib.error.HTTPError as e:
        return [Finding(path, 0, 0, "CPW-SERVER", "error", f"lint request failed: {e.code} {e.reason}")]
    except Exception as e:  # noqa: BLE001 - surface anything the server does to us
        return [Finding(path, 0, 0, "CPW-SERVER", "error", f"lint request failed: {e}")]

    out = []
    for d in result.get("diagnostics", []):
        out.append(
            Finding(
                path,
                int(d.get("line", 0)) + 1,      # the API is zero-based, editors are not
                int(d.get("column", 0)) + 1,
                d.get("code", "CPD-????"),
                d.get("severity", "error"),
                d.get("message", ""),
            )
        )
    return out


# --------------------------------------------------------------------------
# layer 2 - workspace conventions
# --------------------------------------------------------------------------

def split_args(text, start):
    """Split a call's argument list. start is the index just past its '('."""
    depth, quote, args, buf, i = 1, None, [], [], start
    while i < len(text):
        ch = text[i]
        if quote:
            if ch == quote:
                quote = None
            buf.append(ch)
        elif ch in "'\"":
            quote = ch
            buf.append(ch)
        elif ch == "(":
            depth += 1
            buf.append(ch)
        elif ch == ")":
            depth -= 1
            if depth == 0:
                args.append("".join(buf))
                return args
            buf.append(ch)
        elif ch == ";" and depth == 1:
            args.append("".join(buf))
            buf = []
        else:
            buf.append(ch)
        i += 1
    return None


def mask_quoted(line):
    """Blank out quoted comment text, quotes included. Quotes close at end of line."""
    out, quote = [], None
    for ch in line:
        if quote:
            out.append(" ")
            if ch == quote:
                quote = None
        elif ch in "'\"":
            quote = ch
            out.append(" ")
        else:
            out.append(ch)
    return "".join(out)


def check_comment_dollar(path, n, line, out):
    code = mask_quoted(line)
    for i, ch in enumerate(line):
        if ch == "$" and code[i] != "$":
            out.append(
                Finding(path, n, i + 1, "CPW-DOLLAR", "warning",
                        "comment text contains '$'. Refer to macros by their bare name")
            )
            return


def check_expr_args(path, n, line, out):
    if DEF_RE.match(line):
        return
    line = mask_quoted(line)
    for m in CALL_RE.finditer(line):
        name = m.group(1)
        args = split_args(line, m.end())
        if args is None:
            continue
        for a in args:
            a = a.strip()
            if not a or a.startswith(("'", '"')):
                continue
            if NUM_UNIT_RE.match(a) or PROSE_RE.search(a):
                continue
            if EXPR_OP_RE.search(a) or MULT_RE.search(a):
                out.append(
                    Finding(path, n, m.start() + 1, "CPW-EXPRARG", "warning",
                            f"{name} argument {a!r} is an expression. Assign it to a "
                            f"named variable first and pass that")
                )

def check_library_path(path, text, out):
    if "{library}" in text and not re.search(r"^\s*#LibraryPath\b", text, re.M):
        out.append(
            Finding(path, 1, 1, "CPW-LIBPATH", "error",
                    "file uses '{library}' but never declares #LibraryPath")
        )


def convention_findings(path):
    out = []
    text = read_text(path)
    lines = text.splitlines()
    in_def = False
    for n, line in enumerate(lines, 1):
        if DEF_RE.match(line):
            in_def = "=" not in line
            continue
        if in_def:
            if line.strip().lower().startswith("#end def"):
                in_def = False
            continue
        check_comment_dollar(path, n, line, out)
        check_expr_args(path, n, line, out)
    check_library_path(path, text, out)
    return out


# --------------------------------------------------------------------------

SEVERITY_RANK = {"error": 0, "warning": 1, "information": 2}


REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def find_server(root=REPO_ROOT):
    """Newest Calcpad.Server.exe built under Calcpad.Web/backend/bin, or None."""
    bin_dir = os.path.join(root, "Calcpad.Web", "backend", "bin")
    found = [
        os.path.join(bin_dir, config, sub, "Calcpad.Server.exe")
        for config in ("Release", "Debug")
        for sub in ("net10.0", os.path.join("net10.0", "win-x64"))
    ]
    found = [p for p in found if os.path.isfile(p)]
    return max(found, key=os.path.getmtime) if found else None


def main(argv=None):
    default_library = REPO_ROOT
    default_exe = find_server()

    ap = argparse.ArgumentParser(
        prog="cpdlint",
        description="Lint Calcpad .cpd files with the Calcpad engine plus workspace conventions.",
    )
    ap.add_argument("paths", nargs="+", help=".cpd files or directories")
    ap.add_argument("--info", action="store_true", help="include information-severity diagnostics")
    ap.add_argument("--ignore", action="append", default=[], metavar="CODE", help="suppress a code")
    ap.add_argument("--no-default-ignore", action="store_true", help="drop the built-in ignore list")
    ap.add_argument("--no-conventions", action="store_true", help="engine diagnostics only")
    ap.add_argument("--no-engine", action="store_true", help="convention checks only")
    ap.add_argument("--server", default=default_exe, help="path to Calcpad.Server.exe")
    ap.add_argument("--url", default=None, help="use an already-running server at this base URL")
    ap.add_argument("--token", default=None, help="X-Calcpad-Token value")
    ap.add_argument("--library", default=default_library, help="workspace root")
    ap.add_argument("-q", "--quiet", action="store_true", help="print only the summary")
    args = ap.parse_args(argv)

    ignore = set(args.ignore)

    targets = list(iter_cpd(args.paths))
    if not targets:
        print("cpdlint: no .cpd files found", file=sys.stderr)
        return 1

    findings = []

    if not args.no_conventions:
        for path in targets:
            findings.extend(convention_findings(path))

    if not args.no_engine:
        try:
            with CalcpadServer(exe=args.server, url=args.url, token=args.token) as server:
                if not args.quiet:
                    print(f"cpdlint: engine at {server.url}", file=sys.stderr)
                for path in targets:
                    findings.extend(engine_findings(server, path))
        except OSError as e:
            print(f"cpdlint: {e}", file=sys.stderr)
            return 2

    findings = [f for f in findings if f.code not in ignore]
    if not args.info:
        findings = [f for f in findings if f.severity != "information"]

    errors = [f for f in findings if f.is_error]
    warnings = [f for f in findings if f.severity == "warning"]

    if not args.quiet:
        findings.sort(key=lambda f: (f.path, SEVERITY_RANK.get(f.severity, 3), f.line, f.code))
        current = None
        for f in findings:
            if f.path != current:
                current = f.path
                print(f"\n{os.path.relpath(f.path, args.library) if f.path.startswith(args.library) else f.path}")
            loc = f"{f.line}:{f.column}" if f.column else str(f.line)
            print(f"  {loc:<9} {f.severity:<11} {f.code:<10} {f.message}")

    print(
        f"\ncpdlint: {len(targets)} file(s), {len(errors)} error(s), {len(warnings)} warning(s)"
        + (f", ignoring {', '.join(sorted(ignore))}" if ignore else "")
    )
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
