"""MkDocs hook that copies the shared fonts from Resources/Fonts into
docs/media, so the @font-face rules in stylesheets/calcpad-output.css resolve.

MkDocs can only serve files under docs_dir, so the fonts cannot be referenced
in place. The copies are gitignored and regenerated on every build.
"""

import shutil
from pathlib import Path

_SOURCE = Path(__file__).resolve().parents[2] / "Resources" / "Fonts"
_TARGET = Path(__file__).resolve().parents[1] / "media"
_PATTERNS = ("DejaVuSerifCondensed*.ttf", "Jost-*.otf", "DejaVuSerif-LICENSE", "Jost-LICENSE")


def on_config(config, **kwargs):
    if not _SOURCE.is_dir():
        raise FileNotFoundError(f"Shared fonts directory not found: {_SOURCE}")

    _TARGET.mkdir(parents=True, exist_ok=True)
    for pattern in _PATTERNS:
        for src in _SOURCE.glob(pattern):
            dst = _TARGET / src.name
            if not dst.exists() or src.stat().st_mtime > dst.stat().st_mtime:
                shutil.copy2(src, dst)
    return config
