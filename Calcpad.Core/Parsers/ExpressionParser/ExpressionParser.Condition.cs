using System;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private Condition _condition;
        private sealed class Condition
        {
            internal const int RemoveConditionKeyword = Keyword.End_If - Keyword.If;
            private enum Types
            {
                None,
                If,
                ElseIf,
                Else,
                EndIf,
                While
            }
            private readonly struct Item
            {
                internal bool Value { get; }
                internal Types Type { get; }
                // Output and source line of the #if or #while that opened the block.
                internal int Line { get; }
                internal int SourceLine { get; }
                internal Item(bool value, Types type, int line = -1, int sourceLine = 0)
                {
                    Type = type;
                    Value = value;
                    Line = line;
                    SourceLine = sourceLine;
                }
            }

            private int _count;
            private string _keyword;
            private int _keywordLength;
            private const int MaxDepth = 19;
            private readonly Item[] _conditions = new Item[MaxDepth + 1];
            private Types Type => _conditions[Id].Type;
            internal int Id { get; private set; }
            internal bool IsUnchecked { get; private set; }
            internal bool IsSatisfied => _conditions[_count].Value;
            internal bool IsFound { get; private set; }
            internal int KeywordLength => _keywordLength;
            internal bool IsLoop => _conditions[_count].Type == Types.While;

            /// <summary>Where the innermost unclosed #if started, if any.</summary>
            internal (int Line, int SourceLine)? OpenIf
            {
                get
                {
                    for (var i = Id; i > 0; --i)
                        if (_conditions[i].Type != Types.While)
                            return (_conditions[i].Line, _conditions[i].SourceLine);

                    return null;
                }
            }

            internal Condition()
            {
                _conditions[0] = new Item(true, Types.None);
                _keyword = string.Empty;
            }
            private void Add(bool value, int line, int sourceLine)
            {
                ++Id;
                _conditions[Id] = new Item(value, Types.If, line, sourceLine);
                if (IsSatisfied)
                {
                    ++_count;
                    IsFound = false;
                }
            }

            private void Remove()
            {
                --Id;
                if (_count > Id)
                {
                    --_count;
                    IsFound = true;
                }
            }

            private void Change(bool value, Types type)
            {
                _conditions[Id] = new Item(value, type, _conditions[Id].Line, _conditions[Id].SourceLine);
            }

            internal void SetCondition(int index, int line = -1, int sourceLine = 0)
            {
                if (index < 0 || index >= (int)Types.While)
                {
                    if (_keywordLength > 0)
                    {
                        _keywordLength = 0;
                        _keyword = string.Empty;
                    }
                    return;
                }

                var type = (Types)(index + 1);
                _keyword = GetConditinalKeyword(type);
                _keywordLength = _keyword.AsSpan().TrimEnd().Length;
                IsUnchecked = type == Types.If || type == Types.ElseIf;
                if (type > Types.If && type < Types.While && _count == 0)
                    throw Exceptions.ConditionNotInitialized();

                if (Type == Types.Else)
                {
                    if (type == Types.Else)
                        throw Exceptions.DuplicateElse();

                    if (type == Types.ElseIf)
                        throw Exceptions.ElseIfAfterElse();
                }
                if ((type == Types.If || type == Types.While) && Id == MaxDepth)
                    throw Exceptions.NestingTooDeep(MaxDepth);

                switch (type)
                {
                    case Types.If:
                        Add(true, line, sourceLine);
                        break;
                    case Types.While:
                        _conditions[++Id] = new Item(true, type, line, sourceLine);
                        ++_count;
                        break;
                    case Types.ElseIf:
                        Change(true, Types.If);
                        break;
                    case Types.Else:
                        Change(!IsFound, type);
                        break;
                    case Types.EndIf:
                        Remove();
                        break;
                }
            }

            internal void Check(Complex value)
            {
                var result = IsTrue(value);
                if (result)
                    IsFound = true;
                Change(result, Type);
                IsUnchecked = false;
            }

            internal static bool IsTrue(Complex value)
            {
                if (!value.IsReal)
                    throw Exceptions.ConditionComplex();

                var d = value.Re;
                if (double.IsNaN(d) || double.IsInfinity(d))
                    throw Exceptions.ConditionResultInvalid(d.ToString());

                return Math.Abs(d) > 1e-12;
            }

            internal void Check() => IsUnchecked = false;

            public override string ToString() => _keyword;

            internal string ToHtml()
            {
                if (string.IsNullOrEmpty(_keyword))
                    return _keyword;
                return $"<span class=\"cond\">{_keyword}</span>";
            }

            private static string GetConditinalKeyword(Types type)
            {
                return type switch
                {
                    Types.If => "#if ",
                    Types.ElseIf => "#else if ",
                    Types.Else => "#else",
                    Types.EndIf => "#end if",
                    Types.While => "#while ",
                    _ => string.Empty,
                };
            }
        }
    }
}