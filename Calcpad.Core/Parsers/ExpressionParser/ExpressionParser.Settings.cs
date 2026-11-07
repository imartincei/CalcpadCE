using System;
using System.Collections.Generic;
using System.Numerics;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private interface ISettingBinding
        {
            void Apply(ExpressionParser p, SettingsDto dto);
            /// <summary>Copies the engine's current value into <paramref name="prior"/> if <paramref name="dto"/> sets this key.</summary>
            void Capture(ExpressionParser p, SettingsDto dto, SettingsDto prior);
        }

        private sealed class SettingBinding<T>(
            Func<SettingsDto, T?> get,
            Action<SettingsDto, T> set,
            Func<ExpressionParser, T> current,
            Action<ExpressionParser, T> apply) : ISettingBinding where T : struct
        {
            public void Apply(ExpressionParser p, SettingsDto dto)
            {
                if (get(dto) is T value)
                    apply(p, value);
            }

            public void Capture(ExpressionParser p, SettingsDto dto, SettingsDto prior)
            {
                if (get(dto).HasValue)
                    set(prior, current(p));
            }
        }

        private static SettingBinding<T> Plain<T>(
            Func<SettingsDto, T?> get, Action<SettingsDto, T> set,
            Func<Settings, T> read, Action<Settings, T> write) where T : struct =>
            new(get, set, p => read(p.Settings), (p, v) => write(p.Settings, v));

        // Settings mirrored by a special variable, which wins when assigned later.
        private static SettingBinding<bool> FlagVar(string variable,
            Func<SettingsDto, bool?> get, Action<SettingsDto, bool> set,
            Func<Settings, bool> read, Action<Settings, bool> write) =>
            new(get, set,
                p => p.SettingsValue(variable, read(p.Settings) ? 1d : 0d) != 0d,
                (p, v) =>
                {
                    write(p.Settings, v);
                    p._parser.SetVariable(variable, v ? 1d : 0d);
                });

        private static SettingBinding<T> NumberVar<T>(string variable,
            Func<SettingsDto, T?> get, Action<SettingsDto, T> set,
            Func<Settings, T> read, Action<Settings, T> write) where T : struct, INumber<T> =>
            new(get, set,
                p => T.CreateTruncating(p.SettingsValue(variable, double.CreateChecked(read(p.Settings)))),
                (p, v) =>
                {
                    write(p.Settings, v);
                    p._parser.SetVariable(variable, double.CreateChecked(v));
                });

        private static readonly Dictionary<SettingKey, ISettingBinding> SettingBindings = new()
        {
            [SettingKey.Decimals] = Plain(d => d.Decimals, (d, v) => d.Decimals = v,
                s => s.Math.Decimals, (s, v) => s.Math.Decimals = v),
            [SettingKey.Degrees] = new SettingBinding<int>(d => d.Degrees, (d, v) => d.Degrees = v,
                p => p._parser.Degrees,
                (p, v) =>
                {
                    p.Settings.Math.Degrees = v;
                    p._parser.Degrees = v;
                }),
            [SettingKey.ReturnAngleUnits] = FlagVar("ReturnAngleUnits", d => d.ReturnAngleUnits, (d, v) => d.ReturnAngleUnits = v,
                s => s.Math.ReturnAngleUnits, (s, v) => s.Math.ReturnAngleUnits = v),
            [SettingKey.Complex] = new SettingBinding<bool>(d => d.Complex, (d, v) => d.Complex = v,
                p => p.Settings.Math.IsComplex, (p, v) => p._parser.SetComplex(v)),
            [SettingKey.Substitute] = Plain(d => d.Substitute, (d, v) => d.Substitute = v,
                s => s.Math.Substitute, (s, v) => s.Math.Substitute = v),
            [SettingKey.FormatEquations] = Plain(d => d.FormatEquations, (d, v) => d.FormatEquations = v,
                s => s.Math.FormatEquations, (s, v) => s.Math.FormatEquations = v),
            [SettingKey.ZeroSmallMatrixElements] = Plain(d => d.ZeroSmallMatrixElements, (d, v) => d.ZeroSmallMatrixElements = v,
                s => s.Math.ZeroSmallMatrixElements, (s, v) => s.Math.ZeroSmallMatrixElements = v),
            [SettingKey.ShowHiddenOutput] = Plain(d => d.ShowHiddenOutput, (d, v) => d.ShowHiddenOutput = v,
                s => s.Math.ShowHiddenOutput, (s, v) => s.Math.ShowHiddenOutput = v),
            [SettingKey.MaxOutputCount] = Plain(d => d.MaxOutputCount, (d, v) => d.MaxOutputCount = v,
                s => s.Math.MaxOutputCount, (s, v) => s.Math.MaxOutputCount = v),
            [SettingKey.IsUs] = new SettingBinding<bool>(d => d.IsUs, (d, v) => d.IsUs = v,
                p => p._parser.IsUs,
                (p, v) =>
                {
                    p.Settings.IsUs = v;
                    p._parser.IsUs = v;
                }),
            [SettingKey.VectorGraphics] = FlagVar("PlotSVG", d => d.VectorGraphics, (d, v) => d.VectorGraphics = v,
                s => s.Plot.VectorGraphics, (s, v) => s.Plot.VectorGraphics = v),
            [SettingKey.ColorScale] = new SettingBinding<PlotSettings.ColorScales>(
                d => Enum.TryParse<PlotSettings.ColorScales>(d.ColorScale, true, out var cs) ? cs : null,
                (d, v) => d.ColorScale = v.ToString(),
                p => (PlotSettings.ColorScales)(int)p.SettingsValue("PlotPalette", (int)p.Settings.Plot.ColorScale),
                (p, v) =>
                {
                    p.Settings.Plot.ColorScale = v;
                    p._parser.SetVariable("PlotPalette", (int)v);
                }),
            [SettingKey.SmoothScale] = FlagVar("PlotSmooth", d => d.SmoothScale, (d, v) => d.SmoothScale = v,
                s => s.Plot.SmoothScale, (s, v) => s.Plot.SmoothScale = v),
            [SettingKey.Shadows] = FlagVar("PlotShadows", d => d.Shadows, (d, v) => d.Shadows = v,
                s => s.Plot.Shadows, (s, v) => s.Plot.Shadows = v),
            [SettingKey.AdaptivePlot] = FlagVar("PlotAdaptive", d => d.AdaptivePlot, (d, v) => d.AdaptivePlot = v,
                s => s.Plot.IsAdaptive, (s, v) => s.Plot.IsAdaptive = v),
            [SettingKey.PlotWidth] = NumberVar("PlotWidth", d => d.PlotWidth, (d, v) => d.PlotWidth = v,
                s => s.Plot.Width, (s, v) => s.Plot.Width = v),
            [SettingKey.PlotHeight] = NumberVar("PlotHeight", d => d.PlotHeight, (d, v) => d.PlotHeight = v,
                s => s.Plot.Height, (s, v) => s.Plot.Height = v),
            [SettingKey.PlotStep] = NumberVar("PlotStep", d => d.PlotStep, (d, v) => d.PlotStep = v,
                s => s.Plot.Step, (s, v) => s.Plot.Step = v),
            [SettingKey.Precision] = NumberVar("Precision", d => d.Precision, (d, v) => d.Precision = v,
                s => s.Math.Precision, (s, v) => s.Math.Precision = v),
            [SettingKey.Tol] = NumberVar("Tol", d => d.Tol, (d, v) => d.Tol = v,
                s => s.Math.Tol, (s, v) => s.Math.Tol = v),
        };

        private void ApplySetting(SettingKey key, SettingsDto dto) => SettingBindings[key].Apply(this, dto);

        private void CaptureSettings(SettingsDto dto, SettingsDto prior)
        {
            foreach (var binding in SettingBindings.Values)
                binding.Capture(this, dto, prior);
        }

        private double SettingsValue(string name, double fallback)
        {
            try
            {
                return _parser.GetSettingsVariable(name, fallback);
            }
            catch (MathParserException)
            {
                return fallback;
            }
        }
    }
}
