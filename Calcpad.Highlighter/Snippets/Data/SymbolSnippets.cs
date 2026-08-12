using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for Greek characters and other special symbols.
    /// Based on the symbol palette in Calcpad.Wpf (MainWindow.xaml).
    /// QuickType values match the QUICK_TYPE_MAP in calcpad-frontend (quick-type.ts).
    /// </summary>
    public static class SymbolSnippets
    {
        // NOTE: BaseItems must be declared before Items because Items's initializer
        // references it. C# initializes static readonly fields in source order, so a
        // forward reference here resolves to the default (null) value.
        private static readonly SnippetItem[] BaseItems =
        [
            // Greek Lowercase Letters
            new SnippetItem
            {
                Insert = "α",
                Description = "Alpha (lowercase)",
                Label = "α alpha",
                Category = "Symbols/Greek Lowercase",
                QuickType = "a"
            },
            new SnippetItem
            {
                Insert = "β",
                Description = "Beta (lowercase)",
                Label = "β beta",
                Category = "Symbols/Greek Lowercase",
                QuickType = "b"
            },
            new SnippetItem
            {
                Insert = "γ",
                Description = "Gamma (lowercase)",
                Label = "γ gamma",
                Category = "Symbols/Greek Lowercase",
                QuickType = "g"
            },
            new SnippetItem
            {
                Insert = "δ",
                Description = "Delta (lowercase)",
                Label = "δ delta",
                Category = "Symbols/Greek Lowercase",
                QuickType = "d"
            },
            new SnippetItem
            {
                Insert = "ε",
                Description = "Epsilon (lowercase)",
                Label = "ε epsilon",
                Category = "Symbols/Greek Lowercase",
                QuickType = "e"
            },
            new SnippetItem
            {
                Insert = "ζ",
                Description = "Zeta (lowercase)",
                Label = "ζ zeta",
                Category = "Symbols/Greek Lowercase",
                QuickType = "z"
            },
            new SnippetItem
            {
                Insert = "η",
                Description = "Eta (lowercase)",
                Label = "η eta",
                Category = "Symbols/Greek Lowercase",
                QuickType = "h"
            },
            new SnippetItem
            {
                Insert = "θ",
                Description = "Theta (lowercase)",
                Label = "θ theta",
                Category = "Symbols/Greek Lowercase",
                QuickType = "q"
            },
            new SnippetItem
            {
                Insert = "ϑ",
                Description = "Theta variant (lowercase)",
                Label = "ϑ theta variant",
                Category = "Symbols/Greek Lowercase"
            },
            new SnippetItem
            {
                Insert = "ι",
                Description = "Iota (lowercase)",
                Label = "ι iota",
                Category = "Symbols/Greek Lowercase",
                QuickType = "i"
            },
            new SnippetItem
            {
                Insert = "κ",
                Description = "Kappa (lowercase)",
                Label = "κ kappa",
                Category = "Symbols/Greek Lowercase",
                QuickType = "k"
            },
            new SnippetItem
            {
                Insert = "λ",
                Description = "Lambda (lowercase)",
                Label = "λ lambda",
                Category = "Symbols/Greek Lowercase",
                QuickType = "l"
            },
            new SnippetItem
            {
                Insert = "μ",
                Description = "Mu (lowercase)",
                Label = "μ mu",
                Category = "Symbols/Greek Lowercase",
                QuickType = "m"
            },
            new SnippetItem
            {
                Insert = "ν",
                Description = "Nu (lowercase)",
                Label = "ν nu",
                Category = "Symbols/Greek Lowercase",
                QuickType = "n"
            },
            new SnippetItem
            {
                Insert = "ξ",
                Description = "Xi (lowercase)",
                Label = "ξ xi",
                Category = "Symbols/Greek Lowercase",
                QuickType = "x"
            },
            new SnippetItem
            {
                Insert = "ο",
                Description = "Omicron (lowercase)",
                Label = "ο omicron",
                Category = "Symbols/Greek Lowercase",
                QuickType = "o"
            },
            new SnippetItem
            {
                Insert = "π",
                Description = "Pi (lowercase)",
                Label = "π pi",
                Category = "Symbols/Greek Lowercase",
                QuickType = "p"
            },
            new SnippetItem
            {
                Insert = "ρ",
                Description = "Rho (lowercase)",
                Label = "ρ rho",
                Category = "Symbols/Greek Lowercase",
                QuickType = "r"
            },
            new SnippetItem
            {
                Insert = "ς",
                Description = "Sigma final form (lowercase)",
                Label = "ς sigma (final)",
                Category = "Symbols/Greek Lowercase",
                QuickType = "j"
            },
            new SnippetItem
            {
                Insert = "σ",
                Description = "Sigma (lowercase)",
                Label = "σ sigma",
                Category = "Symbols/Greek Lowercase",
                QuickType = "s"
            },
            new SnippetItem
            {
                Insert = "τ",
                Description = "Tau (lowercase)",
                Label = "τ tau",
                Category = "Symbols/Greek Lowercase",
                QuickType = "t"
            },
            new SnippetItem
            {
                Insert = "υ",
                Description = "Upsilon (lowercase)",
                Label = "υ upsilon",
                Category = "Symbols/Greek Lowercase",
                QuickType = "u"
            },
            new SnippetItem
            {
                Insert = "φ",
                Description = "Phi (lowercase)",
                Label = "φ phi",
                Category = "Symbols/Greek Lowercase",
                QuickType = "f"
            },
            new SnippetItem
            {
                Insert = "χ",
                Description = "Chi (lowercase)",
                Label = "χ chi",
                Category = "Symbols/Greek Lowercase",
                QuickType = "c"
            },
            new SnippetItem
            {
                Insert = "ψ",
                Description = "Psi (lowercase)",
                Label = "ψ psi",
                Category = "Symbols/Greek Lowercase",
                QuickType = "y"
            },
            new SnippetItem
            {
                Insert = "ω",
                Description = "Omega (lowercase)",
                Label = "ω omega",
                Category = "Symbols/Greek Lowercase",
                QuickType = "w"
            },

            // Greek Uppercase Letters
            new SnippetItem
            {
                Insert = "Α",
                Description = "Alpha (uppercase)",
                Label = "Α Alpha",
                Category = "Symbols/Greek Uppercase",
                QuickType = "A"
            },
            new SnippetItem
            {
                Insert = "Β",
                Description = "Beta (uppercase)",
                Label = "Β Beta",
                Category = "Symbols/Greek Uppercase",
                QuickType = "B"
            },
            new SnippetItem
            {
                Insert = "Γ",
                Description = "Gamma (uppercase)",
                Label = "Γ Gamma",
                Category = "Symbols/Greek Uppercase",
                QuickType = "G"
            },
            new SnippetItem
            {
                Insert = "Δ",
                Description = "Delta (uppercase)",
                Label = "Δ Delta",
                Category = "Symbols/Greek Uppercase",
                QuickType = "D"
            },
            new SnippetItem
            {
                Insert = "Ε",
                Description = "Epsilon (uppercase)",
                Label = "Ε Epsilon",
                Category = "Symbols/Greek Uppercase",
                QuickType = "E"
            },
            new SnippetItem
            {
                Insert = "Ζ",
                Description = "Zeta (uppercase)",
                Label = "Ζ Zeta",
                Category = "Symbols/Greek Uppercase",
                QuickType = "Z"
            },
            new SnippetItem
            {
                Insert = "Η",
                Description = "Eta (uppercase)",
                Label = "Η Eta",
                Category = "Symbols/Greek Uppercase",
                QuickType = "H"
            },
            new SnippetItem
            {
                Insert = "Θ",
                Description = "Theta (uppercase)",
                Label = "Θ Theta",
                Category = "Symbols/Greek Uppercase",
                QuickType = "Q"
            },
            new SnippetItem
            {
                Insert = "Ι",
                Description = "Iota (uppercase)",
                Label = "Ι Iota",
                Category = "Symbols/Greek Uppercase",
                QuickType = "I"
            },
            new SnippetItem
            {
                Insert = "Κ",
                Description = "Kappa (uppercase)",
                Label = "Κ Kappa",
                Category = "Symbols/Greek Uppercase",
                QuickType = "K"
            },
            new SnippetItem
            {
                Insert = "Λ",
                Description = "Lambda (uppercase)",
                Label = "Λ Lambda",
                Category = "Symbols/Greek Uppercase",
                QuickType = "L"
            },
            new SnippetItem
            {
                Insert = "Μ",
                Description = "Mu (uppercase)",
                Label = "Μ Mu",
                Category = "Symbols/Greek Uppercase",
                QuickType = "M"
            },
            new SnippetItem
            {
                Insert = "Ν",
                Description = "Nu (uppercase)",
                Label = "Ν Nu",
                Category = "Symbols/Greek Uppercase",
                QuickType = "N"
            },
            new SnippetItem
            {
                Insert = "Ξ",
                Description = "Xi (uppercase)",
                Label = "Ξ Xi",
                Category = "Symbols/Greek Uppercase",
                QuickType = "X"
            },
            new SnippetItem
            {
                Insert = "Ο",
                Description = "Omicron (uppercase)",
                Label = "Ο Omicron",
                Category = "Symbols/Greek Uppercase",
                QuickType = "O"
            },
            new SnippetItem
            {
                Insert = "Π",
                Description = "Pi (uppercase)",
                Label = "Π Pi",
                Category = "Symbols/Greek Uppercase",
                QuickType = "P"
            },
            new SnippetItem
            {
                Insert = "Ρ",
                Description = "Rho (uppercase)",
                Label = "Ρ Rho",
                Category = "Symbols/Greek Uppercase",
                QuickType = "R"
            },
            new SnippetItem
            {
                Insert = "Σ",
                Description = "Sigma (uppercase)",
                Label = "Σ Sigma",
                Category = "Symbols/Greek Uppercase",
                QuickType = "S"
            },
            new SnippetItem
            {
                Insert = "Τ",
                Description = "Tau (uppercase)",
                Label = "Τ Tau",
                Category = "Symbols/Greek Uppercase",
                QuickType = "T"
            },
            new SnippetItem
            {
                Insert = "Υ",
                Description = "Upsilon (uppercase)",
                Label = "Υ Upsilon",
                Category = "Symbols/Greek Uppercase",
                QuickType = "U"
            },
            new SnippetItem
            {
                Insert = "Φ",
                Description = "Phi (uppercase)",
                Label = "Φ Phi",
                Category = "Symbols/Greek Uppercase",
                QuickType = "F"
            },
            new SnippetItem
            {
                Insert = "Χ",
                Description = "Chi (uppercase)",
                Label = "Χ Chi",
                Category = "Symbols/Greek Uppercase",
                QuickType = "C"
            },
            new SnippetItem
            {
                Insert = "Ψ",
                Description = "Psi (uppercase)",
                Label = "Ψ Psi",
                Category = "Symbols/Greek Uppercase",
                QuickType = "Y"
            },
            new SnippetItem
            {
                Insert = "Ω",
                Description = "Omega (uppercase)",
                Label = "Ω Omega",
                Category = "Symbols/Greek Uppercase",
                QuickType = "W"
            },

            // Special Symbols
            new SnippetItem
            {
                Insert = "°",
                Description = "Degree sign",
                Label = "° degree",
                Category = "Symbols/Special",
                QuickType = "0"
            },
            new SnippetItem
            {
                Insert = "′",
                Description = "Prime (minutes / feet)",
                Label = "′ prime",
                Category = "Symbols/Special",
                QuickType = "'"
            },
            new SnippetItem
            {
                Insert = "″",
                Description = "Double prime (seconds / inches)",
                Label = "″ double prime",
                Category = "Symbols/Special",
                QuickType = "\""
            },
            new SnippetItem
            {
                Insert = "‴",
                Description = "Triple prime",
                Label = "‴ triple prime",
                Category = "Symbols/Special",
                QuickType = "'''"
            },
            new SnippetItem
            {
                Insert = "⁗",
                Description = "Quadruple prime",
                Label = "⁗ quadruple prime",
                Category = "Symbols/Special",
                QuickType = "''''"
            },
            new SnippetItem
            {
                Insert = "ø",
                Description = "Latin o with stroke (diameter)",
                Label = "ø diameter",
                Category = "Symbols/Special",
                QuickType = "/o"
            },
            new SnippetItem
            {
                Insert = "Ø",
                Description = "Latin O with stroke (diameter, uppercase)",
                Label = "Ø Diameter",
                Category = "Symbols/Special",
                QuickType = "/O"
            },
            new SnippetItem
            {
                Insert = "ϕ",
                Description = "Phi symbol (alternative form)",
                Label = "ϕ phi",
                Category = "Symbols/Special",
                QuickType = "ff"
            },
            new SnippetItem
            {
                Insert = "‰",
                Description = "Per mille sign",
                Label = "‰ per mille",
                Category = "Symbols/Special",
                QuickType = "%"
            },
            new SnippetItem
            {
                Insert = "‱",
                Description = "Per ten thousand sign",
                Label = "‱ per ten thousand",
                Category = "Symbols/Special",
                QuickType = "%%"
            },
            new SnippetItem
            {
                Insert = "∡",
                Description = "Measured angle",
                Label = "∡ angle",
                Category = "Symbols/Special"
            },
            new SnippetItem
            {
                Insert = "ℓ",
                Description = "Length symbol (script l)",
                Label = "ℓ length",
                Category = "Symbols/Special",
                QuickType = "len"
            }
        ];

        public static readonly SnippetItem[] Items =
            BaseItems.Concat(BuildDiacriticItems()).ToArray();

        // Every diacritic-letter combo is emitted as base letter + combining mark
        // (decomposed NFD). This keeps a single canonical form in the palette so
        // identifiers don't accidentally split between precomposed and decomposed
        // versions of the same visible glyph.
        private static IEnumerable<SnippetItem> BuildDiacriticItems()
        {
            var diacritics = new (char Mark, string Name, string Tag)[]
            {
                ('̄', "Bar", "bar"),         // combining macron
                ('̇', "Dot", "dot"),         // combining dot above
                ('̈', "Double Dot", "ddot"), // combining diaeresis
                ('́', "Acute Accent", "acute")
            };

            foreach (var (mark, name, tag) in diacritics)
            {
                for (char c = 'a'; c <= 'z'; c++)
                    yield return MakeDiacriticItem(c, mark, name, tag, "Lowercase");
                for (char c = 'A'; c <= 'Z'; c++)
                    yield return MakeDiacriticItem(c, mark, name, tag, "Uppercase");
            }
        }

        private static SnippetItem MakeDiacriticItem(char baseChar, char mark, string diacriticName, string tag, string caseLabel)
        {
            var glyph = string.Concat(baseChar, mark);
            return new SnippetItem
            {
                Insert = glyph,
                Description = $"{baseChar} with {diacriticName.ToLowerInvariant()}",
                Label = $"{glyph} {baseChar}{tag}",
                Category = $"Symbols/{diacriticName} {caseLabel}"
            };
        }
    }
}
