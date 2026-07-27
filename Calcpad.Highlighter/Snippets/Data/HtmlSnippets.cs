using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for HTML tags and SVG graphics.
    /// Most HTML snippets are UI-only and excluded from linter.
    /// </summary>
    public static class HtmlSnippets
    {
        public static readonly SnippetItem[] Items =
        [
            // ============================================
            // HTML HEADINGS
            // ============================================
            new SnippetItem
            {
                Insert = "'<h3>text</h3>",
                Description = "Heading level 3",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<h4>text</h4>",
                Description = "Heading level 4",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<h5>text</h5>",
                Description = "Heading level 5",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<h6>text</h6>",
                Description = "Heading level 6",
                Category = "HTML"
            },

            // ============================================
            // HTML TEXT FORMATTING
            // ============================================
            new SnippetItem
            {
                Insert = "'<p>text</p>",
                Description = "Paragraph",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<br/>",
                Description = "Line break",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<strong>text</strong>",
                Description = "Bold / Strong emphasis",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<em>text</em>",
                Description = "Italic / Emphasis",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<ins>text</ins>",
                Description = "Inserted / Underlined text",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<del>text</del>",
                Description = "Deleted / Strikethrough text",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<sub>text</sub>",
                Description = "Subscript",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<sup>text</sup>",
                Description = "Superscript",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<span>text</span>",
                Description = "Generic span",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<span class=\"err\">text</span>",
                Description = "Error span (red text)",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<span class=\"ok\">text</span>",
                Description = "Success span (green text)",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<hr/>",
                Description = "Horizontal rule / divider",
                Category = "HTML"
            },

            // ============================================
            // HTML CONTAINERS
            // ============================================
            new SnippetItem
            {
                Insert = "'<div>text</div>",
                Description = "Generic div container",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<div class=\"fold\">\n'<h4>Heading</h4>\n'Folded content\n'</div>",
                Description = "Foldable/collapsible section",
                Category = "HTML"
            },

            // ============================================
            // HTML LISTS
            // ============================================
            new SnippetItem
            {
                Insert = "'<ul>\n'<li>Item 1</li>\n'<li>Item 2</li>\n'<li>Item 3</li>\n'</ul>",
                Description = "Unordered list (bullets)",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "'<ol>\n'<li>Item 1</li>\n'<li>Item 2</li>\n'<li>Item 3</li>\n'</ol>",
                Description = "Ordered list (numbered)",
                Category = "HTML"
            },

            // ============================================
            // HTML TABLES
            // ============================================
            new SnippetItem
            {
                Insert = "\n'<table class=\"bordered\">\n'<thead>\n'<tr><th>col 1</th><th>col 2</th></tr>\n'</thead>\n'<tbody>\n'<tr><td>'11'</td><td>'12'</td></tr>\n'<tr><td>'21'</td><td>'22'</td></tr>\n'</tbody>\n'</table>\n",
                Description = "HTML table with header",
                Category = "HTML"
            },

            // ============================================
            // HTML FORMS / INPUT ELEMENTS
            // ============================================
            new SnippetItem
            {
                Insert = "\n'<p>Select an option: <select name=\"target1\">\n'<option value=\"11;12\">x1; y1</option>\n'<option value=\"21;22\">x2; y2</option>\n'<option value=\"31;32\">x3; y3</option>\n'</select></p>\n'...\n'<p id=\"target1\"> Values:'x = ? {21}','y = ? {22}'</p>\n",
                Description = "Dropdown select input",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "\n'<p>Select: \n'<input name=\"target2\" type=\"radio\" id=\"opt1\" value=\"1\"/>\n'<label for=\"opt1\">option 1</label>\n'<input name=\"target2\" type=\"radio\" id=\"opt2\" value=\"2\"/>\n'<label for=\"opt2\">option 2</label>\n'...\n'<p id=\"target2\">Value -'opt = ? {2}'</p>\n",
                Description = "Radio button group",
                Category = "HTML"
            },
            new SnippetItem
            {
                Insert = "\n'<p><input name=\"target3\" type=\"checkbox\" id=\"chk1\" value=\"3\"/>\n'<label for=\"chk1\">Checkbox 1</label></p>\n'...\n'<p id=\"target3\">Value -'chk = ? {3}'</p>\n",
                Description = "Checkbox input",
                Category = "HTML"
            },

            // ============================================
            // HTML COMMENTS - METADATA
            // ============================================
            new SnippetItem
            {
                Insert = "'<!--{\"desc\": \"text\"}-->",
                Description = "Metadata comment with description",
                Label = "Description metadata",
                Category = "HTML Comments"
            },
            new SnippetItem
            {
                Insert = "'<!--{\"desc\": \"text\", \"paramTypes\": [\"Scalar\"], \"paramDesc\": [\"text\"]}-->",
                Description = "Full metadata comment with description, parameter types, and parameter descriptions",
                Label = "Full parameter metadata",
                Category = "HTML Comments"
            },
            new SnippetItem
            {
                Insert = "'<!--{\"paramTypes\": [\"Scalar\"], \"paramDesc\": [\"text\"]}-->",
                Description = "Metadata comment with parameter types and descriptions",
                Label = "Parameter types + descriptions",
                Category = "HTML Comments"
            },
            new SnippetItem
            {
                Insert = "'<!--{\"paramTypes\": [\"Scalar\"]}-->",
                Description = "Metadata comment with parameter types only",
                Label = "Parameter types only",
                Category = "HTML Comments"
            },

            // ============================================
            // HTML COMMENTS - SETTINGS OVERRIDES
            // ============================================
            new SnippetItem
            {
                Insert = "'<!--{\"settings\": {}}-->",
                Description = "File settings override block. Keys: decimals, degrees, complex, substitute, formatEquations, vectorGraphics, colorScale, etc.",
                Label = "Settings override",
                Category = "HTML Comments"
            },
            new SnippetItem
            {
                Insert = "'<!--{\"settings\": {\"decimals\": 2}}-->",
                Description = "Override decimal places in output (0-15)",
                Label = "Settings: decimals",
                Category = "HTML Comments"
            },
            new SnippetItem
            {
                Insert = "'<!--{\"settings\": {\"degrees\": 1}}-->",
                Description = "Set angle unit to degrees (0=radians, 1=degrees, 2=gradians)",
                Label = "Settings: degrees",
                Category = "HTML Comments"
            },
            new SnippetItem
            {
                Insert = "'<!--{\"settings\": {\"complex\": true}}-->",
                Description = "Enable complex number mode",
                Label = "Settings: complex mode",
                Category = "HTML Comments"
            },

            // ============================================
            // HTML COMMENTS - LINT IGNORE
            // ============================================
            new SnippetItem
            {
                Insert = "'<!--{\"LintIgnore\": [\"CPD-XXXX\"]}-->\n\n'<!--{\"EndLintIgnore\": []}-->",
                Description = "Suppress specific linter diagnostics in a region. Add error codes like CPD-3301.",
                Label = "Lint ignore region (specific codes)",
                Category = "HTML Comments"
            },
            new SnippetItem
            {
                Insert = "'<!--{\"LintIgnore\": []}-->\n\n'<!--{\"EndLintIgnore\": []}-->",
                Description = "Suppress all linter diagnostics in a region",
                Label = "Lint ignore region (all)",
                Category = "HTML Comments"
            },

            // ============================================
            // SVG GRAPHICS - CONTAINER
            // ============================================
            new SnippetItem
            {
                Insert = "#val\n#hide\nw = 400\nh = 400\n#show\n'<svg viewbox=\"'0' '0' 'w' 'h'\" xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" style=\"font-size:16px; width:'w'px; height:'h'px\">\n'<rect x=\"'0'\" y=\"'0'\" width=\"'w'\" height=\"'h'\" style=\"stroke:black; stroke-width:1; fill:WhiteSmoke; fill-opacity:0.2; stroke-opacity:0.1\" />\n'<text x=\"'w/2'\" y=\"'h/2'\" text-anchor=\"middle\" fill=\"red\" style=\"font-size:32px;\">Your drawing goes here!</text>\n'</svg>\n#equ",
                Description = "SVG container template",
                Category = "SVG"
            },

            // ============================================
            // SVG GRAPHICS - SHAPES
            // ============================================
            new SnippetItem
            {
                Insert = "#hide\nx1 = 30\ny1 = 30\nx2 = 380\ny2 = 200\n#show\n'<line x1=\"'x1'\" y1=\"'y1'\" x2=\"'x2'\" y2=\"'y2'\" style=\"stroke:black; stroke-width:2; stroke-opacity:0.8\" />",
                Description = "SVG line",
                Category = "SVG"
            },
            new SnippetItem
            {
                Insert = "#hide\nx = 80\ny = 60\nw = 300\nh = 200\n#show\n'<rect x=\"'x'\" y=\"'y'\" width=\"'w'\" height=\"'h'\" style=\"stroke:black; stroke-width:2; fill:yellow; fill-opacity:0.2; stroke-opacity:0.8\" />",
                Description = "SVG rectangle",
                Category = "SVG"
            },
            new SnippetItem
            {
                Insert = "#hide\ncx = 250\ncy = 150\nr = 70\n#show\n'<circle cx=\"'cx'\" cy=\"'cy'\" r=\"'r'\" style=\"stroke:black; stroke-width:2; fill:lime; fill-opacity:0.2; stroke-opacity:0.8\" />",
                Description = "SVG circle",
                Category = "SVG"
            },
            new SnippetItem
            {
                Insert = "#hide\ncx = 300\ncy = 320\nrx = 80\nry = 50\n#show\n'<ellipse cx=\"'cx'\" cy=\"'cy'\" rx=\"'rx'\" ry=\"'ry'\" style=\"stroke:black; stroke-width:2; fill:magenta; fill-opacity:0.1; stroke-opacity:0.8\" />",
                Description = "SVG ellipse",
                Category = "SVG"
            },
            new SnippetItem
            {
                Insert = "#hide\nx1 = 20','y1 = 40\nx2 = 60','y2 = 350\nx3 = 250','y3 = 300\nx4 = 360','y4 = 150\n#show\n'<polyline points=\"'x1','y1' 'x2','y2' 'x3','y3' 'x4','y4'\" style=\"stroke:black; stroke-width:2; fill:none; fill-opacity:0.2; stroke-opacity:0.8\" />",
                Description = "SVG polyline (connected lines)",
                Category = "SVG"
            },
            new SnippetItem
            {
                Insert = "#hide\nx1 = 150','y1 = 20\nx2 = 10','y2 = 140\nx3 = 120','y3 = 360\nx4 = 280','y4 = 120\n#show\n'<polygon points=\"'x1','y1' 'x2','y2' 'x3','y3' 'x4','y4'\" style=\"stroke:black; stroke-width:2; fill:cyan; fill-opacity:0.2; stroke-opacity:0.8\" />",
                Description = "SVG polygon (closed shape)",
                Category = "SVG"
            },
            new SnippetItem
            {
                Insert = "#hide\nx = 50\ny = 30\n#show\n'<text x=\"'x'\" y=\"'y'\" text-anchor=\"start\">text1</text>\n",
                Description = "SVG text",
                Category = "SVG"
            }
        ];
    }
}
