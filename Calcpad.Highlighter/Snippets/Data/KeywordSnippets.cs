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
                Insert = "#ProjectPath = path",
                Description = "Declares the folder <project> stands for in #include, #read, #write and image paths below this line.",
                Documentation = "One `#ProjectPath` is allowed per document, and it has to come before the first "
                    + "`<project>` reference — a reference reached first reports it as undeclared rather than "
                    + "guessing. A module meant to be `#include`d elsewhere should declare its own inside "
                    + "`#local`...`#global`, so it does not clash with the including document's.",
                Category = "Modules and Macros",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#LibraryPath = path",
                Description = "Declares the folder <library> stands for in #include, #read, #write and image paths below this line.",
                Documentation = "One `#LibraryPath` is allowed per document, and it has to come before the first "
                    + "`<library>` reference — a reference reached first reports it as undeclared rather than "
                    + "guessing. A module meant to be `#include`d elsewhere should declare its own inside "
                    + "`#local`...`#global`, so it does not clash with the including document's.",
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
                Description = "Show the output contents (default). Optionally takes a condition, e.g. '#show x == 5'. Applies to the rest of the document, or up to a matching #end show.",
                Example = "x = 5\n'Conditional - shows everything below if x > 3\n#show x > 3\ny = 2*x\n\n'Bounded - #end show restores the previous visibility\n#show x > 3\nz = 3*x\n#end show\n'Previous visibility restored",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#hide",
                Description = "Hide the output contents. Optionally takes a condition, e.g. '#hide x == 5'. Applies to the rest of the document, or up to a matching #end hide.",
                Example = "x = 5\n'Conditional - hides everything below if x > 3\n#hide x > 3\ny = 2*x\n\n'Bounded - #end hide restores the previous visibility\n#hide x > 3\nz = 3*x\n#end hide\n'Previous visibility restored",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#pre",
                Description = "Show contents on screen only - hidden when printing/exporting to PDF. Optionally takes a condition. Applies to the rest of the document, or up to a matching #end pre.",
                Example = "x = 5\n'Conditional - screen only from here on if x > 3\n#pre x > 3\ny = 2*x\n\n'Bounded - #end pre restores the previous visibility\n#pre x > 3\nz = 3*x\n#end pre\n'Previous visibility restored",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#post",
                Description = "Show contents in the preview and when printing/exporting to PDF - hidden in UI mode. Optionally takes a condition. Applies to the rest of the document, or up to a matching #end post.",
                Example = "x = 5\n'Conditional - hidden in UI mode from here on if x > 3\n#post x > 3\ny = 2*x\n\n'Bounded - #end post restores the previous visibility\n#post x > 3\nz = 3*x\n#end post\n'Previous visibility restored",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#UI L = 10m",
                Description = "Expose the following assignment as an input control in UI mode. Ignored when rendering a report.",
                Category = "UI Inputs",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#UI {\"type\": \"datagrid\", \"rows\": 2, \"columns\": 3, \"columnHeaders\": [\"a\", \"b\", \"c\"]} M = [1; 2; 3 | 4; 5; 6]",
                Description = "Editable grid for a vector or matrix. Rows and columns are auto-detected from the right hand side when omitted.",
                Category = "UI Inputs",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#UI v = [1; 2; 3]",
                Description = "Editable grid whose size is auto-detected from the vector or matrix assigned to it.",
                Category = "UI Inputs",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#UI {\"type\": \"dropdown\", \"keys\": [\"Low\", \"High\"], \"values\": [\"1\", \"2\"]} x = 1",
                Description = "Drop-down list. 'keys' are the labels shown, 'values' are substituted into the calculation.",
                Category = "UI Inputs",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#UI {\"type\": \"radio\", \"keys\": [\"Steel\", \"Concrete\"], \"values\": [\"200GPa\", \"25GPa\"]} E = 200GPa",
                Description = "Radio button group. 'keys' are the labels shown, 'values' are substituted into the calculation.",
                Category = "UI Inputs",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#UI {\"type\": \"checkbox\"} b = 1",
                Description = "Checkbox toggling the value between 1 and 0.",
                Category = "UI Inputs",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#end hide",
                Description = "Restore the visibility state in effect before the matching #hide",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#end show",
                Description = "Restore the visibility state in effect before the matching #show",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#end pre",
                Description = "Restore the visibility state in effect before the matching #pre",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#end post",
                Description = "Restore the visibility state in effect before the matching #post",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#val",
                Description = "Show only the result, without the equation. Optionally takes a condition. Applies to the rest of the document, or up to a matching #end val.",
                Example = "x = 5\n'Conditional - results only from here on if x > 3\n#val x > 3\ny = 2*x\n\n'Bounded - #end val restores the previous output mode\n#val x > 3\nz = 3*x\n#end val\n'Previous output mode restored",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#equ",
                Description = "Show complete equations and results (default). Optionally takes a condition. Applies to the rest of the document, or up to a matching #end equ.",
                Example = "x = 5\n'Conditional - full equations from here on if x > 3\n#equ x > 3\ny = 2*x\n\n'Bounded - #end equ restores the previous output mode\n#equ x > 3\nz = 3*x\n#end equ\n'Previous output mode restored",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#noc",
                Description = "Show equations without results (no calculations). Optionally takes a condition. Applies to the rest of the document, or up to a matching #end noc.",
                Example = "x = 5\n'Conditional - equations without results from here on if x > 3\n#noc x > 3\ny = 2*x\n\n'Bounded - #end noc restores the previous output mode\n#noc x > 3\nz = 3*x\n#end noc\n'Previous output mode restored",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#end val",
                Description = "Restore the output mode in effect before the matching #val",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#end equ",
                Description = "Restore the output mode in effect before the matching #equ",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#end noc",
                Description = "Restore the output mode in effect before the matching #noc",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#varsub",
                Description = "Show equations with both variable names and their substituted values (default). Applies to the rest of the document; takes no condition and has no #end form - switch back with #nosub or #novar.",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#nosub",
                Description = "Show equations with variable names only, without substituted values. Applies to the rest of the document; takes no condition and has no #end form - switch back with #varsub.",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#novar",
                Description = "Show equations with substituted values only, without variable names. Applies to the rest of the document; takes no condition and has no #end form - switch back with #varsub.",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#split",
                Description = "Split long equations after the \"=\" symbol onto a new indented line. Applies to the rest of the document; takes no condition and has no #end form - switch back with #wrap.",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#wrap",
                Description = "Wrap long equations at the end of the line (default). Applies to the rest of the document; takes no condition and has no #end form - switch back with #split.",
                Category = "Output Control",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#round digits",
                Description = "Round output to n digits after decimal point",
                Category = "Settings/Rounding",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#round default",
                Description = "Restore rounding to default settings",
                Category = "Settings/Rounding",
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
                Description = "Override engine settings for subsequent lines via JSON. Use the Properties panel to see the available settings and edit them.",
                Category = "Settings/Overrides",
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
            // The parsing-mode switches #cpd, #html and #markdown are deliberately
            // absent: Calcpad.Core has no Keyword entry for them, so they must lint
            // as invalid keywords until Core implements them.

            // ============================================
            // BREAKPOINTS
            // ============================================
            new SnippetItem
            {
                Insert = "#pause",
                Description = "Pause calculation and wait for user to resume. Not supported in Calcpad.Web.",
                Category = "Breakpoints",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#input",
                Description = "Render input form and wait for user input. Not supported in Calcpad.Web.",
                Category = "Breakpoints",
                KeywordType = "Keyword"
            },

            // ============================================
            // SETTINGS / ANGLE UNITS
            // ============================================
            new SnippetItem
            {
                Insert = "#deg",
                Description = "Set angle units to degrees",
                Category = "Settings/Angle Units",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#rad",
                Description = "Set angle units to radians",
                Category = "Settings/Angle Units",
                KeywordType = "Keyword"
            },
            new SnippetItem
            {
                Insert = "#gra",
                Description = "Set angle units to gradians",
                Category = "Settings/Angle Units",
                KeywordType = "Keyword"
            },
        ];
    }
}
