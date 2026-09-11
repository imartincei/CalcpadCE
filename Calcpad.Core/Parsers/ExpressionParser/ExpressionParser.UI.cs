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
            public string DataValues { get; set; }
            /// <summary>Unit of each cell, kept out of the values when the grid edits numbers only.</summary>
            public string[][] CellUnits { get; set; }
            /// <summary>Right hand side as written, for controls that edit the expression itself.</summary>
            public string SourceValue { get; set; }
            public string DeclarationKey { get; init; }

            public string Key { get; init; }
            public bool KeepsUnits { get; init; }
            public bool AllowsExpression { get; init; }
            public int Rows { get; set; }
            public int Columns { get; set; }
            public bool HasDeclaredShape { get; init; }
            public string[] ColumnHeaders { get; init; }
            public string[] RowHeaders { get; init; }
            public int? Width { get; init; }
            public int? RowHeaderWidth { get; init; }
            public int[] ColumnWidths { get; init; }
            public string[] Keys { get; init; }
            public string[] Values { get; init; }
        }

        public bool EnableUi { get; set; }
        public Dictionary<string, string> UiOverrides { get; set; }
        private List<UiPropertyMetadata> _lineUiControls;
        /// <summary>Error of a control that was still rendered, flushed once its line's paragraph closes.</summary>
        private string _pendingUiError;
        private int _uiTakeIndex;
        private int _uiSkipChars;
        private readonly Dictionary<string, int> _uiVarCounts = [];
        private readonly Dictionary<(string Name, int Line, int Occurrence), int> _uiDeclarationIndex = [];
        private readonly Dictionary<string, int> _uiLineOccurrences = [];
        private bool HasUiControls => _lineUiControls is { Count: > 0 };
        private const string UiKeyword = "#UI";
        private const char ThinSpace = '\u2009'; // MathParser separates a value from its unit with this

        private KeywordResult ParseKeywordUi(ReadOnlySpan<char> s)
        {
            ResetUiState();
            var cursor = UiKeyword.Length;
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
                    AppendError(s.ToString(), string.Format(Messages.Invalid_JSON_in_0, UiKeyword), _currentLine);
                    _uiSkipChars = SkipSpaces(s, UiKeyword.Length);
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
                    AppendError(s.ToString(), Messages.Only_numbers_are_supported_by_the_UI_keyword, _currentLine);
                    return KeywordResult.None;
                }
                var type = UiSyntax.ResolveType(properties.Type, assignment.Rhs);
                if (!UiSyntax.IsValue(assignment.Rhs, type, properties.AllowsExpression))
                {
                    AppendError(s.ToString(), Messages.UI_directives_do_not_support_expressions, _currentLine);
                    return KeywordResult.None;
                }

                int rows = uiRows, columns = uiColumns;
                if (type == "datagrid" && !hasDeclaredShape)
                {
                    if (assignment.Rhs.Length > 1 && assignment.Rhs[0] == '[' && assignment.Rhs[^1] == ']')
                        AutoDetectGridSize(assignment.Rhs, ref rows, ref columns);
                    else
                        AutoDetectGridSizeFromFunction(assignment.Rhs, ref rows, ref columns);
                }
                if (type == "datagrid")
                    CheckGridSize(rows, columns);

                var (declarationKey, key) = GetUiKey(assignment.Name);
                _lineUiControls.Add(new UiPropertyMetadata
                {
                    Type = type,
                    Style = properties.Style,
                    ReportStyle = properties.ReportStyle,
                    VariableName = assignment.Name,
                    SourceValue = assignment.Rhs,
                    DeclarationKey = declarationKey,
                    Key = key,
                    KeepsUnits = properties.KeepsUnits,
                    AllowsExpression = properties.AllowsExpression,
                    Rows = rows,
                    Columns = columns,
                    HasDeclaredShape = hasDeclaredShape,
                    ColumnHeaders = properties.ColumnHeaders,
                    RowHeaders = properties.RowHeaders,
                    Width = properties.GetWidth(),
                    RowHeaderWidth = properties.RowHeaderWidth,
                    ColumnWidths = properties.ColumnWidths,
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

        private static void CheckGridSize(int rows, int columns)
        {
            if ((long)rows * columns > UiDto.MaxSize)
                throw Exceptions.DatagridSizeLimit();
        }

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

        private static void AutoDetectGridSizeFromFunction(ReadOnlySpan<char> rhs, ref int rows, ref int columns)
        {
            var parenStart = rhs.IndexOf('(');
            var parenEnd = rhs.LastIndexOf(')');
            if (parenStart < 0 || parenEnd <= parenStart)
                return;

            var args = rhs[(parenStart + 1)..parenEnd].Trim();
            if (UiSyntax.StartsWithFunction(rhs, "vector"))
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

        private void ResolveDatagridShape(UiPropertyMetadata ui)
        {
            if (ui.Rows > 0 && ui.Columns > 0)
            {
                CheckGridSize(ui.Rows, ui.Columns);
                return;
            }

            var (rows, columns) = _parser.GetVariableShape(ui.VariableName);
            if (ui.Rows == 0)
                ui.Rows = rows;

            if (ui.Columns == 0)
                ui.Columns = columns;

            CheckGridSize(ui.Rows, ui.Columns);
        }

        private void ResolveShapeFromSizingCall(UiPropertyMetadata ui, string expression)
        {
            if (ui.Rows > 0 && ui.Columns > 0 || !_calculate || _isVal < 0)
                return;

            var eqIndex = IndexOfAssignment(expression);
            if (eqIndex < 0)
                return;

            var rhs = expression.AsSpan(eqIndex + 1).Trim();
            var isVector = UiSyntax.StartsWithFunction(rhs, "vector");
            if (!isVector && !UiSyntax.StartsWithFunction(rhs, "matrix"))
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

                    CheckGridSize(ui.Rows, ui.Columns);
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

                CheckGridSize(ui.Rows, ui.Columns);
            }
        }

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
            else
                CaptureSourceValue(ui, expression);

            return expression;
        }

        private static string ResizeDatagridMatrixToFit(UiPropertyMetadata ui, string expression) =>
            ui.HasDeclaredShape ? ReshapeMatrixLiteral(expression, ui.Rows, ui.Columns) : expression;

        private static string ReshapeMatrixLiteral(string s, int rows, int columns)
        {
            var bracketStart = s.IndexOf('[');
            var bracketEnd = s.LastIndexOf(']');
            if (bracketStart < 0 || bracketEnd <= bracketStart)
                return s;

            var cells = SplitMatrixLiteral(s[(bracketStart + 1)..bracketEnd]);
            var pad = "0" + UnitOfFirstCell(cells);
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
                    sb.Append(value.Length == 0 ? pad : value);
                }
            }
            return string.Concat(s.AsSpan(0, bracketStart + 1), sb.ToString(), s.AsSpan(bracketEnd));
        }

        /// <summary>Unit suffix of the first cell that has one, so padding does not mix a bare zero in.</summary>
        private static string UnitOfFirstCell(List<string[]> cells)
        {
            foreach (var row in cells)
                foreach (var cell in row)
                {
                    var i = 0;
                    while (i < cell.Length && (char.IsAsciiDigit(cell[i]) || cell[i] is '.' or '-' or '+'))
                        ++i;

                    if (i > 0 && i < cell.Length)
                        return cell[i..];
                }
            return string.Empty;
        }

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

        private static void CaptureDatagridValues(UiPropertyMetadata ui, string expression)
        {
            var eqIndex = IndexOfAssignment(expression);
            if (eqIndex < 0)
                return;

            var rhs = expression[(eqIndex + 1)..].Trim();
            var bracketStart = rhs.IndexOf('[');
            var bracketEnd = rhs.LastIndexOf(']');
            if (bracketStart >= 0 && bracketEnd > bracketStart)
                ui.DataValues = rhs[(bracketStart + 1)..bracketEnd].Replace(" ", "");
        }

        private static void CaptureSourceValue(UiPropertyMetadata ui, string expression)
        {
            var eqIndex = IndexOfAssignment(expression);
            if (eqIndex >= 0)
                ui.SourceValue = expression[(eqIndex + 1)..].Trim();
        }

        /// <summary>
        /// Fills a grid that has no literal to read from with the value the line produced, keeping
        /// each cell's unit aside when the grid edits numbers only. Falls back to zeros.
        /// </summary>
        private void ResolveDatagridValues(UiPropertyMetadata ui)
        {
            if (ui.DataValues is not null)
                return;

            var grid = _calculate ? _parser.GetUiGridValues(ui.VariableName, ui.Rows, ui.Columns) : null;
            if (grid is not ({ } values, { } units))
            {
                var zeros = string.Join(";", Enumerable.Repeat("0", ui.Columns));
                ui.DataValues = string.Join("|", Enumerable.Repeat(zeros, ui.Rows));
                return;
            }

            if (ui.KeepsUnits)
                ui.CellUnits = units;
            else
                for (var r = 0; r < values.Length; ++r)
                    for (var c = 0; c < values[r].Length; ++c)
                        values[r][c] += units[r][c];

            ui.DataValues = string.Join("|", values.Select(row => string.Join(";", row)));
        }

        private bool TryGetUiOverride(UiPropertyMetadata ui, out string value)
        {
            value = null;
            return UiOverrides is not null &&
                (UiOverrides.TryGetValue(ui.Key, out value) ||
                UiOverrides.TryGetValue(ui.DeclarationKey, out value) ||
                UiOverrides.TryGetValue(ui.VariableName, out value));
        }

        private static string ApplyUiOverride(UiPropertyMetadata ui, string expression, string value)
        {
            var eqIndex = IndexOfAssignment(expression);
            if (eqIndex < 0)
                return null;

            var lhs = expression[..(eqIndex + 1)];
            var rhs = expression[(eqIndex + 1)..].TrimStart();
            if (ui.Type == "datagrid")
            {
                var bracketEnd = rhs.LastIndexOf(']');
                return bracketEnd < 0 || rhs.IndexOf('[') < 0 ?
                    $"{lhs} {value}" :
                    $"{lhs} {value}{rhs[(bracketEnd + 1)..]}";
            }

            // Every other mode replaces the whole right hand side; only the default one
            // rewrites the number in place and leaves the unit where it was written.
            if (ui.Type is "dropdown" or "radio" || !ui.KeepsUnits)
                return $"{lhs} {value}";

            var i = 0;
            while (i < rhs.Length && IsNumericChar(rhs[i]))
                ++i;

            return i == 0 ? null : $"{lhs} {value}{rhs[i..]}";
        }

        private static bool IsNumericChar(char c) =>
            c is >= '0' and <= '9' or '.' or '-' or '+' or 'e' or 'E';

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

        private string InjectUiInput(UiPropertyMetadata ui, string equationHtml) =>
            ui.Type switch
            {
                "dropdown" => ReplaceEquation(equationHtml, (v, u) => BuildUiDropdown(ui, SelectedValue(v, u))),
                "radio" => ReplaceEquation(equationHtml, (v, u) => BuildUiRadio(ui, SelectedValue(v, u))),
                "checkbox" => ReplaceEquation(equationHtml, (v, _) => BuildUiCheckbox(ui, v)),
                // Without the unit pinned to the document it belongs in the box, so the
                // equation's unit markup goes rather than being re-appended after it.
                _ when ui.AllowsExpression => ReplaceEquation(equationHtml, (_, _) => BuildUiEntry(ui, ui.SourceValue)),
                _ when !ui.KeepsUnits => ReplaceEquation(equationHtml, (v, _) => BuildUiEntry(ui, StripSpaces(v) + _parser.ResultUnitsInputText)),
                _ => InjectUiControl(equationHtml, (v, _) => BuildUiEntry(ui, v))
            };

        /// <summary>
        /// The control as it stands when the line did not parse: there is no rendered equation to
        /// take the value from, so it comes from the right hand side as written.
        /// </summary>
        private string BuildUiControlFromSource(UiPropertyMetadata ui)
        {
            var value = ui.SourceValue ?? string.Empty;
            return ui.Type switch
            {
                "dropdown" => BuildUiDropdown(ui, StripSpaces(value)),
                "radio" => BuildUiRadio(ui, StripSpaces(value)),
                "checkbox" => BuildUiCheckbox(ui, value),
                _ => BuildUiEntry(ui, value)
            };
        }

        private static string InjectUiControl(string equationHtml, Func<string, string, string> build)
        {
            var resultStart = ResultStart(equationHtml);
            if (resultStart < 0)
                return equationHtml;

            SplitValueAndUnit(equationHtml[resultStart..], out var value, out var unitHtml);
            return equationHtml[..resultStart] + build(value, unitHtml) + unitHtml;
        }

        private static string ReplaceEquation(string equationHtml, Func<string, string, string> build)
        {
            var resultStart = ResultStart(equationHtml);
            if (resultStart < 0)
                return equationHtml;

            SplitValueAndUnit(equationHtml[resultStart..], out var value, out var unitHtml);
            return build(value, unitHtml);
        }

        private static int ResultStart(string equationHtml)
        {
            const string assignOp = " = ";
            var lastAssign = equationHtml.LastIndexOf(assignOp, StringComparison.Ordinal);
            return lastAssign < 0 ? -1 : lastAssign + assignOp.Length;
        }

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

        private string UiBinding(UiPropertyMetadata ui) =>
            $" data-ui-var=\"{HttpUtility.HtmlAttributeEncode(ui.Key)}\" data-ui-line=\"{_parser.Line}\"";

        private string UiInputBinding(UiPropertyMetadata ui) => UiBinding(ui) + UiModeAttributes(ui);

        private string BuildUiEntry(UiPropertyMetadata ui, string value) =>
            $"<input type=\"text\" class=\"{UiClass(ui, "calcpad-ui-input")}\" value=\"{HttpUtility.HtmlAttributeEncode(value)}\"{UiInputBinding(ui)}>";

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

        private string BuildUiDatagrid(UiPropertyMetadata ui)
        {
            var sb = new StringBuilder()
                .Append($"<div class=\"{UiClass(ui, "calcpad-ui-datagrid")}\"{UiBinding(ui)}")
                .Append($" data-ui-rows=\"{ui.Rows}\" data-ui-columns=\"{ui.Columns}\"")
                .Append($" data-ui-values=\"{HttpUtility.HtmlAttributeEncode(ui.DataValues ?? string.Empty)}\"")
                .Append(UiModeAttributes(ui));
            AppendJsonAttribute(sb, "data-ui-col-headers", ui.ColumnHeaders);
            AppendJsonAttribute(sb, "data-ui-row-headers", ui.RowHeaders);
            AppendJsonAttribute(sb, "data-ui-column-widths", ui.ColumnWidths);
            if (ui.CellUnits is not null && HasAnyUnit(ui.CellUnits))
                AppendJsonAttribute(sb, "data-ui-cell-units", ui.CellUnits);

            if (ui.Width is { } width)
                sb.Append($" data-ui-width=\"{width}\"");

            if (ui.RowHeaderWidth is { } rowHeaderWidth)
                sb.Append($" data-ui-row-header-width=\"{rowHeaderWidth}\"");

            return sb.Append("></div>").ToString();
        }

        /// <summary>Only written when they differ from the default, so existing output is unchanged.</summary>
        private static string UiModeAttributes(UiPropertyMetadata ui) =>
            ui.AllowsExpression ? " data-ui-allow-expression=\"1\"" :
            ui.KeepsUnits ? string.Empty :
            " data-ui-force-units=\"0\"";

        private static void AppendJsonAttribute<T>(StringBuilder sb, string name, T value)
        {
            if (value is not null)
                sb.Append($" {name}=\"{HttpUtility.HtmlAttributeEncode(JsonSerializer.Serialize(value))}\"");
        }

        private static bool HasAnyUnit(string[][] units) =>
            units.Any(row => row.Any(u => !string.IsNullOrEmpty(u)));

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
            _pendingUiError = null;
            _uiTakeIndex = 0;
            _uiSkipChars = 0;
            _uiLineOccurrences.Clear();
        }
    }
}
