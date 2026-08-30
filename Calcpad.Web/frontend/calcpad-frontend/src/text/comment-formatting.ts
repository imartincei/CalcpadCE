export type InlineFormat = 'bold' | 'italic' | 'underline' | 'subscript' | 'superscript';
export type CommentFormat = 'html' | 'markdown';

export const HTML_INLINE: Record<InlineFormat, [string, string]> = {
    bold: ['<strong>', '</strong>'],
    italic: ['<em>', '</em>'],
    underline: ['<ins>', '</ins>'],
    subscript: ['<sub>', '</sub>'],
    superscript: ['<sup>', '</sup>'],
};

export const MARKDOWN_INLINE: Record<InlineFormat, [string, string]> = {
    bold: ['**', '**'],
    italic: ['*', '*'],
    underline: ['++', '++'],
    subscript: ['~', '~'],
    superscript: ['^', '^'],
};

/** Length of the leading run of spaces/tabs on a line. */
export function getIndentLength(lineText: string): number {
    const m = lineText.match(/^[ \t]*/);
    return m ? m[0].length : 0;
}

/** Split a line into its leading indentation and the rest. */
export function splitIndent(lineText: string): [indent: string, rest: string] {
    const len = getIndentLength(lineText);
    return [lineText.slice(0, len), lineText.slice(len)];
}

/**
 * Strip the indentation and comment quote (') from a line, returning
 * [indent, content, trailingQuote]. The quote is looked for after the
 * indentation, not at column 1, so indented comment lines round-trip.
 */
export function stripCommentPrefix(lineText: string): [indent: string, content: string, trailingQuote: string] {
    const [indent, rest] = splitIndent(lineText);
    if (rest.startsWith("'")) {
        const inner = rest.substring(1);
        if (inner.endsWith("'")) {
            return [indent, inner.slice(0, -1), "'"];
        }
        return [indent, inner, ''];
    }
    return [indent, rest, ''];
}

/** True if the line already opens a comment (a ' right after its indentation). */
export function lineHasCommentPrefix(lineText: string): boolean {
    const [, rest] = splitIndent(lineText);
    return rest.startsWith("'");
}

/**
 * True if the character at the given 0-based column already sits inside a
 * text/comment region, mirroring the Calcpad tokenizer's rules: a ' switches
 * the rest of the line to a plain-text comment, and "..." wraps an inline
 * text string within code. Anything inside those regions is emitted as text
 * (HTML tags render), so a formatting hotkey there needs no comment quote.
 */
export function isColumnInTextContext(lineText: string, column: number): boolean {
    let inQuote = false;
    const end = Math.min(column, lineText.length);
    for (let i = 0; i < end; i++) {
        const ch = lineText[i];
        if (inQuote) {
            if (ch === '"') inQuote = false;
        } else if (ch === "'") {
            return true;
        } else if (ch === '"') {
            inQuote = true;
        }
    }
    return inQuote;
}

/**
 * 1-based column at which a missing comment quote should be inserted — right after the line's
 * indentation — or null if none is needed, since HTML/markdown formatting hotkeys need their
 * tags inside a comment to render. No quote is needed when the line already opens a comment, or
 * when the selection already lands inside a text region mid-line, where inserting one would
 * wrongly comment out the preceding code.
 */
export function getCommentPrefixInsertColumn(lineText: string, selectionColumn?: number): number | null {
    const indentLen = getIndentLength(lineText);
    if (lineText.slice(indentLen).startsWith("'")) return null;
    if (selectionColumn !== undefined && isColumnInTextContext(lineText, selectionColumn)) return null;
    return indentLen + 1;
}

/** Build the replacement line text for a heading hotkey, preserving indentation. */
export function buildHeadingLine(lineText: string, level: number, format: CommentFormat): string {
    const [indent, rawContent, trailingQuote] = stripCommentPrefix(lineText);
    let content = rawContent;

    const htmlMatch = content.match(/^<h[1-6]>(.*)<\/h[1-6]>$/);
    if (htmlMatch) content = htmlMatch[1];
    const mdMatch = content.match(/^(#{1,6})\s+(.*)$/);
    if (mdMatch) content = mdMatch[2];

    if (format === 'html') {
        return `${indent}'<h${level}>${content}</h${level}>${trailingQuote}`;
    }
    return `${indent}'${'#'.repeat(level)} ${content}${trailingQuote}`;
}

/** Build the replacement line text for the paragraph hotkey, preserving indentation. */
export function buildParagraphLine(lineText: string): string {
    const [indent, content, trailingQuote] = stripCommentPrefix(lineText);
    return `${indent}'<p>${content}</p>${trailingQuote}`;
}

/**
 * Build the replacement lines for a bulleted/numbered list hotkey. The
 * wrapper tags (<ul>/<ol>) take the first selected line's indentation; each
 * item keeps its own line's indentation.
 */
export function buildListLines(lineTexts: string[], format: CommentFormat, ordered: boolean): string[] {
    if (lineTexts.length === 0) return [];
    const [wrapperIndent] = splitIndent(lineTexts[0]);
    const lines: string[] = [];

    if (format === 'html') {
        lines.push(`${wrapperIndent}'<${ordered ? 'ol' : 'ul'}>`);
        for (const lineText of lineTexts) {
            const [indent, content, tq] = stripCommentPrefix(lineText);
            lines.push(`${indent}'<li>${content}</li>${tq}`);
        }
        lines.push(`${wrapperIndent}'</${ordered ? 'ol' : 'ul'}>`);
    } else if (ordered) {
        let num = 1;
        for (const lineText of lineTexts) {
            const [indent, content, tq] = stripCommentPrefix(lineText);
            lines.push(`${indent}'${num}. ${content}${tq}`);
            num++;
        }
    } else {
        for (const lineText of lineTexts) {
            const [indent, content, tq] = stripCommentPrefix(lineText);
            lines.push(`${indent}'- ${content}${tq}`);
        }
    }

    return lines;
}
