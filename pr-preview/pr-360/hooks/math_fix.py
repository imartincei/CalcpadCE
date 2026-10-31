"""MkDocs hook to convert GitHub-flavored math syntax ($`...`$) to standard
LaTeX delimiters ($...$ / $$...$$) so that pymdownx.arithmatex can process them.
"""

import re


def _replace_match(match):
    content = match.group(1)
    lines = []
    for line in content.splitlines():
        # Strip blockquote markers (>) that appear inside multi-line math
        line = re.sub(r"^(\s*>\s*)+", "", line).strip()
        if line:
            lines.append(line)
    content = " ".join(lines)
    return f"${content}$"


def on_page_markdown(markdown, **kwargs):
    return re.sub(r"\$`(.*?)`\$", _replace_match, markdown, flags=re.DOTALL)
