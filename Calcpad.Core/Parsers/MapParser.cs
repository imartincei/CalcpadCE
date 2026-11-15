using System;

namespace Calcpad.Core
{
    internal class MapParser : PlotParser
    {
        private Func<IValue> _function;
        private PlotSettings.ColorScales[] _colorScales = Enum.GetValues<PlotSettings.ColorScales>();
        private PlotSettings.LightDirections[] _lightDirections = Enum.GetValues<PlotSettings.LightDirections>();
        internal MapParser(MathParser parser, PlotSettings settings) : base(parser, settings) { }

        internal override string Parse(ReadOnlySpan<char> script, bool calculate)
        {
            const int n = 7;
            ReadOnlySpan<char> delimiters = ['{', '@', '=', ':', '&', '=', ':', '}'];
            string result;
            var input = Split(script, delimiters, out var index);
            if (index <= n)
                throw Exceptions.MissingMapDelimiter(delimiters[index], script.ToString());

            for (int i = 0; i < n; ++i)
            {
                if (string.IsNullOrWhiteSpace(input[i]))
                {
                    var part = i;
                    if (part > 3)
                        part -= 3;

                    throw Exceptions.MissingMapItem(Parts[part]);
                }
            }
            input[1] = input[1].Trim();
            input[4] = input[4].Trim();
            Variables.Add(input[1]);
            Variables.Add(input[4]);
            if (calculate)
            {
                var startX = Calculate(input[2], Offsets[2], out var xUnits);
                var endX = Calculate(input[3], Offsets[3], out var endUnits);
                if (!Unit.IsConsistent(xUnits, endUnits))
                    throw Exceptions.InconsistentUnits(Unit.GetText(xUnits), Unit.GetText(endUnits));

                if (endUnits is not null)
                {
                    var factor = endUnits.ConvertTo(xUnits);
                    endX *= factor;
                }
                var startY = Calculate(input[5], Offsets[5], out var yUnits);
                var endY = Calculate(input[6], Offsets[6], out endUnits);
                if (!Unit.IsConsistent(yUnits, endUnits))
                    throw Exceptions.InconsistentUnits(Unit.GetText(yUnits), Unit.GetText(endUnits));

                if (endUnits is not null)
                {
                    var factor = endUnits.ConvertTo(yUnits);
                    endY *= factor;
                }
                if (startX.AlmostEquals(endX) || startY.AlmostEquals(endY))
                    throw Exceptions.PlotLimitsIdentical();

                Parameter[] parameters =
                [
                    new(input[1]),
                    new(input[4])
                ];
                _function = Compile(input[0], Offsets[0], parameters);
                result = GetHtmlImage(parameters[0].Variable, parameters[1].Variable, startX, endX, startY, endY, xUnits, yUnits);
            }
            else
                result = GetHtmlText(input);

            return result;
        }

        private string GetHtmlImage(Variable varX, Variable varY, double startX, double endX, double startY, double endY, Unit xUnits, Unit yUnits)
        {
            int colorScale = (int)Parser.GetSettingsVariable("PlotPalette", (int)Settings.ColorScale);
            Settings.ColorScale = _colorScales[Math.Clamp(colorScale, 0, _colorScales.Length - 1)];
            Settings.Shadows = (int)Parser.GetSettingsVariable("PlotShadows", Settings.Shadows ? 1 : 0) != 0;
            Settings.SmoothScale = (int)Parser.GetSettingsVariable("PlotSmooth", Settings.SmoothScale ? 1 : 0) != 0;
            int lightDirection = (int)Parser.GetSettingsVariable("PlotLightDir", (int)Settings.LightDirection);
            Settings.LightDirection = _lightDirections[Math.Clamp(lightDirection, 0, _lightDirections.Length - 1)];
            return new MapPlotter(Parser, Settings).Plot(_function, varX, varY, startX, endX, startY, endY, xUnits, yUnits);
        }

        private string GetHtmlText(string[] input)
        {
            for (int i = 0, len = input.Length; i < len; ++i)
            {
                input[i] = ToHtml(input[i], Offsets[i]);
            }
            return string.Concat("<span class = \"eq\"><span class=\"cond\">$Map</span>", '{', input[0], " @ ", input[1], " ∈ [", input[2], "; ", input[3], "]}</span>");
        }

    }
}
