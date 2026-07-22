using System.Collections.Generic;
using System.Collections.Frozen;

namespace Calcpad.Highlighter.Linter.Constants
{
    public static class ErrorCodes
    {
        public static readonly FrozenDictionary<string, string> Descriptions = new Dictionary<string, string>
        {
            // Stage 1: Pre-include validation (CPD-11xx)
            ["CPD-1101"] = "Malformed #include statement",
            ["CPD-1102"] = "Missing #include filename",

            // Stage 2: Macro definitions (CPD-22xx)
            ["CPD-2201"] = "Duplicate macro definition",
            ["CPD-2202"] = "Macro name must end with '$'",
            ["CPD-2203"] = "Macro parameter must end with '$'",
            ["CPD-2204"] = "Invalid macro name (must start with a letter)",
            ["CPD-2205"] = "Malformed #def syntax",
            ["CPD-2206"] = "Unmatched #def or #end def",
            ["CPD-2207"] = "Nested macro definition not allowed",
            ["CPD-2208"] = "Macro parameter must start with a letter",
            ["CPD-2209"] = "Macro definition inside control block has no effect",
            ["CPD-2210"] = "Invalid character in macro name",
            ["CPD-2211"] = "Invalid character in macro parameter",
            ["CPD-2212"] = "Duplicate macro parameter",
            ["CPD-2213"] = "Required parameter after optional parameter",

            // Stage 3: Balance (CPD-31xx)
            ["CPD-3101"] = "Unmatched opening parenthesis",
            ["CPD-3102"] = "Unmatched closing parenthesis",
            ["CPD-3103"] = "Unmatched opening square bracket",
            ["CPD-3104"] = "Unmatched closing square bracket",
            ["CPD-3105"] = "Unmatched opening curly brace or control block",
            ["CPD-3106"] = "Unmatched closing curly brace",

            // Stage 3: Naming (CPD-32xx)
            ["CPD-3201"] = "Invalid variable name (must start with letter)",
            // CPD-3202 removed: Variable names CAN overlap with built-in function names
            ["CPD-3203"] = "Invalid function name",
            ["CPD-3204"] = "Function name conflicts with built-in function",
            ["CPD-3205"] = "Variable name conflicts with keyword",
            ["CPD-3206"] = "Variable name conflicts with built-in unit",
            ["CPD-3207"] = "Variable name conflicts with built-in constant",
            ["CPD-3208"] = "Function must have at least one parameter",
            ["CPD-3215"] = "Required parameter after optional parameter in function definition",

            // Stage 3: Usage (CPD-33xx)
            ["CPD-3301"] = "Undefined variable",
            ["CPD-3302"] = "Function called with incorrect parameter count",
            ["CPD-3303"] = "Undefined macro",
            ["CPD-3304"] = "Macro called with incorrect parameter count",
            ["CPD-3305"] = "Undefined function",
            ["CPD-3306"] = "Invalid element access",
            ["CPD-3307"] = "Too few parameters",
            ["CPD-3308"] = "Too many parameters",
            ["CPD-3309"] = "Parameter type mismatch",
            ["CPD-3310"] = "Undefined unit",
            ["CPD-3311"] = "Empty parameter in function call",
            ["CPD-3312"] = "Unused variable",
            ["CPD-3313"] = "Unused function",
            ["CPD-3314"] = "Redefinition of existing function",

            // Stage 3: Semantic (CPD-34xx)
            ["CPD-3401"] = "Invalid operator usage",
            ["CPD-3402"] = "Mismatched operator",
            ["CPD-3403"] = "Command must be at the start of a statement",
            ["CPD-3404"] = "Invalid command syntax",
            ["CPD-3405"] = "Invalid control structure syntax",
            ["CPD-3406"] = "Unknown directive",
            ["CPD-3407"] = "Invalid assignment",
            ["CPD-3408"] = "Invalid CustomUnit syntax",
            ["CPD-3409"] = "# directive not allowed inside command block",
            ["CPD-3410"] = "Invalid command syntax",
            ["CPD-3411"] = "Incomplete expression",
            ["CPD-3412"] = "Command variable mismatch",
            ["CPD-3413"] = "Reassignment of constant",
            ["CPD-3414"] = "Outer scope assignment to undefined variable",
            ["CPD-3415"] = "Invalid #UI format",
            ["CPD-3416"] = "Invalid paramType value",
            ["CPD-3417"] = "Invalid metadata comment JSON",
            ["CPD-3418"] = "Invalid #settings JSON",

            // Stage 3: Format (CPD-36xx)
            ["CPD-3601"] = "Invalid format specifier"
        }.ToFrozenDictionary();

        public static string GetDescription(string code) =>
            Descriptions.TryGetValue(code, out var description) ? description : "Unknown error";
    }
}
