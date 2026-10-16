import type { InsertItem } from 'calcpad-frontend/types/snippets';
import type { SnippetParameterDto } from 'calcpad-frontend/types/snippets';

/**
 * Extract the function name from a snippet's Insert tag.
 * E.g. "sin(§)" → "sin", "len$(§)" → "len$", "π" → null.
 */
export function extractFunctionName(tag: string): string | null {
    const match = tag.match(/^([A-Za-z_][A-Za-z0-9_]*\$?)\(/);
    return match ? match[1] : null;
}

function buildSignatureParamList(params: SnippetParameterDto[] | undefined): string {
    if (!params || params.length === 0) return '()';
    const parts = params.map(p => {
        let name = p.name;
        if (p.isVariadic) name = '...' + name;
        return p.isOptional ? `[${name}]` : name;
    });
    return `(${parts.join('; ')})`;
}

function appendParameterTable(lines: string[], params: SnippetParameterDto[]): void {
    if (params.length === 0) return;
    lines.push('**Parameters:**');
    for (const p of params) {
        let line = '- `' + p.name + '`';
        const typeLabel = p.typeDescription ?? (p.type && p.type !== 'Any' ? p.type : undefined);
        if (typeLabel) line += ` *(${typeLabel})*`;
        if (p.description) line += ' — ' + p.description;
        if (p.isVariadic) line += ' *(variadic)*';
        else if (p.isOptional) line += ' *(optional)*';
        lines.push(line);
    }
    lines.push('');
}

/**
 * Build the markdown body for a built-in snippet hover/completion.
 * Returns a plain string suitable for `monaco.IMarkdownString.value`.
 */
export function buildBuiltinDocMarkdown(item: InsertItem): string {
    const out: string[] = [];
    const funcName = extractFunctionName(item.tag);
    const displayName = funcName ?? item.label ?? item.tag;

    if (funcName) {
        out.push('```calcpad\n' + funcName + buildSignatureParamList(item.parameters) + '\n```');
    } else {
        out.push('```calcpad\n' + displayName + '\n```');
    }

    if (item.description && item.description.toLowerCase() !== displayName.toLowerCase()) {
        out.push('**' + item.description + '**');
    }

    if (item.documentation) {
        out.push(item.documentation);
    }

    if (item.parameters && item.parameters.length > 0) {
        appendParameterTable(out, item.parameters);
    }

    const returnLabel = item.returnTypeDescription
        ?? (item.returnType && item.returnType !== 'Any' ? item.returnType : undefined);
    if (returnLabel) {
        out.push('**Returns:** *' + returnLabel + '*');
    }

    if (item.isElementWise) {
        out.push('*Accepts a scalar, vector, or matrix — applied element-wise, returning the same shape.*');
    }

    if (item.example) {
        out.push('**Example:**\n```calcpad\n' + item.example + '\n```');
    }

    if (item.categoryPath) {
        out.push('---\n*' + item.categoryPath + '*');
    }

    return out.join('\n\n');
}
