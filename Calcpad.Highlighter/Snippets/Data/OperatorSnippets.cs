using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for operators.
    /// </summary>
    public static class OperatorSnippets
    {
        public static readonly SnippetItem[] Items =
        [
            // Arithmetic Operators
            new SnippetItem
            {
                Insert = "!",
                Description = "Factorial",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "^",
                Description = "Exponent (power)",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "/",
                Description = "Division",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "÷",
                Description = "Division bar (inline) / slash (pro mode)",
                Label = "÷ (//)",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "\\",
                Description = "Integer division",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "⦼",
                Description = "Modulo (remainder)",
                Label = "⦼ (%%)",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "*",
                Description = "Multiplication",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "-",
                Description = "Subtraction / Negation",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "+",
                Description = "Addition",
                Category = "Operators",
                KeywordType = "Operator"
            },

            // Comparison Operators
            new SnippetItem
            {
                Insert = "≡",
                Description = "Equal to",
                Label = "≡ (==)",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "≠",
                Description = "Not equal to",
                Label = "≠ (!=)",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "<",
                Description = "Less than",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = ">",
                Description = "Greater than",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "≤",
                Description = "Less than or equal",
                Label = "≤ (<=)",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "≥",
                Description = "Greater than or equal",
                Label = "≥ (>=)",
                Category = "Operators",
                KeywordType = "Operator"
            },

            // Logical Operators
            new SnippetItem
            {
                Insert = "∧",
                Description = "Logical AND",
                Label = "∧ (&&)",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "∨",
                Description = "Logical OR",
                Label = "∨ (||)",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "⊕",
                Description = "Logical XOR",
                Label = "⊕ (^^)",
                Category = "Operators",
                KeywordType = "Operator"
            },

            // Complex Number Operator
            new SnippetItem
            {
                Insert = "∠",
                Description = "Phasor angle operator A∠φ",
                Label = "∠ (<<)",
                Category = "Operators",
                KeywordType = "Operator"
            },

            // Assignment
            new SnippetItem
            {
                Insert = "=",
                Description = "Assignment",
                Category = "Operators",
                KeywordType = "Operator"
            },
            new SnippetItem
            {
                Insert = "←",
                Description = "Outer scope assignment",
                Label = "← (<*)",
                Category = "Operators",
                KeywordType = "Operator"
            }
        ];
    }
}
