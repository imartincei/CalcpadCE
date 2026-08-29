using System;
using System.Collections.Generic;
using System.Text;
using Calcpad.Highlighter.Parsing;

namespace Calcpad.Highlighter.ContentResolution
{
    /// <summary>
    /// Stage 1: Line continuation processing.
    /// </summary>
    public partial class ContentResolver
    {
        /// <summary>
        /// STAGE 1: Process line continuations only
        /// Line continuation occurs when:
        /// 1. Explicit: Line ends with " _" (space followed by underscore)
        /// 2. Implicit: Line ends with one of ;|&@:({[ AND is not a comment AND has unbalanced brackets/braces/parens
        /// </summary>
        private Stage1Result ProcessStage1(List<string> lines)
        {
            var processedLines = new List<string>();
            var sourceMap = new Dictionary<int, int>();
            var lineContinuationMap = new Dictionary<int, List<int>>();
            var lineContinuationSegments = new Dictionary<int, List<LineContinuationSegment>>();

            int i = 0;
            while (i < lines.Count)
            {
                var line = lines[i];
                var (explicitCont, implicitCont, baseContent, parenDepth, bracketDepth, braceDepth) = GetLineContinuationWithDepth(line);

                // Continue if: explicit continuation OR (implicit continuation AND unbalanced delimiters)
                bool hasUnbalanced = parenDepth > 0 || bracketDepth > 0 || braceDepth > 0;
                bool shouldContinue = explicitCont || (implicitCont && hasUnbalanced);

                if (shouldContinue && i < lines.Count - 1)
                {
                    // This line continues to the next
                    var continuedLines = new List<int> { i };
                    var segments = new List<LineContinuationSegment>();

                    // Use StringBuilder instead of string concatenation to avoid O(n^2) allocations
                    var fullLineBuilder = new StringBuilder(baseContent.Length * 3);
                    fullLineBuilder.Append(baseContent);

                    // Track the first segment
                    segments.Add(new LineContinuationSegment
                    {
                        OriginalLine = i,
                        StartColumn = 0,
                        Length = baseContent.Length
                    });

                    // Track bracket depth across lines
                    int currentParenDepth = parenDepth;
                    int currentBracketDepth = bracketDepth;
                    int currentBraceDepth = braceDepth;

                    // Collect all continuation lines
                    int j = i + 1;
                    while (j < lines.Count)
                    {
                        var nextLine = lines[j];
                        continuedLines.Add(j);

                        var (nextExplicitCont, nextImplicitCont, nextContent, nextParenDepth, nextBracketDepth, nextBraceDepth) = GetLineContinuationWithDepth(nextLine);

                        // Update cumulative depth
                        currentParenDepth += nextParenDepth;
                        currentBracketDepth += nextBracketDepth;
                        currentBraceDepth += nextBraceDepth;

                        // Check if we should continue: explicit continuation OR (implicit continuation AND still unbalanced)
                        bool stillUnbalanced = currentParenDepth > 0 || currentBracketDepth > 0 || currentBraceDepth > 0;
                        bool stillContinuing = nextExplicitCont || (nextImplicitCont && stillUnbalanced);

                        if (stillContinuing && j < lines.Count - 1)
                        {
                            segments.Add(new LineContinuationSegment
                            {
                                OriginalLine = j,
                                StartColumn = fullLineBuilder.Length,
                                Length = nextContent.Length
                            });
                            fullLineBuilder.Append(nextContent);
                            j++;
                        }
                        else
                        {
                            // Last line in continuation (balanced or no continuation marker)
                            segments.Add(new LineContinuationSegment
                            {
                                OriginalLine = j,
                                StartColumn = fullLineBuilder.Length,
                                Length = nextLine.Length
                            });
                            fullLineBuilder.Append(nextLine);
                            break;
                        }
                    }

                    processedLines.Add(fullLineBuilder.ToString());
                    var newIndex = processedLines.Count - 1;
                    sourceMap[newIndex] = continuedLines[0];
                    lineContinuationMap[newIndex] = continuedLines;
                    lineContinuationSegments[newIndex] = segments;
                    i = j + 1;
                }
                else
                {
                    processedLines.Add(line);
                    sourceMap[processedLines.Count - 1] = i;
                    i++;
                }
            }

            return new Stage1Result
            {
                Lines = processedLines,
                SourceMap = sourceMap,
                LineContinuationMap = lineContinuationMap,
                LineContinuationSegments = lineContinuationSegments
            };
        }

        /// <summary>
        /// Check if a line has a valid line continuation and calculate bracket depths, using
        /// lightweight span-based comment detection instead of full tokenization. Continuation is
        /// explicit when the line ends with " _" outside a comment, implicit when it ends with one
        /// of ;|&@:({[ outside a comment and brackets are unbalanced.
        /// </summary>
        private static (bool explicitContinuation, bool implicitContinuation, string content, int parenDepth, int bracketDepth, int braceDepth) GetLineContinuationWithDepth(string line)
        {
            if (string.IsNullOrEmpty(line))
                return (false, false, line, 0, 0, 0);

            var lineSpan = line.AsSpan();
            var len = lineSpan.Length;
            int parenDepth = 0;
            int bracketDepth = 0;
            int braceDepth = 0;

            // Find last non-whitespace character position
            int lastCharPosInLine = len - 1;
            while (lastCharPosInLine >= 0 && char.IsWhiteSpace(lineSpan[lastCharPosInLine]))
                lastCharPosInLine--;

            if (lastCharPosInLine < 0)
                return (false, false, line, 0, 0, 0);

            // Lightweight comment detection: walk the line tracking quote state, since a Calcpad
            // comment starts with ' or " and extends to the matching quote or end of line.
            // Brackets are counted only in non-comment regions.
            bool inComment = false;
            char commentChar = '\0';
            int commentStartCol = -1;

            for (int ci = 0; ci < len; ci++)
            {
                var c = lineSpan[ci];

                if (!inComment)
                {
                    if (c == '\'' || c == '"')
                    {
                        inComment = true;
                        commentChar = c;
                        if (commentStartCol < 0) commentStartCol = ci;
                    }
                    else
                    {
                        // Count brackets outside comments
                        if (c == '(') parenDepth++;
                        else if (c == ')') parenDepth--;
                        else if (c == '[') bracketDepth++;
                        else if (c == ']') bracketDepth--;
                        else if (c == '{') braceDepth++;
                        else if (c == '}') braceDepth--;
                    }
                }
                else if (c == commentChar)
                {
                    // End of comment
                    inComment = false;
                    commentChar = '\0';
                }
            }

            // Check for explicit line continuation first (line ends with " _")
            // Explicit continuation works in both code and comments
            if (lastCharPosInLine >= 1 && lineSpan[lastCharPosInLine] == '_' && lineSpan[lastCharPosInLine - 1] == ' ')
            {
                var content = line[..(lastCharPosInLine - 1)]; // Exclude the space before _
                return (true, false, content, parenDepth, bracketDepth, braceDepth);
            }

            // If end is in a comment, no implicit continuation allowed
            bool endIsInComment = inComment && commentStartCol >= 0 && lastCharPosInLine >= commentStartCol;
            if (endIsInComment)
            {
                return (false, false, line, parenDepth, bracketDepth, braceDepth);
            }

            // Check for implicit line continuation (ends with one of the special characters)
            var lastChar = lineSpan[lastCharPosInLine];
            bool hasImplicitContinuation = CharClassifier.IsLineExtension(lastChar);

            // Always return actual bracket depths - they're used to track unbalanced delimiters
            return (false, hasImplicitContinuation, line, parenDepth, bracketDepth, braceDepth);
        }
    }
}
