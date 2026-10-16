/**
 * Heading extraction for the Table of Contents (TOC) sidebar tab.
 *
 * Calcpad documents support three heading syntaxes:
 *   1. Calcpad heading lines   – start with "   (default level 3)
 *   2. HTML headings in text   – '<h1>…</h1>' through '<h6>…</h6>'
 *   3. Markdown headings       – ### through ###### in either " or ' lines
 */

export interface TocHeading {
    level: number;  // 1-6
    text: string;   // Heading text content (stripped of markup)
    line: number;   // 1-indexed line number in the source document
}

/** Strip common markdown inline formatting from text. */
function stripMarkdown(text: string): string {
    return text
        .replace(/\*\*\*(.+?)\*\*\*/g, '$1')   // ***bold-italic***
        .replace(/\*\*(.+?)\*\*/g, '$1')         // **bold**
        .replace(/\*(.+?)\*/g, '$1')              // *italic*
        .replace(/\+\+(.+?)\+\+/g, '$1')         // ++underline++
        .replace(/~~(.+?)~~/g, '$1')              // ~~strikethrough~~
        .replace(/==(.+?)==/g, '$1')              // ==marked==
        .replace(/~(.+?)~/g, '$1')                // ~subscript~
        .replace(/\^(.+?)\^/g, '$1')              // ^superscript^
        .replace(/`(.+?)`/g, '$1')                // `code`
        .trim();
}

/** Strip HTML tags from text. */
function stripHtml(text: string): string {
    return text.replace(/<[^>]*>/g, '').trim();
}

// Matches markdown heading syntax: ### through ######
const MD_HEADING_RE = /^(#{3,6})\s+(.+)$/;

// Matches HTML heading tags: <h1>…</h1> through <h6>…</h6>
// Allows optional trailing ' (Calcpad text-line closing quote)
const HTML_HEADING_RE = /^<h([1-6])(?:\s[^>]*)?>(.+?)<\/h\1>'?$/i;

/**
 * Parse all headings from Calcpad source text.
 *
 * @param text  The full document text (as typed in the editor)
 * @returns     Array of headings with level, cleaned text, and 1-based line number
 */
export function parseHeadings(text: string): TocHeading[] {
    const headings: TocHeading[] = [];
    const lines = text.split(/\r?\n/);

    for (let i = 0; i < lines.length; i++) {
        const raw = lines[i].trimStart();
        if (raw.length === 0) continue;

        const firstChar = raw[0];
        const lineNumber = i + 1; // 1-indexed

        if (firstChar === '"') {
            // Calcpad heading line
            const content = raw.slice(1).trim();
            if (!content) continue;

            // Check for markdown heading syntax inside the heading line
            const mdMatch = content.match(MD_HEADING_RE);
            if (mdMatch) {
                headings.push({
                    level: mdMatch[1].length,
                    text: stripMarkdown(stripHtml(mdMatch[2])),
                    line: lineNumber,
                });
            } else {
                // Plain Calcpad heading – default level 3
                headings.push({
                    level: 3,
                    text: stripMarkdown(stripHtml(content)),
                    line: lineNumber,
                });
            }
        } else if (firstChar === '\'') {
            // Text / comment line – check for HTML or markdown headings
            const content = raw.slice(1).trim();
            if (!content) continue;

            // Check for HTML heading tag
            const htmlMatch = content.match(HTML_HEADING_RE);
            if (htmlMatch) {
                headings.push({
                    level: parseInt(htmlMatch[1], 10),
                    text: stripMarkdown(stripHtml(htmlMatch[2])),
                    line: lineNumber,
                });
                continue;
            }

            // Check for markdown heading syntax
            const mdMatch = content.match(MD_HEADING_RE);
            if (mdMatch) {
                headings.push({
                    level: mdMatch[1].length,
                    text: stripMarkdown(stripHtml(mdMatch[2])),
                    line: lineNumber,
                });
            }
        }
    }

    return headings;
}
