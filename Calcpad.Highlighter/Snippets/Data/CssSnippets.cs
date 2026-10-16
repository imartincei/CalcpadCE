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

        // Seeded with the template's stock values, so it is a no-op until one is changed.
        private static readonly string ReportTable = Lines(
            "#val",
            StyleOpen,
            "'  table.bordered {",
            "'    margin-top: 1em;",
            "'    margin-bottom: 0.5em;",
            "'  }",
            "'  table.bordered th {",
            "'    background-color: #F0F0F0;",
            "'    border: solid 1pt #AAAAAA;",
            "'    padding: 4pt 8pt;",
            "'    min-width: 2em;",
            "'    text-align: center;",
            "'  }",
            "'  table.bordered td {",
            "'    border: solid 1pt #CCCCCC;",
            "'    padding: 4pt 8pt;",
            "'    min-width: 2em;",
            "'    height: 1.6em;",
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

        // Markdown blocks render through Markdig, whose output the report template barely
        // styles. Tables are scoped by thead/tbody, which Calcpad's own tables never emit.
        private static readonly string MdBlockquote = Lines(
            "#val",
            StyleOpen,
            "'  blockquote {",
            "'    margin: 0.8em 0;",
            "'    padding: 2px 12px;",
            "'    border-left: 3px solid #9999aa;",
            "'    color: #444455;",
            "'  }",
            StyleClose,
            "#end val");

        private static readonly string MdCode = Lines(
            "#val",
            StyleOpen,
            "'  pre {",
            "'    background: #f6f6f6;",
            "'    border: 1pt solid #dddddd;",
            "'    padding: 6px 10px;",
            "'    overflow-x: auto;",
            "'  }",
            "'  code { background: #f2f2f2; padding: 0 2px; }",
            "'  pre code { background: none; padding: 0; }",
            StyleClose,
            "#end val");

        private static readonly string MdTable = Lines(
            "#val",
            StyleOpen,
            "'  thead th {",
            "'    background: #f0f0f0;",
            "'    border: 1pt solid #aaaaaa;",
            "'    padding: 4px 8px;",
            "'    text-align: left;",
            "'  }",
            "'  tbody td { border: 1pt solid #cccccc; padding: 4px 8px; }",
            StyleClose,
            "#end val");

        private static readonly string MdTaskList = Lines(
            "#val",
            StyleOpen,
            "'  ul.contains-task-list { list-style: none; padding-left: 1.2em; }",
            "'  li.task-list-item { margin: 2px 0; }",
            "'  li.task-list-item input[type=\"checkbox\"] { margin-right: 6px; }",
            StyleClose,
            "#end val");

        private static readonly string MdEmphasis = Lines(
            "#val",
            StyleOpen,
            "'  ins { text-decoration: underline; text-decoration-color: #44aa77; }",
            "'  del { color: #888888; }",
            "'  hr { border: 0; border-top: 1pt solid #cccccc; margin: 1em 0; }",
            StyleClose,
            "#end val");

        private static readonly string MdAll = Lines(
            "#val",
            StyleOpen,
            "'  blockquote {",
            "'    margin: 0.8em 0;",
            "'    padding: 2px 12px;",
            "'    border-left: 3px solid #9999aa;",
            "'    color: #444455;",
            "'  }",
            "'  pre { background: #f6f6f6; border: 1pt solid #dddddd; padding: 6px 10px; overflow-x: auto; }",
            "'  code { background: #f2f2f2; padding: 0 2px; }",
            "'  pre code { background: none; padding: 0; }",
            "'  thead th { background: #f0f0f0; border: 1pt solid #aaaaaa; padding: 4px 8px; text-align: left; }",
            "'  tbody td { border: 1pt solid #cccccc; padding: 4px 8px; }",
            "'  ul.contains-task-list { list-style: none; padding-left: 1.2em; }",
            "'  li.task-list-item input[type=\"checkbox\"] { margin-right: 6px; }",
            "'  ins { text-decoration: underline; text-decoration-color: #44aa77; }",
            "'  del { color: #888888; }",
            "'  hr { border: 0; border-top: 1pt solid #cccccc; margin: 1em 0; }",
            StyleClose,
            "#end val");

        private const string MarkdownNote =
            "Inside an `#html` or `#markdown` block, write the `<style>` element directly — no " +
            "`\'` quotes and no `#val` wrapper. Those are only needed in Calcpad mode, where an " +
            "unquoted line is an expression.\n\n" +
            "Rules apply to the whole report, so a `.dark-theme` counterpart is needed for anything " +
            "that sets a colour — see the template for the convention.";

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
                Insert = ReportTable,
                Label = "Report Table",
                Description = "Restyle the bordered report table",
                Documentation =
                    "For tables written by hand with `class=\"bordered\"` — the *HTML Table* snippet " +
                    "inserts the skeleton. Seeded with the template\'s stock values, so it is a no-op " +
                    "until a number is changed.\n\n" +
                    "The class is not decoration only: Word export reads it to decide whether the " +
                    "table keeps its borders, so dropping it changes the `.docx` as well as the " +
                    "report.\n\n" +
                    "Markdown tables are a separate case — Markdig gives them no class, so they are " +
                    "reached through `thead`/`tbody` instead. See *Markdown Table*.",
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
            new SnippetItem
            {
                Insert = MdAll,
                Label = "Markdown Elements - All",
                Description = "Style sheet covering what #markdown renders",
                Documentation =
                    "The report template styles headings, paragraphs and Calcpad\'s own output, but " +
                    "leaves most of what Markdown produces on browser defaults. This covers the lot.\n\n" +
                    "| Markdown | Element |\n" +
                    "|---|---|\n" +
                    "| `> quote` | `blockquote > p` |\n" +
                    "| fenced block | `pre > code.language-*` |\n" +
                    "| `` `code` `` | `code` |\n" +
                    "| `---` | `hr` |\n" +
                    "| `- [x] item` | `ul.contains-task-list > li.task-list-item > input` |\n" +
                    "| pipe table | `table > thead > th`, `tbody > td` |\n" +
                    "| `++ins++`, `~~del~~` | `ins`, `del` |\n\n" +
                    "Tables are reached through `thead`/`tbody`, which Calcpad\'s own tables never " +
                    "emit, so matrices and `#UI` datagrids are left alone.\n\n" +
                    MarkdownNote,
                Category = "CSS"
            },
            new SnippetItem
            {
                Insert = MdBlockquote,
                Label = "Markdown Blockquote",
                Description = "Style a > blockquote",
                Documentation =
                    "Markdig wraps the text in a paragraph, so the element is `blockquote > p`. The " +
                    "template has no rule of its own, so without this a quote is an indent and " +
                    "nothing else.\n\n" + MarkdownNote,
                Category = "CSS"
            },
            new SnippetItem
            {
                Insert = MdCode,
                Label = "Markdown Code Block",
                Description = "Style fenced code blocks and inline code",
                Documentation =
                    "A fenced block is `<pre><code class=\"language-xxx\">`, taking the language from " +
                    "the fence. The template sizes `code` at 9pt but gives it no background, so the " +
                    "`pre code` rule clears the inline background inside a block.\n\n" +
                    "Highlighting is not applied — the class is there for a stylesheet to target, " +
                    "not a highlighter.\n\n" + MarkdownNote,
                Category = "CSS"
            },
            new SnippetItem
            {
                Insert = MdTable,
                Label = "Markdown Table",
                Description = "Add borders to a pipe table",
                Documentation =
                    "The template sets `border-collapse: collapse` and padding but no borders, so a " +
                    "pipe table renders borderless.\n\n" +
                    "Scoped through `thead`/`tbody` rather than `table`, because Calcpad\'s own " +
                    "matrices and `#UI` datagrids are tables too and emit neither.\n\n" + MarkdownNote,
                Category = "CSS"
            },
            new SnippetItem
            {
                Insert = MdTaskList,
                Label = "Markdown Task List",
                Description = "Style - [x] task lists",
                Documentation =
                    "Markdig marks the list `contains-task-list` and each item `task-list-item`, then " +
                    "emits a disabled `<input type=\"checkbox\">`. Dropping the bullet is what makes " +
                    "the box read as the marker.\n\n" +
                    "The boxes are always disabled — a rendered report is not a form. Use `#UI` with " +
                    "`\"type\": \"checkbox\"` for one the reader can tick.\n\n" + MarkdownNote,
                Category = "CSS"
            },
            new SnippetItem
            {
                Insert = MdEmphasis,
                Label = "Markdown Emphasis Extras",
                Description = "Style ++ins++, ~~del~~ and horizontal rules",
                Documentation =
                    "`#markdown` enables Markdig\'s emphasis extras: `++ins++`, `~~del~~`, `~sub~` " +
                    "and `^sup^`.\n\n" +
                    "`sub` and `sup` are left out on purpose — Calcpad uses both for subscripts and " +
                    "exponents in every equation it renders, so a bare `sub`/`sup` rule restyles the " +
                    "maths along with the prose. Target them through a wrapper if you need to.\n\n" +
                    MarkdownNote,
                Category = "CSS"
            },
        ];
    }
}
