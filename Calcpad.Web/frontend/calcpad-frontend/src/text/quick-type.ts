/**
 * Find quick type replacement at the given position.
 * Checks for patterns starting with ~ and up to 4 characters after ~.
 */
export function findQuickTypeReplacement(lineText: string, endPosition: number, quickTypeMap: Map<string, string>): {
    startPos: number;
    endPos: number;
    replacement: string;
} | null {
    const maxLength = 5; // ~ + up to 4 characters

    for (let len = 2; len <= maxLength && len <= endPosition; len++) {
        const startPos = endPosition - len;
        const candidate = lineText.substring(startPos, endPosition);

        if (candidate[0] === '~') {
            const replacement = quickTypeMap.get(candidate);
            if (replacement) {
                return { startPos, endPos: endPosition, replacement };
            }
        }
    }

    return null;
}
