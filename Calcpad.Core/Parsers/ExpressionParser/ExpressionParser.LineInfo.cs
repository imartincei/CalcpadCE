using System.Collections.Generic;
namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private readonly struct LineInfo
        {
            internal readonly Keyword Keyword;
            internal readonly List<Token> Tokens;
            internal readonly int SourceLine;
            internal bool IsCached => Tokens is not null;

            internal LineInfo(List<Token> tokens, Keyword keyword, int sourceLine = 0)
            {
                Tokens = tokens;
                Keyword = keyword;
                SourceLine = sourceLine;
            }
        }
    }
}
