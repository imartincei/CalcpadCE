import type { InsertItem } from '../types/snippets';

const SNIPPET_PLACEHOLDER = '§';

function escapeSnippetText(text: string): string {
    return text.replace(/[\\$}]/g, '\\$&');
}

/**
 * Build a TextMate-style snippet string from an InsertItem by replacing each `§` placeholder in
 * the tag with a `${N:name}` tab stop defaulting to the parameter name (`...` for variadic
 * placeholders). Output is compatible with both Monaco and VS Code snippet insertion.
 */
export function buildInsertSnippet(item: InsertItem): string {
    const segments = item.tag.split(SNIPPET_PLACEHOLDER);
    let result = escapeSnippetText(segments[0]);
    for (let i = 1; i < segments.length; i++) {
        const param = item.parameters?.[i - 1];
        const name = param ? param.name : '...';
        result += '${' + i + ':' + escapeSnippetText(name) + '}';
        result += escapeSnippetText(segments[i]);
    }
    return result;
}

/** True when the snippet's tag contains at least one `§` placeholder. */
export function hasSnippetPlaceholders(item: InsertItem): boolean {
    return item.tag.includes(SNIPPET_PLACEHOLDER);
}

/**
 * Replace every `§` in `text` with the corresponding parameter name from
 * `item.parameters` (or `...` for variadic placeholders beyond the declared
 * parameters). Used for plain-text display (no snippet syntax) — e.g. the
 * autocomplete dropdown label and the Insert Tab.
 */
export function replaceParameterPlaceholders(
    text: string,
    item: { parameters?: { name: string }[] }
): string {
    if (!text.includes(SNIPPET_PLACEHOLDER)) return text;
    const segments = text.split(SNIPPET_PLACEHOLDER);
    let result = segments[0];
    for (let i = 1; i < segments.length; i++) {
        const param = item.parameters?.[i - 1];
        result += (param ? param.name : '...') + segments[i];
    }
    return result;
}

/**
 * Format the display label for an InsertItem: prefers `item.label` over the
 * tag, then substitutes `§` placeholders with parameter names.
 */
export function formatInsertLabel(item: InsertItem): string {
    return replaceParameterPlaceholders(item.label || item.tag, item);
}
