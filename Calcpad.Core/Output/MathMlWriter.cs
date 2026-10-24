using System;
using System.Net;
using System.Text;

namespace Calcpad.Core
{
    internal class MathMlWriter : OutputWriter
    {
        internal MathMlWriter(MathSettings settings, bool phasor) : base(settings, phasor)
        {
            AngleUnits =
            [
                "<mi mathvariant=\"normal\">°</mi>",
                string.Empty,
                ThinSpaceElement + FormatUnitsStatic("grad"),
            ];
        }

        protected override OutputFormat FormatType => OutputFormat.MathMl;
        internal const string UnitDivision = "<mo class=\"unit\" lspace=\"0.05em\" rspace=\"0.05em\">∕</mo>";
        internal const string UnitProduct = "<mo class=\"unit\" lspace=\"0.05em\" rspace=\"0.05em\">·</mo>";
        internal const string ThinSpaceElement = "<mspace width=\"0.17em\"/>";
        private const string VectorSpace = "<mspace width=\"0.5em\"/>";

        internal static string Wrap(string s) => $"<math>{Normalize(s)}</math>";

        internal static string Keyword(string s) => $"<mtext class=\"cond\"> {s} </mtext>";

        private static string Row(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "<mrow></mrow>";

            return s.Contains('<') ? $"<mrow>{s}</mrow>" : Token(s);
        }

        private static string Mn(string s) => $"<mn>{s}</mn>";

        internal override string UnitString(Unit units) => units.MathMl;

        internal override string FormatInput(string s, Unit units, int line, bool isCalculated)
        {
            string output;
            if (s == "?")
                output = $"<input type=\"text\" size=\"2\" name=\"Var\" class=\"input-{line}\">";
            else if (isCalculated)
                output = $"<u class=\"input-{line}\">{s}</u>";
            else
                output = $"<input type=\"text\" size=\"2\" name=\"Var\" class=\"input-{line}\" value=\"{s}\">";

            output = $"<mtext>{output}</mtext>";
            if (units is not null)
                return string.Concat(output, ThinSpaceElement, units.MathMl);

            return output;
        }

        internal override string AppendSubscript(string sa, string sb)
        {
            const string end = "</mrow></msub>";
            var i = sa.LastIndexOf(end, StringComparison.Ordinal);
            if (i < 0)
                return FormatSubscript(sa, sb);

            var s = sb.Contains('<') ? $"<mtext class=\"sub\">.</mtext>{sb}" : SubText("." + sb);
            return string.Concat(sa[..i], s, sa[i..]);
        }

        internal override string FormatSubscript(string sa, string sb) =>
            $"<msub>{Row(sa)}<mrow>{(sb.Contains('<') ? sb : SubText(sb))}</mrow></msub>";

        private static string SubText(string s) => $"<mtext class=\"sub\">{s}</mtext>";

        internal override string FormatVariable(string name, string value, bool isBold)
        {
            var isVector = name[0] == '⃗';
            if (isVector)
            {
                isBold = false;
                name = name[1..];
            }
            var cls = isBold ? "var bold" : "var";
            var attributes = string.IsNullOrEmpty(value) ?
                $" class=\"{cls}\"" :
                $" class=\"{cls} value\" data-value='{value}'";

            string sub = null;
            var i = name.IndexOf('_');
            if (i > 0 && i + 1 < name.Length)
            {
                sub = name[(i + 1)..];
                name = name[..i];
            }
            var s = $"<mi{attributes}>{name}</mi>";
            if (isVector)
                s = $"<mover accent=\"true\">{s}<mo class=\"m-vec\">→</mo></mover>";

            return sub is null ? s : FormatSubscript(s, sub);
        }

        internal override string FormatUnits(string s) => s == "10" ? Mn(s) : FormatUnitsStatic(s);

        private static string FormatUnitsStatic(string s) => $"<mi class=\"unit\" mathvariant=\"normal\">{s}</mi>";

        internal override string FormatFunction(string s)
        {
            var i = s.IndexOf('_');
            if (i > 0 && i + 1 < s.Length)
                return FormatSubscript(Function(s[..i]), s[(i + 1)..]);

            return Function(s);

            static string Function(string s) => $"<mi class=\"fn\" mathvariant=\"normal\">{s}</mi>";
        }

        internal override string FormatRoot(string s, int level = 0, string n = "2") =>
            n == "2" ?
                $"<msqrt>{s}</msqrt>" :
                $"<mroot>{Row(s)}{Row(n)}</mroot>";

        internal override string FormatOperator(char c) => c switch
        {
            '/' or '÷' => "<mo>/</mo>",
            '*' => "<mo>·</mo>",
            '<' => "<mo>&lt;</mo>",
            '>' => "<mo>&gt;</mo>",
            '-' => "<mo>−</mo>",
            Calculator.NegChar => "<mo form=\"prefix\">-</mo>",
            '|' => "<mo stretchy=\"false\">|</mo>",
            '⦼' => "<mo>mod</mo>",
            '∧' => "<mo>and</mo>",
            '∨' => "<mo>or</mo>",
            '⊕' => "<mo>xor</mo>",
            _ => $"<mo>{c}</mo>"
        };

        internal override string FormatPower(string sa, string sb, int level, int order)
        {
            const string end = "</mrow></msup>";
            if (order == PowerOrder && sa.EndsWith(end, StringComparison.Ordinal))
                return string.Concat(sa[..^end.Length], FormatOperator('*'), sb, end);

            return $"<msup>{Row(sa)}{Row(sb)}</msup>";
        }

        internal override string FormatDivision(string sa, string sb, int level) =>
            level < 4 ?
                $"<mfrac>{Row(sa)}{Row(sb)}</mfrac>" :
                string.Concat(sa, "<mo>÷</mo>", sb);

        internal override string FormatNary(string symbol, string sub, string sup, string expr) =>
            $"<mrow><munderover><mo class=\"m-nary\" largeop=\"true\" movablelimits=\"false\">{symbol}</mo>{Row(sub)}{Row(sup)}</munderover>{expr}</mrow>";

        internal override string FormatValue(in IScalarValue value)
        {
            var u = value.Units;
            var s = FormatComplex(value.Re, value.Im, u?.FormatString);
            if (u is null)
                return s;

            var uMathMl = u.MathMl;
            if (string.IsNullOrEmpty(uMathMl))
                return s;

            if (!(value.IsReal || phasor && s.Contains('∠')))
                s = AddBrackets(s);

            if (u.IsAngle && u.Text.StartsWith('°'))
                return s + uMathMl;

            return string.Concat(s, ThinSpaceElement, uMathMl);
        }

        internal override string AddBrackets(string s, int level = 0, char left = '(', char right = ')') =>
            $"<mrow>{Fence(left)}{s}{Fence(right)}</mrow>";

        private static string Fence(char c) => c switch
        {
            ' ' => string.Empty,
            '<' => "<mo>&lt;</mo>",
            '>' => "<mo>&gt;</mo>",
            _ => $"<mo symmetric=\"false\">{c}</mo>"
        };

        internal override string FormatAbs(string s, int level = 0) => AddBrackets(s, level, '|', '|');

        internal override string FormatReal(double d, string format, bool zeroSmall)
        {
            var s = FormatNumberHelper(d, format);
            if (double.IsNaN(d) || double.IsInfinity(d))
                return $"<mtext class=\"err\">{s.Trim()}</mtext>";

            var i = s.LastIndexOf('E');
            if (i <= 0)
                return Mn(s);

            var i1 = i + 1;
            var sign = s[i1];
            if (i1 < s.Length - 1)
                ++i1;

            if (sign is '+' or '-' or '0')
            {
                if (zeroSmall && sign == '-')
                    return Mn("0");

                format ??= formatString;
                if (s[i1] == '0' && (format is null || !format.Contains('E')))
                    i1++;
            }
            var ms = s[..i];
            ms = string.Equals(ms, "1", StringComparison.Ordinal) ?
                string.Empty :
                Mn(ms) + "<mo>×</mo>";

            var es = s[i1..];
            if (sign == '+' && format is not null && (format.Contains("E+") || format.StartsWith('E')))
                es = "+" + es;
            else if (sign == '-')
                es = "-" + es;

            return $"{ms}<msup><mn>10</mn>{Mn(es)}</msup>";
        }

        internal override string FormatComplex(double re, double im, string format)
        {
            if (double.IsNaN(re) && double.IsNaN(im))
                return "<mtext class=\"err\">Undefined</mtext>";

            if (double.IsInfinity(re) && double.IsInfinity(im))
                return "<mtext class=\"err\">∞</mtext>";

            const string imaginary = "<mi mathvariant=\"normal\">i</mi>";
            var t = Complex.GetType(re, im);
            if (t == Complex.Types.Real)
                return FormatReal(re, format, false);

            if (t == Complex.Types.Imaginary)
                return FormatReal(im, format, false) + imaginary;

            if (phasor)
            {
                var abs = Math.Sqrt(re * re + im * im);
                var phase = Math.Atan2(im, re) * AngleFactors[degrees];
                return string.Concat(
                    FormatReal(abs, format, false),
                    "<mo>∠</mo>",
                    FormatReal(phase, format, false),
                    AngleUnits[degrees]);
            }
            return string.Concat(
                FormatReal(re, format, false),
                im < 0 ? "<mo>−</mo>" : "<mo>+</mo>",
                FormatReal(Math.Abs(im), format, false),
                imaginary);
        }

        internal override string FormatSwitch(string[] sa, int level = 0)
        {
            const string switchName = "<mi class=\"fn\" mathvariant=\"normal\">switch</mi>";
            var len = sa.Length;
            if (len == 1)
                return switchName + AddBrackets(sa[0], level);

            if (len == 2)
                return switchName + AddBrackets(string.Concat(sa[0], FormatOperator(';'), sa[1]), level);

            var sb = new StringBuilder("<mrow><mo symmetric=\"false\">{</mo><mtable class=\"m-cases\">")
                .Append(CaseRow("if", sa[0], sa[1]));
            for (int i = 2; i < len; i += 2)
            {
                if (len - i == 1)
                    sb.Append(ElseRow(sa[i]));
                else
                    sb.Append(CaseRow("else if", sa[i], sa[i + 1]));
            }
            return sb.Append("</mtable></mrow>").ToString();
        }

        internal override string CloseCurlyBrackets(string sa, int level) =>
            sa.EndsWith("</mtable></mrow>", StringComparison.Ordinal) ?
                string.Concat(sa[..^7], "<mo symmetric=\"false\">}</mo></mrow>") :
                sa;

        internal override string FormatIf(string sc, string sa, string sb, int level = 0) =>
            $"<mrow><mo symmetric=\"false\">{{</mo><mtable class=\"m-cases\">{CaseRow("if", sc, sa)}{ElseRow(sb)}</mtable></mrow>";

        private static string CaseRow(string keyword, string condition, string value) =>
            $"<mtr><mtd><mtext>{keyword} </mtext>{condition}<mo>:</mo></mtd><mtd>{value}</mtd></mtr>";

        private static string ElseRow(string value) =>
            $"<mtr><mtd><mtext>else:</mtext></mtd><mtd>{value}</mtd></mtr>";

        internal override string FormatMatrix(Matrix matrix)
        {
            var sb = new StringBuilder("<mrow><mo symmetric=\"false\">[</mo><mtable class=\"m-matrix\">");
            var nc = matrix.ColCount;
            var zeroThreshold = GetMaxVisibleMatrixValue(matrix, out int _, out int _) * 1e-14;
            if (matrix is not HpMatrix hp_m)
                hp_m = null;

            var units = hp_m?.Units;
            for (int i = 0, nr = matrix.RowCount; i < nr; ++i)
            {
                sb.Append("<mtr>");
                if (i == maxCount)
                {
                    sb.Insert(sb.Length, "<mtd><mo>⋮</mo></mtd>", Math.Min(maxCount, nc));
                    if (nc > maxCount)
                        sb.Append("<mtd><mo>⋱</mo></mtd><mtd><mo>⋮</mo></mtd>");

                    sb.Append("</mtr><mtr>");
                    i = nr - 1;
                }
                for (int j = 0; j < nc; ++j)
                {
                    if (j == maxCount)
                    {
                        sb.Append("<mtd><mo>⋯</mo></mtd>");
                        j = nc - 1;
                    }
                    string s;
                    if (hp_m is null)
                        s = FormatMatrixValue(matrix[i, j], zeroThreshold);
                    else
                    {
                        var d = hp_m.GetValue(i, j);
                        s = FormatReal(d, units?.FormatString, zeroSmallElements && Math.Abs(d) < zeroThreshold);
                    }
                    sb.Append("<mtd>").Append(s).Append("</mtd>");
                }
                sb.Append("</mtr>");
            }
            sb.Append("</mtable><mo symmetric=\"false\">]</mo></mrow>");
            if (units is not null)
                sb.Append(ThinSpaceElement).Append(units.MathMl);

            return sb.ToString();
        }

        internal override string FormatVector(Vector vector)
        {
            var sb = new StringBuilder();
            const double tol = 1e-14;
            var zeroThreshold = GetMaxVisibleVectorValue(vector) * tol;
            if (zeroThreshold > tol)
                zeroThreshold = tol;

            if (vector is not HpVector hp_v)
                hp_v = null;

            var units = hp_v?.Units;
            var len = vector.Length;
            for (int i = 0; i < len; ++i)
            {
                if (i > 0)
                    sb.Append(VectorSpace);

                if (i == maxCount)
                {
                    var n = len - maxCount;
                    sb.Append($"<mtext title=\"{n - Math.Sign(n - 1)} elements skipped.\">...</mtext>")
                        .Append(VectorSpace);
                    break;
                }
                AppendElement(i);
            }
            var last = len - 1;
            if (maxCount < last)
                AppendElement(last);

            var s = AddBrackets(sb.ToString(), 0, '[', ']');
            if (units is not null)
                s = string.Concat(s, ThinSpaceElement, units.MathMl);

            return s;

            void AppendElement(int index)
            {
                if (hp_v is null)
                    sb.Append(FormatMatrixValue(vector[index], zeroThreshold));
                else
                {
                    var d = hp_v.GetValue(index);
                    sb.Append(FormatReal(d, units?.FormatString, zeroSmallElements && Math.Abs(d) < zeroThreshold));
                }
            }
        }

        internal override string FormatMatrixValue(RealValue value, double zeroThreshold)
        {
            var d = value.D;
            var s = FormatReal(d, value.Units?.FormatString, zeroSmallElements && Math.Abs(d) < zeroThreshold);
            return value.Units is null ?
                s :
                string.Concat(s, ThinSpaceElement, value.Units.MathMl);
        }

        internal override string FormatBlock(string[] sa)
        {
            var sb = new StringBuilder("<mtable class=\"m-block\">");
            foreach (var s in sa)
                if (!string.IsNullOrEmpty(s))
                    sb.Append("<mtr><mtd>").Append(s).Append("</mtd></mtr>");

            return sb.Append("</mtable>").ToString();
        }

        // MathML drops text outside token elements.
        internal static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length + 32);
            var text = new StringBuilder();
            var depth = 0;
            var i = 0;
            var n = s.Length;
            while (i < n)
            {
                var c = s[i];
                if (c != '<')
                {
                    if (depth > 0)
                        sb.Append(c);
                    else
                        text.Append(c);

                    ++i;
                    continue;
                }
                var j = s.IndexOf('>', i);
                if (j < 0)
                    j = n - 1;

                if (depth == 0)
                    FlushText();

                var tag = s.AsSpan(i, j - i + 1);
                if (IsTokenTag(tag, out var isClosing))
                    depth += isClosing ? -1 : 1;

                sb.Append(tag);
                i = j + 1;
            }
            FlushText();
            return sb.ToString();

            void FlushText()
            {
                if (text.Length == 0)
                    return;

                var t = text.ToString();
                text.Clear();
                if (t.Contains('&'))
                    t = WebUtility.HtmlDecode(t);

                t = t.Trim();
                if (t.Length > 0)
                    sb.Append(Token(t));
            }
        }

        private static bool IsTokenTag(ReadOnlySpan<char> tag, out bool isClosing)
        {
            isClosing = tag.Length > 1 && tag[1] == '/';
            if (tag.EndsWith("/>"))
                return false;

            var name = tag[(isClosing ? 2 : 1)..];
            var end = name.IndexOfAny(" >\t\n");
            if (end >= 0)
                name = name[..end];

            return name is "mi" or "mn" or "mo" or "mtext" or "ms";
        }

        private static string Token(string s)
        {
            var e = WebUtility.HtmlEncode(s);
            if (IsNumber(s))
                return Mn(e);

            if (s.Length == 1)
                return char.IsLetter(s[0]) ? $"<mi>{e}</mi>" : $"<mo>{e}</mo>";

            return s.AsSpan().IndexOfAnyExcept("+−-=·,;:()[]{}|/∕…∈∠.×÷") < 0 ?
                $"<mo>{e}</mo>" :
                $"<mtext>{e}</mtext>";
        }

        private static bool IsNumber(string s)
        {
            var hasDigit = false;
            for (int i = 0; i < s.Length; ++i)
            {
                var c = s[i];
                if (char.IsDigit(c))
                    hasDigit = true;
                else if (!(c == '.' || c == ',' || i == 0 && c == '-'))
                    return false;
            }
            return hasDigit;
        }
    }
}
