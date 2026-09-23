using Markdig;
using Markdig.Renderers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private enum ParseMode { Cpd, Html, Markdown }

        private static readonly MarkdownPipeline MarkdownModePipeline = new MarkdownPipelineBuilder()
            .UseEmphasisExtras()
            .UseListExtras()
            .UsePipeTables()
            .UseTaskLists()
            .UseAutoLinks()
            .Build();

        private ParseMode _parseMode;
        private bool _modeSuppressed;
        private readonly Stack<(ParseMode, bool)> _parseModeStack = new();
        private readonly StringBuilder _mdBuffer = new();
        private readonly List<(int Line, int SourceLine)> _mdLines = new();

        private bool IsNonCpdMode => _parseMode != ParseMode.Cpd;

        private void ParseNonCpdModeLine(ReadOnlySpan<char> line)
        {
            if (!_isVisible || _htmlLines >= MaxHtmlLines)
                return;

            if (++_htmlLines == MaxHtmlLines)
            {
                FlushMarkdown();
                AppendError(line.ToString(), string.Format(Messages.The_output_is_longer_than_0_lines_The_rest_will_be_skipped, MaxHtmlLines), _currentLine);
                return;
            }

            if (_parseMode == ParseMode.Markdown)
            {
                _mdBuffer.Append(line).Append('\n');
                _mdLines.Add((_currentLine, _parser.Line));
                return;
            }

            var htmlId = HtmlId;
            var trimmed = line.TrimStart();
            if (htmlId.Length == 0 || trimmed.IsEmpty)
                _sb.Append(line).AppendLine();
            else
                _sb.Append(line[..^trimmed.Length]).AppendLine(InsertAttribute(trimmed, htmlId));
        }

        /// <summary>Renders the buffered markdown, tagging each top-level block with its first source line.</summary>
        private void FlushMarkdown()
        {
            if (_mdLines.Count == 0)
                return;

            var document = Markdown.Parse(_mdBuffer.ToString(), MarkdownModePipeline);
            using StringWriter writer = new();
            HtmlRenderer renderer = new(writer);
            MarkdownModePipeline.Setup(renderer);
            var output = writer.GetStringBuilder();
            foreach (var block in document)
            {
                renderer.Render(block);
                renderer.Writer.Flush();
                var html = output.ToString().TrimEnd();
                output.Clear();
                if (html.Length == 0)
                    continue;

                var (line, sourceLine) = _mdLines[Math.Clamp(block.Line, 0, _mdLines.Count - 1)];
                var attributes = LineAttributes(line, sourceLine);
                _sb.AppendLine(attributes.Length == 0 ? html : InsertAttribute(html, attributes));
            }
            _mdBuffer.Clear();
            _mdLines.Clear();
        }
    }
}
