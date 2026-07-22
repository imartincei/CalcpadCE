using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for keywords (program flow control, output control, etc.).
    /// </summary>
    public static class KeywordSnippets
    {
        public static readonly SnippetItem[] Items =
        [
            // ============================================
            // PROGRAM FLOW CONTROL - IF STATEMENTS
            // ============================================
            new SnippetItem
            {
                Insert = "#if",
                Description = "If condition",
                Category = "Program Flow Control",
                KeywordType = "ControlBlockKeyword"
            },
            new SnippetItem
            {
                Insert = "#if condition\n\texpression\n#end if",
                Description = "Simple If...End If block",
                Label = "#if...#end if",
                Category = "Program Flow Control"
            },
            new SnippetItem
            {
                Insert = "#if condition\n\texpression\n#else\n\texpression\n#end if",
                Description = "If...Else...End If block",
                Label = "#if...#else...#end if",
                Category = "Program Flow Control"
            },
            new SnippetItem
            {
                Insert = "#if condition\n\texpression\n#else if condition\n\texpression\n#else\n\texpression\n#end if",
                Description = "If...Else If...Else...End If block",
                Label = "#if...#else if...#end if",
                Category = "Program Flow Control"
            },
            new SnippetItem
            {
                Insert = "#else if condition",
                Description = "Else If clause",
                Category = "Program Flow Control",
                KeywordType = "ControlBlockKeyword"
            },
            new SnippetItem
            {
                Insert = "#else",
                Description = "Else clause",
                Category = "Program Flow Control",
                KeywordType = "ControlBlockKeyword"
            },
            new SnippetItem
            {
                Insert = "#end if",
                Description = "End If",
                Category = "Program Flow Control",
                KeywordType = "EndKeyword"
            },

            // ============================================
            // ITERATION BLOCKS
            // ============================================
            new SnippetItem
            {
                Insert = "#repeat",
                Description = "Repeat loop start",
                Category = "Iteration Blocks",
                KeywordType = "ControlBlockKeyword"
            },
            new SnippetItem
            {
                Insert = "#for",
                Description = "For loop start",
                Category = "Iteration Blocks",
                KeywordType = "ControlBlockKeyword"
            },
            new SnippetItem
            {
                Insert = "#while",
                Description = "While loop start",
                Category = "Iteration Blocks",
                KeywordType = "ControlBlockKeyword"
            },
            new SnippetItem
            {
                Insert = "#repeat count\n\texpression\n#loop",
                Description = "Repeat loop (fixed number of iterations)",
                Label = "#repeat...#loop",
                Category = "Iteration Blocks"
            },
            new SnippetItem
            {
                Insert = "#for i = 1 : n\n\texpression\n#loop",
                Description = "For loop with counter",
                Label = "#for...#loop",
                Category = "Iteration Blocks"
            },
            new SnippetItem
            {
                Insert = "#while condition\n\texpression\n#loop",
                Description = "While loop with condition",
                Label = "#while...#loop",
                Category = "Iteration Blocks"
            },
            new SnippetItem
            {
                Insert = "#loop",
                Description = "End of loop block",
                Category = "Iteration Blocks",
                KeywordType = "EndKeyword"
            },
            new SnippetItem
            {
                Insert = "#break",
                Description = "Break out of current loop",
                Category = "Iteration Blocks",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#continue",
                Description = "Continue to next iteration",
                Category = "Iteration Blocks",
                KeywordType = "Keyword"
            },

            // ============================================
            // MODULES AND MACROS
            // ============================================
            new SnippetItem
            {
                Insert = "#include filename",
                Description = "Include external file (module). Path is relative to the current file or the library path.",
                Category = "Modules and Macros",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#local",
                Description = "Start local section (not included when file is imported)",
                Category = "Modules and Macros",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#global",
                Description = "Start global section (included when file is imported)",
                Category = "Modules and Macros",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#def",
                Description = "Define macro or string variable",
                Category = "Modules and Macros",
                KeywordType = "ControlBlockKeyword"
            },
            new SnippetItem
            {
                Insert = "#def name$ = expression",
                Description = "Inline string variable definition",
                Label = "#def var$ = ...",
                Category = "Modules and Macros"
            },
            new SnippetItem
            {
                Insert = "#def name$\n\texpression\n#end def",
                Description = "Multiline string variable definition",
                Label = "#def var$...#end def",
                Category = "Modules and Macros"
            },
            new SnippetItem
            {
                Insert = "#def name$(param$) = expression",
                Description = "Inline macro with parameters",
                Label = "#def macro$(params) = ...",
                Category = "Modules and Macros"
            },
            new SnippetItem
            {
                Insert = "#def name$(param$)\n\texpression\n#end def",
                Description = "Multiline macro with parameters",
                Label = "#def macro$(params)...#end def",
                Category = "Modules and Macros"
            },
            new SnippetItem
            {
                Insert = "#end def",
                Description = "End of macro/string variable definition",
                Category = "Modules and Macros",
                KeywordType = "EndKeyword"
            },

            // ============================================
            // EXTERNAL DATA
            // ============================================
            new SnippetItem
            {
                Insert = "#read M from filename",
                Description = "Read matrix from text/CSV or Excel file",
                Label = "#read M from file",
                Category = "External Data",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#read M from filename.csv@R1C1:R2C2 TYPE=R SEP=','",
                Description = "Read matrix from a CSV/text file with all options. " +
                    "@R1C1:R2C2 = cell range (row, column). " +
                    "TYPE: R=Raw (default), D=Diagonal, C=Column, L=Lower triangular, U=Upper triangular, S=Symmetric, V=Vector. " +
                    "SEP: column separator character (default ',').",
                Label = "#read M from file.csv (all options)",
                Category = "External Data"
            },
            new SnippetItem
            {
                Insert = "#read M from filename.xlsx@Sheet1!A1:B2 TYPE=R",
                Description = "Read matrix from an Excel file (.xlsx/.xlsm) with all options. " +
                    "@Sheet!A1:B2 = sheet name and cell range. " +
                    "TYPE: R=Raw (default), D=Diagonal, C=Column, L=Lower triangular, U=Upper triangular, S=Symmetric, V=Vector.",
                Label = "#read M from file.xlsx (all options)",
                Category = "External Data"
            },
            new SnippetItem
            {
                Insert = "#write M to filename",
                Description = "Write matrix to text/CSV or Excel file",
                Label = "#write M to file",
                Category = "External Data",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#write M to filename.csv@R1C1:R2C2 TYPE=N SEP=','",
                Description = "Write matrix to a CSV/text file with all options. " +
                    "@R1C1:R2C2 = cell range (row, column). " +
                    "TYPE: Y=Compact (transpose special matrix types), N=Normal (default). " +
                    "SEP: column separator character (default ',').",
                Label = "#write M to file.csv (all options)",
                Category = "External Data"
            },
            new SnippetItem
            {
                Insert = "#write M to filename.xlsx@Sheet1!A1:B2 TYPE=N",
                Description = "Write matrix to an Excel file (.xlsx/.xlsm) with all options. " +
                    "@Sheet!A1:B2 = sheet name and cell range. " +
                    "TYPE: Y=Compact (transpose special matrix types), N=Normal (default).",
                Label = "#write M to file.xlsx (all options)",
                Category = "External Data"
            },
            new SnippetItem
            {
                Insert = "#append M to filename",
                Description = "Append matrix to text/CSV or Excel file",
                Label = "#append M to file",
                Category = "External Data",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#append M to filename.csv@R1C1:R2C2 TYPE=N SEP=','",
                Description = "Append matrix to a CSV/text file with all options. " +
                    "@R1C1:R2C2 = cell range (row, column). " +
                    "TYPE: Y=Compact (transpose special matrix types), N=Normal (default). " +
                    "SEP: column separator character (default ',').",
                Label = "#append M to file.csv (all options)",
                Category = "External Data"
            },
            new SnippetItem
            {
                Insert = "#append M to filename.xlsx@Sheet1!A1:B2 TYPE=N",
                Description = "Append matrix to an Excel file (.xlsx/.xlsm) with all options. " +
                    "@Sheet!A1:B2 = sheet name and cell range. " +
                    "TYPE: Y=Compact (transpose special matrix types), N=Normal (default).",
                Label = "#append M to file.xlsx (all options)",
                Category = "External Data"
            },

            // ============================================
            // READ ONLY
            // ============================================
            new SnippetItem
            {
                Insert = "#const",
                Description = "Define a constant (readonly) variable or function",
                Category = "Read Only",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#const name = value",
                Description = "Define a constant variable",
                Label = "#const var = ...",
                Category = "Read Only"
            },
            new SnippetItem
            {
                Insert = "#const f(x) = expression",
                Description = "Define a constant function",
                Label = "#const f(x) = ...",
                Category = "Read Only"
            },

            // ============================================
            // OUTPUT CONTROL
            // ============================================
            new SnippetItem
            {
                Insert = "#show",
                Description = "Show the output contents (default)",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#hide",
                Description = "Hide the output contents",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#pre",
                Description = "Show contents only before calculations",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#post",
                Description = "Show contents only after calculations",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#val",
                Description = "Show only the result, without the equation",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#equ",
                Description = "Show complete equations and results (default)",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#noc",
                Description = "Show equations without results (no calculations)",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#varsub",
                Description = "Show equations with variables and substituted values (default)",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#nosub",
                Description = "Do not substitute variables (no substitution)",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#novar",
                Description = "Show equations only with substituted values (no variables)",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#round digits",
                Description = "Round output to n digits after decimal point",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#round default",
                Description = "Restore rounding to default settings",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#format spec",
                Description = "Specify custom format string",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#format default",
                Description = "Restore default formatting",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#settings {\"decimals\": 4, \"units\": \"cm\"}",
                Description = "Override engine settings for subsequent lines via JSON",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#phasor",
                Description = "Set complex number output to polar phasor (A angle phi)",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#complex",
                Description = "Set complex number output to cartesian algebraic (a + ib)",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#md on",
                Description = "Enable markdown in comments",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#md off",
                Description = "Disable markdown in comments",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#cpd",
                Description = "Switch parsing mode to Calcpad (default)",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#html",
                Description = "Switch parsing mode to raw HTML (no Calcpad evaluation)",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#markdown",
                Description = "Switch parsing mode to Markdown (no Calcpad evaluation)",
                Category = "Output Control",
                KeywordType = "Keyword"
            },

            // ============================================
            // BREAKPOINTS
            // ============================================
            new SnippetItem
            {
                Insert = "#pause",
                Description = "Pause calculation and wait for user to resume",
                Category = "Breakpoints",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#input",
                Description = "Render input form and wait for user input",
                Category = "Breakpoints",
                KeywordType = "Keyword"
            },

            // ============================================
            // ANGLE UNITS
            // ============================================
            new SnippetItem
            {
                Insert = "#deg",
                Description = "Set angle units to degrees",
                Category = "Angle Units",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#rad",
                Description = "Set angle units to radians",
                Category = "Angle Units",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#gra",
                Description = "Set angle units to gradians",
                Category = "Angle Units",
                KeywordType = "Keyword"
            },
        ];
    }
}
