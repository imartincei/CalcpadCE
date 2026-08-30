using System;
using System.Collections.Generic;

namespace Calcpad.Highlighter.Linter.Helpers
{
    /// <summary>
    /// Output-value mode set by the #equ / #val / #noc directives (mirrors ExpressionParser's
    /// _isVal of 0 / 1 / -1). Under <see cref="NoCalculation"/> equations are rendered but not
    /// evaluated, so identifiers need not resolve to a definition.
    /// </summary>
    public enum OutputMode
    {
        Equations,
        Values,
        NoCalculation
    }

    /// <summary>
    /// Scope mode set by the #local / #global directives. A #local section is excluded when the
    /// file is pulled in through #include (mirrors Core's CalcpadReader.Include filtering).
    /// </summary>
    public enum ScopeMode
    {
        Global,
        Local
    }

    /// <summary>
    /// Visibility mode set by the #show / #hide / #pre / #post directives (mirrors
    /// ExpressionParser's _isVisible, driven by ForPrint for #pre/#post).
    /// </summary>
    public enum VisibilityMode
    {
        Shown,
        Hidden,
        ScreenOnly,
        PrintOnly
    }

    /// <summary>
    /// Tracks the running state of Calcpad's mode directives as lines are visited in order, where
    /// within each category the most recent directive wins (see ExpressionParser.ParseKeyword):
    ///   - output value:  #equ / #val / #noc (and #end equ / #end val / #end noc)
    ///   - scope:          #global / #local
    ///   - markdown:       #md [on] / #md off
    ///   - substitution:   #varsub / #nosub / #novar (and #end varsub / #end nosub / #end novar)
    ///   - angle:          #rad / #deg / #gra
    ///   - line breaking:  #wrap / #split
    ///   - number type:    #complex / #phasor
    ///   - visibility:     #show / #hide / #pre / #post (and their #end forms)
    /// Only the categories the tooling consumes are tracked, and an #end form pops back to the
    /// state in effect before its opener, mirroring ExpressionParser's own stacks.
    /// </summary>
    public sealed class DirectiveState
    {
        private readonly Stack<OutputMode> _outputStack = new();
        private readonly Stack<VisibilityMode> _visibilityStack = new();

        public OutputMode Output { get; private set; } = OutputMode.Equations;
        public VisibilityMode Visibility { get; private set; } = VisibilityMode.Shown;
        public ScopeMode Scope { get; private set; } = ScopeMode.Global;
        public bool IsMarkdownOn { get; private set; }

        /// <summary>
        /// Updates the tracked state from a trimmed directive line, ignoring non-tracked
        /// directives. Prefix matching mirrors Core's Validator.IsKeyword / GetKeyword.
        /// </summary>
        public void Apply(ReadOnlySpan<char> trimmedLine)
        {
            if (Matches(trimmedLine, "#end val") || Matches(trimmedLine, "#end equ") || Matches(trimmedLine, "#end noc"))
                Output = _outputStack.Count > 0 ? _outputStack.Pop() : OutputMode.Equations;
            else if (Matches(trimmedLine, "#equ"))
            {
                _outputStack.Push(Output);
                Output = OutputMode.Equations;
            }
            else if (Matches(trimmedLine, "#val"))
            {
                _outputStack.Push(Output);
                Output = OutputMode.Values;
            }
            else if (Matches(trimmedLine, "#noc"))
            {
                _outputStack.Push(Output);
                Output = OutputMode.NoCalculation;
            }
            else if (Matches(trimmedLine, "#end hide") || Matches(trimmedLine, "#end show") ||
                     Matches(trimmedLine, "#end pre") || Matches(trimmedLine, "#end post"))
                Visibility = _visibilityStack.Count > 0 ? _visibilityStack.Pop() : VisibilityMode.Shown;
            else if (Matches(trimmedLine, "#hide"))
            {
                _visibilityStack.Push(Visibility);
                Visibility = VisibilityMode.Hidden;
            }
            else if (Matches(trimmedLine, "#show"))
            {
                _visibilityStack.Push(Visibility);
                Visibility = VisibilityMode.Shown;
            }
            else if (Matches(trimmedLine, "#pre"))
            {
                _visibilityStack.Push(Visibility);
                Visibility = VisibilityMode.ScreenOnly;
            }
            else if (Matches(trimmedLine, "#post"))
            {
                _visibilityStack.Push(Visibility);
                Visibility = VisibilityMode.PrintOnly;
            }
            else if (Matches(trimmedLine, "#global"))
                Scope = ScopeMode.Global;
            else if (Matches(trimmedLine, "#local"))
                Scope = ScopeMode.Local;
            else if (Matches(trimmedLine, "#md"))
                ApplyMarkdown(trimmedLine);
        }

        // #md and #md on enable markdown; #md off disables it (mirrors ExpressionParser.ParseKeywordMd).
        private void ApplyMarkdown(ReadOnlySpan<char> trimmedLine)
        {
            var arg = trimmedLine.Length > 3 ? trimmedLine[3..].Trim() : ReadOnlySpan<char>.Empty;
            if (arg.Equals("off", StringComparison.OrdinalIgnoreCase))
                IsMarkdownOn = false;
            else if (arg.IsEmpty || arg.Equals("on", StringComparison.OrdinalIgnoreCase))
                IsMarkdownOn = true;
        }

        private static bool Matches(ReadOnlySpan<char> trimmedLine, ReadOnlySpan<char> directive) =>
            trimmedLine.StartsWith(directive, StringComparison.OrdinalIgnoreCase);
    }
}
