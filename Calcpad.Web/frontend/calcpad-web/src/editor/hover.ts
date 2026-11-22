import * as monaco from 'monaco-editor';
import type {
    MacroDefinition,
    FunctionDefinition,
    VariableDefinition,
    CustomUnitDefinition,
} from 'calcpad-frontend/types/api';
import { getVariableTypeAt } from 'calcpad-frontend/text/completion-format';
import { buildBuiltinDocMarkdown, extractFunctionName } from './builtin-docs';
import { getActiveDocumentKey, type EditorBridge } from './bridge';

/**
 * Single-character test for CalcPad identifier chars: ASCII letters/digits/underscore,
 * comma (used in indexed forms), and any non-ASCII Unicode (Greek letters etc.).
 */
const IDENT_CHAR = /[A-Za-z0-9_,-￿]/;

/** Where a symbol's "Source:" file links to at a hover position, or null for no link. */
export type SourceLinker = (
    model: monaco.editor.ITextModel,
    position: monaco.Position,
    sourceFile: string,
) => Promise<monaco.Uri | null>;

export function registerHoverProvider(bridge: EditorBridge, linkSource?: SourceLinker): monaco.IDisposable {
    return monaco.languages.registerHoverProvider('calcpad', {
        async provideHover(model, position) {
            const lineText = model.getLineContent(position.lineNumber);
            const match = findWordAround(lineText, position.column - 1);
            if (!match) return null;

            const word = match.word;
            const range = new monaco.Range(
                position.lineNumber, match.start + 1,
                position.lineNumber, match.end + 1,
            );

            const definitions = bridge.definitions.getCachedDefinitions(getActiveDocumentKey());
            const link = async (symbol: { source: string; sourceFile?: string }) =>
                symbol.source !== 'local' && symbol.sourceFile && linkSource
                    ? (await linkSource(model, position, symbol.sourceFile))?.toString()
                    : undefined;
            if (definitions) {
                const macro = definitions.macros.find(m => m.name === word);
                if (macro) return { contents: [{ value: buildMacroMarkdown(macro, await link(macro)), isTrusted: true }], range };

                // As in Core, a name is a function only when "(" directly follows it.
                const func = definitions.functions.find(f => f.name === word);
                const variable = definitions.variables.find(v => v.name === word);
                if (func && (!variable || lineText[match.end] === '(')) {
                    return { contents: [{ value: buildFunctionMarkdown(func, await link(func)), isTrusted: true }], range };
                }
                if (variable) return { contents: [{ value: buildVariableMarkdown(variable, position.lineNumber - 1, await link(variable)), isTrusted: true }], range };

                const unit = definitions.customUnits.find(u => u.name === word);
                if (unit) return { contents: [{ value: buildCustomUnitMarkdown(unit, await link(unit)), isTrusted: true }], range };
            }

            if (bridge.snippets.isLoaded()) {
                const builtin = bridge.snippets.getAllItems().find(item => {
                    if (item.keywordType !== 'Function') return false;
                    return extractFunctionName(item.tag) === word;
                });
                if (builtin) {
                    return { contents: [{ value: buildBuiltinDocMarkdown(builtin), isTrusted: true }], range };
                }
            }

            return null;
        },
    });
}

function findWordAround(lineText: string, column: number): { word: string; start: number; end: number } | null {
    let start = column;
    let end = column;
    while (start > 0 && IDENT_CHAR.test(lineText[start - 1])) start--;
    while (end < lineText.length && IDENT_CHAR.test(lineText[end])) end++;
    if (start === end) return null;
    let word = lineText.substring(start, end);
    let realEnd = end;
    if (lineText[end] === '$') {
        word += '$';
        realEnd = end + 1;
    }
    return { word, start, end: realEnd };
}

function buildMacroMarkdown(macro: MacroDefinition, sourceLink?: string): string {
    const out: string[] = [];
    const paramStr = macro.parameters.length > 0 ? `(${macro.parameters.join('; ')})` : '';
    out.push('```calcpad\n' + macro.name + paramStr + '\n```');

    if (macro.source !== 'local' && macro.sourceFile) {
        out.push(sourceLine(macro.sourceFile, sourceLink));
    }
    if (macro.description) out.push(macro.description);
    appendParameterDocs(out, macro.parameters, macro.paramTypes, macro.paramDescriptions);
    return out.join('\n\n');
}

function buildFunctionMarkdown(func: FunctionDefinition, sourceLink?: string): string {
    const out: string[] = [];
    const paramStr = func.parameters.length > 0 ? `(${func.parameters.join('; ')})` : '()';
    out.push('```calcpad\n' + func.name + paramStr + '\n```');

    if (func.source !== 'local' && func.sourceFile) {
        out.push(sourceLine(func.sourceFile, sourceLink));
    }
    if (func.description) out.push(func.description);
    if (func.returnType) out.push('Returns: *' + func.returnType + '*');
    appendParameterDocs(out, func.parameters, func.paramTypes, func.paramDescriptions);
    if (func.expression) {
        out.push('**Expression:**\n```calcpad\n' + func.expression + '\n```');
    }
    return out.join('\n\n');
}

function buildVariableMarkdown(variable: VariableDefinition, line: number, sourceLink?: string): string {
    const out: string[] = [];
    out.push('```calcpad\n' + variable.name + ' = ' + (variable.expression ?? '') + '\n```');
    if (variable.source !== 'local' && variable.sourceFile) {
        out.push(sourceLine(variable.sourceFile, sourceLink));
    }
    const type = getVariableTypeAt(variable, line);
    if (type) out.push('Type: *' + type + '*');
    if (variable.description) out.push(variable.description);
    return out.join('\n\n');
}

function buildCustomUnitMarkdown(unit: CustomUnitDefinition, sourceLink?: string): string {
    const out: string[] = [];
    out.push('```calcpad\n.' + unit.name + ' = ' + (unit.expression ?? '') + '\n```');
    if (unit.source !== 'local' && unit.sourceFile) {
        out.push(sourceLine(unit.sourceFile, sourceLink));
    }
    if (unit.description) out.push(unit.description);
    return out.join('\n\n');
}

function sourceLine(sourceFile: string, link?: string): string {
    return link ? `Source: [\`${sourceFile}\`](<${link}>)` : 'Source: `' + sourceFile + '`';
}

function appendParameterDocs(
    out: string[],
    params?: string[],
    paramTypes?: string[],
    paramDescriptions?: string[],
): void {
    if (!params || params.length === 0) return;

    const hasTypes = !!paramTypes && paramTypes.length > 0;
    const hasDescs = !!paramDescriptions && paramDescriptions.length > 0;

    if (hasTypes || hasDescs) {
        const lines: string[] = ['**Parameters:**'];
        for (let i = 0; i < params.length; i++) {
            const name = params[i];
            const type = hasTypes && i < paramTypes!.length ? paramTypes![i] : undefined;
            const desc = hasDescs && i < paramDescriptions!.length ? paramDescriptions![i] : undefined;
            let line = '- `' + name + '`';
            if (type) line += ` *(${type})*`;
            if (desc) line += ' — ' + desc;
            lines.push(line);
        }
        out.push(lines.join('\n'));
    } else {
        out.push('Parameters: `' + params.join('; ') + '`');
    }
}
