/**
 * Keywords that increase indentation (block openers).
 */
export const INDENT_INCREASE_PATTERNS: RegExp[] = [
    /^\s*#if\b/,
    /^\s*#else\s+if\b/,
    /^\s*#else\s*$/,
    /^\s*#else\s+'/,
    /^\s*#for\b/,
    /^\s*#repeat\b/,
    /^\s*#def\s+\w+\$?\s*(?:\([^)]*\))?\s*$/,
    /^\s*#def\s+\w+\$?\s*(?:\([^)]*\))?\s+'/,
];

/**
 * Keywords that decrease indentation (block closers).
 */
export const INDENT_DECREASE_PATTERNS: RegExp[] = [
    /^\s*#end\s+if\b/,
    /^\s*#else\s+if\b/,
    /^\s*#else\s*$/,
    /^\s*#else\s+'/,
    /^\s*#loop\b/,
    /^\s*#end\s+def\b/,
];

/**
 * Check if line should increase indent for the next line.
 */
export function shouldIncreaseIndent(lineText: string): boolean {
    for (const pattern of INDENT_INCREASE_PATTERNS) {
        if (pattern.test(lineText)) return true;
    }
    return false;
}

/**
 * Check if line should decrease its own indent.
 */
export function shouldDecreaseIndent(lineText: string): boolean {
    for (const pattern of INDENT_DECREASE_PATTERNS) {
        if (pattern.test(lineText)) return true;
    }
    return false;
}

/**
 * Get the indentation (leading whitespace) of a line.
 */
export function getIndentation(lineText: string): string {
    const match = /^(\s*)/.exec(lineText);
    return match ? match[1] : '';
}

/**
 * Check if line could have just completed a dedent keyword.
 */
export function couldCompleteDedentKeyword(lineText: string): boolean {
    const trimmed = lineText.trim();
    return (
        trimmed === '#else' ||
        trimmed.startsWith('#else if') ||
        trimmed === '#end if' ||
        trimmed === '#loop' ||
        trimmed === '#end def'
    );
}

/**
 * Calculate expected indentation for a dedent keyword by walking backward through lines.
 * The getLine function should return the text of the line at the given index.
 */
export function calculateExpectedIndent(
    lineNumber: number,
    getLine: (lineNumber: number) => string,
    alsoIncreases: boolean
): string {
    let depth = 1;

    for (let i = lineNumber - 1; i >= 0; i--) {
        const lineText = getLine(i);

        if (lineText.trim() === '') continue;

        if (shouldDecreaseIndent(lineText) && !shouldIncreaseIndent(lineText)) {
            depth++;
        } else if (shouldIncreaseIndent(lineText)) {
            depth--;
            if (depth === 0) {
                return getIndentation(lineText);
            }
        }
    }

    return '';
}
