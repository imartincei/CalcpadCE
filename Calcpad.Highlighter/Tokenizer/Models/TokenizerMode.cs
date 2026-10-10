namespace Calcpad.Highlighter.Tokenizer.Models
{
    /// <summary>
    /// Controls the level of detail the tokenizer produces.
    /// </summary>
    public enum TokenizerMode
    {
        /// <summary>
        /// Lightweight mode for syntax highlighting only.
        /// Produces tokens but does not extract full definition metadata.
        /// </summary>
        Highlight,

        /// <summary>
        /// Full analysis mode for the linter, extracting variable definitions with expressions,
        /// function definitions with params and body, custom units, command blocks, #for loop
        /// variables and #read variables. Replaces the regex-based DefinitionCollection.
        /// </summary>
        Lint
    }
}
