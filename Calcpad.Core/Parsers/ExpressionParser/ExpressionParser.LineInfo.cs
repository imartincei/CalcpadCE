using System.Collections.Generic;
namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private readonly struct LineInfo
        {
            internal readonly Keyword Keyword;
            internal readonly List<Token> Tokens;
            // Original source-file line number carried through macro/include expansion
            // (via the '\v{line}' marker MacroParser writes). Cached alongside Tokens so
            // repeat-loop iterations that hit the fast path can restore _parser.Line and
            // any downstream HTML markers (data-source-line, error-link targets) stay
            // pointed at the true source line instead of drifting to whatever ran last.
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
