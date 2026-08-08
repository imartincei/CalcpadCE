import { CalcpadTokenType } from '../types/api';

/**
 * Semantic token types matching C# server TokenType enum (1:1 mapping).
 * Used by both VS Code SemanticTokensProvider and Monaco DocumentSemanticTokensProvider.
 */
export const SEMANTIC_TOKEN_TYPES = [
    // Core Syntax (1-4)
    'const',              // 1: Numeric constants
    'operator',           // 2: Operators
    'bracket',            // 3: Brackets
    'lineContinuation',   // 4: Line continuation marker

    // Identifiers (5-11)
    'variable',           // 5: Variable identifiers
    'localVariable',      // 6: Local variables (function params, loop vars)
    'function',           // 7: Function names
    'macro',              // 8: Macro names
    'macroParameter',     // 9: Macro parameters in #def statements
    'units',              // 10: Unit identifiers
    'setting',            // 11: Setting variables

    // Keywords and Commands (12-15)
    'keyword',            // 12: Keywords starting with #
    'controlBlockKeyword', // 13: Control block keywords (#if, #for, etc.)
    'endKeyword',         // 14: End keywords (#end if, #loop)
    'command',            // 15: Commands starting with $

    // File and Data Exchange (16-18)
    'include',            // 16: Include file paths
    'filePath',           // 17: File paths in data exchange
    'dataExchangeKeyword', // 18: Sub-keywords (from, to, sep, type)

    // Comments and Documentation (19-25)
    'comment',            // 19: Plain text comments
    'htmlComment',        // 20: HTML comments
    'tag',                // 21: HTML tags
    'htmlContent',        // 22: HTML content
    'javascript',         // 23: JavaScript code
    'css',                // 24: CSS code
    'svg',                // 25: SVG markup

    // Special (26-27)
    'input',              // 26: Input markers
    'format',             // 27: Format specifiers
];

/**
 * Map server typeId to semantic token type name (1:1 with C# enum).
 */
export const TOKEN_TYPE_MAP: Record<number, string> = {
    [CalcpadTokenType.Const]: 'const',
    [CalcpadTokenType.Operator]: 'operator',
    [CalcpadTokenType.Bracket]: 'bracket',
    [CalcpadTokenType.LineContinuation]: 'lineContinuation',
    [CalcpadTokenType.Variable]: 'variable',
    [CalcpadTokenType.LocalVariable]: 'localVariable',
    [CalcpadTokenType.Function]: 'function',
    [CalcpadTokenType.Macro]: 'macro',
    [CalcpadTokenType.MacroParameter]: 'macroParameter',
    [CalcpadTokenType.Units]: 'units',
    [CalcpadTokenType.Setting]: 'setting',
    [CalcpadTokenType.Keyword]: 'keyword',
    [CalcpadTokenType.ControlBlockKeyword]: 'controlBlockKeyword',
    [CalcpadTokenType.EndKeyword]: 'endKeyword',
    [CalcpadTokenType.Command]: 'command',
    [CalcpadTokenType.Include]: 'include',
    [CalcpadTokenType.FilePath]: 'filePath',
    [CalcpadTokenType.DataExchangeKeyword]: 'dataExchangeKeyword',
    [CalcpadTokenType.Comment]: 'comment',
    [CalcpadTokenType.HtmlComment]: 'htmlComment',
    [CalcpadTokenType.Tag]: 'tag',
    [CalcpadTokenType.HtmlContent]: 'htmlContent',
    [CalcpadTokenType.JavaScript]: 'javascript',
    [CalcpadTokenType.Css]: 'css',
    [CalcpadTokenType.Svg]: 'svg',
    [CalcpadTokenType.Input]: 'input',
    [CalcpadTokenType.Format]: 'format',
};

/**
 * Map server typeId to the index in SEMANTIC_TOKEN_TYPES array.
 * Returns -1 if typeId is unknown or None.
 */
export function mapTokenTypeToIndex(typeId: number): number {
    const tokenTypeName = TOKEN_TYPE_MAP[typeId];
    if (!tokenTypeName) {
        return -1;
    }
    return SEMANTIC_TOKEN_TYPES.indexOf(tokenTypeName);
}
