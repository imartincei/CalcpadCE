namespace Calcpad.Highlighter.Tokenizer.Models
{
    /// <summary>
    /// Token types for syntax highlighting, matching the original Highlighter.cs Types enum
    /// </summary>
    public enum TokenType
    {
        // ===== Core Syntax =====

        /// <summary>Whitespace or unknown content</summary>
        None = 0,

        /// <summary>Numeric constants (e.g., 123, 3.14, 1e-5)</summary>
        Const = 1,

        /// <summary>Operators (e.g., +, -, *, /, =, ≤, ≥)</summary>
        Operator = 2,

        /// <summary>Brackets: (), [], {}</summary>
        Bracket = 3,

        /// <summary>
        /// Line continuation marker (underscore _ at end of line).
        /// Used to continue expressions across multiple lines.
        /// </summary>
        LineContinuation = 4,

        // ===== Identifiers =====

        /// <summary>Variable identifiers</summary>
        Variable = 5,

        /// <summary>
        /// Local variables scoped to a single expression or command block.
        /// Includes: function parameters, loop variables (#for), command scope variables (@var in $Sum, $Root, etc.)
        /// The linter should skip undefined variable checks for these tokens.
        /// </summary>
        LocalVariable = 6,

        /// <summary>Function names (built-in or user-defined)</summary>
        Function = 7,

        /// <summary>Macro names (ending with $)</summary>
        Macro = 8,

        /// <summary>
        /// Macro parameters in #def statements.
        /// e.g., in #def macro$(param1$; param2$), param1$ and param2$ are MacroParameter.
        /// These are local to the macro definition scope.
        /// </summary>
        MacroParameter = 9,

        /// <summary>Unit identifiers (e.g., m, kg, N/m²)</summary>
        Units = 10,

        /// <summary>
        /// Special setting variables used by the backend.
        /// Includes: PlotHeight, PlotWidth, PlotSVG, Precision, Tol, etc.
        /// These are not commands but configuration variables that affect behavior.
        /// </summary>
        Setting = 11,

        // ===== Keywords and Commands =====

        /// <summary>Keywords starting with # (e.g., #if, #else, #def)</summary>
        Keyword = 12,

        /// <summary>
        /// Control block keywords that start or control blocks.
        /// Includes: #if, #repeat, #for, #while, #def, #else, #else if, #break, #continue
        /// </summary>
        ControlBlockKeyword = 13,

        /// <summary>
        /// End keywords that close control blocks.
        /// Includes: #end if, #end def, #loop
        /// </summary>
        EndKeyword = 14,

        /// <summary>Commands starting with $ (e.g., $plot, $find, $sum)</summary>
        Command = 15,

        // ===== File and Data Exchange =====

        /// <summary>Include file paths</summary>
        Include = 16,

        /// <summary>
        /// File paths in data exchange keywords (#read, #write, #append).
        /// Distinguished from Include for separate handling.
        /// </summary>
        FilePath = 17,

        /// <summary>
        /// Sub-keywords in data exchange statements (#read, #write, #append).
        /// Includes: from, to, sep, type - these are not directives but parameters.
        /// </summary>
        DataExchangeKeyword = 18,

        // ===== Comments and Documentation =====

        /// <summary>Plain text comments enclosed in ' or " without HTML content</summary>
        Comment = 19,

        /// <summary>HTML comments (<!-- ... -->)</summary>
        HtmlComment = 20,

        /// <summary>HTML tags within comments</summary>
        Tag = 21,

        /// <summary>HTML content (text between HTML tags)</summary>
        HtmlContent = 22,

        /// <summary>JavaScript code within script tags in comments</summary>
        JavaScript = 23,

        /// <summary>CSS code within style tags in comments</summary>
        Css = 24,

        /// <summary>SVG markup within svg tags in comments</summary>
        Svg = 25,

        // ===== Special =====

        /// <summary>Input markers (? or #{...})</summary>
        Input = 26,

        /// <summary>Format specifiers (e.g., :f2, :e3)</summary>
        Format = 27,

        /// <summary>JSON payload following the #settings directive (`#settings {...}`).</summary>
        SettingsJson = 28,

        // [Future Reserved] 29-30 previously held StringFunction, StringTable.
        // Kept as reserved ordinals to avoid renumbering serialized values.
        FutureReserved29 = 29,
        FutureReserved30 = 30,

        /// <summary>Path value following #ProjectPath/#LibraryPath (e.g. `#ProjectPath C:/Jobs/1042`).</summary>
        PathRoot = 31
    }
}
