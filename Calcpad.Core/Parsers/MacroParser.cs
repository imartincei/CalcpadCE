using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;

namespace Calcpad.Core
{
    public class MacroParser
    {
        private readonly struct Macro
        {
            private readonly string _contents;
            private readonly string[] _parameters;
            // Null entry = required
            private readonly string[] _defaults;
            // Longest first, so a parameter never matches the start of a longer one
            private readonly int[] _order;
            private readonly char[] _starts;

            internal Macro(string contents, List<string> parameters, List<string> defaults)
            {
                _contents = contents;
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
                    _starts = [.. _order.Select(i => p[i][0]).Distinct()];
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

            internal string[] ResolveArguments(List<string> positional, Dictionary<string, string> keywords)
            {
                if (positional.Count > ParameterCount)
                    throw new MathParserException(Messages.Invalid_number_of_arguments, "CPD-3304");

                var result = new string[ParameterCount];
                positional.CopyTo(result);

                foreach (var kv in keywords)
                {
                    var j = GetParameterIndex(kv.Key);
                    if (j < 0)
                        throw new MathParserException(string.Format(Messages.Unknown_keyword_argument_0, kv.Key), "CPD-3314");
                    if (result[j] is not null)
                        throw new MathParserException(string.Format(Messages.Duplicate_argument_0, kv.Key), "CPD-3316");

                    result[j] = kv.Value;
                }
                MathParser.FillDefaults(result, _defaults);
                return result;
            }

            // One pass, so inserted arguments are never substituted again
            internal string Run(string[] arguments)
            {
                if (ParameterCount == 0)
                    return _contents;

                var body = _contents.AsSpan();
                var sb = new StringBuilder(body.Length + 64);
                for (int i = 0, len = body.Length; i < len;)
                {
                    var next = body[i..].IndexOfAny(_starts);
                    if (next < 0)
                    {
                        sb.Append(body[i..]);
                        break;
                    }
                    next += i;
                    sb.Append(body[i..next]);
                    var j = MatchParameter(body[next..]);
                    if (j < 0)
                    {
                        sb.Append(body[next]);
                        i = next + 1;
                        continue;
                    }
                    var s = arguments[j];
                    sb.Append(s.Length > 1 && s[0] == ' ' ? s.AsSpan(1) : s);
                    i = next + _parameters[j].Length;
                }
                return sb.ToString();
            }

            private int MatchParameter(ReadOnlySpan<char> s)
            {
                foreach (var j in _order)
                    if (s.StartsWith(_parameters[j], StringComparison.Ordinal))
                        return j;

                return -1;
            }
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
        private bool _traceAborted;

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
            }
            var macroBuilder = new StringBuilder(1000);
            var macroName = string.Empty;
            var lineNumber = includeLine;
            var macroDefCount = 0;
            var defLine = -1;
            var hasErrors = false;
            ReadOnlySpan<char> lineContent = "code";
            List<string> macroParameters = null;
            var traceFile = includeLine == 0 ? null : SourceFilePath;
            var fileLine = -1;
            var traceSource = -1;
            var defTraceSource = -1;
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
                    ++fileLine;
                    if (Trace is not null)
                    {
                        traceText = sourceLine.ToString();
                        traceSource = IsIncludeLine(lineContent)
                            ? -1
                            : Trace.AddSource(traceText, lineNumber, traceFile, fileLine);
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
                        continue;
                    }

                    if (_macros.Count != 0)
                    {
                        try
                        {
                            var expanded = Trace is null ? null : new List<string>();
                            var insertCode = ApplyMacros(sourceLine, null, expanded);
                            if (expanded is { Count: > 0 })
                                AppendExpansion(insertCode, expanded);
                            else
                            {
                                var insertLines = insertCode.EnumerateLines();
                                foreach (var line in insertLines)
                                    AppendLine(line);
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace?.OutputLines.Add(new() { Text = traceText, SourceIndex = traceSource });
                            AppendError(lineContent, ex.Message, (ex as MathParserException)?.Code ?? "CPD-2200");
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
                    Trace?.Errors.Add(new(defTraceSource, message));
                    _errorList.Add(new CalcpadError
                    {
                        SourceLine = defLine,
                        OutputLine = _parsedLineNumber + 1,
                        Message = message,
                        Source = CalcpadErrorSource.Macro,
                        Code = "CPD-2206",
                    });
                }
            }
            catch (Exception ex)
            {
                AppendError(lineContent, ex.Message, (ex as MathParserException)?.Code ?? "CPD-2200");
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
                    AppendError(lineContent, error, "CPD-1106");
                else
                    AppendLine(sourceLine);
            }

            void ParseInclude(ReadOnlySpan<char> lineContent)
            {
                if (!TryGetIncludePath(lineContent, out var start, out var length))
                {
                    AppendError(lineContent, Messages.Missing_source_file_for_include, "CPD-1102");
                    return;
                }
                var rawFileName = lineContent.Slice(start, length).ToString();
                var nf1 = lineContent.LastIndexOf('#');

                Queue<string> fields = new();
                if (nf1 > 0)
                {
                    var nf2 = lineContent.LastIndexOf('}');
                    if (nf2 < 0)
                        AppendError(lineContent, Messages.Brackets_not_closed, "CPD-1101");
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
                    AppendError(lineContent, tokenError, "CPD-1106");
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
                    AppendError(lineContent, string.Format(Messages.Circular_include_detected_0, rawFileName), "CPD-1105");
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
                    AppendError(lineContent, Messages.File_not_found, "CPD-1104");
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

                traceSource = Trace.AddSource(traceText, lineNumber, traceFile, fileLine);
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
                    AppendError(lineContent, string.Format(Messages.Duplicate_macro_name_0, macroName), "CPD-2201");

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
                                SymbolError(lineContent, c, textSpan.IsEmpty && char.IsDigit(c) ? "CPD-2208" : "CPD-2211");

                            if (++i == len)
                            {
                                c = '\0';
                                break;
                            }
                            continue;
                        }
                        var parameterName = textSpan.ToString();
                        if (defaultValue is not null)
                            seenOptional = true;
                        else if (seenOptional)
                            AppendError(lineContent, string.Format(Messages.Required_parameter_after_optional_0, parameterName), "CPD-2213");

                        if (macroParameters.Contains(parameterName))
                            AppendError(lineContent, string.Format(Messages.Duplicate_macro_parameter_names_0_and_1, parameterName, parameterName), "CPD-2212");
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
                    AddMacro(macroName, lineContent[i..].ToString(), macroParameters, macroDefaults, true);
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
                if (c is not ('\0' or ' ' or '(' or '='))
                    SymbolError(lineContent, c, name.IsEmpty && char.IsDigit(c) ? "CPD-2204" : "CPD-2210");
                else
                    AppendError(lineContent, string.Format(Messages.Invalid_macro_name_0, name.ToString()), name.IsEmpty ? "CPD-2205" : "CPD-2202");
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
                    AddMacro(macroName, macroBuilder.ToString(), macroParameters, macroDefaults, false);

                macroName = string.Empty;
                macroDefaults = null;
                macroBuilder.Clear();
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

            void AppendExpansion(string insertCode, List<string> expanded)
            {
                var count = 0;
                foreach (var _ in insertCode.EnumerateLines())
                    ++count;

                var index = 0;
                foreach (var line in insertCode.EnumerateLines())
                {
                    AppendLine(line, false);
                    Trace.OutputLines.Add(new()
                    {
                        Text = line.ToString(),
                        SourceIndex = traceSource,
                        ExpandedMacros = expanded,
                        ContentIndex = index++,
                        ContentCount = count
                    });
                }
            }

            void SymbolError(ReadOnlySpan<char> lineContent, char c, string code)
            {
                AppendError(lineContent, string.Format(Messages.Invalid_symbol_0_in_macro_name, c), code);
            }

            void AppendError(ReadOnlySpan<char> lineContent, string errorMessage, string code)
            {
                var marker = addLineNumbers ? $"\v{lineNumber}" : string.Empty;
                sb.AppendLine(string.Format(Messages.Error_in_0_on_line_1_2, HttpUtility.HtmlEncode(lineContent.ToString()), LineHtml(lineNumber), errorMessage) + marker);
                ++_parsedLineNumber;
                hasErrors = true;
                Trace?.Errors.Add(new(traceSource, errorMessage));
                _errorList.Add(new CalcpadError
                {
                    SourceLine = lineNumber,
                    OutputLine = _parsedLineNumber,
                    Message = errorMessage,
                    Source = CalcpadErrorSource.Macro,
                    Code = code,
                });
            }

            void AddMacro(string name, string contents, List<string> parameters, List<string> defaults, bool isInline)
            {
                var isAdded = _macros.TryAdd(name, new Macro(contents, parameters, defaults));
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
        /// Drops #local sections and the file metadata footer from included content. The footer's
        /// 'uiOverrides' and 'fileInfo' belong to the file itself; other metadata comments are kept.
        /// </summary>
        /// <param name="keepLineCount">Blank filtered lines instead of removing them.</param>
        public static string FilterIncludedContent(string content, bool keepLineCount = false)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            var isLocal = false;
            var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            var footerStart = FindMetadataFooterStart(lines);
            var outputLines = new List<string>(lines.Length);
            for (var i = 0; i < lines.Length; ++i)
            {
                var line = lines[i];
                bool keep;
                if (i >= footerStart)
                    keep = false;
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
                else
                    keep = !isLocal;

                if (keep)
                    outputLines.Add(line);
                else if (keepLineCount)
                    outputLines.Add(string.Empty);
            }
            return string.Join(Environment.NewLine, outputLines);
        }

        // Mirrors the editors' findMetadataFooter: anchor, then 'uiOverrides', then 'fileInfo' last.
        private static int FindMetadataFooterStart(string[] lines)
        {
            var start = lines.Length;
            var end = LastContentLine(lines, lines.Length);
            var block = MetadataCommentStart(lines, end);
            if (block >= 0 && BlockContains(lines, block, end, "\"fileInfo\""))
            {
                start = block;
                end = LastContentLine(lines, block);
                block = MetadataCommentStart(lines, end);
            }
            if (block >= 0 && BlockContains(lines, block, end, "\"uiOverrides\""))
            {
                start = block;
                end = LastContentLine(lines, block);
            }
            if (end >= 0 && lines[end].Trim() is "'<!--CalcpadCE Metadata-->" or "\"<!--CalcpadCE Metadata-->")
                start = end;

            return start;
        }

        private static int LastContentLine(string[] lines, int before)
        {
            for (var i = before - 1; i >= 0; --i)
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    return i;

            return -1;
        }

        // Opening line of the metadata comment that ends on 'end', following '_' continuations.
        private static int MetadataCommentStart(string[] lines, int end)
        {
            if (end < 0 || !lines[end].Contains("-->", StringComparison.Ordinal))
                return -1;

            var i = end;
            while (!IsMetadataOpener(lines[i]) && i > 0 && lines[i - 1].TrimEnd().EndsWith('_'))
                --i;

            return IsMetadataOpener(lines[i]) ? i : -1;
        }

        private static bool IsMetadataOpener(string line)
        {
            var trimmed = line.TrimStart();
            return (trimmed.StartsWith('\'') || trimmed.StartsWith('"'))
                && trimmed.Contains("<!--{", StringComparison.Ordinal);
        }

        private static bool BlockContains(string[] lines, int start, int end, string key)
        {
            for (var i = start; i <= end; ++i)
                if (lines[i].Contains(key, StringComparison.Ordinal))
                    return true;

            return false;
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

        private string ApplyMacros(ReadOnlySpan<char> lineContent, HashSet<string> currentlyExpanding = null, List<string> expanded = null)
        {
            var index = lineContent.IndexOf("$");
            if (index < 0)
                return lineContent.ToString();

            index = lineContent.IndexOf(FieldsMarker);
            var stringBuilder = new StringBuilder(200);
            var macroArguments = new List<string>();
            var keywordArguments = new Dictionary<string, string>(StringComparer.Ordinal);
            var bracketCount = 0;
            var insideArgList = false;
            var seenKeyword = false;
            var emptyMacro = new Macro(null, null, null);
            var macro = emptyMacro;
            string macroKey = null;
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
                            if (!keywordArguments.TryAdd(name.ToString(), ApplyMacros(value, currentlyExpanding, expanded)))
                                throw new MathParserException(string.Format(Messages.Duplicate_argument_0, name.ToString()), "CPD-3316");
                        }
                        else
                        {
                            if (seenKeyword)
                                throw new MathParserException(Messages.Invalid_number_of_arguments, "CPD-3304");

                            var s = ApplyMacros(textSpan.Cut(), currentlyExpanding, expanded);
                            macroArguments.Add(string.IsNullOrWhiteSpace(s) ? string.Empty : s);
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
                        throw Exceptions.UndefinedMacro(macroName);
                    else if (j > 0)
                        stringBuilder.Append(macroName[..j]);

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
                        var s = ExpandMacro(macro, macroArguments, keywordArguments, macroKey, ref currentlyExpanding, expanded);
                        var sbLength = stringBuilder.Length;
                        SetLineInputFields(s, stringBuilder, fields, false);
                        if (stringBuilder.Length == sbLength)
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
                            stringBuilder.Append(textSpan.Cut());
                            textSpan.Reset(i);
                        }
                        stringBuilder.Append(c);
                    }
                }
            }
            if (macro.IsEmpty)
            {
                if (!textSpan.IsEmpty)
                    stringBuilder.Append(textSpan.Cut());
            }
            else if (!insideArgList)
            {
                var s = ExpandMacro(macro, macroArguments, keywordArguments, macroKey, ref currentlyExpanding, expanded);
                stringBuilder.Append(s);
            }
            return stringBuilder.ToString();
        }

        private string ExpandMacro(Macro macro, List<string> macroArguments, Dictionary<string, string> keywordArguments, string macroKey, ref HashSet<string> currentlyExpanding, List<string> expanded)
        {
            if (currentlyExpanding != null && currentlyExpanding.Contains(macroKey))
                throw Exceptions.CircularMacroReference(macroKey);
            currentlyExpanding ??= new HashSet<string>(StringComparer.Ordinal);
            currentlyExpanding.Add(macroKey);
            var arguments = macro.ResolveArguments(macroArguments, keywordArguments);
            expanded?.Add(macroKey);
            try { return ApplyMacros(macro.Run(arguments), currentlyExpanding, expanded); }
            finally { currentlyExpanding.Remove(macroKey); }
        }

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


        private static bool IsInlineDef(ReadOnlySpan<char> s) => s[(s.IndexOf(')') + 1)..].Contains('=');

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
