using System;
using System.Collections.Generic;

namespace Calcpad.Core
{
    internal abstract class PlotParser
    {
        protected MathParser Parser;
        protected PlotSettings Settings;

        /// <summary>The names after @, set by <see cref="Parse"/>.</summary>
        internal List<string> Variables { get; } = [];

        protected PlotParser(MathParser parser, PlotSettings settings)
        {
            Parser = parser;
            Settings = settings;
        }

        internal abstract string Parse(ReadOnlySpan<char> script, bool calculate);

        /// <summary>Where each part of the script starts, after leading spaces.</summary>
        protected int[] Offsets;

        protected string[] Split(ReadOnlySpan<char> script, ReadOnlySpan<char> delimiters, out int index)
        {
            var n = delimiters.Length - 1;
            var input = new string[n];
            Offsets = new int[n];
            index = 0;
            var bracketCount = 0;
            var ts = new TextSpan(script);
            var del = delimiters[0];
            for (int i = 0, len = script.Length; i < len; ++i)
            {
                var c = script[i];
                if (c == '{')
                    ++bracketCount;

                if (c == del && bracketCount == 1)
                {
                    if (index > 0)
                    {
                        var part = ts.Cut();
                        input[index - 1] = part.Trim().ToString();
                        Offsets[index - 1] = ts.Start + part.Length - part.TrimStart().Length;
                    }
                    ++index;
                    if (index > n)
                        break;
                    del = delimiters[index];
                    ts.Reset(i + 1);
                }
                else
                    ts.Expand();

                if (c == '}')
                    --bracketCount;
            }
            return input;
        }

        /// <summary>The pieces of <paramref name="s"/> between <paramref name="separator"/>s, with their offsets.</summary>
        protected static (string Text, int Offset)[] Split(string s, int offset, char separator)
        {
            var parts = s.Split(separator);
            var result = new (string, int)[parts.Length];
            for (int i = 0, at = offset; i < parts.Length; at += parts[i].Length + 1, ++i)
                result[i] = (parts[i], at);
            return result;
        }

        protected string ToHtml(string text, int offset)
        {
            var html = string.Empty;
            At(text, offset, () =>
            {
                Parser.Parse(text);
                html = Parser.ToHtml();
            });
            return html;
        }

        protected double Calculate(string text, int offset, out Unit units)
        {
            Unit u = null;
            var value = 0d;
            At(text, offset, () =>
            {
                Parser.Parse(text);
                value = Parser.CalculateReal();
                u = Parser.Units;
            });
            units = u;
            return value;
        }

        protected Func<IValue> Compile(string text, int offset, Parameter[] parameters)
        {
            Func<IValue> f = null;
            At(text, offset, () => f = Parser.Compile(text, parameters));
            return f;
        }

        // Errors and read names in a part point into the script
        private void At(string text, int offset, Action action)
        {
            var nameOffset = Parser.NameOffset;
            Parser.NameOffset = offset;
            try
            {
                action();
            }
            catch (MathParserException ex)
            {
                if (ex.Position >= 0)
                    ex.Shift(offset);
                else
                {
                    var t = text.AsSpan();
                    ex.Position = offset + t.Length - t.TrimStart().Length;
                    ex.EndPosition = offset + t.TrimEnd().Length;
                }
                throw;
            }
            finally
            {
                Parser.NameOffset = nameOffset;
            }
        }

        protected static readonly string[] Parts =
        [
            "function",
            "variable name",
            "left margin",
            "right margin"
        ];
    }
}