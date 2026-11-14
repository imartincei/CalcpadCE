using System;
using System.Collections.Generic;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private bool _isCheck;

        // A name a line read, and whether it was defined then
        private sealed class ReadName(string name, int line, int sourceLine, int start, int end)
        {
            internal readonly string Name = name;
            internal readonly int Line = line, SourceLine = sourceLine, Start = start, End = end;
            // Null until known
            internal bool? Defined;
        }

        private readonly List<ReadName> _readNames = [];
        private readonly List<ReadName> _loopReads = [];
        private readonly List<(ReadName Read, MathParserException Error)> _loopErrors = [];
        // Names only the run would define
        private readonly HashSet<string> _checkDefined = new(StringComparer.Ordinal);
        // The check pass doesn't run loops, so it tracks only their structure
        private readonly Stack<(int Id, int Line, int SourceLine)> _checkLoops = new();

        /// <summary>
        /// Checks every line without calculating, including untaken branches and loop bodies, and never stops.
        /// It reads and writes no files, and it changes <see cref="Settings"/> as #settings and #round lines do.
        /// </summary>
        public IReadOnlyList<CalcpadError> Check(string sourceCode)
        {
            _isCheck = true;
            try
            {
                Parse(sourceCode.AsSpan(), false, false);
            }
            finally
            {
                _isCheck = false;
            }
            return _errorList;
        }

        private void ResetCheckState()
        {
            _checkLoops.Clear();
            _readNames.Clear();
            _loopReads.Clear();
            _checkDefined.Clear();
            _loopErrors.Clear();
            if (_isCheck)
                _parser.ReadNames = [];
        }

        private void CheckLoopStart()
        {
            if (_isCheck)
                _checkLoops.Push((_condition.Id, _currentLine, _parser.Line));
        }

        private void CheckLoopEnd(ReadOnlySpan<char> s)
        {
            if (_checkLoops.Count == 0)
                AppendError(s.ToString(), Messages.loop_without_a_corresponding_repeat, _currentLine, "CPD-3105", CalcpadErrorKind.Structure);
            else if (_checkLoops.Pop().Id != _condition.Id)
                AppendError(s.ToString(), Messages.Entangled_if__end_if__and_repeat__loop_blocks, _currentLine, "CPD-3105", CalcpadErrorKind.Structure);
            if (_checkLoops.Count == 0)
                ResolveLoopReads();
        }

        private void RecordWarning(string message, string code, int startColumn, int endColumn)
        {
            if (!_isCheck || !_errorKeys.Add((_currentLine, code, message)))
                return;

            _errorList.Add(new CalcpadError
            {
                SourceLine = _parser?.Line > 0 ? _parser.Line : _currentLine + 1,
                OutputLine = _currentLine + 1,
                Message = message,
                Source = CalcpadErrorSource.Expression,
                Code = code,
                Kind = CalcpadErrorKind.Syntax,
                Severity = CalcpadErrorSeverity.Warning,
                StartColumn = startColumn,
                EndColumn = endColumn
            });
        }

        // Function bodies resolve names when called, and loop bodies see later lines on the next pass,
        // so those reads are judged at the end of the document or of the outermost loop.
        /// <param name="nameColumn">Column of the parsed expression's start, to point at each name, or -1.</param>
        private void TakeReadNames(int line, int sourceLine, int start = -1, int end = -1, bool isFunction = false, int nameColumn = -1)
        {
            if (_parser?.ReadNames is not { Count: > 0 } names)
                return;

            foreach (var (name, at) in names)
            {
                var read = nameColumn >= 0 && at >= 0
                    ? new ReadName(name, line, sourceLine, nameColumn + at, nameColumn + at + name.Length)
                    : new ReadName(name, line, sourceLine, start, end);
                if (_checkLoops.Count != 0)
                {
                    if (!isFunction)
                        _loopReads.Add(read);
                }
                else if (!isFunction)
                    read.Defined = IsCheckDefined(name);
                _readNames.Add(read);
            }
            names.Clear();
        }

        private void ResolveLoopReads()
        {
            foreach (var read in _loopReads)
                read.Defined = IsCheckDefined(read.Name);
            _loopReads.Clear();

            // A later line of the loop may define it for the next pass
            foreach (var (read, ex) in _loopErrors)
            {
                var defined = ex.Code == "CPD-3305" ? _parser.IsDefinedFunction(read.Name) : IsCheckDefined(read.Name);
                if (!defined)
                    RecordError(read.Line, ex.Message, true, ex.Code, ex.Kind, read.SourceLine, read.Start, read.End);
            }
            _loopErrors.Clear();
        }

        private bool IsCheckDefined(string name) => _checkDefined.Contains(name) || _parser.IsDefinedName(name);

        // Names read before anything defines them, or that nothing defines at all
        private void ReportUndefinedNames()
        {
            ResolveLoopReads();
            foreach (var read in _readNames)
            {
                if (read.Defined ?? IsCheckDefined(read.Name))
                    continue;

                var ex = Exceptions.UndefinedVariableOrUnits(read.Name);
                RecordError(read.Line, ex.Message, true, ex.Code, ex.Kind, read.SourceLine, read.Start, read.End);
            }
            _readNames.Clear();
            _loopReads.Clear();
        }
    }
}
