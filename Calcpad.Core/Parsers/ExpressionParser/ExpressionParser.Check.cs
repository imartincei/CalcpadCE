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
            // For a function body: the first call, where it was judged
            internal CalcpadCallSite Call;
            internal bool IsFunctionBody;
        }

        // What a function body reads and calls, judged at its first call
        private sealed class FunctionBody(List<ReadName> reads, List<string> callees)
        {
            internal readonly List<ReadName> Reads = reads;
            internal readonly List<string> Callees = callees;
            internal bool IsCalled;
        }

        private readonly Dictionary<string, FunctionBody> _functionBodies = new(StringComparer.Ordinal);

        private readonly List<ReadName> _readNames = [];
        private readonly List<ReadName> _loopReads = [];
        private readonly List<(ReadName Read, MathParserException Error)> _loopErrors = [];
        // Names only the run would define
        private readonly HashSet<string> _checkDefined = new(StringComparer.Ordinal);
        // Recursive definitions; their calls report nothing more
        private readonly HashSet<string> _recursiveFunctions = new(StringComparer.Ordinal);
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
            _recursiveFunctions.Clear();
            _functionBodies.Clear();
            if (_isCheck)
            {
                _parser.ReadNames = [];
                _parser.FunctionCalls = [];
            }
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

        // Function bodies resolve names when first called, and loop bodies see later lines on the next pass,
        // so those reads are judged then, or at the end of the outermost loop or of the document.
        /// <param name="nameColumn">Column of the parsed expression's start, to point at each name, or -1.</param>
        private void TakeReadNames(int line, int sourceLine, int start = -1, int end = -1, bool isFunction = false, int nameColumn = -1)
        {
            var calls = _parser?.FunctionCalls;
            var function = isFunction ? _parser.DefinedFunctionName : null;
            List<ReadName> bodyReads = function is null ? null : [];
            if (_parser?.ReadNames is { Count: > 0 } names)
            {
                foreach (var (name, at) in names)
                {
                    var read = nameColumn >= 0 && at >= 0
                        ? new ReadName(name, line, sourceLine, nameColumn + at, nameColumn + at + name.Length)
                        : new ReadName(name, line, sourceLine, start, end);
                    if (bodyReads is not null)
                    {
                        read.IsFunctionBody = true;
                        bodyReads.Add(read);
                    }
                    else if (_checkLoops.Count != 0)
                        _loopReads.Add(read);
                    else
                        read.Defined = IsCheckDefined(name);
                    _readNames.Add(read);
                }
                names.Clear();
            }
            if (calls is not { Count: > 0 } && function is null)
                return;

            if (function is not null)
                _functionBodies[function] = new(bodyReads, calls.ConvertAll(c => c.Name));
            else
                foreach (var call in calls)
                {
                    var (callStart, callEnd) = nameColumn >= 0 && call.Start >= 0 && call.End > call.Start
                        ? (nameColumn + call.Start, nameColumn + call.End)
                        : (start, end);
                    JudgeFunctionBody(call.Name, new(call.Name, sourceLine, callStart, callEnd));
                }
            calls.Clear();
        }

        // The callees of a function are first called with it
        private void JudgeFunctionBody(string name, CalcpadCallSite call)
        {
            if (!_functionBodies.TryGetValue(name, out var body) || body.IsCalled)
                return;

            body.IsCalled = true;
            foreach (var read in body.Reads)
            {
                read.Call = call with { Function = name };
                if (_checkLoops.Count != 0)
                    _loopReads.Add(read);
                else
                    read.Defined = IsCheckDefined(read.Name);
            }
            foreach (var callee in body.Callees)
                JudgeFunctionBody(callee, call);
        }

        private void ResolveLoopReads()
        {
            foreach (var read in _loopReads)
                read.Defined = IsCheckDefined(read.Name);
            _loopReads.Clear();

            // A later line of the loop may define it for the next pass
            foreach (var (read, ex) in _loopErrors)
            {
                var defined = ex.Code == "CPD-3305" ? IsFunctionKnown(read.Name) : IsCheckDefined(read.Name);
                if (!defined)
                    RecordError(read.Line, ex.Message, true, ex.Code, ex.Kind, read.SourceLine, read.Start, read.End);
            }
            _loopErrors.Clear();
        }

        private void ClearReads()
        {
            _parser.ReadNames?.Clear();
            _parser.FunctionCalls?.Clear();
        }

        private bool IsFunctionKnown(string name) => _parser.IsDefinedFunction(name) || _recursiveFunctions.Contains(name);

        private void TrackFunctionDefinition(bool isRecursive)
        {
            if (!_isCheck || _parser.DefinedFunction is not { } name)
                return;
            if (isRecursive)
                _recursiveFunctions.Add(name);
            else
                _recursiveFunctions.Remove(name);
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
                RecordError(read.Line, ex.Message, true, ex.Code, ex.Kind, read.SourceLine, read.Start, read.End, read.Call, read.IsFunctionBody);
            }
            _readNames.Clear();
            _loopReads.Clear();
        }
    }
}
