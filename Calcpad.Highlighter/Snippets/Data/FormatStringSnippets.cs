using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for inline numeric format strings (e.g. "x = 12.345:f3").
    /// These are UI-only snippets excluded from the linter, inserted directly after
    /// a number or unit in an output expression.
    /// </summary>
    public static class FormatStringSnippets
    {
        public static readonly SnippetItem[] Items =
        [
            // ============================================
            // EXPONENTIAL
            // ============================================
            new SnippetItem
            {
                Insert = ":e",
                Description = "Exponential notation, default precision",
                Documentation = "Displays the value in exponential (scientific) notation, e.g. 1.234568e+005. `n` sets the number of decimal digits in the mantissa (range 0-17, default 6). Use uppercase `E` for uppercase notation.",
                Example = "x = 123456.789:e",
                Category = "Format Strings"
            },
            new SnippetItem
            {
                Insert = ":e3",
                Description = "Exponential notation with 3 mantissa digits",
                Documentation = "Same as `:e` but with an explicit digit count. Change the `3` to any value from 0 to 17.",
                Example = "x = 123456.789:e3",
                Category = "Format Strings"
            },

            // ============================================
            // FIXED-POINT
            // ============================================
            new SnippetItem
            {
                Insert = ":f",
                Description = "Fixed-point notation, default precision",
                Documentation = "Always displays a fixed number of decimal digits, padding with zeros if needed, e.g. 123:F2 -> 123.00. `n` sets the digit count (range 0-17, default 2).",
                Example = "x = 123.456789:f",
                Category = "Format Strings"
            },
            new SnippetItem
            {
                Insert = ":f3",
                Description = "Fixed-point notation with 3 decimal digits",
                Documentation = "Same as `:f` but with an explicit digit count. Change the `3` to any value from 0 to 17.",
                Example = "x = 123.456789:f3",
                Category = "Format Strings"
            },

            // ============================================
            // GENERAL
            // ============================================
            new SnippetItem
            {
                Insert = ":g",
                Description = "General notation, default precision",
                Documentation = "Displays fixed-point or scientific notation, whichever is more compact. `n` sets the number of significant digits (range 0-17, default 15).",
                Example = "x = 123.456789:g",
                Category = "Format Strings"
            },
            new SnippetItem
            {
                Insert = ":g3",
                Description = "General notation with 3 significant digits",
                Documentation = "Same as `:g` but with an explicit significant digit count. Automatically switches to scientific notation for very large or small values, e.g. 123456m:G3 -> 1.23×10^5 m.",
                Example = "x = 0.0012345678:g3",
                Category = "Format Strings"
            },

            // ============================================
            // NUMBER
            // ============================================
            new SnippetItem
            {
                Insert = ":n",
                Description = "Number with digit grouping, default precision",
                Documentation = "Fixed-point notation with thousands separators, e.g. 123456:N3 -> 123,456.000. Uses the thousands/decimal separators from Regional Settings. `n` sets the decimal digit count (range 0-17, default 2).",
                Example = "x = 123456:n",
                Category = "Format Strings"
            },
            new SnippetItem
            {
                Insert = ":n3",
                Description = "Number with digit grouping and 3 decimal digits",
                Documentation = "Same as `:n` but with an explicit decimal digit count. Change the `3` to any value from 0 to 17.",
                Example = "x = 123456:n3",
                Category = "Format Strings"
            },

            // ============================================
            // CURRENCY
            // ============================================
            new SnippetItem
            {
                Insert = ":c",
                Description = "Currency notation, default precision",
                Documentation = "Fixed-point notation with digit grouping and the currency symbol from Windows Regional Settings, e.g. 123456:C -> 123,456.00 €. `n` sets the decimal digit count (range 0-17, default 2).",
                Example = "x = 123.456789:c",
                Category = "Format Strings"
            },
            new SnippetItem
            {
                Insert = ":c3",
                Description = "Currency notation with 3 decimal digits",
                Documentation = "Same as `:c` but with an explicit decimal digit count. Change the `3` to any value from 0 to 17.",
                Example = "x = 0.0012345678:c3",
                Category = "Format Strings"
            },

            // ============================================
            // CUSTOM
            // ============================================
            new SnippetItem
            {
                Insert = ":0.000",
                Description = "Custom format - fixed decimal places, zero-padded",
                Documentation = "Custom format built from placeholder characters: `0` always shows a digit (or zero if unavailable), `#` shows a digit only if present, `.` marks the decimal point, `,` marks a group separator. `:0.000` always shows exactly 3 decimals, e.g. 123.45:0.0000 -> 123.4500.",
                Example = "x = 123.45:0.0000",
                Category = "Format Strings"
            },
            new SnippetItem
            {
                Insert = ":#,#.00",
                Description = "Custom format - digit grouping with optional leading digits",
                Documentation = "`#` placeholders are optional and dropped when there is no digit to show; `,` adds a thousands separator. `:#,#.00` groups the integer part and always shows 2 decimals, e.g. 1234567:#,#.0 -> 1,234,567.0.",
                Example = "x = 1234567:#,#.00",
                Category = "Format Strings"
            },
            new SnippetItem
            {
                Insert = ":0.00e+00",
                Description = "Custom format - explicit exponential notation",
                Documentation = "Combines digit placeholders with `e+00` / `e-00` to force scientific notation with a custom exponent format, e.g. 1234567:0.00e+00 -> 1.23e+06.",
                Example = "x = 1234567:0.00e+00",
                Category = "Format Strings"
            }
        ];
    }
}
