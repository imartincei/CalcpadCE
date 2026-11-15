using System;

namespace Calcpad.Core
{
    internal class ChartParser : PlotParser
    {

        private Func<IValue>[] _fx, _fy;
        internal ChartParser(MathParser parser, PlotSettings settings) : base(parser, settings) { }

        internal override string Parse(ReadOnlySpan<char> script, bool calculate)
        {
            const int n = 4;
            ReadOnlySpan<char> delimiters = ['{', '@', '=', ':', '}'];
            var input = Split(script, delimiters, out var index);
            if (index <= 4)
                throw Exceptions.MissingPlotDelimiter(delimiters[index], script.ToString());

            for (int j = 0; j < n; ++j)
                if (string.IsNullOrWhiteSpace(input[j]))
                    throw Exceptions.MissingPlotItem(Parts[j], script.ToString());

            Variables.Add(input[1].Trim());
            string result;
            if (calculate)
            {
                var charts = Split(input[0], Offsets[0], '&');
                input[1] = input[1].Trim();
                Parameter[] parameters = [new(input[1])];
                var count = charts.Length;
                _fx = new Func<IValue>[count];
                _fy = new Func<IValue>[count];
                for (int i = 0; i < count; ++i)
                {
                    var xy = Split(charts[i].Text, charts[i].Offset, '|');
                    if (xy.Length > 2)
                        throw Exceptions.MultiplePlotDelimiters(script.ToString());
                    else if (xy.Length > 0)
                    {
                        if (xy.Length == 1)
                            _fy[i] = Compile(xy[0].Text, xy[0].Offset, parameters);
                        else
                        {
                            _fx[i] = Compile(xy[0].Text, xy[0].Offset, parameters);
                            _fy[i] = Compile(xy[1].Text, xy[1].Offset, parameters);
                        }
                    }
                }
                var left = Calculate(input[2], Offsets[2], out var leftUnits);
                var right = Calculate(input[3], Offsets[3], out var rightUnits);
                if (!Unit.IsConsistent(leftUnits, rightUnits))
                    throw Exceptions.InconsistentUnits(Unit.GetText(leftUnits), Unit.GetText(rightUnits));

                if (leftUnits is not null)
                {
                    var factor = rightUnits.ConvertTo(leftUnits);
                    right *= factor;
                }
                result = GetHtmlImage(parameters[0].Variable, left, right, leftUnits);
            }
            else
                result = GetHtmlText(input);

            return result;
        }

        private string GetHtmlImage(Variable var, double left, double right, Unit u)
        {
            var chartPlotter = new ChartPlotter(Parser, Settings);
            var plotResult = chartPlotter.Plot(_fx, _fy, var, left, right, u);
            return plotResult;
        }

        private string GetHtmlText(string[] input)
        {
            var charts = Split(input[0], Offsets[0], '&');
            var html = new string[charts.Length];
            input[1] = input[1].Trim();
            for (int i = 0; i < charts.Length; ++i)
            {
                var xy = Split(charts[i].Text, charts[i].Offset, '|');
                if (xy.Length > 2)
                    throw Exceptions.MultiplePlotDelimiters(input[0]);

                string sx, sy;
                if (xy.Length == 1)
                {
                    sx = ToHtml(input[1], Offsets[1]);
                    sy = ToHtml(xy[0].Text, xy[0].Offset);
                }
                else
                {
                    sx = ToHtml(xy[0].Text, xy[0].Offset);
                    sy = ToHtml(xy[1].Text, xy[1].Offset);
                }
                html[i] = string.Concat(sx, " | ", sy);
            }
            input[0] = string.Join(" & ", html);
            for (int i = 1, len = input.Length; i < len; ++i)
            {
                input[i] = ToHtml(input[i], Offsets[i]);
            }
            return string.Concat("<span class = \"eq\"><span class=\"cond\">$Plot</span>{", input[0], " @ ", input[1], " ∈ [", input[2], "; ", input[3] + "]}</span>");
        }
    }
}
