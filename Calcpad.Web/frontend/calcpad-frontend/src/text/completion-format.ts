import type {
    MacroDefinition,
    FunctionDefinition,
    VariableDefinition,
    CustomUnitDefinition,
} from '../types/api';
import type { InsertItem } from '../types/snippets';
import {
    buildInsertSnippet,
    formatInsertLabel,
    hasSnippetPlaceholders,
} from './snippet-insert';

/**
 * Editor-neutral completion data shared by the Monaco (calcpad-web) and
 * VS Code (vscode-calcpad) providers. Each consumer maps `kind` and the
 * presence of `isSnippet` to its own CompletionItem / CompletionItemKind /
 * SnippetString equivalents.
 */
export type CompletionKind = 'macro' | 'function' | 'variable' | 'unit' | 'snippet';

export interface CompletionData {
    label: string;
    kind: CompletionKind;
    insertText: string;
    /** True when `insertText` uses TextMate snippet syntax (`${1:name}`). */
    isSnippet: boolean;
    detail: string;
    /** Markdown documentation. */
    documentation: string;
    sortText: string;
}

const SORT_USER = '0_';
const SORT_BUILTIN = '1_';

function escapeSnippetText(text: string): string {
    return text.replace(/[\\$}]/g, '\\$&');
}

/**
 * Build a snippet body from raw parameter names: `${1:a}; ${2:b}`. Strips
 * the `$` / `?` suffixes and any `:type` annotation from each parameter so
 * the placeholder text is just the readable name.
 */
export function buildParameterSnippet(params: string[]): string {
    return params.map((param, index) => {
        let clean = param.trim();
        if (clean.endsWith('$')) clean = clean.slice(0, -1);
        if (clean.endsWith('?')) clean = clean.slice(0, -1);
        clean = clean.split(':')[0].trim();
        return '${' + (index + 1) + ':' + escapeSnippetText(clean) + '}';
    }).join('; ');
}

export function buildParameterizedDoc(
    heading: string,
    description: string | undefined,
    params: string[] | undefined,
    paramTypes: string[] | undefined,
    paramDescriptions: string[] | undefined,
    sourceFile: string | undefined,
    defaults: (string | null)[] | undefined,
): string {
    let doc = `**${heading}**`;
    if (sourceFile) doc += `\n\nSource: \`${sourceFile}\``;
    if (description) doc += '\n\n' + description;

    if (params && params.length > 0) {
        const hasTypes = !!paramTypes && paramTypes.length > 0;
        const hasDescs = !!paramDescriptions && paramDescriptions.length > 0;
        const hasDefaults = !!defaults && defaults.length > 0;

        if (hasTypes || hasDescs || hasDefaults) {
            doc += '\n\n**Parameters:**';
            for (let i = 0; i < params.length; i++) {
                const name = params[i];
                const type = hasTypes && i < paramTypes!.length ? paramTypes![i] : undefined;
                const desc = hasDescs && i < paramDescriptions!.length ? paramDescriptions![i] : undefined;
                const def = hasDefaults && i < defaults!.length ? defaults![i] : undefined;
                let line = `\n- \`${name}\``;
                if (type) line += ` *(${type})*`;
                if (desc) line += ` — ${desc}`;
                if (def !== undefined && def !== null) {
                    line += ` *(default: ${def})*`;
                } else if (hasDefaults) {
                    line += ` *(required)*`;
                }
                doc += line;
            }
        } else {
            doc += '\n\nParameters: `' + params.join('; ') + '`';
        }
    }

    return doc;
}

function externalSourceLabel(source: string, sourceFile: string | undefined): string | undefined {
    return source !== 'local' ? (sourceFile || source) : undefined;
}

export function formatMacroCompletion(macro: MacroDefinition): CompletionData {
    const hasParams = macro.parameters.length > 0;
    const insertText = hasParams
        ? macro.name + '(' + buildParameterSnippet(macro.parameters) + ')'
        : macro.name;
    const source = externalSourceLabel(macro.source, macro.sourceFile);
    return {
        label: macro.name,
        kind: 'macro',
        insertText,
        isSnippet: hasParams,
        detail: macro.description || (hasParams ? 'Macro(' + macro.parameters.join('; ') + ')' : 'Macro'),
        documentation: buildParameterizedDoc(
            'User-defined macro', macro.description,
            macro.parameters, macro.paramTypes, macro.paramDescriptions,
            source, macro.defaults,
        ),
        sortText: SORT_USER + macro.name,
    };
}

export function formatFunctionCompletion(func: FunctionDefinition): CompletionData {
    const source = externalSourceLabel(func.source, func.sourceFile);
    return {
        label: func.name,
        kind: 'function',
        insertText: func.name + '(' + buildParameterSnippet(func.parameters) + ')',
        isSnippet: true,
        detail: func.description || ('Function(' + func.parameters.join('; ') + ')'),
        documentation: buildParameterizedDoc(
            'User-defined function', func.description,
            func.parameters, func.paramTypes, func.paramDescriptions,
            source, func.defaults,
        ),
        sortText: SORT_USER + func.name,
    };
}

export function formatVariableCompletion(variable: VariableDefinition): CompletionData {
    let doc = '**User-defined variable**\n\nValue: `' + (variable.expression || '') + '`';
    if (variable.source !== 'local' && variable.sourceFile) {
        doc += '\n\nSource: `' + variable.sourceFile + '`';
    }
    if (variable.description) doc += '\n\n' + variable.description;
    return {
        label: variable.name,
        kind: 'variable',
        insertText: variable.name,
        isSnippet: false,
        detail: variable.description || ('Variable = ' + (variable.expression || '')),
        documentation: doc,
        sortText: SORT_USER + variable.name,
    };
}

export function formatCustomUnitCompletion(unit: CustomUnitDefinition): CompletionData {
    let doc = '**Custom unit**\n\nDefinition: `' + (unit.expression || '') + '`';
    if (unit.source !== 'local' && unit.sourceFile) {
        doc += '\n\nSource: `' + unit.sourceFile + '`';
    }
    return {
        label: unit.name,
        kind: 'unit',
        insertText: unit.name,
        isSnippet: false,
        detail: 'Custom Unit = ' + (unit.expression || ''),
        documentation: doc,
        sortText: SORT_USER + unit.name,
    };
}

export function formatBuiltinSnippetCompletion(item: InsertItem): CompletionData {
    const asSnippet = hasSnippetPlaceholders(item);
    const label = formatInsertLabel(item);
    return {
        label,
        kind: 'snippet',
        insertText: asSnippet ? buildInsertSnippet(item) : item.tag,
        isSnippet: asSnippet,
        detail: item.categoryPath || 'Built-in',
        documentation: item.description,
        sortText: SORT_BUILTIN + label,
    };
}
