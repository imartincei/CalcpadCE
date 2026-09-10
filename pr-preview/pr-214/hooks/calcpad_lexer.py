"""Pygments lexer for the Calcpad language.

Used at MkDocs build time by docs/hooks/render_examples.py to syntax-highlight
example .cpd source code, and (when registered into pygments) by pymdownx.highlight
to highlight ```calcpad fenced blocks in narrative documentation pages.

Token categories are derived from:
  - Calcpad.Cli/Syntax/Sublime/calcpad.sublime-syntax (regex patterns)
  - Calcpad.Wpf/HighLighter.cs (canonical operator/keyword sets)
  - Calcpad.Core/Calculator/{Calculator,RealCalculator,VectorCalculator,
    MatrixCalculator}.cs (canonical built-in function names)
"""

from pygments.lexer import RegexLexer, bygroups, include, words
from pygments.token import (
    Comment,
    Keyword,
    Name,
    Number,
    Operator,
    Punctuation,
    Text,
    Whitespace,
)


# ---------------------------------------------------------------------------
# Canonical name sets (mirror Calcpad.Core/Calculator/*.cs)
# Update these lists when corresponding C# dictionaries change.
# ---------------------------------------------------------------------------

# Calculator.cs FunctionIndex (1-arg scalar functions)
_SCALAR_FUNCTIONS = (
    "sin", "cos", "tan", "csc", "sec", "cot",
    "asin", "acos", "atan", "acsc", "asec", "acot",
    "sinh", "cosh", "tanh", "csch", "sech", "coth",
    "asinh", "acosh", "atanh", "acsch", "asech", "acoth",
    "log", "ln", "log_2", "exp", "abs", "sign",
    "sqr", "sqrt", "cbrt", "round", "floor", "ceiling", "trunc",
    "re", "im", "phase", "conj", "random", "fact", "not", "timer",
    "gammaln", "gamma", "erf", "erfc", "fresnelc", "fresnels",
    "si", "ci", "shi", "chi", "ei", "li", "dawsonf",
    "elliptick", "elliptice",
    "besselj0", "besselj1", "bessely0", "bessely1",
    "besseli0", "besseli1", "besselk0", "besselk1",
    "ai", "ai\u2032", "bi", "bi\u2032", "lambertw",
    # Function2Index
    "atan2", "root", "mod", "mandelbrot",
    "gammainc\u203e", "gammainc_", "beta",
    "ellipticf", "ellipticei", "ellipticp",
    "jacobiam", "jacobisn", "jacobicn", "jacobidn",
    "jacobics", "jacobicd", "jacobidc", "jacobisc",
    "jacobisd", "jacobids", "jacobins", "jacobinc", "jacobind",
    "besseljn", "besselyn", "besseljv", "besselyv",
    "besseljv\u2032", "besselyv\u2032",
    "besseljs", "besselys", "besseljs\u2032", "besselys\u2032",
    "besselin", "besselkn", "besseliv", "besselkv",
    "besseliv\u2032", "besselkv\u2032",
    # Function3Index
    "if", "betainc", "ellipticpi",
    # MultiFunctionIndex
    "min", "max", "sum", "sumsq", "srss", "average", "product", "mean",
    "switch", "and", "or", "xor", "gcd", "lcm",
    # InterpolationIndex
    "take", "line", "spline",
)

# VectorCalculator.cs (FunctionIndex / Function2Index / Function3Index / MultiFunctionIndex)
_VECTOR_FUNCTIONS = (
    "vector", "len", "size", "sort", "rsort", "order", "revorder", "reverse",
    "norm", "norm_2", "norm_e", "norm_1", "norm_i", "unit", "vector_hp",
    "resize", "fill", "first", "last", "extract", "dot", "cross", "norm_p",
    "slice", "range", "range_hp", "search", "count",
    "find", "find_eq", "find_ne", "find_lt", "find_le", "find_gt", "find_ge",
    "lookup", "lookup_eq", "lookup_ne",
    "lookup_lt", "lookup_le", "lookup_gt", "lookup_ge",
    "join",
)

# MatrixCalculator.cs (FunctionIndex / Function2Index / IterativeFunctionIndex /
# Function3Index / Function4Index / Function5Index / MultiFunctionIndex)
_MATRIX_FUNCTIONS = (
    "identity", "utriang", "ltriang", "symmetric", "vec2diag", "diag2vec",
    "vec2row", "vec2col", "n_rows", "n_cols",
    "mnorm", "mnorm_1", "mnorm_2", "mnorm_e", "mnorm_i",
    "cond", "cond_1", "cond_2", "cond_e", "cond_i",
    "det", "rank", "transp", "trace", "inverse", "adj", "cofactor",
    "qr", "svd", "cholesky",
    "identity_hp", "utriang_hp", "ltriang_hp", "symmetric_hp",
    "hp", "ishp", "getunits", "clrunits", "fft", "ift", "lu",
    "matrix", "diagonal", "column", "row", "col",
    "extract_rows", "extract_cols", "mfill",
    "sort_cols", "rsort_cols", "sort_rows", "rsort_rows",
    "order_cols", "revorder_cols", "order_rows", "revorder_rows",
    "mcount", "mfind", "mfind_eq", "mfind_ne",
    "mfind_lt", "mfind_le", "mfind_gt", "mfind_ge",
    "lsolve", "clsolve", "msolve", "cmsolve",
    "hprod", "fprod", "kprod",
    "matrix_hp", "diagonal_hp", "column_hp", "setunits", "matmul",
    "slsolve", "smsolve", "eigenvals", "eigenvecs", "eigen",
    "fill_row", "fill_col", "mresize",
    "msearch",
    "hlookup", "hlookup_eq", "hlookup_ne",
    "hlookup_lt", "hlookup_le", "hlookup_gt", "hlookup_ge",
    "vlookup", "vlookup_eq", "vlookup_ne",
    "vlookup_lt", "vlookup_le", "vlookup_gt", "vlookup_ge",
    "copy", "add",
    "submatrix",
    "join_cols", "join_rows", "augment", "stack",
)

_BUILTINS = tuple(sorted(set(_SCALAR_FUNCTIONS + _VECTOR_FUNCTIONS + _MATRIX_FUNCTIONS)))

# Compiler directives (#-prefixed). Sorted longest-first so multi-word forms like
# "end if" / "md on" / "else if" match before their prefixes.
_DIRECTIVES = (
    "else if", "end if", "end def", "md on", "md off",
    "if", "else", "for", "while", "repeat", "loop", "break", "continue",
    "show", "hide", "pre", "post", "val", "equ", "noc", "nosub", "novar",
    "varsub", "rad", "deg", "gra", "include", "local", "global", "def",
    "pause", "input", "split", "wrap", "phasor", "complex", "const",
    "append", "read", "write", "round", "md",
)

# $-method / iterative-procedure names
_METHODS = (
    "Root", "Find", "Inf", "Sup", "Area", "Integral", "Slope", "Derivative",
    "Sum", "Product", "Repeat", "Map", "Plot", "Block", "Inline", "While",
)


# ---------------------------------------------------------------------------
# Lexer
# ---------------------------------------------------------------------------

class CalcpadLexer(RegexLexer):
    """Lexer for Calcpad source files (.cpd)."""

    name = "Calcpad"
    aliases = ["calcpad", "cpd"]
    filenames = ["*.cpd"]
    mimetypes = ["text/x-calcpad"]

    # Identifier characters: ASCII letters, Greek letters, degree/empty-set/angle
    # symbols, digits, underscore, comma, primes, super- and subscripts.
    _id_start = (
        r"[A-Za-z\u03b1-\u03c9\u0391-\u03a9\u00b0\u00f8\u00d8\u2221]"
    )
    _id_cont = (
        r"[A-Za-z\u03b1-\u03c9\u0391-\u03a9\u00b0\u00f8\u00d8\u2221"
        r"0-9_,\u2032\u2033\u2034\u2057"
        r"\u2070\u00b9\u00b2\u00b3\u2074-\u2079\u207f\u207a\u207b]"
    )
    _ident = _id_start + _id_cont + "*"

    # Case-insensitive keyword/function/method matching to mirror the engine.
    flags = 0  # don't use re.IGNORECASE globally — only on specific token rules

    tokens = {
        "root": [
            (r"[ \t]+", Whitespace),
            (r"\r?\n", Text),

            # Inline-HTML / comment text. Calcpad treats matching "..." pairs
            # as *title* comments (rendered bold over a green tint in WPF) and
            # '...' pairs as plain comments / pass-through text. Map them to
            # distinct Pygments tokens so CSS can style the title variant.
            (r"'[^'\r\n]*'?", Comment.Single),
            (r'"[^"\r\n]*"?', Comment.Special),

            # $-methods (iterative procedures): $Plot{...}, $Integral{...}, ...
            (r"\$\s*(?i:" + "|".join(_METHODS) + r")\b", Name.Decorator),

            # String-macro definition: `#def name$ = <plain text until EOL>`.
            # The right-hand side is literal text (often raw HTML/CSS), so
            # render it without further tokenization. Match the directive,
            # name, and `=` with their normal styling, then push into a state
            # that consumes the rest of the line as plain Text.
            (r"(#\s*(?i:def))(\s+)(" + _ident + r"\$)(\s*)(=)",
             bygroups(Keyword, Whitespace, Name.Variable, Whitespace, Operator),
             "string_def_value"),

            # #-directives. Match the # then the directive word(s). Use a
            # non-greedy alternation sorted longest-first.
            (r"#\s*(?i:" + "|".join(d.replace(" ", r"\s+") for d in _DIRECTIVES) + r")\b",
             Keyword),
            (r"#", Keyword),  # standalone # (e nput placeholder etc.)

            # Numbers, optionally followed by a unit suffix on the same line.
            (r"(\d+\.\d+|\.\d+|\d+)([ \t]*)([A-Za-z\u00b0\u2126\u2127][A-Za-z0-9_\u00b0\u2126\u2127]*)",
             bygroups(Number, Whitespace, Name.Attribute)),
            (r"\d+\.\d+|\.\d+|\d+", Number),

            # Built-in functions (case-insensitive). Only match when used as a
            # function call (followed by '('), so identifiers like "Line" or
            # "Label" inside macro bodies (#def chart$ = Line 1) stay variable-
            # styled.
            (words(_BUILTINS, prefix=r"(?i)\b", suffix=r"\b(?=\s*\()"), Name.Builtin),

            # Operators and angle/assignment symbols.
            (r"[\^/\u00f7\\\u29bc*\-+<>\u2264\u2265\u2261\u2260=\u2190"
             r"\u2227\u2228\u2295\u2220!]",
             Operator),

            # Punctuation / brackets / separators.
            (r"[(){}\[\];,&@:|.]", Punctuation),

            # ?-input placeholders (`?` and `?{...}` are Calcpad input fields).
            (r"\?", Name.Tag),

            # Identifiers (variables / user-defined names). Must come last so
            # the more specific rules above win.
            (_ident, Name.Variable),

            # Anything else.
            (r".", Text),
        ],

        "string_def_value": [
            (r"[^\r\n]+", Text),
            (r"\r?\n", Text, "#pop"),
        ],
    }


# ---------------------------------------------------------------------------
# Pygments registration helper
# ---------------------------------------------------------------------------

def register():
    """Register CalcpadLexer with Pygments so that pymdownx.highlight can find
    it for ```calcpad / ```cpd fenced code blocks.

    MkDocs hooks are loaded by file path, not as a regular Python package, so
    entry-point discovery is unavailable. Patch the lookup functions in-process,
    including any consumer module (pymdownx.highlight, mkdocs.contrib...) that
    has already imported those names by the time we run.
    """
    import sys
    import pygments.lexers as _pl

    aliases = set(CalcpadLexer.aliases)

    _orig_get = _pl.get_lexer_by_name
    _orig_find = _pl.find_lexer_class_by_name

    def get_lexer_by_name(_alias, **options):
        if _alias in aliases:
            return CalcpadLexer(**options)
        return _orig_get(_alias, **options)

    def find_lexer_class_by_name(_alias):
        if _alias in aliases:
            return CalcpadLexer
        return _orig_find(_alias)

    _pl.get_lexer_by_name = get_lexer_by_name
    _pl.find_lexer_class_by_name = find_lexer_class_by_name

    # Also patch every already-loaded module that re-exported the originals
    # (e.g. pymdownx.highlight does `from pygments.lexers import get_lexer_by_name`
    # and would otherwise keep the unpatched reference forever).
    for mod in list(sys.modules.values()):
        if mod is None or mod is _pl:
            continue
        try:
            if getattr(mod, "get_lexer_by_name", None) is _orig_get:
                mod.get_lexer_by_name = get_lexer_by_name
            if getattr(mod, "find_lexer_class_by_name", None) is _orig_find:
                mod.find_lexer_class_by_name = find_lexer_class_by_name
        except Exception:
            pass
