using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calcpad.Core
{
    public enum UiKey
    {
        Type,
        Mode,
        Style,
        ReportStyle,
        ForceUnits,
        AllowExpression,
        Rows,
        Columns,
        ColumnHeaders,
        RowHeaders,
        Width,
        RowHeaderWidth,
        ColumnWidths,
        Keys,
        Values
    }

    /// <summary>JSON payload of the <c>#UI {...}</c> directive.</summary>
    public sealed class UiDto : DirectiveDto<UiDto, UiKey>
    {
        public string Type { get; set; }
        public string Mode { get; set; }
        public string Style { get; set; }
        public string ReportStyle { get; set; }
        public bool? ForceUnits { get; set; }
        public bool? AllowExpression { get; set; }
        public int? Rows { get; set; }
        public int? Columns { get; set; }
        public string[] ColumnHeaders { get; set; }
        public string[] RowHeaders { get; set; }
        /// <summary>Total grid width in pixels, or a percentage of the line. Null means the grid's natural width.</summary>
        public JsonElement? Width { get; set; }
        public int? RowHeaderWidth { get; set; }
        public int[] ColumnWidths { get; set; }
        public string[] Keys { get; set; }
        public string[] Values { get; set; }

        /// <summary>The control types <see cref="ExpressionParser"/> knows how to render.</summary>
        public static readonly IReadOnlyList<string> KnownTypes =
            ["entry", "datagrid", "dropdown", "radio", "checkbox"];

        /// <summary>Most cells a datagrid may hold before it is rejected as too large.</summary>
        internal const int MaxSize = 100_000;

        /// <summary>True for the types whose choices come from the paired keys and values arrays.</summary>
        public bool HasOptions => Type is "dropdown" or "radio";

        /// <summary>An expression takes no appended unit, so allowExpression settles this.</summary>
        [JsonIgnore]
        public bool KeepsUnits => AllowExpression != true && ForceUnits != false;

        [JsonIgnore]
        public bool AllowsExpression => AllowExpression == true;

        /// <summary>The declared total width as a pixel count or a "75%" share of the line, null when undeclared.</summary>
        public string GetWidth()
        {
            if (Width is not { } w)
                return null;

            if (w.ValueKind == JsonValueKind.Number && w.TryGetDouble(out var d))
                return ((int)Math.Round(d)).ToString(CultureInfo.InvariantCulture);

            return w.ValueKind == JsonValueKind.String && TryGetPercent(w.GetString(), out var percent) ?
                percent.ToString(CultureInfo.InvariantCulture) + "%" :
                null;
        }

        /// <summary>"full" is the spelled out form of 100%.</summary>
        private static bool TryGetPercent(string s, out double percent)
        {
            percent = 100;
            if (s is null)
                return false;

            s = s.Trim();
            return s == "full" ||
                s.EndsWith('%') &&
                double.TryParse(s[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out percent) &&
                percent > 0;
        }

        protected override void Validate(List<DirectiveError<UiKey>> errors)
        {
            if (Type is not null && !KnownTypes.Contains(Type))
                errors.Add(new(UiKey.Type, string.Format(
                    Messages.The_UI_type_0_is_not_recognized_expected_one_of_1, Type, string.Join(", ", KnownTypes))));

            if (Mode is not null && !Mode.Equals("number", StringComparison.OrdinalIgnoreCase))
                errors.Add(new(UiKey.Mode, Messages.Only_numbers_are_supported_by_the_UI_keyword));

            // These types substitute the whole right hand side from the 'values' array already.
            if (Type is "dropdown" or "radio" or "checkbox")
            {
                if (ForceUnits is not null)
                    errors.Add(new(UiKey.ForceUnits, string.Format(Messages.The_UI_0_does_not_apply_to_1, "forceUnits", Type)));

                if (AllowExpression is not null)
                    errors.Add(new(UiKey.AllowExpression, string.Format(Messages.The_UI_0_does_not_apply_to_1, "allowExpression", Type)));
            }

            // An undeclared type is settled later, by ValidateResolvedType.
            if (Type is "entry" or "dropdown" or "radio" or "checkbox")
                ValidateGridProperties(errors);

            CheckNotNegative(errors, UiKey.Rows, "rows", Rows);
            CheckNotNegative(errors, UiKey.Columns, "columns", Columns);
            CheckNotNegative(errors, UiKey.RowHeaderWidth, "rowHeaderWidth", RowHeaderWidth);
            ValidateWidth(errors);

            if (HasOptions)
            {
                if (Keys is null || Values is null)
                    errors.Add(new(UiKey.Keys, string.Format(
                        Messages.The_UI_0_requires_both_keys_and_values_arrays, Type)));
                else if (Keys.Length != Values.Length)
                    errors.Add(new(UiKey.Keys, string.Format(
                        Messages.The_UI_0_keys_and_values_arrays_must_have_the_same_length, Type, Keys.Length, Values.Length)));
            }

            // An omitted size is auto-detected later, from a right hand side not seen here.
            CheckHeaderCount(errors, UiKey.ColumnHeaders, "columnHeaders", ColumnHeaders?.Length, Columns, "columns");
            CheckHeaderCount(errors, UiKey.RowHeaders, "rowHeaders", RowHeaders?.Length, Rows, "rows");
            CheckHeaderCount(errors, UiKey.ColumnWidths, "columnWidths", ColumnWidths?.Length, Columns, "columns");
        }

        /// <summary>The grid properties against the type the right hand side settled on.</summary>
        public IReadOnlyList<DirectiveError<UiKey>> ValidateResolvedType(string type)
        {
            var errors = new List<DirectiveError<UiKey>>();
            if (Type is null && type != "datagrid")
                ValidateGridProperties(errors);

            return errors;
        }

        /// <summary>Geometry and headers a control that is not a grid has nothing to do with.</summary>
        private void ValidateGridProperties(List<DirectiveError<UiKey>> errors)
        {
            CheckGridOnly(errors, UiKey.Rows, "rows", Rows);
            CheckGridOnly(errors, UiKey.Columns, "columns", Columns);
            CheckGridOnly(errors, UiKey.ColumnHeaders, "columnHeaders", ColumnHeaders);
            CheckGridOnly(errors, UiKey.RowHeaders, "rowHeaders", RowHeaders);
            CheckGridOnly(errors, UiKey.Width, "width", Width);
            CheckGridOnly(errors, UiKey.RowHeaderWidth, "rowHeaderWidth", RowHeaderWidth);
            CheckGridOnly(errors, UiKey.ColumnWidths, "columnWidths", ColumnWidths);
        }

        private static void CheckGridOnly<T>(List<DirectiveError<UiKey>> errors, UiKey key, string name, T value)
        {
            if (value is not null)
                errors.Add(new(key, string.Format(Messages.The_UI_0_only_applies_to_a_datagrid, name)));
        }

        private void ValidateWidth(List<DirectiveError<UiKey>> errors)
        {
            if (Width is not { } w)
                return;

            if (w.ValueKind == JsonValueKind.Number)
            {
                if (w.TryGetDouble(out var d) && d < 0)
                    errors.Add(new(UiKey.Width, string.Format(Messages.The_UI_0_must_not_be_negative, "width")));

                return;
            }
            if (w.ValueKind != JsonValueKind.String || !TryGetPercent(w.GetString(), out _))
                errors.Add(new(UiKey.Width, Messages.The_UI_width_must_be_a_number_or_a_percentage));
        }

        private static void CheckNotNegative(List<DirectiveError<UiKey>> errors, UiKey key, string name, int? value)
        {
            if (value < 0)
                errors.Add(new(key, string.Format(Messages.The_UI_0_must_not_be_negative, name)));
        }

        private static void CheckHeaderCount(List<DirectiveError<UiKey>> errors, UiKey key, string name, int? count, int? size, string sizeName)
        {
            if (count is null || size is null || count <= size)
                return;

            errors.Add(new(key, string.Format(
                Messages.The_UI_0_has_1_entries_but_the_grid_has_2_3, name, count.Value, size.Value, sizeName)));
        }
    }
}
