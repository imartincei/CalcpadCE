import logging
import os
import re
import subprocess
import tempfile
import time
from concurrent.futures import ThreadPoolExecutor
from itertools import repeat
from pathlib import Path
from typing import Callable, Iterable


log = logging.getLogger("render_cpd")


def find_cli(repo_root: Path) -> Path:
    """Return the path to an existing Cli executable."""
    is_win = os.name == "nt"
    exe_name = "Cli.exe" if is_win else "Cli"
    rid = "win-x64" if is_win else "linux-x64"
    tfm = "net10.0"

    candidates = [
        repo_root / "cli-build" / exe_name,
        repo_root / "Calcpad.Cli" / "bin" / "Release" / tfm / rid / "publish" / exe_name,
        repo_root / "Calcpad.Cli" / "bin" / "Debug" / tfm / rid / "publish" / exe_name,
        repo_root / "Calcpad.Cli" / "bin" / "Debug" / tfm / exe_name,
        repo_root / "Calcpad.Cli" / "bin" / "Release" / tfm / exe_name,
    ]

    for candidate in candidates:
        if candidate.exists():
            log.info(f"Using CLI at {candidate}")
            return candidate

    searched = "\n  ".join(str(candidate) for candidate in candidates)
    raise FileNotFoundError(
        "Calcpad CLI executable not found.\n"
        f"Searched:\n  {searched}\n"
        "Build or publish it first, for example:\n"
        "  dotnet build Calcpad.Cli/"
    )


def render_fragment(
    cli_path: Path,
    cpd_path: Path,
    label: str | None = None,
    extra_env: dict[str, str] | None = None,
) -> bytes:
    """Run the CLI in body-only silent mode and return normalized HTML bytes."""
    name = label or cpd_path.name
    started = time.perf_counter()
    tmp_fd, out_path = tempfile.mkstemp(suffix=".html")
    os.close(tmp_fd)
    render_env = os.environ.copy()
    if extra_env is not None:
        render_env.update(extra_env)
    try:
        result = subprocess.run(
            [str(cli_path), str(cpd_path), out_path, "-b", "-s"],
            capture_output=True,
            timeout=60,
            env=render_env,
        )
        if result.returncode != 0:
            stderr = result.stderr.decode(errors="replace").strip()
            stdout = result.stdout.decode(errors="replace").strip()
            raise RuntimeError(
                f"CLI failed (exit {result.returncode}) for {cpd_path.name}"
                + (f"\n  stderr: {stderr}" if stderr else "")
                + (f"\n  stdout: {stdout}" if stdout else "")
            )
        if not (os.path.exists(out_path) and os.path.getsize(out_path) > 0):
            raise RuntimeError(f"CLI produced no output for {cpd_path.name}")

        html = Path(out_path).read_text(encoding="utf-8")
        html = re.sub(r'(src|href)\s*=\s*"\.\./\.\./\s*', r'\1="', html)
        html = html.replace("\r\n", "\n")
        elapsed_ms = round((time.perf_counter() - started) * 1000)
        log.info(f"Rendered  {name} [{elapsed_ms}ms]")
        return html.encode("utf-8")
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError(f"CLI timed out rendering {cpd_path.name}") from exc
    except OSError as exc:
        raise RuntimeError(f"Failed to run CLI for {cpd_path.name}: {exc}") from exc
    finally:
        if os.path.exists(out_path):
            os.unlink(out_path)


def render_many(
    cli_path: Path,
    cpd_paths: Iterable[Path],
    label_for: Callable[[Path], str] | None = None,
    max_workers: int | None = None,
    extra_env: dict[str, str] | None = None,
) -> dict[Path, bytes]:
    """Render multiple .cpd files in parallel, preserving input order."""
    paths = list(cpd_paths)
    if not paths:
        return {}

    workers = max_workers or min(os.cpu_count() or 1, len(paths))
    labels = [label_for(path) if label_for is not None else None for path in paths]

    with ThreadPoolExecutor(max_workers=workers) as pool:
        fragments = list(pool.map(render_fragment, repeat(cli_path), paths, labels, repeat(extra_env)))

    return dict(zip(paths, fragments))
