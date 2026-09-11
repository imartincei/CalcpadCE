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

    /// <summary>
    /// JSON payload of the <c>#UI {...}</c> directive.
    /// </summary>
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
        /// <summary>Total grid width in pixels, or "100%". Null means the grid's natural width.</summary>
        public JsonElement? Width { get; set; }
        public int? RowHeaderWidth { get; set; }
        public int[] ColumnWidths { get; set; }
        public string[] Keys { get; set; }
        public string[] Values { get; set; }

        /// <summary>The control types <see cref="ExpressionParser"/> knows how to render.</summary>
        public static readonly IReadOnlyList<string> KnownTypes =
            ["entry", "datagrid", "dropdown", "radio", "checkbox"];

        /// <summary>Maximum number of cells (rows × columns) in a datagrid before rejecting as too large for UI interaction.</summary>
        internal const int MaxSize = 100_000;

        /// <summary>True for the types whose choices come from the paired keys and values arrays.</summary>
        public bool HasOptions => Type is "dropdown" or "radio";

        /// <summary>
        /// A free expression cannot have a unit appended to it, so <see cref="AllowExpression"/>
        /// settles <see cref="ForceUnits"/> rather than combining with it.
        /// </summary>
        [JsonIgnore]
        public bool KeepsUnits => AllowExpression != true && ForceUnits != false;

        [JsonIgnore]
        public bool AllowsExpression => AllowExpression == true;

        /// <summary>The declared total width in pixels, -1 for "100%", or null when undeclared.</summary>
        public int? GetWidth()
        {
            if (Width is not { } w)
                return null;

            if (w.ValueKind == JsonValueKind.Number && w.TryGetDouble(out var d))
                return (int)Math.Round(d);

            return w.ValueKind == JsonValueKind.String && IsFullWidth(w.GetString()) ? -1 : null;
        }

        private static bool IsFullWidth(string s) =>
            s is not null && s.Trim() is "100%" or "full";

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

            // Only checked against a declared size: an omitted one is auto-detected later,
            // from the right hand side the payload cannot see.
            CheckHeaderCount(errors, UiKey.ColumnHeaders, "columnHeaders", ColumnHeaders?.Length, Columns, "columns");
            CheckHeaderCount(errors, UiKey.RowHeaders, "rowHeaders", RowHeaders?.Length, Rows, "rows");
            CheckHeaderCount(errors, UiKey.ColumnWidths, "columnWidths", ColumnWidths?.Length, Columns, "columns");
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
            if (w.ValueKind != JsonValueKind.String || !IsFullWidth(w.GetString()))
                errors.Add(new(UiKey.Width, Messages.The_UI_width_must_be_a_number_or_100_percent));
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
