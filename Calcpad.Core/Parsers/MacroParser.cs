using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;

namespace Calcpad.Core
{
    public class MacroParser
    {
        /// <summary>Offset in the text read of each character, or -1 for text not read from the source.</summary>
        private readonly struct Origins(int[] columns, int offset)
        {
            internal static Origins None => new(null, -1);

            internal int this[int i] =>
                columns is not null ? columns[offset + i] : offset < 0 ? -1 : offset + i;

            internal Origins Slice(int start) =>
                columns is null && offset < 0 ? this : new(columns, offset + start);

            internal void CopyTo(Span<int> destination)
            {
                if (columns is not null)
                    columns.AsSpan(offset, destination.Length).CopyTo(destination);
                else if (offset < 0)
                    destination.Fill(-1);
                else
                    for (var i = 0; i < destination.Length; ++i)
                        destination[i] = offset + i;
            }

            internal int[] ToArray(int start, int length)
            {
                var result = new int[length];
                Slice(start).CopyTo(result);
                return result;
            }
        }

        private readonly record struct Mapped(string Text, Origins Origins);

        private sealed class MacroException(string code, string message) : Exception(message)
        {
            internal string Code { get; } = code;
            internal int Start { get; set; } = -1;
            internal int End { get; set; } = -1;
        }

        // Tracks origins only in Debug mode
        private sealed class MappedBuilder(int capacity, bool track)
        {
            private readonly StringBuilder _text = new(capacity);
            private int[] _origins = track ? new int[capacity] : null;

            private Span<int> Grow(int count)
            {
                var length = _text.Length;
                if (length + count > _origins.Length)
                    Array.Resize(ref _origins, Math.Max(length + count, 2 * _origins.Length));

                return _origins.AsSpan(length, count);
            }

            internal void Append(char c, int origin)
            {
                if (_origins is not null)
                    Grow(1)[0] = origin;

                _text.Append(c);
            }

            internal void Append(ReadOnlySpan<char> s, Origins origins)
            {
                if (_origins is not null)
                    origins.CopyTo(Grow(s.Length));

                _text.Append(s);
            }

            internal void Append(Mapped m) => Append(m.Text, m.Origins);

            internal Mapped ToMapped() => new(_text.ToString(), _origins is null ? Origins.None : new(_origins, 0));
        }

        private readonly struct Macro
        {
            private readonly string _contents;
            private readonly Origins _origins;
            private readonly string[] _parameters;
            // Null entry = required
            private readonly string[] _defaults;
            // Longest first, so a parameter never matches the start of a longer one
            private readonly int[] _order;
            private readonly SearchValues<char> _starts;

            internal Macro(string contents, Origins origins, List<string> parameters, List<string> defaults)
            {
                _contents = contents;
                _origins = origins;
                if (parameters is null)
                {
                    _parameters = null;
                    _defaults = null;
                    _order = null;
                    _starts = null;
                }
                else
                {
                    string[] p = [.. parameters];
                    _parameters = p;
                    _defaults = [.. defaults];
                    _order = [.. Enumerable.Range(0, p.Length)
                        .Where(i => p[i].Length > 0)
                        .OrderByDescending(i => p[i].Length)];
                    _starts = SearchValues.Create([.. _order.Select(i => p[i][0]).Distinct()]);
                    HasDefaults = Array.Exists(_defaults, d => d is not null);
                }
            }

            internal int GetParameterIndex(ReadOnlySpan<char> name)
            {
                if (_parameters is null)
                    return -1;

                for (int i = 0, len = _parameters.Length; i < len; ++i)
                    if (name.SequenceEqual(_parameters[i]))
                        return i;

                return -1;
            }

            internal Mapped[] ResolveArguments(List<Mapped> positional, Dictionary<string, Mapped> keywords)
            {
                if (positional.Count > ParameterCount)
                    throw ArgumentCountError();

                var result = new Mapped[ParameterCount];
                positional.CopyTo(result);

                foreach (var kv in keywords)
                {
                    var j = GetParameterIndex(kv.Key);
                    if (j < 0)
                        throw new MacroException("CPD-3314", string.Format(Messages.Unknown_keyword_argument_0, kv.Key));
                    if (result[j].Text is not null)
                        throw DuplicateArgumentError(kv.Key);

                    result[j] = kv.Value;
                }
                for (var i = 0; i < result.Length; ++i)
                    if (result[i].Text is null)
                        result[i] = new(_defaults[i] ?? throw ArgumentCountError(), Origins.None);

                return result;
            }

            // One pass, so inserted arguments are never substituted again.
            internal Mapped Run(Mapped[] arguments, bool track)
            {
                if (ParameterCount == 0)
                    return new(_contents, _origins);

                var body = _contents.AsSpan();
                var sb = new MappedBuilder(body.Length + 64, track);
                for (int i = 0, len = body.Length; i < len;)
                {
                    var next = body[i..].IndexOfAny(_starts);
                    next = next < 0 ? len : i + next;
                    var j = next < len ? MatchParameter(body[next..]) : -1;
                    if (j < 0 && next < len)
                        ++next;

                    sb.Append(body[i..next], _origins.Slice(i));
                    i = next;
                    if (j < 0)
                        continue;

                    var s = arguments[j];
                    var skip = s.Text.Length > 1 && s.Text[0] == ' ' ? 1 : 0;
                    sb.Append(s.Text.AsSpan(skip), s.Origins.Slice(skip));
                    i += _parameters[j].Length;
                }
                return sb.ToMapped();
            }

            private int MatchParameter(ReadOnlySpan<char> s)
            {
                foreach (var j in _order)
                    if (s.StartsWith(_parameters[j], StringComparison.Ordinal))
                        return j;

                return -1;
            }
            internal static MacroException ArgumentCountError(int start = -1, int end = -1) =>
                new("CPD-3304", Messages.Invalid_number_of_arguments) { Start = start, End = end };

            internal static MacroException DuplicateArgumentError(string name, int start = -1, int end = -1) =>
                new("CPD-3316", string.Format(Messages.Duplicate_argument_0, name)) { Start = start, End = end };

            internal bool IsEmpty => _contents is null;
            internal int ParameterCount => _parameters?.Length ?? 0;
            internal bool HasDefaults { get; }
        }

        private enum Keywords
        {
            None,
            Def,
            EndDef,
            Include,
            ProjectPath,
            LibraryPath,
        }
        private const string DefKeyword = "#def";
        private const string EndDefKeyword = "#end def";
        private const string IncludeKeyword = "#include";
        private const string ProjectPathKeyword = "#projectpath";
        private const string LibraryPathKeyword = "#librarypath";
        private const string FieldsMarker = "#{";
        private readonly List<int> _lineNumbers = [];
        // Filesystem path comparison must match the host OS to avoid false-positive circular
        // detection on case-sensitive filesystems (Linux) or missed detection on Windows.
        private static readonly StringComparer PathComparer =
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        private readonly HashSet<string> _includeStack = new(PathComparer);
        private PathRoots _pathRoots = new();
        public PathRoots PathRoots => _pathRoots;
        private readonly Dictionary<string, Macro> _macros = new(StringComparer.Ordinal);
        public Func<string, Queue<string>, string> Include;
        public string SourceFilePath { get; set; }
        private readonly List<CalcpadError> _errorList = new();
        public IReadOnlyList<CalcpadError> Errors => _errorList;
        public MacroTrace Trace { get; set; }

        /// <summary>Records where each character of an expansion was read, for <see cref="MacroTraceOutputLine.SourceOffsets"/>.</summary>
        public bool Debug { get; set; }
        private bool _traceAborted;
        private int _sourceOffset;

        private static Keywords GetKeyword(ReadOnlySpan<char> s)
        {
            if (s.Length < DefKeyword.Length)
                return Keywords.None;
            if (s.StartsWith(DefKeyword, StringComparison.OrdinalIgnoreCase))
                return Keywords.Def;
            if (s.StartsWith(EndDefKeyword, StringComparison.OrdinalIgnoreCase))
                return Keywords.EndDef;
            if (s.StartsWith(IncludeKeyword, StringComparison.OrdinalIgnoreCase))
                return Keywords.Include;
            if (s.StartsWith(ProjectPathKeyword, StringComparison.OrdinalIgnoreCase))
                return Keywords.ProjectPath;
            if (s.StartsWith(LibraryPathKeyword, StringComparison.OrdinalIgnoreCase))
                return Keywords.LibraryPath;

            return Keywords.None;
        }

        public static bool TryGetIncludePath(ReadOnlySpan<char> line, out int start, out int length)
        {
            start = 0;
            length = 0;
            if (!line.StartsWith(IncludeKeyword, StringComparison.OrdinalIgnoreCase))
                return false;

            var pathStart = IncludeKeyword.Length;
            var n = line.IndexOfAny('\'', '"');
            var nf1 = line.LastIndexOf('#');
            if (n <= pathStart || nf1 > 0 && nf1 < n)
                n = nf1;

            if (n <= pathStart)
                n = line.Length;

            var name = line[pathStart..n];
            start = pathStart + name.Length - name.TrimStart().Length;
            length = name.Trim().Length;
            return length > 0;
        }

        private int _parsedLineNumber;
        public bool Parse(string sourceCode, out string outCode, StringBuilder sb, int includeLine, bool addLineNumbers)
        {
            var sourceLines = sourceCode.EnumerateLines();
            if (includeLine == 0)
            {
                sb = new StringBuilder(sourceCode.Length);
                _macros.Clear();
                _lineNumbers.Clear();
                _includeStack.Clear();
                _parsedLineNumber = 0;
                _errorList.Clear();
                _pathRoots = new PathRoots();
                _traceAborted = false;
                _sourceOffset = 0;
            }
            var macroBuilder = new StringBuilder(1000);
            var macroOrigins = new List<int>();
            int lineOffset = 0, contentOffset = 0;
            var macroName = string.Empty;
            var lineNumber = includeLine;
            var macroDefCount = 0;
            var hasErrors = false;
            ReadOnlySpan<char> lineContent = "code";
            List<string> macroParameters = null;
            var traceFile = includeLine == 0 ? null : SourceFilePath;
            var fileLine = -1;
            var traceSource = -1;
            var defTraceSource = -1;
            var defLine = -1;
            string traceText = null;
            List<string> macroDefaults = null;
            try
            {
                foreach (ReadOnlySpan<char> sourceLine in sourceLines)
                {
                    if (includeLine == 0)
                    {
                        _lineNumbers.Add(_parsedLineNumber);
                        ++lineNumber;
                    }

                    lineContent = sourceLine.TrimStart();
                    lineOffset = _sourceOffset;
                    contentOffset = lineOffset + sourceLine.Length - lineContent.Length;
                    _sourceOffset += sourceLine.Length + 1;
                    ++fileLine;
                    if (Trace is not null)
                    {
                        traceText = sourceLine.ToString();
                        traceSource = IsIncludeLine(lineContent)
                            ? -1
                            : Trace.AddSource(traceText, lineNumber, traceFile, fileLine, lineOffset);
                    }
                    if (lineContent.IsEmpty)
                    {
                        AppendLine(sourceLine);
                        continue;
                    }
                    if (lineContent[0] == '#' && ParseKeyword(lineContent, sourceLine))
                        continue;

                    if (macroDefCount > 0)
                    {
                        macroBuilder.Append(sourceLine).AppendLine();
                        if (Debug)
                        {
                            for (var k = 0; k < sourceLine.Length; ++k)
                                macroOrigins.Add(lineOffset + k);
                            while (macroOrigins.Count < macroBuilder.Length)
                                macroOrigins.Add(-1);
                        }
                        continue;
                    }

                    if (_macros.Count != 0)
                    {
                        try
                        {
                            var expanded = Trace is null ? null : new List<string>();
                            var insertCode = ApplyMacros(sourceLine, Debug ? new Origins(null, lineOffset) : Origins.None, null, expanded);
                            if (expanded is { Count: > 0 })
                                AppendExpansion(insertCode, expanded);
                            else
                            {
                                var insertLines = insertCode.Text.EnumerateLines();
                                foreach (var line in insertLines)
                                    AppendLine(line);
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace?.OutputLines.Add(new() { Text = traceText, SourceIndex = traceSource });
                            AppendException(lineContent, ex);
                        }
                        continue;
                    }
                    AppendLine(sourceLine);
                }
                if (includeLine == 0)
                    _lineNumbers.Add(_parsedLineNumber);

                if (macroDefCount > 0)
                {
                    var message = Messages.Macro_definition_block_not_closed_Missing_end_def;
                    sb.Append(message);
                    hasErrors = true;
                    Trace?.Errors.Add(new(defTraceSource, message, "CPD-2206"));
                    _errorList.Add(new CalcpadError
                    {
                        SourceLine = defLine,
                        OutputLine = _parsedLineNumber + 1,
                        Message = message,
                        Source = CalcpadErrorSource.Macro,
                        Code = "CPD-2206",
                        Kind = CalcpadErrorKind.Structure
                    });
                }
            }
            catch (Exception ex)
            {
                AppendException(lineContent, ex);
                _traceAborted = true;
            }
            finally
            {
                outCode = sb.ToString();
                if (includeLine == 0 && Trace is not null)
                    Trace.IsComplete = !_traceAborted;
            }
            return hasErrors;

            bool ParseKeyword(ReadOnlySpan<char> lineContent, ReadOnlySpan<char> sourceLine)
            {
                var keyword = GetKeyword(lineContent);
                switch (keyword)
                {
                    case Keywords.Include:
                        ParseInclude(lineContent);
                        return true;
                    case Keywords.Def:
                        ParseDef(lineContent);
                        return true;
                    case Keywords.EndDef:
                        ParseEndDef(lineContent);
                        return true;
                    case Keywords.ProjectPath:
                    case Keywords.LibraryPath:
                        ParsePathRoot(lineContent, sourceLine);
                        return true;
                    default:
                        return false;
                }
            }

            void ParsePathRoot(ReadOnlySpan<char> lineContent, ReadOnlySpan<char> sourceLine)
            {
                PathRoots.IsDeclaration(lineContent, out var isProject, out var start, out var length);
                if (length == 0)
                {
                    AppendError(lineContent, string.Format(Messages.Missing_path_value_0,
                        isProject ? "#ProjectPath" : "#LibraryPath"), "CPD-1106");
                    return;
                }

                var rawValue = lineContent.Slice(start, length).ToString();
                var declaringDir = !string.IsNullOrEmpty(SourceFilePath)
                    ? Path.GetDirectoryName(SourceFilePath) : null;
                if (!_pathRoots.TryDeclare(isProject, rawValue, declaringDir, out var error))
                    AppendError(lineContent, error, "CPD-1106", contentOffset + start, contentOffset + start + length - 1);
                else
                    AppendLine(sourceLine);
            }

            void ParseInclude(ReadOnlySpan<char> lineContent)
            {
                if (!TryGetIncludePath(lineContent, out var start, out var length))
                {
                    TraceFailedInclude(string.Empty);
                    AppendError(lineContent, Messages.Missing_source_file_for_include, "CPD-1102");
                    return;
                }
                var rawFileName = lineContent.Slice(start, length).ToString();
                int pathStart = contentOffset + start, pathEnd = pathStart + length - 1;
                var nf1 = lineContent.LastIndexOf('#');

                Queue<string> fields = new();
                if (nf1 > 0)
                {
                    var nf2 = lineContent.LastIndexOf('}');
                    if (nf2 < 0)
                        AppendError(lineContent, Messages.Brackets_not_closed, "CPD-1101", contentOffset + nf1, contentOffset + lineContent.Length - 1);
                    else
                    {
                        SplitEnumerator split = lineContent[(nf1 + FieldsMarker.Length)..nf2].EnumerateSplits(';');
                        foreach (var item in split)
                            fields.Enqueue(item.Trim().ToString());
                    }
                }

                var sourceDir = !string.IsNullOrEmpty(SourceFilePath)
                    ? Path.GetDirectoryName(SourceFilePath) : null;
                if (!_pathRoots.TryExpand(rawFileName, out var tokenExpanded, out var tokenError))
                {
                    TraceFailedInclude(rawFileName);
                    AppendError(lineContent, tokenError, "CPD-1106", pathStart, pathEnd);
                    return;
                }
                var expanded = Environment.ExpandEnvironmentVariables(tokenExpanded);
                var resolvedPath = sourceDir != null
                    ? Path.GetFullPath(expanded, sourceDir)
                    : Path.GetFullPath(expanded);
                var fileExists = File.Exists(resolvedPath);

                if (!_includeStack.Add(resolvedPath))
                {
                    TraceFailedInclude(tokenExpanded);
                    AppendError(lineContent, string.Format(Messages.Circular_include_detected_0, rawFileName), "CPD-1105", pathStart, pathEnd);
                    return;
                }
                try
                {
                    if (fileExists)
                    {
                        Trace?.Includes.Add(new(resolvedPath, tokenExpanded, lineNumber));
                        ParseWithSourcePath(Include(resolvedPath, fields), resolvedPath);
                        return;
                    }
                    TraceFailedInclude(tokenExpanded);
                    AppendError(lineContent, Messages.File_not_found, "CPD-1104", pathStart, pathEnd);
                }
                finally
                {
                    _includeStack.Remove(resolvedPath);
                }
            }

            void TraceFailedInclude(string fileName)
            {
                if (Trace is null)
                    return;

                if (traceSource < 0)
                    traceSource = Trace.AddSource(traceText, lineNumber, traceFile, fileLine, lineOffset);

                Trace.OutputLines.Add(new() { Text = traceText, SourceIndex = traceSource, FailedInclude = fileName });
            }

            void ParseWithSourcePath(string content, string newSourcePath)
            {
                var savedSourcePath = SourceFilePath;
                SourceFilePath = newSourcePath;
                try { Parse(content, out _, sb, lineNumber, addLineNumbers); }
                finally { SourceFilePath = savedSourcePath; }
            }

            void ParseDef(ReadOnlySpan<char> lineContent)
            {
                if (macroDefCount > 0)
                {
                    AppendError(lineContent, Messages.Invalid_inside_macro_definition, "CPD-2207");
                    if (!IsInlineDef(lineContent))
                        ++macroDefCount;

                    return;
                }
                var textSpan = new TextSpan(lineContent);
                int i = DefKeyword.Length, len = lineContent.Length;
                var c = EatSpace(lineContent, ref i);
                textSpan.Reset(i);
                while (i < len)
                {
                    c = lineContent[i];
                    if (c == '$')
                    {
                        textSpan.Expand();
                        macroName = textSpan.ToString();
                        break;
                    }
                    if (!Validator.IsMacroLetter(c, textSpan.Length))
                        break;

                    textSpan.Expand();
                    ++i;
                }
                if (macroName.Length == 0)
                {
                    InvalidMacroName(lineContent, textSpan, i);
                    if (!IsInlineDef(lineContent))
                        OpenDef();

                    return;
                }
                if (_macros.ContainsKey(macroName))
                    AppendError(lineContent, string.Format(Messages.Duplicate_macro_name_0, macroName), "CPD-2201",
                        contentOffset + textSpan.Start, contentOffset + i);

                c = EatSpace(lineContent, ref i);
                if (c == '(')
                {
                    macroParameters = [];
                    macroDefaults = [];
                    var seenOptional = false;
                    c = EatSpace(lineContent, ref i);
                    textSpan.Reset(i);
                    while (i < len)
                    {
                        c = lineContent[i];
                        if (c == ' ')
                        {
                            if ((c = EatSpace(lineContent, ref i)) == '\0')
                                break;

                            continue;
                        }
                        string defaultValue = null;
                        if (c == '$')
                        {
                            textSpan.Expand();
                            c = EatSpace(lineContent, ref i);
                            if (c == '=')
                            {
                                EatSpace(lineContent, ref i);
                                var end = MathParser.IndexOfTopLevel(lineContent[i..], ';');
                                if (end < 0)
                                    end = len - i;

                                defaultValue = lineContent.Slice(i, end).Trim().ToString();
                                i += end;
                                c = i < len ? lineContent[i] : '\0';
                            }
                        }
                        else if (c != ';' && c != ')')
                        {
                            if (Validator.IsMacroLetter(c, textSpan.Length))
                                textSpan.Expand();
                            else if (c != '\n')
                                AppendError(lineContent, string.Format(Messages.Invalid_symbol_0_in_macro_name, c),
                                    textSpan.IsEmpty && char.IsDigit(c) ? "CPD-2208" : "CPD-2211", contentOffset + i, contentOffset + i);

                            if (++i == len)
                            {
                                c = '\0';
                                break;
                            }
                            continue;
                        }
                        var parameterName = textSpan.ToString();
                        int paramStart = contentOffset + textSpan.Start, paramEnd = paramStart + parameterName.Length - 1;
                        if (defaultValue is not null)
                            seenOptional = true;
                        else if (seenOptional)
                            AppendError(lineContent, string.Format(Messages.Required_parameter_after_optional_0, parameterName), "CPD-2213", paramStart, paramEnd);

                        if (macroParameters.Contains(parameterName))
                            AppendError(lineContent, string.Format(Messages.Duplicate_macro_parameter_names_0_and_1, parameterName, parameterName), "CPD-2212", paramStart, paramEnd);
                        else
                        {
                            macroParameters.Add(parameterName);
                            macroDefaults.Add(defaultValue);
                        }
                        if (c == ')')
                            break;

                        if ((c = EatSpace(lineContent, ref i)) == '\0')
                            break;

                        textSpan.Reset(i);
                    }
                    if (c != ')')
                        AppendError(lineContent, Messages.Brackets_not_closed, "CPD-2205");

                    c = EatSpace(lineContent, ref i);
                }
                else
                    macroParameters = null;

                if (c == '=')
                {
                    c = EatSpace(lineContent, ref i);
                    AddMacro(macroName, lineContent[i..].ToString(), Debug ? new Origins(null, contentOffset + i) : Origins.None, macroParameters, macroDefaults, true);
                    macroName = string.Empty;
                    macroDefaults = null;
                }
                else
                    OpenDef();
            }

            void OpenDef()
            {
                defTraceSource = traceSource;
                defLine = lineNumber;
                ++macroDefCount;
            }

            void InvalidMacroName(ReadOnlySpan<char> lineContent, TextSpan name, int i)
            {
                var c = i < lineContent.Length ? lineContent[i] : '\0';
                int start = contentOffset + name.Start, end = contentOffset + i - 1;
                if (c is not ('\0' or ' ' or '(' or '='))
                    AppendError(lineContent, string.Format(Messages.Invalid_symbol_0_in_macro_name, c),
                        name.IsEmpty && char.IsDigit(c) ? "CPD-2204" : "CPD-2210", end + 1, end + 1);
                else if (name.IsEmpty)
                    AppendError(lineContent, string.Format(Messages.Invalid_macro_name_0, string.Empty), "CPD-2205");
                else
                    AppendError(lineContent, string.Format(Messages.Macro_name_0_must_end_with_dollar, name.ToString()), "CPD-2202", start, end);
            }

            void ParseEndDef(ReadOnlySpan<char> lineContent)
            {
                if (macroDefCount < 1)
                {
                    AppendError(lineContent, Messages.There_is_no_matching_def, "CPD-2206");
                    return;
                }
                if (--macroDefCount > 0)
                    return;

                macroBuilder.RemoveLastLineIfEmpty();
                if (macroName.Length > 0)
                {
                    var origins = Debug ? new Origins([.. macroOrigins[..macroBuilder.Length]], 0) : Origins.None;
                    AddMacro(macroName, macroBuilder.ToString(), origins, macroParameters, macroDefaults, false);
                }
                macroName = string.Empty;
                macroDefaults = null;
                macroBuilder.Clear();
                macroOrigins.Clear();
            }

            void AppendLine(ReadOnlySpan<char> line, bool trace = true)
            {
                if (addLineNumbers)
                    sb.Append(line).Append('\v').Append(lineNumber).AppendLine();
                else
                    sb.Append(line).AppendLine();

                ++_parsedLineNumber;
                if (trace && Trace is not null)
                    Trace.OutputLines.Add(new() { Text = line.ToString(), SourceIndex = traceSource });
            }

            void AppendExpansion(Mapped insertCode, List<string> expanded)
            {
                var text = insertCode.Text.AsSpan();
                var count = 0;
                foreach (var _ in text.EnumerateLines())
                    ++count;

                int index = 0, start = 0;
                foreach (var line in text.EnumerateLines())
                {
                    AppendLine(line, false);
                    Trace.OutputLines.Add(new()
                    {
                        Text = line.ToString(),
                        SourceIndex = traceSource,
                        ExpandedMacros = expanded,
                        SourceOffsets = Debug ? insertCode.Origins.ToArray(start, line.Length) : null,
                        ContentIndex = index++,
                        ContentCount = count
                    });
                    start += line.Length;
                    if (start < text.Length)
                        start += text[start] == '\r' && start + 1 < text.Length && text[start + 1] == '\n' ? 2 : 1;
                }
            }

            void AppendException(ReadOnlySpan<char> lineContent, Exception ex)
            {
                if (ex is MacroException m)
                    AppendError(lineContent, m.Message, m.Code, m.Start, m.End);
                else
                    AppendError(lineContent, ex.Message, "CPD-2200");
            }

            void AppendError(ReadOnlySpan<char> lineContent, string errorMessage, string code, int start = -1, int end = -1)
            {
                var marker = addLineNumbers ? $"\v{lineNumber}" : string.Empty;
                sb.AppendLine(string.Format(Messages.Error_in_0_on_line_1_2, HttpUtility.HtmlEncode(lineContent.ToString()), LineHtml(lineNumber), errorMessage) + marker);
                ++_parsedLineNumber;
                hasErrors = true;
                if (Trace is not null)
                {
                    if (traceSource < 0)
                        traceSource = Trace.AddSource(traceText, lineNumber, traceFile, fileLine, lineOffset);

                    Trace.Errors.Add(new(traceSource, errorMessage, code, start, end));
                }
                _errorList.Add(new CalcpadError
                {
                    SourceLine = lineNumber,
                    OutputLine = _parsedLineNumber,
                    Message = errorMessage,
                    Source = CalcpadErrorSource.Macro,
                    Code = code,
                    Kind = CalcpadErrorKind.Structure
                });
            }

            void AddMacro(string name, string contents, Origins origins, List<string> parameters, List<string> defaults, bool isInline)
            {
                var isAdded = _macros.TryAdd(name, new Macro(contents, origins, parameters, defaults));
                Trace?.Macros.Add(new()
                {
                    Name = name,
                    Parameters = parameters is null ? [] : [.. parameters],
                    Defaults = defaults is null ? [] : [.. defaults],
                    Content = isInline ? [contents] : SplitLines(contents),
                    SourceIndex = isInline ? traceSource : defTraceSource,
                    EndSourceIndex = traceSource,
                    IsInline = isInline,
                    IsDuplicate = !isAdded
                });
            }
            static string LineHtml(int line) => $"[<a href=\"#0\" data-text=\"{line}\">{line}</a>]";
        }

        /// <summary>
        /// Drops #local sections and saved host metadata comments from included content.
        /// 'uiOverrides' only applies to the file that carries it, as the host restores it
        /// from the open document before #include is expanded. 'fileInfo' and the metadata
        /// footer go too, so an included file's details don't show up in the host file.
        /// </summary>
        /// <param name="keepLineCount">Blank filtered lines instead of removing them.</param>
        public static string FilterIncludedContent(string content, bool keepLineCount = false)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            var isLocal = false;
            var isHostComment = false;
            var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            var outputLines = new List<string>(lines.Length);
            foreach (var line in lines)
            {
                bool keep;
                if (isHostComment)
                {
                    if (line.Contains("-->", StringComparison.Ordinal))
                        isHostComment = false;

                    keep = false;
                }
                else if (Validator.IsKeyword(line, "#local"))
                {
                    isLocal = true;
                    keep = false;
                }
                else if (Validator.IsKeyword(line, "#global"))
                {
                    isLocal = false;
                    keep = false;
                }
                else if (IsHostCommentStart(line))
                {
                    isHostComment = !line.Contains("-->", StringComparison.Ordinal);
                    keep = false;
                }
                else
                    keep = !isLocal;

                if (keep)
                    outputLines.Add(line);
                else if (keepLineCount)
                    outputLines.Add(string.Empty);
            }
            return string.Join(Environment.NewLine, outputLines);
        }

        // A cheap substring check rather than a JSON parse; the host always writes the keys verbatim.
        private static bool IsHostCommentStart(string line)
        {
            var trimmed = line.TrimStart();
            return (trimmed.StartsWith('\'') || trimmed.StartsWith('"'))
                && trimmed.Contains("<!--", StringComparison.Ordinal)
                && (trimmed.Contains("\"uiOverrides\"", StringComparison.Ordinal)
                    || trimmed.Contains("\"fileInfo\"", StringComparison.Ordinal)
                    || trimmed.Contains("<!--CalcpadCE Metadata-->", StringComparison.Ordinal));
        }

        private static bool IsIncludeLine(ReadOnlySpan<char> lineContent) =>
            !lineContent.IsEmpty && lineContent[0] == '#' && GetKeyword(lineContent) == Keywords.Include;

        private static List<string> SplitLines(string s)
        {
            var lines = new List<string>();
            foreach (var line in s.EnumerateLines())
                lines.Add(line.ToString());

            return lines;
        }

        public static Queue<string> GetFields(ReadOnlySpan<char> s, char delimiter)
        {
            var fields = new Queue<string>();
            var split = s.EnumerateSplits(delimiter);
            foreach (var item in split)
                fields.Enqueue(item.Trim().ToString());

            return fields;
        }

        private Mapped ApplyMacros(ReadOnlySpan<char> lineContent, Origins origins, HashSet<string> currentlyExpanding = null, List<string> expanded = null)
        {
            var index = lineContent.IndexOf("$");
            if (index < 0)
                return new(lineContent.ToString(), origins);

            index = lineContent.IndexOf(FieldsMarker);
            var stringBuilder = new MappedBuilder(200, Debug);
            var macroArguments = new List<Mapped>();
            var keywordArguments = new Dictionary<string, Mapped>(StringComparer.Ordinal);
            var bracketCount = 0;
            var insideArgList = false;
            var seenKeyword = false;
            var emptyMacro = new Macro(null, Origins.None, null, null);
            var macro = emptyMacro;
            string macroKey = null;
            var callStart = 0;
            Queue<string> fields = null;
            if (index >= 0)
            {
                var s = lineContent[(index + FieldsMarker.Length)..];
                lineContent = lineContent[..index];
                var n = s.IndexOf('}');
                if (n < 0)
                    n = s.Length;
                fields = GetFields(s[..n], ';');
            }
            var textSpan = new TextSpan(lineContent);
            for (int i = 0, len = lineContent.Length; i < len; ++i)
            {
                var c = lineContent[i];
                if (insideArgList)
                {
                    if (c == '(')
                    {
                        if (bracketCount == 0)
                            textSpan.Reset(i + 1);
                        ++bracketCount;
                    }
                    else if (c == ')')
                        --bracketCount;

                    if (c == ';' && bracketCount == 1 || c == ')' && bracketCount == 0)
                    {
                        var rawArgument = textSpan.Cut().Trim();
                        if (c == ')' && rawArgument.IsEmpty && macro.HasDefaults &&
                            macroArguments.Count == 0 && keywordArguments.Count == 0)
                        {
                            insideArgList = false;
                            textSpan.Reset(i + 1);
                            continue;
                        }
                        if (TryParseKeywordArgument(rawArgument, macro, out var name, out var value))
                        {
                            seenKeyword = true;
                            lineContent.Overlaps(value, out var valueStart);
                            if (!keywordArguments.TryAdd(name.ToString(), ApplyMacros(value, origins.Slice(valueStart), currentlyExpanding, expanded)))
                                throw Macro.DuplicateArgumentError(name.ToString(), origins[callStart], origins[i]);
                        }
                        else
                        {
                            if (seenKeyword)
                                throw Macro.ArgumentCountError(origins[callStart], origins[i]);

                            var s = ApplyMacros(textSpan.Cut(), origins.Slice(textSpan.Start), currentlyExpanding, expanded);
                            macroArguments.Add(string.IsNullOrWhiteSpace(s.Text) ? new(string.Empty, Origins.None) : s);
                        }
                        textSpan.Reset(i + 1);
                        if (c == ')')
                            insideArgList = false;
                    }
                    else if (bracketCount > 1 || c != '(')
                        textSpan.Expand();
                }
                else if (c == '$' && !textSpan.IsEmpty)
                {
                    textSpan.Expand();
                    var macroName = textSpan.ToString();
                    int j, mlen = macroName.Length - 1;
                    macroKey = null;
                    for (j = 0; j < mlen; ++j)
                    {
                        var candidate = macroName[j..];
                        if (_macros.TryGetValue(candidate, out macro))
                        {
                            macroKey = candidate;
                            break;
                        }
                    }

                    if (macro.IsEmpty)
                        throw new MacroException("CPD-3303", string.Format(Messages.Macro_not_defined_0, macroName))
                        {
                            Start = origins[textSpan.Start],
                            End = origins[i]
                        };
                    if (j > 0)
                        stringBuilder.Append(macroName.AsSpan(0, j), origins.Slice(textSpan.Start));

                    callStart = textSpan.Start + j;
                    bracketCount = 0;
                    macroArguments.Clear();
                    keywordArguments.Clear();
                    seenKeyword = false;
                    insideArgList = macro.ParameterCount > 0;
                    textSpan.Reset(i);
                }
                else
                {
                    if (!macro.IsEmpty)
                    {
                        var s = ExpandMacro(macro, macroArguments, keywordArguments, macroKey, ref currentlyExpanding, expanded, origins[callStart], origins[i - 1]);
                        var filled = fields is { Count: > 0 } ? new StringBuilder() : null;
                        SetLineInputFields(s.Text, filled, fields, false);
                        if (filled is { Length: > 0 })
                            stringBuilder.Append(filled.ToString(), Origins.None);
                        else
                            stringBuilder.Append(s);

                        textSpan.Reset(i);
                        macro = emptyMacro;
                    }
                    if (Validator.IsMacroLetter(c, textSpan.Length))
                    {
                        if (textSpan.IsEmpty)
                            textSpan.Reset(i);

                        textSpan.Expand();
                    }
                    else
                    {
                        if (!textSpan.IsEmpty)
                        {
                            stringBuilder.Append(textSpan.Cut(), origins.Slice(textSpan.Start));
                            textSpan.Reset(i);
                        }
                        stringBuilder.Append(c, origins[i]);
                    }
                }
            }
            if (macro.IsEmpty)
            {
                if (!textSpan.IsEmpty)
                    stringBuilder.Append(textSpan.Cut(), origins.Slice(textSpan.Start));
            }
            else if (!insideArgList)
            {
                var s = ExpandMacro(macro, macroArguments, keywordArguments, macroKey, ref currentlyExpanding, expanded, origins[callStart], origins[lineContent.Length - 1]);
                stringBuilder.Append(s);
            }
            return stringBuilder.ToMapped();
        }

        // start and end locate the call for errors that don't locate themselves.
        private Mapped ExpandMacro(Macro macro, List<Mapped> macroArguments, Dictionary<string, Mapped> keywordArguments, string macroKey, ref HashSet<string> currentlyExpanding, List<string> expanded, int start, int end)
        {
            try
            {
                if (currentlyExpanding != null && currentlyExpanding.Contains(macroKey))
                    throw new MacroException("CPD-3317", string.Format(Messages.Circular_reference_detected_for_macro_0, macroKey));
                currentlyExpanding ??= new HashSet<string>(StringComparer.Ordinal);
                currentlyExpanding.Add(macroKey);
                var arguments = macro.ResolveArguments(macroArguments, keywordArguments);
                expanded?.Add(macroKey);
                var body = macro.Run(arguments, Debug);
                try { return ApplyMacros(body.Text, body.Origins, currentlyExpanding, expanded); }
                finally { currentlyExpanding.Remove(macroKey); }
            }
            catch (MacroException ex) when (ex.Start < 0)
            {
                ex.Start = start;
                ex.End = end;
                throw;
            }
        }

        // For error recovery: whether a #def line has a body after its parameters.
        private static bool IsInlineDef(ReadOnlySpan<char> s) => s[(s.IndexOf(')') + 1)..].Contains('=');

        // Existing macro names stay positional so "a$ = 1" can still expand to an assignment
        private bool TryParseKeywordArgument(ReadOnlySpan<char> arg, in Macro macro, out ReadOnlySpan<char> parameterName, out ReadOnlySpan<char> value)
        {
            var i = 0;
            while (i < arg.Length && Validator.IsMacroLetter(arg[i], i))
                ++i;

            if (i > 0 && i < arg.Length && arg[i] == '$')
            {
                var rest = arg[(i + 1)..].TrimStart();
                if (rest.Length > 1 && rest[0] == '=' && rest[1] != '=')
                {
                    parameterName = arg[..(i + 1)];
                    if (macro.GetParameterIndex(parameterName) >= 0 ||
                        !_macros.ContainsKey(parameterName.ToString()))
                    {
                        value = rest[1..].Trim();
                        return true;
                    }
                }
            }
            parameterName = default;
            value = default;
            return false;
        }


        private static char EatSpace(ReadOnlySpan<char> s, ref int index)
        {
            var len = s.Length - 1;
            while (index < len)
            {
                ++index;
                if (s[index] != ' ')
                    return s[index];
            }
            return '\0';
        }

        public static int CountInputFields(ReadOnlySpan<char> s) =>
            CountOrHasInputFields(s, false);

        public static bool HasInputFields(ReadOnlySpan<char> s) =>
            CountOrHasInputFields(s, true) > 0;

        private static int CountOrHasInputFields(ReadOnlySpan<char> s, bool hasAny)
        {
            if (s.IsEmpty)
                return 0;

            var count = 0;
            var commentEnumerator = s.EnumerateComments();
            foreach (var item in commentEnumerator)
            {
                if (!item.IsEmpty && item[0] != '"' && item[0] != '\'')
                {
                    foreach (var c in item)
                    {
                        if (c == '?')
                        {
                            if (hasAny)
                                return 1;

                            ++count;
                        }
                    }
                }
            }
            return count;
        }

        public static bool SetLineInputFields(string s, StringBuilder sb, Queue<string> fields, bool forceLine)
        {
            if (string.IsNullOrEmpty(s) || fields is null || fields.Count == 0)
                return false;

            var inputChar = '\0';
            var count = fields.Count;
            var commentEnumerator = s.AsSpan().EnumerateComments();
            foreach (var item in commentEnumerator)
            {
                if (!item.IsEmpty)
                {
                    if (item[0] == '"' || item[0] == '\'')
                        sb.Append(item);
                    else
                    {
                        var j0 = 0;
                        var len = item.Length;
                        for (int j = 0; j < len; ++j)
                        {
                            var c = item[j];
                            if (c == '?')
                                inputChar = c;
                            else if (c == '{' && inputChar == '?')
                            {
                                inputChar = c;
                                sb.Append(item[j0..(j + 1)]);
                            }
                            else if (c == '}' && inputChar == '{')
                            {
                                inputChar = '\0';
                                if (!fields.TryDequeue(out string val))
                                    return false;

                                sb.Append(val);
                                j0 = j;
                            }
                            else if (inputChar == '{')
                                continue;
                            else if (c != ' ' && AddField(item[j0..j]))
                                j0 = j;
                        }
                        if (!AddField($"{item[j0..]} "))
                            sb.Append($"{item[j0..]}");
                    }
                }
            }
            if (forceLine && fields.Count != 0)
            {
                RemoveLineFields(sb);
                AddLineFields(sb, fields);
            }
            return fields.Count < count;

            bool AddField(ReadOnlySpan<char> s)
            {
                if (inputChar != '?') return false;
                inputChar = '\0';
                if (!fields.TryDequeue(out var val))
                    return false;

                sb.Append($"{s}{{{val}}}");
                return true;
            }
        }

        private static void RemoveLineFields(StringBuilder sb)
        {
            var len = sb.Length;
            var i = len;
            while (--i > 0)
                if (sb[i] == '{')
                    break;

            if (i > 1 && sb[i - 1] == '#')
            {
                while (--i > 0)
                    if (sb[i] != ' ')
                        break;
            }
            else
                i = -1;

            if (i > -1)
            {
                len -= i - 1;
                if (len > 0)
                    sb.Remove(i - 1, len);
            }
        }

        private static void AddLineFields(StringBuilder sb, Queue<string> fields)
        {
            sb.Append(HasUnclosedComment(sb) ? "' #{" : " #{");

            while (fields.TryDequeue(out string val))
            {
                sb.Append(val);
                sb.Append(';');
            }
            sb[^1] = '}';
        }

        private static bool HasUnclosedComment(StringBuilder sb)
        {
            var commentChar = '\0';
            var commentCount = 0;
            for (int i = 0, len = sb.Length; i < len; ++i)
            {
                var c = sb[i];
                if (commentChar == '\0')
                {
                    if (c == '\'' || c == '"')
                    {
                        commentChar = c;
                        ++commentCount;
                    }
                }
                else if (c == commentChar)
                    ++commentCount;
            }
            return commentCount % 2 == 1;
        }

        public int GetUnwarpedLineNumber(int sourceLineNumber)
        {
            if (sourceLineNumber < 1 || sourceLineNumber >= _lineNumbers.Count)
                return sourceLineNumber;

            return _lineNumbers[sourceLineNumber];
        }
    }
}
