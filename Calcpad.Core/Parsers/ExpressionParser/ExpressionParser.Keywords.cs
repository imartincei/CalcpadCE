
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private enum Keyword
        {
            None,
            Hide,
            Show,
            Pre,
            Post,
            End_Hide,
            End_Show,
            End_Pre,
            End_Post,
            Val,
            Equ,
            Noc,
            End_Val,
            End_Equ,
            End_Noc,
            NoSub,
            NoVar,
            VarSub,
            Const,
            Split,
            Wrap,
            Deg,
            Rad,
            Gra,
            Round,
            Format,
            If,
            Else_If,
            Else,
            End_If,
            While,
            For,
            Repeat,
            Loop,
            Break,
            Continue,
            Local,
            Global,
            Pause,
            Input,
            Md,
            Read,
            Write,
            Append,
            Phasor,
            Complex,
            Settings,
            Ui,
            ProjectPath,
            LibraryPath,
            SkipLine
        }
        private enum KeywordResult
        {
            None,
            Continue,
            Break
        }

        private Keyword _previousKeyword = Keyword.None;
        private static string[] KeywordNames;
        private static Keyword[] KeywordValues;
        private static List<int>[] KeywordIndex;
        private static int MaxKeywordLength;

        private static void InitKeyWordStrings()
        {
            var n = 'z' - 'a';
            KeywordNames = Enum.GetNames<Keyword>().Skip(1).ToArray();
            MaxKeywordLength = KeywordNames.Max(s => s.Length);
            KeywordValues = Enum.GetValues<Keyword>().Skip(1).ToArray();
            KeywordIndex = new List<int>[n];
            for (int i = 0, len = KeywordNames.Length; i < len; ++i)
            {
                var lower = KeywordNames[i].ToLowerInvariant().Replace('_', ' ');
                KeywordNames[i] = lower;
                var j = lower[0] - 'a';
                if (KeywordIndex[j] is null)
                    KeywordIndex[j] = [i];
                else
                    KeywordIndex[j].Add(i);
            }
        }

        private static Keyword GetKeyword(ReadOnlySpan<char> s)
        {
            var n = Math.Min(MaxKeywordLength, s.Length - 1);
            if (n < 3)
                return Keyword.None;

            var i = char.ToLowerInvariant(s[1]) - 'a';
            if (i < 0 || i >= KeywordNames.Length)
                return Keyword.None;

            var ind = KeywordIndex[i];
            if (ind is null)
                return Keyword.None;

            Span<char> lower = stackalloc char[n];
            s.Slice(1, n).ToLowerInvariant(lower);
            for (int j = 0; j < ind.Count; ++j)
            {
                var k = ind[j];
                if (lower.StartsWith(KeywordNames[k]))
                    return KeywordValues[k];
            }
            return Keyword.None;
        }

        KeywordResult ParseKeyword(ReadOnlySpan<char> s, ref Keyword keyword)
        {
            if (_isPausedByUser)
                keyword = Keyword.Pause;
            else if (s[0] == '#' && keyword == Keyword.None)
                keyword = GetKeyword(s);

            if (keyword == Keyword.None)
                return KeywordResult.None;

            switch (keyword)
            {
                case Keyword.Hide: SetVisibility(s, 5, Settings.Math.ShowHiddenOutput && _isVisible); break;
                case Keyword.Show: SetVisibility(s, 5, true); break;
                case Keyword.Pre: SetVisibility(s, 4, !ForPrint); break;
                case Keyword.Post: SetVisibility(s, 5, !EnableUi); break;
                case Keyword.End_Hide:
                case Keyword.End_Show:
                case Keyword.End_Pre:
                case Keyword.End_Post:
                    _isVisible = _visibilityStack.Count > 0 ? _visibilityStack.Pop() : true;
                    break;
                case Keyword.Input:
                    return ParseKeywordInput();
                case Keyword.Pause:
                    return ParseKeywordPause();
                case Keyword.Val: SetOutputMode(s, 4, 1); break;
                case Keyword.Equ: SetOutputMode(s, 4, 0); break;
                case Keyword.Noc: SetOutputMode(s, 4, -1); break;
                case Keyword.End_Val:
                case Keyword.End_Equ:
                case Keyword.End_Noc:
                    _isVal = _outputModeStack.Count > 0 ? _outputModeStack.Pop() : 0;
                    break;
                case Keyword.NoSub:
                    _parser.VariableSubstitution = MathParser.VariableSubstitutionOptions.VariablesOnly;
                    break;
                case Keyword.NoVar:
                    _parser.VariableSubstitution = MathParser.VariableSubstitutionOptions.SubstitutionsOnly;
                    break;
                case Keyword.VarSub:
                    _parser.VariableSubstitution = MathParser.VariableSubstitutionOptions.VariablesAndSubstitutions;
                    break;
                case Keyword.Const:
                    _parser.IsConst = true;
                    return KeywordResult.None;
                case Keyword.Split:
                    _parser.Split = true;
                    break;
                case Keyword.Wrap:
                    _parser.Split = false;
                    break;
                case Keyword.Deg:
                    _parser.Degrees = 0;
                    break;
                case Keyword.Rad:
                    _parser.Degrees = 1;
                    break;
                case Keyword.Gra:
                    _parser.Degrees = 2;
                    break;
                case Keyword.Round:
                    ParseKeywordRound(s);
                    break;
                case Keyword.Format:
                    ParseKeywordFormat(s);
                    break;
                case Keyword.Repeat:
                    ParseKeywordRepeat(s);
                    break;
                case Keyword.For:
                    ParseKeywordFor(s);
                    break;
                case Keyword.While:
                    ParseKeywordWhile(s);
                    break;
                case Keyword.Loop:
                    ParseKeywordLoop(s);
                    break;
                case Keyword.Break:
                    if (ParseKeywordBreak())
                        return KeywordResult.Break;
                    break;
                case Keyword.Continue:
                    ParseKeywordContinue();
                    break;
                case Keyword.Md:
                    ParseKeywordMd(s);
                    break;
                case Keyword.Read:
                    ParseKeywordRead(s);
                    break;
                case Keyword.Write:
                case Keyword.Append:
                    ParseKeywordWrite(s, keyword);
                    break;
                case Keyword.Phasor:
                    _parser.Phasor = true;
                    break;
                case Keyword.Complex:
                    _parser.Phasor = false;
                    break;
                case Keyword.Settings:
                    ParseKeywordSettings(s);
                    break;
                case Keyword.Ui:
                    return ParseKeywordUi(s);
                case Keyword.ProjectPath:
                case Keyword.LibraryPath:
                    ParseKeywordPathRoot(s);
                    break;
                default:
                    if (keyword != Keyword.Global && keyword != Keyword.Local)
                        return KeywordResult.None;
                    break;
            }
            return KeywordResult.Continue;
        }

        private void SetVisibility(ReadOnlySpan<char> s, int kwdLength, bool value)
        {
            _visibilityStack.Push(_isVisible);
            if (IsDirectiveConditionMet(s, kwdLength))
                _isVisible = value;
        }

        private void SetOutputMode(ReadOnlySpan<char> s, int kwdLength, int value)
        {
            _outputModeStack.Push(_isVal);
            if (IsDirectiveConditionMet(s, kwdLength))
                _isVal = value;
        }

        private bool IsDirectiveConditionMet(ReadOnlySpan<char> s, int kwdLength)
        {
            if (!_condition.IsSatisfied)
                return false;

            if (s.Length <= kwdLength)
                return true;

            var expr = s[kwdLength..].Trim();
            if (expr.IsWhiteSpace())
                return true;

            if (!_calculate)
                return false;

            try
            {
                _parser.Parse(expr);
                _parser.Calculate();
                return Condition.IsTrue(_parser.Result);
            }
            catch (MathParserException ex)
            {
                AppendError(s.ToString(), ex.Message, _currentLine);
                return false;
            }
        }

        KeywordResult ParseKeywordInput()
        {
            if (_condition.IsSatisfied)
            {
                _previousKeyword = Keyword.Input;
                if (_calculate)
                {
                    _startLine = _currentLine + 1;
                    _pauseCharCount = _sb.Length;
                    _calculate = false;
                    return KeywordResult.Continue;
                }
                return KeywordResult.Break;
            }
            return _calculate ? KeywordResult.Continue : KeywordResult.Break;
        }

        KeywordResult ParseKeywordPause()
        {
            if (_condition.IsSatisfied && (_calculate || _startLine > 0))
            {
                if (_calculate)
                {
                    if (_isPausedByUser)
                        _startLine = _currentLine;
                    else
                        _startLine = _currentLine + 1;
                }

                if (_previousKeyword != Keyword.Input)
                    _pauseCharCount = _sb.Length;

                _previousKeyword = Keyword.Pause;
                _isPausedByUser = false;
                return KeywordResult.Break;
            }
            if (_isVisible && !_calculate)
                _sb.Append($"<p{HtmlId} class=\"cond\">#pause</p>");

            return KeywordResult.Continue;
        }

        private void ParseKeywordRound(ReadOnlySpan<char> s)
        {
            if (s.Length > 6)
            {
                var expr = s[6..].Trim();
                if (expr.SequenceEqual("default"))
                    Settings.Math.Decimals = _decimals;
                else if (int.TryParse(expr, out int n))
                    Settings.Math.Decimals = n;
                else
                {
                    try
                    {
                        _parser.Parse(expr);
                        _parser.Calculate();
                        Settings.Math.Decimals = (int)Math.Round(_parser.Real, MidpointRounding.AwayFromZero);
                    }
                    catch (MathParserException ex)
                    {
                        AppendError(s.ToString(), ex.Message, _currentLine);
                    }
                }
            }
            else
                Settings.Math.Decimals = _decimals;
        }

        private void ParseKeywordRepeat(ReadOnlySpan<char> s)
        {
            ReadOnlySpan<char> expression = s.Length > 7 ? // #repeat - 7
                s[7..].Trim() :
                [];

            if (_calculate)
            {
                if (_condition.IsSatisfied)
                {
                    var count = 0d;
                    if (!expression.IsWhiteSpace())
                    {
                        try
                        {
                            _parser.Parse(expression);
                            _parser.Calculate();
                            if (_parser.Real > Loop.MaxCount)
                                AppendError(s.ToString(), string.Format(Messages.Number_of_iterations_exceeds_the_maximum_0, Loop.MaxCount), _currentLine);
                            else
                                count = Math.Round(_parser.Real, MidpointRounding.AwayFromZero);
                        }
                        catch (MathParserException ex)
                        {
                            AppendError(s.ToString(), ex.Message, _currentLine);
                        }
                    }
                    else
                        count = -1d;

                    _loops.Push(new RepeatLoop(_currentLine, count, _condition.Id));
                }
            }
            else if (_isVisible)
            {
                if (expression.IsWhiteSpace())
                    _sb.Append($"<p{HtmlId} class=\"cond\">#repeat</p><div class=\"indent\">");
                else
                {
                    try
                    {
                        _parser.Parse(expression);
                        _sb.Append($"<p{HtmlId}><span class=\"cond\">#repeat</span> <span class=\"eq\">{_parser.ToHtml()}</span></p><div class=\"indent\">");
                    }
                    catch (MathParserException ex)
                    {
                        AppendError(s.ToString(), ex.Message, _currentLine);
                    }
                }
            }
        }

        private void ParseKeywordFor(ReadOnlySpan<char> s)
        {
            ReadOnlySpan<char> expression = s.Length > 4 ? // #for - 4
                s[4..].Trim() :
                [];

            if (expression.IsWhiteSpace())
                throw Exceptions.ExpressionEmpty();

            (int loopStart, int loopEnd) = GetForLoopLimits(expression);
            if (loopStart > -1 &&
                loopEnd > loopStart)
            {
                var varName = expression[..loopStart].Trim().ToString();
                var startExpr = expression[(loopStart + 1)..loopEnd].Trim();
                var endExpr = expression[(loopEnd + 1)..].Trim();
                if (Validator.IsVariable(varName))
                {
                    if (_calculate)
                    {
                        if (_condition.IsSatisfied)
                        {
                            try
                            {
                                _parser.Parse(startExpr);
                                _parser.Calculate();
                                var r1 = _parser.Result;
                                var u1 = _parser.Units;
                                _parser.Parse(endExpr);
                                _parser.Calculate();
                                var r2 = _parser.Result;
                                var u2 = _parser.Units;
                                IScalarValue start, end;
                                if (r1.IsReal && r2.IsReal)
                                {
                                    start = new RealValue(r1.Re, u1);
                                    end = new RealValue(r2.Re, u2);
                                }
                                else
                                {
                                    start = new ComplexValue(r1, u1);
                                    end = new ComplexValue(r2, u2);
                                }
                                var count = Math.Abs((end - start).Re) + 1;
                                if (count > Loop.MaxCount)
                                {
                                    AppendError(s.ToString(), string.Format(Messages.Number_of_iterations_exceeds_the_maximum_0, Loop.MaxCount), _currentLine);
                                    return;
                                }
                                var counter = _parser.GetVariableRef(varName);
                                _loops.Push(new ForLoop(_currentLine, start, end, counter, _condition.Id));
                                _parser.SetVariable(varName, start);
                            }
                            catch (MathParserException ex)
                            {
                                AppendError(s.ToString(), ex.Message, _currentLine);
                            }
                        }
                    }
                    else if (_isVisible)
                    {
                        try
                        {
                            var varHtml = new HtmlWriter(null, _parser.Phasor).FormatVariable(varName, string.Empty, false);
                            _parser.Parse(startExpr);
                            var startHtml = _parser.ToHtml();
                            _parser.Parse(endExpr);
                            var endHtml = _parser.ToHtml();
                            _sb.Append($"<p{HtmlId}><span class=\"cond\">#for</span> <span class=\"eq\">{varHtml} = {startHtml} : {endHtml}</span></p><div class=\"indent\">");
                        }
                        catch (MathParserException ex)
                        {
                            AppendError(s.ToString(), ex.Message, _currentLine);
                        }
                    }
                }
            }
        }

        private void ParseKeywordWhile(ReadOnlySpan<char> s)
        {
            ReadOnlySpan<char> expression = s.Length > 6 ? // #while - 6
                s[7..].Trim() :
                [];

            if (expression.IsWhiteSpace())
                throw Exceptions.ExpressionEmpty();

            if (_calculate)
            {
                if (_condition.IsSatisfied)
                {
                    try
                    {
                        var commentStart = expression.IndexOf('\'');
                        var condition = commentStart < 0 ? expression : expression[..commentStart];
                        _parser.Parse(condition);
                        _parser.Calculate();
                        _condition.SetCondition(Keyword.While - Keyword.If);
                        _condition.Check(_parser.Result);
                        if (_condition.IsSatisfied)
                        {
                            _loops.Push(new WhileLoop(_currentLine, expression.ToString(), _condition.Id));
                            if (commentStart >= 0)
                                ParseTokens(GetTokens(expression[commentStart..]), false, false);
                        }
                    }
                    catch (MathParserException ex)
                    {
                        AppendError(s.ToString(), ex.Message, _currentLine);
                    }
                }
            }
            else if (_isVisible)
            {
                try
                {
                    _sb.Append($"<p{HtmlId}><span class=\"cond\">#while</span> ");
                    ParseTokens(GetTokens(expression), true, false);
                    _sb.Append("</p><div class=\"indent\">");
                }
                catch (MathParserException ex)
                {
                    AppendError(s.ToString(), ex.Message, _currentLine);
                }
            }
        }

        private void ParseKeywordLoop(ReadOnlySpan<char> s)
        {
            if (_calculate)
            {
                if (_condition.IsSatisfied)
                {
                    if (_loops.Count == 0)
                        AppendError(s.ToString(), Messages.loop_without_a_corresponding_repeat, _currentLine);
                    else
                    {
                        var next = _loops.Peek();
                        if (next.Id != _condition.Id)
                            AppendError(s.ToString(), Messages.Entangled_if__end_if__and_repeat__loop_blocks, _currentLine);
                        else if (!Iterate(next, true))
                            _loops.Pop();
                    }
                }
                else if (_condition.IsLoop)
                    _condition.SetCondition(Condition.RemoveConditionKeyword);
            }
            else if (_isVisible)
                _sb.Append($"</div><p{HtmlId} class=\"cond\">#loop</p>");
        }

        private bool Iterate(Loop loop, bool removeWhileCondition)
        {
            if (loop is ForLoop forLoop)
                forLoop.IncrementCounter();
            else if (loop is WhileLoop whileLoop)
            {
                var expression = whileLoop.Condition;
                var commentStart = expression.IndexOfAny(['\'', '"']);
                if (commentStart < 0)
                    commentStart = expression.Length;

                var condition = expression.AsSpan(0, commentStart);
                _parser.Parse(condition);
                _parser.Calculate();
                _condition.Check(_parser.Result);
                if (_condition.IsSatisfied)
                {
                    if (commentStart < expression.Length - 1)
                        ParseTokens(GetTokens(expression.AsSpan(commentStart)), false, false);
                }
                else
                {
                    if (removeWhileCondition)
                        _condition.SetCondition(Condition.RemoveConditionKeyword);

                    loop.Break();
                }
            }
            if (loop.Iterate(ref _currentLine))
            {
                _parser.ResetStack();
                return true;
            }
            return false;
        }

        private bool ParseKeywordBreak()
        {
            if (_calculate)
            {
                if (_condition.IsSatisfied)
                {
                    if (_loops.Count != 0)
                        _loops.Peek().Break();
                    else
                        return true;
                }
            }
            else if (_isVisible)
                _sb.Append($"<p{HtmlId} class=\"cond\">#break</p>");

            return false;
        }

        internal void ParseKeywordContinue()
        {
            if (_calculate)
            {
                if (_condition.IsSatisfied)
                {
                    if (_loops.Count == 0)
                        AppendError("#continue", Messages.continue_without_a_corresponding_repeat, _currentLine);
                    else
                    {
                        var loop = _loops.Peek();
                        if (Iterate(loop, false))
                            while (_condition.Id > loop.Id)
                                _condition.SetCondition(Condition.RemoveConditionKeyword);
                        else
                            loop.Break();
                    }
                }
            }
            else if (_isVisible)
                _sb.Append($"<p{HtmlId} class=\"cond\">#continue</p>");
        }

        private static (int, int) GetForLoopLimits(ReadOnlySpan<char> expression)
        {
            (int start, int end) = (-1, -1);
            int n1 = 0, n2 = 0, n3 = 0;
            for (int i = 0, len = expression.Length; i < len; ++i)
            {
                switch (expression[i])
                {
                    case '=': start = i; break;
                    case ':' when n1 == 0 && n2 == 0 && n3 == 0: end = i; return (start, end);
                    case '(': ++n1; break;
                    case ')': --n1; break;
                    case '{': ++n2; break;
                    case '}': --n2; break;
                    case '[': ++n3; break;
                    case ']': --n3; break;
                }
            }
            return (start, end);
        }

        private void ParseKeywordFormat(ReadOnlySpan<char> s)
        {
            if (s.Length > 7)
            {
                var expr = s[7..].Trim();
                if (expr.SequenceEqual("default"))
                    Settings.Math.FormatString = null;
                else
                {
                    var format = expr.ToString();
                    if (Validator.IsValidFormatString(format))
                        Settings.Math.FormatString = format;
                    else
                        AppendError("#format " + format, Messages.Invalid_format_string_0, _currentLine);
                }
            }
            else
                Settings.Math.FormatString = null;
        }
        private void ParseKeywordPathRoot(ReadOnlySpan<char> s)
        {
            // MacroParser already declared and validated this line against the folder of the file
            // that wrote it — which this flattened text no longer identifies — so its answer wins.
            if (_hasInheritedPathRoots)
                return;

            PathRoots.IsDeclaration(s, out var isProject, out var start, out var length);
            if (length == 0)
            {
                AppendError(s.ToString(), string.Format(Messages.Missing_path_value_0,
                    isProject ? "#ProjectPath" : "#LibraryPath"), _currentLine);
                return;
            }

            var rawValue = s.Slice(start, length).ToString();
            var declaringDir = !string.IsNullOrEmpty(SourceFilePath)
                ? System.IO.Path.GetDirectoryName(SourceFilePath) : null;
            if (!_pathRoots.TryDeclare(isProject, rawValue, declaringDir, out var error))
                AppendError(s.ToString(), error, _currentLine);
        }

        private const string SettingsKeyword = "#settings";

        private void ParseKeywordSettings(ReadOnlySpan<char> s)
        {
            var json = s[SettingsKeyword.Length..].Trim();
            if (json.IsEmpty)
            {
                AppendError(s.ToString(), string.Format(Messages.Invalid_settings_0, json.ToString()), _currentLine);
                return;
            }
            try
            {
                var dto = SettingsDto.Parse(json.ToString());
                if (dto is null)
                    return;

                HashSet<SettingKey> invalid = null;
                foreach (var error in dto.Validate())
                {
                    (invalid ??= []).Add(error.Key);
                    AppendError(s.ToString(), error.Message, _currentLine);
                }

                foreach (SettingKey key in Enum.GetValues<SettingKey>())
                    if (invalid is null || !invalid.Contains(key))
                        ApplySetting(key, dto);
            }
            catch (JsonException)
            {
                AppendError(s.ToString(), string.Format(Messages.Invalid_settings_0, json.ToString()), _currentLine);
            }
        }

        private void ApplySetting(SettingKey key, SettingsDto dto)
        {
            switch (key)
            {
                case SettingKey.Decimals:
                    if (dto.Decimals.HasValue)
                        Settings.Math.Decimals = dto.Decimals.Value;
                    break;
                case SettingKey.Degrees:
                    if (dto.Degrees.HasValue)
                    {
                        Settings.Math.Degrees = dto.Degrees.Value;
                        _parser.Degrees = dto.Degrees.Value;
                    }
                    break;
                case SettingKey.Complex:
                    if (dto.Complex.HasValue)
                        _parser.SetComplex(dto.Complex.Value);
                    break;
                case SettingKey.Substitute:
                    if (dto.Substitute.HasValue)
                        Settings.Math.Substitute = dto.Substitute.Value;
                    break;
                case SettingKey.FormatEquations:
                    if (dto.FormatEquations.HasValue)
                        Settings.Math.FormatEquations = dto.FormatEquations.Value;
                    break;
                case SettingKey.ZeroSmallMatrixElements:
                    if (dto.ZeroSmallMatrixElements.HasValue)
                        Settings.Math.ZeroSmallMatrixElements = dto.ZeroSmallMatrixElements.Value;
                    break;
                case SettingKey.ShowHiddenOutput:
                    if (dto.ShowHiddenOutput.HasValue)
                        Settings.Math.ShowHiddenOutput = dto.ShowHiddenOutput.Value;
                    break;
                case SettingKey.MaxOutputCount:
                    if (dto.MaxOutputCount.HasValue)
                        Settings.Math.MaxOutputCount = dto.MaxOutputCount.Value;
                    break;
                case SettingKey.Units:
                    if (dto.Units is not null)
                    {
                        Settings.Units = dto.Units;
                        _parser.SetVariable("Units", new RealValue(UnitsFactor()));
                    }
                    break;
                case SettingKey.IsUs:
                    if (dto.IsUs.HasValue)
                    {
                        Settings.IsUs = dto.IsUs.Value;
                        _parser.IsUs = dto.IsUs.Value;
                    }
                    break;
                case SettingKey.VectorGraphics:
                    if (dto.VectorGraphics.HasValue)
                    {
                        Settings.Plot.VectorGraphics = dto.VectorGraphics.Value;
                        _parser.SetVariable("PlotSVG", dto.VectorGraphics.Value ? 1d : 0d);
                    }
                    break;
                case SettingKey.ColorScale:
                    if (dto.ColorScale is not null &&
                        Enum.TryParse<PlotSettings.ColorScales>(dto.ColorScale, true, out var cs))
                    {
                        Settings.Plot.ColorScale = cs;
                        _parser.SetVariable("PlotPalette", (int)cs);
                    }
                    break;
                case SettingKey.SmoothScale:
                    if (dto.SmoothScale.HasValue)
                    {
                        Settings.Plot.SmoothScale = dto.SmoothScale.Value;
                        _parser.SetVariable("PlotSmooth", dto.SmoothScale.Value ? 1d : 0d);
                    }
                    break;
                case SettingKey.Shadows:
                    if (dto.Shadows.HasValue)
                    {
                        Settings.Plot.Shadows = dto.Shadows.Value;
                        _parser.SetVariable("PlotShadows", dto.Shadows.Value ? 1d : 0d);
                    }
                    break;
                case SettingKey.AdaptivePlot:
                    if (dto.AdaptivePlot.HasValue)
                    {
                        Settings.Plot.IsAdaptive = dto.AdaptivePlot.Value;
                        _parser.SetVariable("PlotAdaptive", dto.AdaptivePlot.Value ? 1d : 0d);
                    }
                    break;
                case SettingKey.PlotWidth:
                    if (dto.PlotWidth.HasValue)
                    {
                        Settings.Plot.Width = dto.PlotWidth.Value;
                        _parser.SetVariable("PlotWidth", dto.PlotWidth.Value);
                    }
                    break;
                case SettingKey.PlotHeight:
                    if (dto.PlotHeight.HasValue)
                    {
                        Settings.Plot.Height = dto.PlotHeight.Value;
                        _parser.SetVariable("PlotHeight", dto.PlotHeight.Value);
                    }
                    break;
                case SettingKey.PlotStep:
                    if (dto.PlotStep.HasValue)
                    {
                        Settings.Plot.Step = dto.PlotStep.Value;
                        _parser.SetVariable("PlotStep", dto.PlotStep.Value);
                    }
                    break;
                case SettingKey.Precision:
                    if (dto.Precision.HasValue)
                    {
                        Settings.Math.Precision = dto.Precision.Value;
                        _parser.SetVariable("Precision", dto.Precision.Value);
                    }
                    break;
                case SettingKey.Tol:
                    if (dto.Tol.HasValue)
                    {
                        Settings.Math.Tol = dto.Tol.Value;
                        _parser.SetVariable("Tol", dto.Tol.Value);
                    }
                    break;
            }
        }

        private void ParseKeywordMd(ReadOnlySpan<char> s)
        {
            if (s.Length > 3)
            {
                var expr = s[3..].Trim();
                if (expr.Equals("on", StringComparison.OrdinalIgnoreCase))
                    _isMarkdownOn = true;
                else if (expr.Equals("off", StringComparison.OrdinalIgnoreCase))
                    _isMarkdownOn = false;
                else
                    AppendError(s.ToString(), string.Format(Messages.Invalid_keyword_0, expr.ToString()), _currentLine);
            }
            else
                _isMarkdownOn = true;
        }

        private void ParseKeywordRead(ReadOnlySpan<char> s)
        {
            if (_calculate)
            {
                if (_condition.IsSatisfied)
                {
                    var sourceDir = !string.IsNullOrEmpty(SourceFilePath)
                        ? System.IO.Path.GetDirectoryName(SourceFilePath) : null;
                    var options = new ReadWriteOptions(s, 0, sourceDir, _pathRoots);
                    if (options.Name.IsEmpty)
                        return;

                    var data = DataExchange.Read(options);
                    if (options.Type == 'V')
                        _parser.SetVector(options.Name, data, options.IsHp);
                    else
                        _parser.SetMatrix(options.Name, data, options.Type, options.IsHp);

                    if (_isVisible)
                        ReportDataExchageResult(options, "read from");
                }
            }
            else if (_isVisible)
                _sb.Append($"<p><span{HtmlId} class=\"cond\">#read</span> {s[5..]}</p>");
        }

        private void ParseKeywordWrite(ReadOnlySpan<char> s, Keyword keyword)
        {
            if (_calculate)
            {
                if (_condition.IsSatisfied)
                {
                    var sourceDir = !string.IsNullOrEmpty(SourceFilePath)
                        ? System.IO.Path.GetDirectoryName(SourceFilePath) : null;
                    var options = new ReadWriteOptions(s, keyword - Keyword.Read, sourceDir, _pathRoots);
                    if (options.Name.IsEmpty)
                        return;

                    var m = _parser.GetMatrix(options.Name.ToString(), options.Type);
                    DataExchange.Write(options, m);
                    if (_isVisible)
                        ReportDataExchageResult(options, keyword == Keyword.Write ? "written to" : "appended to");
                }
            }
            else if (_isVisible)
                _sb.Append($"<p><span{HtmlId} class=\"cond\">#write</span> {s[6..]}</p>");
        }

        private void ReportDataExchageResult(ReadWriteOptions options, string command)
        {
            var url = $"file:///{options.FullPath.Replace('\\', '/')}";
            _sb.Append($"<p{HtmlId}>")
               .Append($"Matrix <span class=\"eq\">{new HtmlWriter(Settings.Math, false).FormatVariable(options.Name.ToString(), string.Empty, true)}</span>")
               .Append($" was successfully {command} <a href=\"{url}\">{options.Path}.{options.Ext}</a>");
            if (options.IsExcel)
            {
                if (!options.Sheet.IsEmpty)
                    _sb.Append($"@{options.Sheet}");
                if (!options.Start.IsEmpty)
                    _sb.Append($"!{options.Start}");
                if (!options.End.IsEmpty)
                    _sb.Append($":{options.End}");
            }
            else
            {
                if (!options.Start.IsEmpty)
                    _sb.Append($"@{options.Start}");
                if (!options.End.IsEmpty)
                    _sb.Append($":{options.End}");

                _sb.Append($" <small>SEP</small>='{options.Separator}'");
            }
            _sb.Append($" <small>TYPE</small>={options.Type}");
            _sb.Append("</p>");
        }
    }
}
