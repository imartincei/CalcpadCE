import * as vscode from 'vscode';
import type { InsertItem, SnippetParameterDto } from 'calcpad-frontend';

/**
 * Extract the function name from a snippet's Insert tag. Calcpad built-in function names are
 * ASCII with an optional trailing `$`, e.g. "sin(§)" -> "sin", "len$(§)" -> "len$", "π" -> null.
 */
export function extractFunctionName(tag: string): string | null {
    const match = tag.match(/^([A-Za-z_][A-Za-z0-9_]*\$?)\(/);
    return match ? match[1] : null;
}

/**
 * Build a parameter list string for the signature line, e.g. "(x; y)" or "(x; [y])".
 */
function buildSignatureParamList(params: SnippetParameterDto[] | undefined): string {
    if (!params || params.length === 0) {
        return '()';
    }

    const parts = params.map(p => {
        let name = p.name;
        if (p.isVariadic) name = `...${name}`;
        return p.isOptional ? `[${name}]` : name;
    });
    return `(${parts.join('; ')})`;
}

/**
 * Append a Markdown parameter table for a built-in function.
 * Mirrors the visual style of CalcpadHoverProvider.appendParameterDocs so user-defined
 * and built-in hovers feel consistent.
 */
function appendParameterTable(md: vscode.MarkdownString, params: SnippetParameterDto[]): void {
    if (params.length === 0) return;

    md.appendMarkdown('**Parameters:**\n');
    for (const p of params) {
        let line = `- \`${p.name}\``;

        const typeLabel = p.typeDescription ?? (p.type && p.type !== 'Any' ? p.type : undefined);
        if (typeLabel) line += ` *(${typeLabel})*`;

        if (p.description) line += ` — ${p.description}`;

        if (p.isVariadic) line += ' *(variadic)*';
        else if (p.isOptional) line += ' *(optional)*';

        md.appendMarkdown(line + '\n');
    }
    md.appendMarkdown('\n');
}

/**
 * Build the full hover/completion Markdown for a built-in snippet item.
 * Used by both CalcpadHoverProvider and CalcpadCompletionProvider so the two stay in sync.
 */
export function buildBuiltinDocMarkdown(item: InsertItem): vscode.MarkdownString {
    const md = new vscode.MarkdownString();
    md.isTrusted = true;
    md.supportThemeIcons = true;

    const funcName = extractFunctionName(item.tag);
    const displayName = funcName ?? item.label ?? item.tag;

    // Signature
    if (funcName) {
        md.appendCodeblock(`${funcName}${buildSignatureParamList(item.parameters)}`, 'calcpad');
    } else {
        md.appendCodeblock(displayName, 'calcpad');
    }

    // Short label (e.g. "Sine") - skip if it duplicates the function name
    if (item.description && item.description.toLowerCase() !== displayName.toLowerCase()) {
        md.appendMarkdown(`**${item.description}**\n\n`);
    }

    // Long-form documentation
    if (item.documentation) {
        md.appendMarkdown(item.documentation + '\n\n');
    }

    // Parameter table
    if (item.parameters && item.parameters.length > 0) {
        appendParameterTable(md, item.parameters);
    }

    // Return type
    const returnLabel = item.returnTypeDescription
        ?? (item.returnType && item.returnType !== 'Any' ? item.returnType : undefined);
    if (returnLabel) {
        md.appendMarkdown(`**Returns:** *${returnLabel}*\n\n`);
    }

    // Element-wise note (useful context for vector/matrix-capable scalar functions)
    if (item.isElementWise) {
        md.appendMarkdown('*Accepts a scalar, vector, or matrix — applied element-wise, returning the same shape.*\n\n');
    }

    // Example
    if (item.example) {
        md.appendMarkdown('**Example:**\n');
        md.appendCodeblock(item.example, 'calcpad');
    }

    // Category footer
    if (item.categoryPath) {
        md.appendMarkdown(`\n---\n*${item.categoryPath}*`);
    }

    return md;
}
