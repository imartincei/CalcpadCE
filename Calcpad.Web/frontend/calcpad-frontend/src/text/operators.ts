/**
 * Mapping of C-style operator sequences to their Unicode replacements.
 */
export const OPERATOR_REPLACEMENTS: Record<string, string> = {
    '==': '≡',
    '!=': '≠',
    '>=': '≥',
    '<=': '≤',
    '%%': '⦼',
    '&&': '∧',
    '||': '∨',
    '<*': '←'
};

/**
 * Check if a character can trigger an operator replacement.
 */
export function isOperatorTriggerChar(char: string): boolean {
    return ['=', '%', '&', '|', '*'].includes(char);
}

/**
 * Check if position is inside a string literal or comment.
 */
export function isInsideStringOrComment(lineText: string, position: number): boolean {
    let inString = false;
    let stringChar = '';
    let inComment = false;

    for (let i = 0; i < position; i++) {
        const char = lineText[i];

        if (!inString && !inComment) {
            if (char === '"' || char === "'") {
                inString = true;
                stringChar = char;
            } else if (char === "'") {
                inComment = true;
            }
        } else if (inString && char === stringChar) {
            if (i === 0 || lineText[i - 1] !== '\\') {
                inString = false;
                stringChar = '';
            }
        }
    }

    return inString || inComment;
}

/**
 * Find operator replacement at the given end position in the line text.
 * Returns the replacement info or null if no replacement found.
 */
export function findOperatorReplacement(lineText: string, endPosition: number): {
    startPos: number;
    endPos: number;
    replacement: string;
} | null {
    if (endPosition >= 2) {
        const twoChar = lineText.substring(endPosition - 2, endPosition);
        if (OPERATOR_REPLACEMENTS[twoChar]) {
            return {
                startPos: endPosition - 2,
                endPos: endPosition,
                replacement: OPERATOR_REPLACEMENTS[twoChar]
            };
        }
    }
    return null;
}
