export type InlineFormat = 'bold' | 'italic' | 'underline' | 'subscript' | 'superscript';
export type CommentFormat = 'html' | 'markdown';
export type ParseMode = 'cpd' | CommentFormat;

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

/**
 * Parse mode in effect at the 0-based `line`, from the #html/#cpd/#markdown directives above it.
 * Openers push and any #end form pops, as in ExpressionParser.
 */
export function getParseModeAt(getLine: (index: number) => string, line: number): ParseMode {
    const stack: ParseMode[] = [];
    let mode: ParseMode = 'cpd';
    for (let i = 0; i < line; i++) {
        const text = getLine(i).trim();
        if (/^#end (html|cpd|markdown)(\s|$)/i.test(text)) {
            mode = stack.pop() ?? 'cpd';
            continue;
        }
        const opener = /^#(html|cpd|markdown)(\s|$)/i.exec(text);
        if (opener) {
            stack.push(mode);
            mode = opener[1].toLowerCase() as ParseMode;
        }
    }
    return mode;
}

/**
 * Split a line into [indent, content, trailingQuote]. Raw lines, in #html/#markdown mode,
 * have no comment quotes to strip.
 */
function splitContent(lineText: string, raw: boolean): [indent: string, content: string, trailingQuote: string] {
    return raw ? [...splitIndent(lineText), ''] : stripCommentPrefix(lineText);
}

/** Build the replacement line text for a heading hotkey, preserving indentation. */
export function buildHeadingLine(lineText: string, level: number, format: CommentFormat, raw = false): string {
    const [indent, rawContent, trailingQuote] = splitContent(lineText, raw);
    const quote = raw ? '' : "'";
    let content = rawContent;

    const htmlMatch = content.match(/^<h[1-6]>(.*)<\/h[1-6]>$/);
    if (htmlMatch) content = htmlMatch[1];
    const mdMatch = content.match(/^(#{1,6})\s+(.*)$/);
    if (mdMatch) content = mdMatch[2];

    if (format === 'html') {
        return `${indent}${quote}<h${level}>${content}</h${level}>${trailingQuote}`;
    }
    return `${indent}${quote}${'#'.repeat(level)} ${content}${trailingQuote}`;
}

/** Build the replacement line text for the paragraph hotkey, preserving indentation. */
export function buildParagraphLine(lineText: string, raw = false): string {
    const [indent, content, trailingQuote] = splitContent(lineText, raw);
    return `${indent}${raw ? '' : "'"}<p>${content}</p>${trailingQuote}`;
}

/**
 * Build the replacement lines for a bulleted/numbered list hotkey. The
 * wrapper tags (<ul>/<ol>) take the first selected line's indentation; each
 * item keeps its own line's indentation.
 */
export function buildListLines(lineTexts: string[], format: CommentFormat, ordered: boolean, raw = false): string[] {
    if (lineTexts.length === 0) return [];
    const [wrapperIndent] = splitIndent(lineTexts[0]);
    const q = raw ? '' : "'";
    const lines: string[] = [];

    if (format === 'html') {
        lines.push(`${wrapperIndent}${q}<${ordered ? 'ol' : 'ul'}>`);
        for (const lineText of lineTexts) {
            const [indent, content, tq] = splitContent(lineText, raw);
            lines.push(`${indent}${q}<li>${content}</li>${tq}`);
        }
        lines.push(`${wrapperIndent}${q}</${ordered ? 'ol' : 'ul'}>`);
    } else if (ordered) {
        let num = 1;
        for (const lineText of lineTexts) {
            const [indent, content, tq] = splitContent(lineText, raw);
            lines.push(`${indent}${q}${num}. ${content}${tq}`);
            num++;
        }
    } else {
        for (const lineText of lineTexts) {
            const [indent, content, tq] = splitContent(lineText, raw);
            lines.push(`${indent}${q}- ${content}${tq}`);
        }
    }

    return lines;
}

/** True if the line, after its indentation, is wrapped in an HTML comment. */
export function isHtmlCommentLine(lineText: string): boolean {
    const [, rest] = splitIndent(lineText);
    return rest.startsWith('<!--') && rest.trimEnd().endsWith('-->');
}

/** Wrap a line in an HTML comment, the comment syntax for #html/#markdown content. */
export function wrapHtmlComment(lineText: string): string {
    const [indent, rest] = splitIndent(lineText);
    return `${indent}<!-- ${rest} -->`;
}

/** Remove the HTML comment that {@link wrapHtmlComment} added, if any. */
export function unwrapHtmlComment(lineText: string): string {
    if (!isHtmlCommentLine(lineText)) return lineText;
    const [indent, rest] = splitIndent(lineText);
    const inner = rest.trimEnd().slice(4, -3);
    return indent + inner.replace(/^ /, '').replace(/ $/, '');
}
