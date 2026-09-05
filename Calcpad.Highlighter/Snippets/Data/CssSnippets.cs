using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for CSS blocks that restyle the rendered report and the
    /// <c>#UI</c> form controls. UI-only, so they are excluded from the linter.
    /// </summary>
    public static class CssSnippets
    {
        // Joined explicitly rather than written as a raw string literal, so the inserted
        // text keeps LF newlines no matter what line endings the source file is checked
        // out with.
        private static string Lines(params string[] lines) => string.Join('\n', lines);

        // Every block is wrapped in #val ... #end val: without it each comment line is
        // wrapped in its own <p>, which lands inside the <style> element and invalidates
        // the rules around it. #end val restores whatever output mode was in effect, where
        // #equ would force equation mode. Font and class names use double quotes because a
        // single quote closes the comment and drops the rest of the line into equation mode.
        private const string StyleOpen = "'<style>";
        private const string StyleClose = "'</style>";

        private static readonly string ReportFonts = Lines(
            "#val",
            StyleOpen,
            "'  /* Base size for body text and equations. Everything in % scales with this. */",
            "'  body {",
            "'    font-size: 11pt;",
            "'  }",
            "'  .eq, input[type=\"text\"], table.matrix,",
            "'  .eq small var, .eq small i {",
            "'    font-family: \"Georgia Pro\", \"Century Schoolbook\", \"Times New Roman\", Times, serif;",
            "'  }",
            "'  .eq var { font-size: 11.5pt; }      /* variable names */",
            "'  .eq i { font-size: 10pt; }          /* units */",
            "'  .eq sub { font-size: 85%; }         /* subscripts */",
            "'  .eq small { font-size: 70%; }       /* n-ary limits */",
            "'  .eq small var { font-size: 8.5pt; }",
            "'  .eq small i { font-size: 6pt; }",
            "'  .matrix .td { font-size: 10pt; }    /* matrix cells */",
            "'  input[type=\"text\"] { font-size: 10pt; }",
            "'  .nary {",
            "'    font-family: \"Georgia Pro Light\", \"Georgia Pro\", serif;",
            "'    font-weight: 300;",
            "'    font-size: 240%;",
            "'  }",
            StyleClose,
            "#end val");

        private static readonly string UiAllControls = Lines(
            "#val",
            StyleOpen,
            "'  .calcpad-ui-input.highlight { background-color: #eeeeee; border: 1px solid #aaaaaa; }",
            "'  .calcpad-ui-dropdown.primary { font-weight: 600; }",
            "'  .calcpad-ui-radio.compact .calcpad-ui-radio-label { margin-right: 4px; }",
            "'  .calcpad-ui-checkbox.switch { accent-color: #2a8f3f; }",
            "'  .calcpad-ui-datagrid.bordered { border: 2px solid #444444; }",
            "'  p.boxed { border: 1px solid #cccccc; padding: 2px 4px; }",
            StyleClose,
            "#end val");

        private static string UiControl(string rule) => Lines("#val", StyleOpen, "'  " + rule, StyleClose, "#end val");

        private static readonly string UiEntry =
            UiControl(".calcpad-ui-input.highlight { background-color: #eeeeee; border: 1px solid #aaaaaa; }");

        private static readonly string UiDropdown =
            UiControl(".calcpad-ui-dropdown.primary { font-weight: 600; }");

        private static readonly string UiRadio =
            UiControl(".calcpad-ui-radio.compact .calcpad-ui-radio-label { margin-right: 4px; }");

        private static readonly string UiCheckbox =
            UiControl(".calcpad-ui-checkbox.switch { accent-color: #2a8f3f; }");

        private static readonly string UiDatagrid =
            UiControl(".calcpad-ui-datagrid.bordered { border: 2px solid #444444; }");

        private static readonly string UiReportLine =
            UiControl("p.boxed { border: 1px solid #cccccc; padding: 2px 4px; }");

        private const string StyleNote =
            "Pair it with a `style` class on the directive, which applies in Input mode only:\n\n" +
            "`#UI {\"style\": \"highlight\"} depth = 2m`\n\n" +
            "Combine the base class with your own so the rule only hits the controls you marked — " +
            "`.calcpad-ui-input.highlight`, not `.highlight`. Several classes can be listed at once: " +
            "`\"style\": \"highlight wide\"`.";

        public static readonly SnippetItem[] Items =
        [
            new SnippetItem
            {
                Insert = ReportFonts,
                Label = "Report Fonts",
                Description = "Override the report font family and sizes",
                Documentation =
                    "Restyles the rendered output, seeded with the template's stock sizes so it is a " +
                    "no-op until a number is changed.\n\n" +
                    "`body` is the master size control: `.eq` has no size of its own and inherits it, " +
                    "and `.eq sub`, `.eq small` and `.nary` are percentages that follow it. The `pt` " +
                    "entries do not — variable names, units, matrix cells and input fields are fixed " +
                    "in the template and have to be moved separately. Headings are sized in `em` off " +
                    "`body`, so they already scale and are not listed.\n\n" +
                    "The family is set to Georgia Pro, which is not bundled and must be installed on " +
                    "the machine viewing the report.",
                Category = "CSS"
            },
            new SnippetItem
            {
                Insert = UiAllControls,
                Label = "UI Controls - All",
                Description = "Style sheet covering every #UI control type",
                Documentation =
                    "One rule per control base class, as a starting point.\n\n" +
                    "| Type | Element | Base class |\n" +
                    "|---|---|---|\n" +
                    "| `entry` | `<input type=\"text\">` | `calcpad-ui-input` |\n" +
                    "| `dropdown` | `<select>` | `calcpad-ui-dropdown` |\n" +
                    "| `radio` | `<span>` | `calcpad-ui-radio`, labels `calcpad-ui-radio-label` |\n" +
                    "| `checkbox` | `<input type=\"checkbox\">` | `calcpad-ui-checkbox` |\n" +
                    "| `datagrid` | `<div>` | `calcpad-ui-datagrid` |\n\n" +
                    StyleNote + "\n\n" +
                    "`p.boxed` is for `reportStyle`, which lands on the line's paragraph everywhere " +
                    "the line is not a control — Preview, Report and every export but the input form.",
                Category = "CSS"
            },
            new SnippetItem
            {
                Insert = UiEntry,
                Label = "UI Entry Box",
                Description = "Style an #UI entry control",
                Documentation = "Targets `<input type=\"text\">` controls.\n\n" + StyleNote,
                Category = "CSS"
            },
            new SnippetItem
            {
                Insert = UiDropdown,
                Label = "UI Dropdown",
                Description = "Style an #UI dropdown control",
                Documentation =
                    "Targets `<select>` controls.\n\n" +
                    "`#UI {\"type\": \"dropdown\", \"style\": \"primary\", " +
                    "\"keys\": [\"Low\", \"High\"], \"values\": [\"1\", \"2\"]} g = 1`",
                Category = "CSS"
            },
            new SnippetItem
            {
                Insert = UiRadio,
                Label = "UI Radio Group",
                Description = "Style an #UI radio control",
                Documentation =
                    "The class lands on the wrapping `<span>`; each button's `<label>` carries " +
                    "`calcpad-ui-radio-label`, so spacing is set through a descendant selector.\n\n" +
                    "`#UI {\"type\": \"radio\", \"style\": \"compact\", " +
                    "\"keys\": [\"Steel\", \"Concrete\"], \"values\": [\"200GPa\", \"25GPa\"]} E = 200GPa`",
                Category = "CSS"
            },
            new SnippetItem
            {
                Insert = UiCheckbox,
                Label = "UI Checkbox",
                Description = "Style an #UI checkbox control",
                Documentation =
                    "Targets `<input type=\"checkbox\">` controls.\n\n" +
                    "`#UI {\"type\": \"checkbox\", \"style\": \"switch\"} flag = 1`",
                Category = "CSS"
            },
            new SnippetItem
            {
                Insert = UiDatagrid,
                Label = "UI Datagrid",
                Description = "Style the container of an #UI datagrid",
                Documentation =
                    "The class reaches the grid's outer `<div>` only. A grid is a third-party " +
                    "widget, so its cells, headers and context menu are styled by a stylesheet " +
                    "that ships with the application, not from the document — see *Customizing " +
                    "the `#UI` Datagrid* in `DEVELOPER.md`. Column widths and overall grid size " +
                    "are set by the preview script and are not adjustable from CSS at all.\n\n" +
                    "`#UI {\"type\": \"datagrid\", \"style\": \"bordered\"} T = [1; 2 | 3; 4]`",
                Category = "CSS"
            },
            new SnippetItem
            {
                Insert = UiReportLine,
                Label = "UI Report Line",
                Description = "Style a #UI line outside the input form",
                Documentation =
                    "For `reportStyle`, which is the counterpart to `style`: it lands on the " +
                    "element wrapping the line everywhere the line is *not* a control — Preview, " +
                    "Report and every export but the input form. That element is a paragraph, so " +
                    "target it as `p.boxed`.\n\n" +
                    "`#UI {\"reportStyle\": \"boxed\"} P = 25kN`",
                Category = "CSS"
            },
        ];
    }
}
