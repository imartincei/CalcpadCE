using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private sealed class UiPropertyMetadata
        {
            public string Type { get; init; }
            public string Style { get; init; }
            public string ReportStyle { get; init; }
            public string VariableName { get; init; }
            /// <summary>Grid cells captured for the widget; set while the value is prepared.</summary>
            public string DataValues { get; set; }
            /// <summary>"name:n", n being the ordinal of this declaration among same named ones.</summary>
            public string DeclarationKey { get; init; }
            /// <summary>
            /// The control identity: <see cref="DeclarationKey"/>, with ":p" or ":p.q"
            /// appended inside loops for the enclosing pass numbers.
            /// </summary>
            public string Key { get; init; }
            /// <summary>Grid rows; filled in from the evaluated value when the source sized
            /// the grid with an expression, so not known until the line has been calculated.</summary>
            public int Rows { get; set; }
            public int Columns { get; set; }
            /// <summary>True when the JSON declared both rows and columns, rather than them
            /// being auto-detected. Only then is the right hand side literal reshaped.</summary>
            public bool HasDeclaredShape { get; init; }
            public string[] ColumnHeaders { get; init; }
            public string[] RowHeaders { get; init; }
            public string[] Keys { get; init; }
            public string[] Values { get; init; }
        }

        /// <summary>
        /// When true, #UI keywords inject interactive input elements into the HTML output
        /// and #post blocks are hidden. When false, #UI lines render exactly as they would
        /// without the keyword, apart from an optional "reportStyle" class.
        /// </summary>
        public bool EnableUi { get; set; }

        /// <summary>
        /// Maps #UI control keys to override values. An entry here replaces the right hand
        /// side of the annotated assignment before it is evaluated. Applied in both UI and
        /// report mode, so a report shows the values that were entered.
        /// Keys are the control identity emitted in data-ui-var, e.g. "name:2" or "name:2:3"
        /// inside a loop. Progressively broader forms are accepted as fallbacks: "name:2"
        /// covers every pass of that declaration, a bare "name" covers every declaration.
        /// </summary>
        public Dictionary<string, string> UiOverrides { get; set; }

        /// <summary>
        /// The controls declared on the current line, in source order. A #UI line may carry
        /// several assignments separated by inline comments - <c>#UI 'd ='d = 1', 'x = 2'</c> -
        /// and each one becomes its own control, sharing the line's JSON properties but
        /// keyed and overridden separately.
        /// </summary>
        private List<UiPropertyMetadata> _lineUiControls;
        /// <summary>How far <see cref="TakeUiControl"/> has consumed <see cref="_lineUiControls"/>.</summary>
        private int _uiTakeIndex;
        private int _uiSkipChars;
        private readonly Dictionary<string, int> _uiVarCounts = [];
        private readonly Dictionary<(string Name, int Line, int Occurrence), int> _uiDeclarationIndex = [];
        /// <summary>Occurrences of each name so far on the current line, so a name used twice gets two keys.</summary>
        private readonly Dictionary<string, int> _uiLineOccurrences = [];

        /// <summary>True when the current line declared any #UI control.</summary>
        private bool HasUiControls => _lineUiControls is { Count: > 0 };

        private const int UiKeywordLength = 3; // "#ui"
        private const char ThinSpace = '\u2009'; // MathParser separates a value from its unit with this

        /// <summary>
        /// Parses the #UI keyword arguments from the line. The JSON block is optional -
        /// type and grid size are auto-detected from the expression when it is omitted.
        /// Always computes _uiSkipChars so the prefix is stripped before tokenization.
        /// One control is recorded per assignment on the line; each is consumed later,
        /// once MathParser has rendered the expression it belongs to.
        /// </summary>
        private KeywordResult ParseKeywordUi(ReadOnlySpan<char> s)
        {
            ResetUiState();
            var cursor = UiKeywordLength;
            while (cursor < s.Length && s[cursor] == ' ')
                ++cursor;

            var properties = new UiDto();
            if (cursor < s.Length && s[cursor] == '{')
            {
                var braceEnd = s.IndexOf('}');
                if (braceEnd < 0)
                {
                    AppendError(s.ToString(), Messages.Improper_format_for_UI_keyword_Missing_closing_brace, _currentLine);
                    _uiSkipChars = s.Length;
                    return KeywordResult.None;
                }
                try
                {
                    properties = UiDto.Parse(s[cursor..(braceEnd + 1)].ToString()) ?? new UiDto();
                }
                catch (JsonException)
                {
                    AppendError(s.ToString(), Messages.Improper_format_for_UI_keyword_Invalid_JSON, _currentLine);
                    _uiSkipChars = SkipSpaces(s, UiKeywordLength);
                    return KeywordResult.None;
                }
                _uiSkipChars = SkipSpaces(s, braceEnd + 1);
            }
            else
                _uiSkipChars = cursor;

            var errors = properties.Validate();
            if (errors.Count > 0)
            {
                foreach (var error in errors)
                    AppendError(s.ToString(), error.Message, _currentLine);

                return KeywordResult.None;
            }

            int uiRows = properties.Rows ?? 0, uiColumns = properties.Columns ?? 0;
            var hasDeclaredShape = uiRows > 0 && uiColumns > 0;
            _lineUiControls = [];
            foreach (var assignment in UiSyntax.EnumerateAssignments(s[_uiSkipChars..]))
            {
                if (assignment.Name.EndsWith('$'))
                {
                    AppendError(s.ToString(), Messages.String_mode_is_not_supported_by_the_UI_keyword, _currentLine);
                    return KeywordResult.None;
                }
                if (!UiSyntax.IsValue(assignment.Rhs))
                {
                    AppendError(s.ToString(), Messages.UI_directives_do_not_support_expressions, _currentLine);
                    return KeywordResult.None;
                }

                var type = properties.Type ?? (IsDatagridRhs(assignment.Rhs) ? "datagrid" : "entry");
                int rows = uiRows, columns = uiColumns;
                if (type == "datagrid" && !hasDeclaredShape)
                {
                    if (assignment.Rhs.Length > 1 && assignment.Rhs[0] == '[' && assignment.Rhs[^1] == ']')
                        AutoDetectGridSize(assignment.Rhs, ref rows, ref columns);
                    else
                        AutoDetectGridSizeFromFunction(assignment.Rhs, ref rows, ref columns);
                }

                var (declarationKey, key) = GetUiKey(assignment.Name);
                _lineUiControls.Add(new UiPropertyMetadata
                {
                    Type = type,
                    Style = properties.Style,
                    ReportStyle = properties.ReportStyle,
                    VariableName = assignment.Name,
                    DeclarationKey = declarationKey,
                    Key = key,
                    Rows = rows,
                    Columns = columns,
                    HasDeclaredShape = hasDeclaredShape,
                    ColumnHeaders = properties.ColumnHeaders,
                    RowHeaders = properties.RowHeaders,
                    Keys = properties.Keys,
                    Values = properties.Values
                });
            }
            if (_lineUiControls.Count == 0)
            {
                _lineUiControls = null;
                AppendError(s.ToString(), Messages.The_UI_keyword_requires_a_variable_assignment, _currentLine);
            }
            return KeywordResult.None;
        }

        /// <summary>
        /// The control for the expression about to be rendered, or null when it declares
        /// none. Matched by variable name rather than by position, so a segment that is not
        /// an assignment - or any mismatch between comment splitting and tokenization -
        /// simply renders as it normally would.
        /// </summary>
        private UiPropertyMetadata TakeUiControl(string expression)
        {
            if (!HasUiControls)
                return null;

            var eqIndex = IndexOfAssignment(expression);
            if (eqIndex < 1)
                return null;

            var name = ExtractVariableName(expression.AsSpan(0, eqIndex));
            for (var i = _uiTakeIndex; i < _lineUiControls.Count; ++i)
            {
                if (_lineUiControls[i].VariableName != name)
                    continue;

                _uiTakeIndex = i + 1;
                return _lineUiControls[i];
            }
            return null;
        }

        /// <summary>
        /// Builds the identity of a control: the variable name, the ordinal of its
        /// declaration among same named ones, and the enclosing loop passes.
        ///
        /// The ordinal is keyed on the source line, so re-running a line - which is what a
        /// loop does - reuses it rather than advancing the count. It is assigned even when
        /// the line is inside an unsatisfied #if, so flipping a branch does not renumber the
        /// controls that follow it. Loop passes then separate the repeats of one declaration.
        /// </summary>
        private (string DeclarationKey, string Key) GetUiKey(string varName)
        {
            var occurrence = _uiLineOccurrences.GetValueOrDefault(varName);
            _uiLineOccurrences[varName] = occurrence + 1;
            var id = (varName, _currentLine, occurrence);
            if (!_uiDeclarationIndex.TryGetValue(id, out var ordinal))
            {
                ordinal = _uiVarCounts.GetValueOrDefault(varName) + 1;
                _uiVarCounts[varName] = ordinal;
                _uiDeclarationIndex[id] = ordinal;
            }
            var declarationKey = $"{varName}:{ordinal}";
            if (_loops.Count == 0)
                return (declarationKey, declarationKey);

            var passes = new int[_loops.Count];
            var i = _loops.Count;
            foreach (var loop in _loops) // enumerates innermost first
                passes[--i] = loop.Pass;

            return (declarationKey, $"{declarationKey}:{string.Join('.', passes)}");
        }

        /// <summary>
        /// Index of the assignment '=' in <paramref name="s"/>, ignoring inline comments.
        /// A #UI line usually labels its control with one, and the label can itself contain
        /// an '=' - <c>#UI '2&amp;middot;&lt;i&gt;r&lt;/i&gt; ='d = 1</c>. Returns -1 when
        /// the line makes no assignment.
        /// </summary>
        private static int IndexOfAssignment(ReadOnlySpan<char> s)
        {
            // Segments are the consumed prefix each time, so their lengths sum to the offset.
            var offset = 0;
            foreach (var segment in s.EnumerateComments())
            {
                if (UiSyntax.IsCode(segment))
                {
                    var i = segment.IndexOf('=');
                    if (i >= 0)
                        return offset + i;
                }
                offset += segment.Length;
            }
            return -1;
        }

        /// <summary>
        /// The variable name on the left of the assignment, with the inline comment that
        /// labels the control removed - it is display text, not part of the name.
        /// </summary>
        private static string ExtractVariableName(ReadOnlySpan<char> lhs)
        {
            var sb = new StringBuilder();
            foreach (var segment in lhs.EnumerateComments())
            {
                if (UiSyntax.IsCode(segment))
                    sb.Append(segment);
            }
            return sb.ToString().Trim();
        }

        private static int SkipSpaces(ReadOnlySpan<char> s, int start)
        {
            while (start < s.Length && s[start] == ' ')
                ++start;
            return start;
        }

        /// <summary>
        /// True when the right hand side is a vector/matrix literal or a vector()/matrix() call.
        /// </summary>
        private static bool IsDatagridRhs(ReadOnlySpan<char> rhs) =>
            rhs.Length > 1 && rhs[0] == '[' && rhs[^1] == ']' ||
            StartsWithFunction(rhs, "vector") ||
            StartsWithFunction(rhs, "matrix");

        /// <summary>
        /// Checks if rhs starts with a function name followed by optional whitespace then '('.
        /// </summary>
        private static bool StartsWithFunction(ReadOnlySpan<char> rhs, ReadOnlySpan<char> funcName)
        {
            if (!rhs.StartsWith(funcName, StringComparison.OrdinalIgnoreCase))
                return false;

            var rest = rhs[funcName.Length..].TrimStart();
            return rest.Length > 0 && rest[0] == '(';
        }

        /// <summary>
        /// Auto-detects rows and columns from a vector/matrix literal.
        /// '|' separates rows, ';' separates elements within a row, so a vector
        /// [1; 2; 3] is displayed as a single row of three columns.
        /// </summary>
        private static void AutoDetectGridSize(ReadOnlySpan<char> rhs, ref int rows, ref int columns)
        {
            var bracketStart = rhs.IndexOf('[');
            var bracketEnd = rhs.LastIndexOf(']');
            if (bracketStart < 0 || bracketEnd <= bracketStart)
                return;

            var content = rhs[(bracketStart + 1)..bracketEnd];
            var pipeIndex = content.IndexOf('|');
            var firstRow = pipeIndex < 0 ? content : content[..pipeIndex];
            if (rows == 0)
                rows = pipeIndex < 0 ? 1 : content.Count('|') + 1;
            if (columns == 0)
                columns = firstRow.Count(';') + 1;
        }

        /// <summary>
        /// Auto-detects rows and columns from a vector(n) or matrix(m; n) call written with
        /// literal counts. Computed ones - matrix(r; c), matrix(len(x); len(y)) - are left
        /// for <see cref="ResolveDatagridShape"/> to read off the evaluated value.
        /// </summary>
        private static void AutoDetectGridSizeFromFunction(ReadOnlySpan<char> rhs, ref int rows, ref int columns)
        {
            var parenStart = rhs.IndexOf('(');
            var parenEnd = rhs.LastIndexOf(')');
            if (parenStart < 0 || parenEnd <= parenStart)
                return;

            var args = rhs[(parenStart + 1)..parenEnd].Trim();
            if (StartsWithFunction(rhs, "vector"))
            {
                if (int.TryParse(args, out var n) && n > 0)
                {
                    if (rows == 0) rows = 1;
                    if (columns == 0) columns = n;
                }
                return;
            }
            var semicolon = args.IndexOf(';');
            if (semicolon > 0 &&
                int.TryParse(args[..semicolon].Trim(), out var m) && m > 0 &&
                int.TryParse(args[(semicolon + 1)..].Trim(), out var k) && k > 0)
            {
                if (rows == 0) rows = m;
                if (columns == 0) columns = k;
            }
        }

        /// <summary>
        /// Fills in a grid shape the source did not spell out - matrix(r; c),
        /// matrix(len(x); len(y)) - from the vector or matrix the line evaluated to, a vector
        /// being a single row. Runs once the line has been calculated, which is why the
        /// grid element is the one carrying the shape the widget reads.
        /// </summary>
        private void ResolveDatagridShape(UiPropertyMetadata ui)
        {
            if (ui.Rows > 0 && ui.Columns > 0)
                return;

            var (rows, columns) = _parser.GetVariableShape(ui.VariableName);
            if (ui.Rows == 0)
                ui.Rows = rows;

            if (ui.Columns == 0)
                ui.Columns = columns;
        }

        /// <summary>
        /// Fills in the grid shape from a vector(n)/matrix(m; n) call whose counts are
        /// expressions, by evaluating the arguments. An override replaces the whole call, so
        /// the shape it asks for has to be known before that happens - otherwise the entered
        /// values keep whatever shape they were saved with and editing the source no longer
        /// resizes the grid. Leaves the shape alone when an argument cannot be evaluated,
        /// which puts <see cref="ResolveDatagridShape"/> back in charge of it.
        /// </summary>
        private void ResolveShapeFromSizingCall(UiPropertyMetadata ui, string expression)
        {
            if (ui.Rows > 0 && ui.Columns > 0 || !_calculate || _isVal < 0)
                return;

            var eqIndex = IndexOfAssignment(expression);
            if (eqIndex < 0)
                return;

            var rhs = expression.AsSpan(eqIndex + 1).Trim();
            var isVector = StartsWithFunction(rhs, "vector");
            if (!isVector && !StartsWithFunction(rhs, "matrix"))
                return;

            var parenStart = rhs.IndexOf('(');
            var parenEnd = rhs.LastIndexOf(')');
            if (parenStart < 0 || parenEnd <= parenStart)
                return;

            var args = rhs[(parenStart + 1)..parenEnd];
            if (isVector)
            {
                var n = EvaluateCount(args);
                if (n > 0)
                {
                    if (ui.Rows == 0)
                        ui.Rows = 1;

                    if (ui.Columns == 0)
                        ui.Columns = n;
                }
                return;
            }
            var separator = IndexOfArgumentSeparator(args);
            if (separator < 0)
                return;

            var m = EvaluateCount(args[..separator]);
            var k = EvaluateCount(args[(separator + 1)..]);
            if (m > 0 && k > 0)
            {
                if (ui.Rows == 0)
                    ui.Rows = m;

                if (ui.Columns == 0)
                    ui.Columns = k;
            }
        }

        /// <summary>
        /// Index of the ';' separating the arguments of a call, ignoring the ones belonging to
        /// a nested call - matrix(max(a; b); n). Returns -1 when there is only one argument.
        /// </summary>
        private static int IndexOfArgumentSeparator(ReadOnlySpan<char> args)
        {
            var depth = 0;
            for (var i = 0; i < args.Length; ++i)
            {
                var c = args[i];
                if (c is '(' or '[' or '{')
                    ++depth;
                else if (c is ')' or ']' or '}')
                    --depth;
                else if (c == ';' && depth == 0)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// The whole number a grid sizing argument evaluates to, or 0 when it does not
        /// evaluate to one. Runs on the live parser, so it reads the same variables the line
        /// itself will - including ones a #UI control has overridden. The caller parses the
        /// real expression straight afterwards, which resets what this leaves behind.
        /// </summary>
        private int EvaluateCount(ReadOnlySpan<char> expression)
        {
            try
            {
                _parser.Parse(expression, false);
                _parser.Calculate(false);
                var n = Math.Round(_parser.Real);
                return n > 0 && n < int.MaxValue ? (int)n : 0;
            }
            catch (MathParserException)
            {
                _parser.ResetStack();
                return 0;
            }
        }

        /// <summary>
        /// Rewrites the assignment before MathParser evaluates it: fits a datagrid literal
        /// to the declared shape, applies any override, then captures the resulting cells
        /// for the grid widget. Only the override runs in report mode - it is the entered
        /// value - while the declared shape describes the grid and so applies to the form.
        ///
        /// A datagrid override is fitted to the shape the source asks for, so editing the
        /// source resizes the grid even once values have been entered: cells outside the new
        /// shape are trimmed off and new ones come in as zeros. What was saved is left alone,
        /// so shrinking a grid and growing it back brings the trimmed values with it.
        /// </summary>
        private string PrepareUiExpression(UiPropertyMetadata ui, string expression)
        {
            var isDatagrid = ui.Type == "datagrid";
            if (isDatagrid && EnableUi)
                expression = ResizeDatagridMatrixToFit(ui, expression);

            if (TryGetUiOverride(ui, out var value))
            {
                if (isDatagrid)
                {
                    ResolveShapeFromSizingCall(ui, expression);
                    if (ui.Rows > 0 && ui.Columns > 0)
                        value = ReshapeMatrixLiteral(value, ui.Rows, ui.Columns);
                }
                var overridden = ApplyUiOverride(ui, expression, value);
                if (overridden is not null)
                    expression = overridden;
            }

            if (isDatagrid)
                CaptureDatagridValues(ui, expression);

            return expression;
        }

        /// <summary>
        /// Fits the right hand side literal to the rows and columns the JSON block declared,
        /// keeping the values already written: cells beyond the literal become 0, and cells
        /// beyond the declared shape are dropped. A right hand side that is not a bracket
        /// literal - vector(n), matrix(m; n) - is left alone, since it sizes itself.
        /// </summary>
        private static string ResizeDatagridMatrixToFit(UiPropertyMetadata ui, string expression) =>
            ui.HasDeclaredShape ? ReshapeMatrixLiteral(expression, ui.Rows, ui.Columns) : expression;

        /// <summary>
        /// Rewrites the bracket literal in <paramref name="s"/> to <paramref name="rows"/> by
        /// <paramref name="columns"/> cells, keeping the values already written: cells the
        /// literal does not reach become 0 and cells outside the shape are dropped. Text with
        /// no literal in it is returned unchanged.
        /// </summary>
        private static string ReshapeMatrixLiteral(string s, int rows, int columns)
        {
            var bracketStart = s.IndexOf('[');
            var bracketEnd = s.LastIndexOf(']');
            if (bracketStart < 0 || bracketEnd <= bracketStart)
                return s;

            var cells = SplitMatrixLiteral(s[(bracketStart + 1)..bracketEnd]);
            var sb = new StringBuilder();
            for (var r = 0; r < rows; ++r)
            {
                if (r > 0)
                    sb.Append(" | ");

                for (var c = 0; c < columns; ++c)
                {
                    if (c > 0)
                        sb.Append("; ");

                    var value = r < cells.Count && c < cells[r].Length ? cells[r][c] : string.Empty;
                    sb.Append(value.Length == 0 ? "0" : value);
                }
            }
            return string.Concat(s.AsSpan(0, bracketStart + 1), sb.ToString(), s.AsSpan(bracketEnd));
        }

        /// <summary>
        /// Splits the inside of a vector/matrix literal into rows of cells.
        /// '|' separates rows, ';' separates cells within a row.
        /// </summary>
        private static List<string[]> SplitMatrixLiteral(string content)
        {
            var rows = new List<string[]>();
            foreach (var row in content.Split('|'))
            {
                var cells = row.Split(';');
                for (var i = 0; i < cells.Length; ++i)
                    cells[i] = cells[i].Trim();

                rows.Add(cells);
            }
            return rows;
        }

        /// <summary>
        /// Captures the datagrid cell values so they can be handed to the grid widget.
        /// Bracket literals are taken as written; vector(n)/matrix(m; n) produce zeros.
        /// </summary>
        private static void CaptureDatagridValues(UiPropertyMetadata ui, string expression)
        {
            var eqIndex = IndexOfAssignment(expression);
            if (eqIndex < 0)
                return;

            var rhs = expression[(eqIndex + 1)..].Trim();
            var bracketStart = rhs.IndexOf('[');
            var bracketEnd = rhs.LastIndexOf(']');
            if (bracketStart >= 0 && bracketEnd > bracketStart)
            {
                ui.DataValues = rhs[(bracketStart + 1)..bracketEnd].Replace(" ", "");
                return;
            }
            var row = string.Join(";", Enumerable.Repeat("0", ui.Columns));
            ui.DataValues = string.Join("|", Enumerable.Repeat(row, ui.Rows));
        }

        /// <summary>
        /// The override that applies to a control, if any. Narrowest match wins: this exact
        /// control, then every pass of this declaration, then every declaration of the name -
        /// the last being what a hand written override for a variable that appears only once
        /// looks like.
        /// </summary>
        private bool TryGetUiOverride(UiPropertyMetadata ui, out string value)
        {
            value = null;
            return UiOverrides is not null &&
                (UiOverrides.TryGetValue(ui.Key, out value) ||
                UiOverrides.TryGetValue(ui.DeclarationKey, out value) ||
                UiOverrides.TryGetValue(ui.VariableName, out value));
        }

        /// <summary>
        /// Substitutes the override value into the assignment before MathParser sees it.
        /// Returns null when the assignment has nothing to substitute into, so the caller
        /// keeps the original text.
        /// </summary>
        private static string ApplyUiOverride(UiPropertyMetadata ui, string expression, string value)
        {
            var eqIndex = IndexOfAssignment(expression);
            if (eqIndex < 0)
                return null;

            var lhs = expression[..(eqIndex + 1)];
            var rhs = expression[(eqIndex + 1)..].TrimStart();
            if (ui.Type == "datagrid")
            {
                // A grid sized by vector(n)/matrix(m; n) has no literal to write into, so the
                // entered one takes the place of the whole call.
                var bracketEnd = rhs.LastIndexOf(']');
                return bracketEnd < 0 || rhs.IndexOf('[') < 0 ?
                    $"{lhs} {value}" :
                    $"{lhs} {value}{rhs[(bracketEnd + 1)..]}";
            }
            // A dropdown or radio picks one of the declared values, which carries its own
            // unit, so it replaces the whole right hand side. An entry holds the number
            // alone - the unit stays in the markup beside it - so only the number is swapped.
            if (ui.Type is "dropdown" or "radio")
                return $"{lhs} {value}";

            var i = 0;
            while (i < rhs.Length && IsNumericChar(rhs[i]))
                ++i;

            return i == 0 ? null : $"{lhs} {value}{rhs[i..]}";
        }

        private static bool IsNumericChar(char c) =>
            c is >= '0' and <= '9' or '.' or '-' or '+' or 'e' or 'E';

        /// <summary>
        /// Returns the data-ui-* attributes for the element wrapping the whole line.
        /// </summary>
        private string GetUiAttributes(UiPropertyMetadata ui)
        {
            var sb = new StringBuilder()
                .Append($" data-ui-type=\"{ui.Type}\"")
                .Append($" data-ui-line=\"{_parser.Line}\"")
                .Append($" data-ui-var=\"{HttpUtility.HtmlAttributeEncode(ui.Key)}\"");
            if (ui.Style is not null)
                sb.Append($" data-ui-style=\"{HttpUtility.HtmlAttributeEncode(ui.Style)}\"");
            if (ui.Type == "datagrid")
                sb.Append($" data-ui-rows=\"{ui.Rows}\" data-ui-columns=\"{ui.Columns}\"");

            return sb.ToString();
        }

        /// <summary>
        /// Merges the "reportStyle" class into the attribute string produced by HtmlId,
        /// which already carries a class in Debug mode.
        /// </summary>
        private string AddReportStyle(UiPropertyMetadata ui, string attributes)
        {
            var style = ui.ReportStyle;
            if (string.IsNullOrEmpty(style))
                return attributes;

            const string classAttr = "class=\"";
            var i = attributes.IndexOf(classAttr, StringComparison.Ordinal);
            return i < 0 ?
                $"{attributes} class=\"{HttpUtility.HtmlAttributeEncode(style)}\"" :
                attributes.Insert(i + classAttr.Length, HttpUtility.HtmlAttributeEncode(style) + " ");
        }

        /// <summary>
        /// Replaces the result part of the rendered equation with the matching input control.
        /// An entry keeps the "name = " prefix and the unit around it, since it shows the
        /// number itself; the other controls stand alone, so the whole equation gives way to
        /// them and any label is the text the author writes beside the directive.
        /// </summary>
        private string InjectUiInput(UiPropertyMetadata ui, string equationHtml) =>
            ui.Type switch
            {
                "dropdown" => ReplaceEquation(equationHtml, (v, u) => BuildUiDropdown(ui, SelectedValue(v, u))),
                "radio" => ReplaceEquation(equationHtml, (v, u) => BuildUiRadio(ui, SelectedValue(v, u))),
                "checkbox" => ReplaceEquation(equationHtml, (v, _) => BuildUiCheckbox(ui, v)),
                _ => InjectUiControl(equationHtml, (v, _) => BuildUiEntry(ui, v))
            };

        private static string InjectUiControl(string equationHtml, Func<string, string, string> build)
        {
            var resultStart = ResultStart(equationHtml);
            if (resultStart < 0)
                return equationHtml;

            SplitValueAndUnit(equationHtml[resultStart..], out var value, out var unitHtml);
            return equationHtml[..resultStart] + build(value, unitHtml) + unitHtml;
        }

        /// <summary>
        /// Renders the control in place of the whole equation. The value and unit are still
        /// read off it, since a dropdown or radio needs them to tell which of its values is
        /// the current one.
        /// </summary>
        private static string ReplaceEquation(string equationHtml, Func<string, string, string> build)
        {
            var resultStart = ResultStart(equationHtml);
            if (resultStart < 0)
                return equationHtml;

            SplitValueAndUnit(equationHtml[resultStart..], out var value, out var unitHtml);
            return build(value, unitHtml);
        }

        /// <summary>Index just past the last " = " of the rendered equation, or -1.</summary>
        private static int ResultStart(string equationHtml)
        {
            const string assignOp = " = ";
            var lastAssign = equationHtml.LastIndexOf(assignOp, StringComparison.Ordinal);
            return lastAssign < 0 ? -1 : lastAssign + assignOp.Length;
        }

        /// <summary>
        /// The rendered result as a declared value looks - number and unit, no markup and
        /// no spaces - so it can be matched against the "values" the JSON block listed.
        /// </summary>
        private static string SelectedValue(string value, string unitHtml)
        {
            var sb = new StringBuilder(StripSpaces(value));
            var inTag = false;
            foreach (var c in unitHtml)
            {
                if (c == '<')
                    inTag = true;
                else if (c == '>')
                    inTag = false;
                else if (!inTag && c is not (' ' or ThinSpace))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static string StripSpaces(string s) => s.Replace(" ", string.Empty);

        private static string UiClass(UiPropertyMetadata ui, string baseClass) =>
            HttpUtility.HtmlAttributeEncode(ui.Style is null ?
                baseClass :
                $"{baseClass} {ui.Style}");

        /// <summary>
        /// The control identity plus the source line it came from, so a host can write an
        /// edited value back into the document. The line matches the "data-source-line" the
        /// wrapping tag carries, i.e. 1 based and mapped back through macro expansion.
        /// </summary>
        private string UiBinding(UiPropertyMetadata ui) =>
            $" data-ui-var=\"{HttpUtility.HtmlAttributeEncode(ui.Key)}\" data-ui-line=\"{_parser.Line}\"";

        private string BuildUiEntry(UiPropertyMetadata ui, string value) =>
            $"<input type=\"text\" class=\"{UiClass(ui, "calcpad-ui-input")}\" value=\"{HttpUtility.HtmlAttributeEncode(value)}\"{UiBinding(ui)}>";

        private string BuildUiDropdown(UiPropertyMetadata ui, string value)
        {
            var sb = new StringBuilder()
                .Append($"<select class=\"{UiClass(ui, "calcpad-ui-dropdown")}\"{UiBinding(ui)}>");
            for (int i = 0, len = ui.Keys.Length; i < len; ++i)
            {
                var selected = StripSpaces(ui.Values[i]) == value ? " selected" : string.Empty;
                sb.Append($"<option value=\"{HttpUtility.HtmlAttributeEncode(ui.Values[i])}\"{selected}>")
                  .Append($"{HttpUtility.HtmlEncode(ui.Keys[i])}</option>");
            }
            return sb.Append("</select>").ToString();
        }

        private string BuildUiRadio(UiPropertyMetadata ui, string value)
        {
            var group = HttpUtility.HtmlAttributeEncode($"ui-radio-{ui.Key}");
            var sb = new StringBuilder()
                .Append($"<span class=\"{UiClass(ui, "calcpad-ui-radio")}\"{UiBinding(ui)}>");
            for (int i = 0, len = ui.Keys.Length; i < len; ++i)
            {
                var isChecked = StripSpaces(ui.Values[i]) == value ? " checked" : string.Empty;
                sb.Append("<label class=\"calcpad-ui-radio-label\">")
                  .Append($"<input type=\"radio\" name=\"{group}\" value=\"{HttpUtility.HtmlAttributeEncode(ui.Values[i])}\"{isChecked}>")
                  .Append($" {HttpUtility.HtmlEncode(ui.Keys[i])}</label>");
            }
            return sb.Append("</span>").ToString();
        }

        private string BuildUiCheckbox(UiPropertyMetadata ui, string value)
        {
            var isChecked = value.Trim() == "1" ? " checked" : string.Empty;
            return $"<input type=\"checkbox\" class=\"{UiClass(ui, "calcpad-ui-checkbox")}\"{UiBinding(ui)}{isChecked}>";
        }

        /// <summary>
        /// Returns the grid container. Emitted after the closing tag of the line so it is a
        /// block level sibling rather than nested inside inline elements.
        /// </summary>
        private string BuildUiDatagrid(UiPropertyMetadata ui)
        {
            var sb = new StringBuilder()
                .Append($"<div class=\"{UiClass(ui, "calcpad-ui-datagrid")}\"{UiBinding(ui)}")
                .Append($" data-ui-rows=\"{ui.Rows}\" data-ui-columns=\"{ui.Columns}\"")
                .Append($" data-ui-values=\"{HttpUtility.HtmlAttributeEncode(ui.DataValues ?? string.Empty)}\"");
            if (ui.ColumnHeaders is not null)
                sb.Append($" data-ui-col-headers=\"{HttpUtility.HtmlAttributeEncode(string.Join(",", ui.ColumnHeaders))}\"");
            if (ui.RowHeaders is not null)
                sb.Append($" data-ui-row-headers=\"{HttpUtility.HtmlAttributeEncode(string.Join(",", ui.RowHeaders))}\"");

            return sb.Append("></div>").ToString();
        }

        /// <summary>
        /// Splits a result fragment like "5 &lt;i&gt;ft&lt;/i&gt;" into value and unit markup,
        /// so the unit stays outside the input control. The value is plain text, so the
        /// markup begins where the unit does - splitting on the first tag rather than on
        /// the first &lt;i&gt; keeps a compound unit, which wraps its parts in a span,
        /// whole instead of cutting into it.
        /// </summary>
        private static void SplitValueAndUnit(string resultHtml, out string value, out string unitHtml)
        {
            var unitStart = resultHtml.IndexOf('<');
            if (unitStart < 0)
            {
                value = resultHtml.Trim();
                unitHtml = string.Empty;
            }
            else if (unitStart == 0)
            {
                value = string.Empty;
                unitHtml = resultHtml;
            }
            else
            {
                value = resultHtml[..unitStart].TrimEnd(ThinSpace, ' ');
                unitHtml = ThinSpace + resultHtml[unitStart..];
            }
            // The angle units are written straight after the number, without markup of
            // their own, so they have to be split off the value by hand.
            var i = value.Length;
            while (i > 0 && value[i - 1] is '°' or '′' or '″')
                --i;

            if (i == value.Length)
                return;

            unitHtml = value[i..] + unitHtml;
            value = value[..i];
        }

        private void ResetUiState()
        {
            _lineUiControls = null;
            _uiTakeIndex = 0;
            _uiSkipChars = 0;
            _uiLineOccurrences.Clear();
        }
    }
}
