using System.Text.RegularExpressions;

namespace Calcpad.Highlighter.Linter.Constants
{
    public static class CalcpadPatterns
    {
        // Character sets based on Calcpad.Core.Validator and TypeScript linter constants
        // Includes: letters, Greek letters, special symbols, digits, subscripts, superscripts
        public const string IdentifierStartChars = @"a-zA-Zα-ωΑ-Ω°øØ∡℧";
        public const string IdentifierChars = @"a-zA-Zα-ωΑ-Ω°øØ∡℧0-9_,′″‴⁗⁰¹²³⁴⁵⁶⁷⁸⁹ⁿ⁺⁻₀₁₂₃₄₅₆₇₈₉₊₋₌₍₎";

        // Macro names are more restrictive than regular identifiers (from Calcpad.Core.Validator.IsMacroLetter):
        // Only ASCII letters (a-z, A-Z), underscore (_), and digits (0-9, after first position)
        // NO Greek letters, NO special symbols like ′ (prime), NO subscripts/superscripts
        public const string MacroNameStartChars = @"a-zA-Z_";
        public const string MacroNameChars = @"a-zA-Z0-9_";
        public const string MacroIdentifierChars = @"a-zA-Z0-9_\$";

        // Basic identifier pattern (variable/function name)
        public static readonly Regex Identifier = new(
            $@"(?<![{IdentifierChars}])([{IdentifierStartChars}][{IdentifierChars}]*)(?![{IdentifierChars}])",
            RegexOptions.Compiled);

        // Variable assignment pattern: identifier = expression (captures name and expression)
        public static readonly Regex VariableAssignment = new(
            $@"^\s*([{IdentifierStartChars}][{IdentifierChars}]*)\s*=\s*(.+)",
            RegexOptions.Compiled);

        // Function definition pattern: identifier(params) = expression
        public static readonly Regex FunctionDefinition = new(
            $@"^\s*([{IdentifierStartChars}][{IdentifierChars}]*)\s*\(([^)]*)\)\s*=",
            RegexOptions.Compiled);

        // Macro call pattern: macroName$(params)
        // Macro names only allow ASCII letters, digits, underscore - not Greek or special chars
        public static readonly Regex MacroCall = new(
            $@"\b([{MacroNameStartChars}][{MacroNameChars}]*\$)(?:\(([^)]*)\))?",
            RegexOptions.Compiled);

        // Inline macro definition: #def macroName$(params) = expression
        // Macro names only allow ASCII letters, digits, underscore - not Greek or special chars
        public static readonly Regex InlineMacroDef = new(
            $@"#def\s+([{MacroNameStartChars}][{MacroNameChars}]*\$?)(?:\(([^)]*)\))?\s*=\s*(.+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Multiline macro definition: #def macroName$(params)
        // Macro names only allow ASCII letters, digits, underscore - not Greek or special chars
        public static readonly Regex MultilineMacroDef = new(
            $@"#def\s+([{MacroNameStartChars}][{MacroNameChars}]*\$?)(?:\(([^)]*)\))?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // #include pattern
        public static readonly Regex IncludeStatement = new(
            @"^\s*#include\s+(.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // #read pattern
        public static readonly Regex ReadStatement = new(
            @"^\s*#read\s+(.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Line continuation pattern (line ending with _ before any comment)
        // Note: This is a simple pattern - actual comment detection is done in ContentResolver
        public static readonly Regex LineContinuation = new(
            @"^(.*?)(\s+_\s*)$",
            RegexOptions.Compiled);

        // Function call pattern: identifier(params)
        public static readonly Regex FunctionCall = new(
            $@"([{IdentifierStartChars}][{IdentifierChars}]*)\s*\(([^)]*)\)",
            RegexOptions.Compiled);

        // Invalid operators (double/triple operators that shouldn't exist)
        public static readonly Regex InvalidOperators = new(
            @"\+\+|--|\*\*|//|&&|\|\|",
            RegexOptions.Compiled);

        // Keyword after # pattern - only captures the first word
        public static readonly Regex HashKeyword = new(
            @"^\s*#(\w+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Command block pattern: $Inline{...}, $Block{...}, $While{...}
        // These command blocks have their own local scope where variables can be defined and used locally
        public static readonly Regex CommandBlockStart = new(
            @"\$(Inline|Block|While)\s*\{",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Function definition containing command block (for detecting functions with local scope bodies)
        // Pattern: funcName(params) = $Inline{...} or $Block{...} or $While{...}
        public static readonly Regex FunctionWithCommandBlock = new(
            $@"^\s*([{IdentifierStartChars}][{IdentifierChars}]*)\s*\(([^)]*)\)\s*=\s*\$(Inline|Block|While)\s*\{{",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Regex to extract comment sections from a line ('text' or "text", including unclosed)
        public static readonly Regex CommentSection = new(
            @"'[^']*'?|""[^""]*""?",
            RegexOptions.Compiled);

        // Regex to find macro parameters in comments
        // Macro parameters use restricted character set (ASCII letters, digits, underscore)
        // Parameters have OPTIONAL $ suffix (e.g., "param" or "param$")
        public static readonly Regex MacroParamInComment = new(
            $@"(?<![{MacroNameChars}\$])([{MacroNameStartChars}][{MacroNameChars}]*\$?)(?![{MacroNameChars}])",
            RegexOptions.Compiled);

        // Loose pattern to extract macro name (allows any characters before ( or =)
        // Used for error reporting when strict patterns don't match
        public static readonly Regex LooseMacroNameExtract = new(
            @"#def\s+([^\s(=]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Pattern for valid macro name (from Calcpad.Core.Validator.IsMacroLetter)
        // Must start with letter or underscore, then letters/digits/underscores, optional $ at end
        public static readonly Regex ValidMacroName = new(
            $@"^[{MacroNameStartChars}][{MacroNameChars}]*\$?$",
            RegexOptions.Compiled);

        // Pattern for valid macro parameter (same as name but $ is optional)
        public static readonly Regex ValidMacroParam = new(
            $@"^[{MacroNameStartChars}][{MacroNameChars}]*\$?$",
            RegexOptions.Compiled);
    }
}
