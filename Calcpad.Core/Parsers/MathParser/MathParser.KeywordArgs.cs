using System;
using System.Collections.Generic;

namespace Calcpad.Core
{
    public partial class MathParser
    {
        private static Token[] ReadDefault(Queue<Token> input)
        {
            var tokens = new Queue<Token>();
            var depth = 0;
            while (input.Count != 0)
            {
                var t = input.Peek();
                if (depth == 0 && (t.Type == TokenTypes.Divisor || t.Type == TokenTypes.BracketRight))
                    break;

                if (t.Type == TokenTypes.BracketLeft || t.Type == TokenTypes.SquareBracketLeft)
                    ++depth;
                else if (t.Type == TokenTypes.BracketRight || t.Type == TokenTypes.SquareBracketRight)
                    --depth;

                tokens.Enqueue(input.Dequeue());
            }
            return Input.GetRpn(tokens);
        }

        // "f(3; z = 4)" → supplied arguments "3; 4" with map [0, 2]; omitted ones are filled from defaults.
        private Queue<Token> ResolveCallArguments(Queue<Token> input)
        {
            var hasCall = false;
            foreach (var t in input)
                hasCall |= t.Type == TokenTypes.CustomFunction && t.Index >= 0;

            if (!hasCall)
                return input;

            var tokens = input.ToArray();
            var isResolved = false;
            bool[] removed = null;
            for (int i = 0, len = tokens.Length; i < len - 2; ++i)
            {
                var t = tokens[i];
                if (t.Type != TokenTypes.CustomFunction || t.Index < 0 || tokens[i + 1].Type != TokenTypes.BracketLeft)
                    continue;

                var close = IndexOfClosingBracket(tokens, i + 1);
                if (close < 0)
                    break;

                // Definition head: "f(x; y = 2) = ..."
                if (i == 0 && close + 1 < len && IsAssignment(tokens[close + 1].Content) || close == i + 2)
                    continue;

                var starts = ArgumentStarts(tokens, i + 2, close);
                var cf = _functions[t.Index];
                var n = cf.ParameterCount;
                var isKeyword = new bool[starts.Count];
                var hasKeyword = false;
                for (var k = 0; k < starts.Count; ++k)
                    hasKeyword |= isKeyword[k] = IsKeywordArgument(tokens, starts[k]);

                if (!hasKeyword && starts.Count >= n)
                    continue;

                var map = new int[starts.Count];
                var isSupplied = new bool[n];
                var positionalCount = 0;
                for (var k = 0; k < starts.Count; ++k)
                {
                    int index;
                    if (isKeyword[k])
                    {
                        var name = tokens[starts[k]].Content;
                        index = cf.IndexOfParameter(name);
                        if (index < 0)
                            throw Exceptions.UnknownKeywordArgument(name);

                        if (isSupplied[index])
                            throw Exceptions.DuplicateArgument(name);

                        removed ??= new bool[len];
                        removed[starts[k]] = removed[starts[k] + 1] = true;
                    }
                    else if (k > positionalCount || positionalCount >= n)
                        throw Exceptions.InvalidNumberOfArguments();
                    else
                        index = positionalCount++;

                    map[k] = index;
                    isSupplied[index] = true;
                }
                for (var p = 0; p < n; ++p)
                    if (!isSupplied[p] && cf.Defaults?[p] is null)
                        throw Exceptions.InvalidNumberOfArguments();

                isResolved = true;
                tokens[i] = new FunctionToken(t.Content)
                {
                    Type = TokenTypes.CustomFunction,
                    Index = t.Index,
                    ParameterCount = starts.Count,
                    ArgumentMap = map,
                    PositionalCount = positionalCount
                };
            }
            if (!isResolved)
                return input;

            var output = new Queue<Token>(tokens.Length);
            var assignmentIndex = _assignmentIndex;
            for (var i = 0; i < tokens.Length; ++i)
                if (removed?[i] != true)
                    output.Enqueue(tokens[i]);
                else if (i < assignmentIndex)
                    --_assignmentIndex;

            return output;
        }

        private static bool IsKeywordArgument(Token[] tokens, int start) =>
            start + 1 < tokens.Length &&
            (tokens[start] is VariableToken || tokens[start].Type == TokenTypes.Unit) &&
            tokens[start + 1].Type == TokenTypes.Operator &&
            tokens[start + 1].Content == "=";

        private static int IndexOfClosingBracket(Token[] tokens, int open)
        {
            var depth = 0;
            for (int i = open, len = tokens.Length; i < len; ++i)
            {
                var tt = tokens[i].Type;
                if (tt == TokenTypes.BracketLeft || tt == TokenTypes.SquareBracketLeft)
                    ++depth;
                else if ((tt == TokenTypes.BracketRight || tt == TokenTypes.SquareBracketRight) && --depth == 0)
                    return i;
            }
            return -1;
        }

        private static List<int> ArgumentStarts(Token[] tokens, int start, int end)
        {
            var starts = new List<int>(4) { start };
            var depth = 0;
            for (var i = start; i < end; ++i)
            {
                var tt = tokens[i].Type;
                if (tt == TokenTypes.BracketLeft || tt == TokenTypes.SquareBracketLeft)
                    ++depth;
                else if (tt == TokenTypes.BracketRight || tt == TokenTypes.SquareBracketRight)
                    --depth;
                else if (depth == 0 && tt == TokenTypes.Divisor)
                    starts.Add(i + 1);
            }
            return starts;
        }

        internal static void FillDefaults(string[] arguments, string[] defaults)
        {
            for (var i = 0; i < arguments.Length; ++i)
                arguments[i] ??= defaults[i] ?? throw Exceptions.InvalidNumberOfArguments();
        }

        // Index of the first target outside brackets, or of an unmatched closing bracket.
        internal static int IndexOfTopLevel(ReadOnlySpan<char> s, char target)
        {
            var depth = 0;
            for (int i = 0, len = s.Length; i < len; ++i)
            {
                var c = s[i];
                if (c == target && depth == 0)
                    return i;

                if (c is '(' or '[' or '{')
                    ++depth;
                else if (c is ')' or ']' or '}' && --depth < 0)
                    return i;
            }
            return -1;
        }
    }
}
