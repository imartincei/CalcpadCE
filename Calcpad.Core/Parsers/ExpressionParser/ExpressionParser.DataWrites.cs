namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        /// <summary>
        /// Whether this parse may run <c>#write</c>/<c>#append</c>. A host that re-renders on
        /// every keystroke clears it, so the document's output is not rewritten each time.
        /// </summary>
        public bool AllowDataWrite { get; set; } = true;
    }
}
