using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Markdown alternatives for HTML formatting.
    /// These are UI-only snippets excluded from linter.
    /// </summary>
    public static class MarkdownSnippets
    {
        public static readonly SnippetItem[] Items =
        [
            new SnippetItem
            {
                Insert = "### text",
                Description = "Markdown heading level 3",
                Category = "Markdown"
            },
            new SnippetItem
            {
                Insert = "#### text",
                Description = "Markdown heading level 4",
                Category = "Markdown"
            },
            new SnippetItem
            {
                Insert = "##### text",
                Description = "Markdown heading level 5",
                Category = "Markdown"
            },
            new SnippetItem
            {
                Insert = "###### text",
                Description = "Markdown heading level 6",
                Category = "Markdown"
            },
            new SnippetItem
            {
                Insert = "**text**",
                Description = "Markdown bold / strong",
                Category = "Markdown"
            },
            new SnippetItem
            {
                Insert = "*text*",
                Description = "Markdown italic / emphasis",
                Category = "Markdown"
            },
            new SnippetItem
            {
                Insert = "++text++",
                Description = "Markdown underline / insert",
                Category = "Markdown"
            },
            new SnippetItem
            {
                Insert = "~~text~~",
                Description = "Markdown strikethrough / delete",
                Category = "Markdown"
            },
            new SnippetItem
            {
                Insert = "~text~",
                Description = "Markdown subscript",
                Category = "Markdown"
            },
            new SnippetItem
            {
                Insert = "^text^",
                Description = "Markdown superscript",
                Category = "Markdown"
            },
            new SnippetItem
            {
                Insert = "---",
                Description = "Markdown horizontal rule",
                Category = "Markdown"
            }
        ];
    }
}
