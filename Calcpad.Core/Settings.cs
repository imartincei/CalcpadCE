using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Calcpad.Core
{
    public enum SettingKey
    {
        Decimals,
        Degrees,
        Complex,
        Substitute,
        FormatEquations,
        ZeroSmallMatrixElements,
        MaxOutputCount,
        Units,
        IsUs,
        VectorGraphics,
        ColorScale,
        SmoothScale,
        Shadows,
        AdaptivePlot,
        PlotWidth,
        PlotHeight,
        PlotStep,
        Precision,
        Tol
    }

    public readonly record struct SettingError(SettingKey Key, string Message);

    /// <summary>
    /// JSON payload of the <c>#settings {...}</c> directive.
    /// </summary>
    public sealed class SettingsDto
    {
        public int? Decimals { get; set; }
        public int? Degrees { get; set; }
        public bool? Complex { get; set; }
        public bool? Substitute { get; set; }
        public bool? FormatEquations { get; set; }
        public bool? ZeroSmallMatrixElements { get; set; }
        public int? MaxOutputCount { get; set; }
        public string Units { get; set; }
        public bool? IsUs { get; set; }
        public bool? VectorGraphics { get; set; }
        public string ColorScale { get; set; }
        public bool? SmoothScale { get; set; }
        public bool? Shadows { get; set; }
        public bool? AdaptivePlot { get; set; }
        public int? PlotWidth { get; set; }
        public int? PlotHeight { get; set; }
        public int? PlotStep { get; set; }
        public double? Precision { get; set; }
        public double? Tol { get; set; }

        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        /// <summary>Recognized keys, derived from <see cref="SettingKey"/> (case-insensitive).</summary>
        public static readonly IReadOnlySet<string> KnownKeys =
            new HashSet<string>(Enum.GetNames(typeof(SettingKey)), StringComparer.OrdinalIgnoreCase);

        /// <summary>Deserializes a <c>#settings</c> JSON payload. Throws <see cref="JsonException"/> on malformed JSON or a wrong value type.</summary>
        public static SettingsDto Parse(string json) => JsonSerializer.Deserialize<SettingsDto>(json, Options);

        /// <summary>
        /// Returns an entry for every provided value that is outside its allowed range.
        /// Bounds mirror how <c>Calcpad.Core</c> constrains each value: <c>decimals</c> and
        /// <c>maxOutputCount</c> match their setter clamps, <c>precision</c>/<c>tol</c> match the
        /// solver's read clamp, <c>degrees</c> is a hard array index (0..2), and the plot
        /// dimensions only require a positive size (Core imposes no upper bound).
        /// </summary>
        public IReadOnlyList<SettingError> Validate()
        {
            var errors = new List<SettingError>();
            CheckRange(errors, SettingKey.Decimals, "decimals", Decimals, 0, 15);
            CheckRange(errors, SettingKey.Degrees, "degrees", Degrees, 0, 2);
            CheckRange(errors, SettingKey.MaxOutputCount, "maxOutputCount", MaxOutputCount, 5, 100);
            CheckRange(errors, SettingKey.PlotWidth, "plotWidth", PlotWidth, 1, null);
            CheckRange(errors, SettingKey.PlotHeight, "plotHeight", PlotHeight, 1, null);
            CheckRange(errors, SettingKey.PlotStep, "plotStep", PlotStep, 0, null);
            CheckRange(errors, SettingKey.Precision, "precision", Precision, 1e-15, 1e-2);
            CheckRange(errors, SettingKey.Tol, "tol", Tol, 1e-15, 1e-2);
            if (ColorScale is not null && !Enum.TryParse<PlotSettings.ColorScales>(ColorScale, true, out _))
                errors.Add(new(SettingKey.ColorScale, $"'colorScale' must be one of: {string.Join(", ", Enum.GetNames(typeof(PlotSettings.ColorScales)))}"));
            return errors;
        }

        private static void CheckRange<T>(List<SettingError> errors, SettingKey key, string name, T? value, double min, double? max) where T : struct, IConvertible
        {
            if (value is null)
                return;
            var d = value.Value.ToDouble(null);
            if (d < min || max.HasValue && d > max.Value)
                errors.Add(new(key, max.HasValue
                    ? $"'{name}' must be between {min} and {max.Value}"
                    : $"'{name}' must be at least {min}"));
        }
    }

    [Serializable()]
    public class Settings
    {
        public MathSettings Math { get; set; } = new();
        public PlotSettings Plot { get; set; } = new();
        public string Units { get; set; } = "m";
        public bool IsUs { get; set; }
    }

    [Serializable()]
    public class MathSettings
    {
        private int _decimals;
        private int _maxOutputCount;
        public int Decimals
        {
            get => _decimals;
            set
            {
                _decimals = value switch
                {
                    <= 0 => 0,
                    >= 15 => 15,
                    _ => value
                };
            }
        }
        public int Degrees { get; set; }
        public bool IsComplex { get; set; }
        public bool Substitute { get; set; }
        public bool FormatEquations { get; set; }
        public bool ZeroSmallMatrixElements { get; set; }
        public int MaxOutputCount
        {
            get => _maxOutputCount;
            set
            {
                _maxOutputCount = value switch
                {
                    <= 5 => 5,
                    >= 100 => 100,
                    _ => value
                };
            }
        }
        public string FormatString { get; set; }
        public double Precision { get; set; }
        public double Tol { get; set; }

        public MathSettings()
        {
            Decimals = 2;
            Degrees = 0;
            IsComplex = false;
            Substitute = true;
            FormatEquations = true;
            ZeroSmallMatrixElements = true;
            MaxOutputCount = 20;
            Precision = 1e-14;
            Tol = 1e-6;
        }
    }

    [Serializable()]
    public class PlotSettings
    {
        private bool _shadows;
        public bool IsAdaptive { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Step { get; set; }
        public double ScreenScaleFactor { get; set; } = 2.0;
        // Obsolete: WPF-only, to be removed later.
        public string ImagePath { get; set; }
        public string ImageUri { get; set; }
        public bool VectorGraphics { get; set; }
        public ColorScales ColorScale { get; set; }
        public bool SmoothScale { get; set; }
        public bool Shadows
        {
            set => _shadows = value;
            get => _shadows && ColorScale != ColorScales.Gray || ColorScale == ColorScales.None;
        }
        public LightDirections LightDirection { get; set; }

        public enum LightDirections
        {
            North,
            NorthEast,
            East,
            SouthEast,
            South,
            SouthWest,
            West,
            NorthWest
        }

        public enum ColorScales
        {
            None,
            Gray,
            Rainbow,
            Terrain,
            VioletToYellow,
            GreenToYellow,
            Blues,
            BlueToYellow,
            BlueToRed,
            PurpleToYellow,
        }

        public PlotSettings()
        {
            IsAdaptive = true;
            Width = 500;
            Height = 300;
            Step = 0;
            ImagePath = string.Empty;
            ImageUri = string.Empty;
            VectorGraphics = false;
            ColorScale = ColorScales.Rainbow;
            SmoothScale = false;
            Shadows = true;
            LightDirection = LightDirections.NorthWest;
        }
    }
}
