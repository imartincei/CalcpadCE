using System;
using System.Collections.Generic;
using System.Linq;

namespace Calcpad.Core
{
    public enum UiKey
    {
        Type,
        Mode,
        Style,
        ReportStyle,
        Rows,
        Columns,
        ColumnHeaders,
        RowHeaders,
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
        public int? Rows { get; set; }
        public int? Columns { get; set; }
        public string[] ColumnHeaders { get; set; }
        public string[] RowHeaders { get; set; }
        public string[] Keys { get; set; }
        public string[] Values { get; set; }

        /// <summary>The control types <see cref="ExpressionParser"/> knows how to render.</summary>
        public static readonly IReadOnlyList<string> KnownTypes =
            ["entry", "datagrid", "dropdown", "radio", "checkbox"];

        /// <summary>True for the types whose choices come from the paired keys and values arrays.</summary>
        public bool HasOptions => Type is "dropdown" or "radio";

        protected override void Validate(List<DirectiveError<UiKey>> errors)
        {
            if (Type is not null && !KnownTypes.Contains(Type))
                errors.Add(new(UiKey.Type, string.Format(
                    Messages.The_UI_type_0_is_not_recognized_expected_one_of_1, Type, string.Join(", ", KnownTypes))));

            if (Mode is not null && !Mode.Equals("number", StringComparison.OrdinalIgnoreCase))
                errors.Add(new(UiKey.Mode, Messages.Only_numbers_are_supported_by_the_UI_keyword));

            CheckNotNegative(errors, UiKey.Rows, "rows", Rows);
            CheckNotNegative(errors, UiKey.Columns, "columns", Columns);

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
            CheckHeaderCount(errors, UiKey.ColumnHeaders, "columnHeaders", ColumnHeaders, Columns, "columns");
            CheckHeaderCount(errors, UiKey.RowHeaders, "rowHeaders", RowHeaders, Rows, "rows");
        }

        private static void CheckNotNegative(List<DirectiveError<UiKey>> errors, UiKey key, string name, int? value)
        {
            if (value < 0)
                errors.Add(new(key, string.Format(Messages.The_UI_0_must_not_be_negative, name)));
        }

        private static void CheckHeaderCount(List<DirectiveError<UiKey>> errors, UiKey key, string name, string[] headers, int? size, string sizeName)
        {
            if (headers is null || size is null || headers.Length <= size)
                return;

            errors.Add(new(key, string.Format(
                Messages.The_UI_0_has_1_entries_but_the_grid_has_2_3, name, headers.Length, size.Value, sizeName)));
        }
    }
}
