import * as monaco from 'monaco-editor';
import {
    formatBuiltinSnippetCompletion,
    formatCustomUnitCompletion,
    formatFunctionCompletion,
    formatMacroCompletion,
    formatVariableCompletion,
} from 'calcpad-frontend/text/completion-format';
import type { CompletionData, CompletionKind } from 'calcpad-frontend/text/completion-format';
import { getActiveDocumentKey, type EditorBridge } from './bridge';
import { parseDirectiveLine } from './include-completions';

/**
 * Register a CompletionItemProvider that surfaces:
 *   1. User-defined macros, functions, variables, and custom units (including
 *      those pulled in via #include) from the cached definitions for the
 *      active document.
 *   2. Built-in snippets (functions, commands, units, constants, etc.) from
 *      the snippet service.
 */
export function registerCompletionProvider(bridge: EditorBridge): monaco.IDisposable {
    const provider: monaco.languages.CompletionItemProvider = {
        triggerCharacters: ['#', '$', '.'],

        provideCompletionItems(model, position) {
            // Inside a #include/#read/#write/#append/#ProjectPath/#LibraryPath
            // path, only registerIncludeCompletionProvider's file suggestions
            // should show.
            const lineToCursor = model.getLineContent(position.lineNumber).substring(0, position.column - 1);
            if (parseDirectiveLine(lineToCursor)) return { suggestions: [] };

            const word = model.getWordUntilPosition(position);
            const range: monaco.IRange = {
                startLineNumber: position.lineNumber,
                startColumn: word.startColumn,
                endLineNumber: position.lineNumber,
                endColumn: word.endColumn,
            };

            const suggestions: monaco.languages.CompletionItem[] = [];

            const defs = bridge.definitions.getCachedDefinitions(getActiveDocumentKey());
            if (defs) {
                for (const macro of defs.macros) suggestions.push(toMonaco(formatMacroCompletion(macro), range));
                for (const func of defs.functions) suggestions.push(toMonaco(formatFunctionCompletion(func), range));
                for (const variable of defs.variables) suggestions.push(toMonaco(formatVariableCompletion(variable), range));
                for (const unit of defs.customUnits) suggestions.push(toMonaco(formatCustomUnitCompletion(unit), range));
            }

            // Only snippets that define a keyword (function, command, unit, constant,
            // operator, setting, control keyword). UI-only entries like HTML tags,
            // markdown syntax, symbols, and block templates are excluded.
            for (const item of bridge.snippets.getAllItems()) {
                if (!item.keywordType) continue;
                suggestions.push(toMonaco(formatBuiltinSnippetCompletion(item), range));
            }

            return { suggestions };
        },
    };

    return monaco.languages.registerCompletionItemProvider('calcpad', provider);
}

const KIND_MAP: Record<CompletionKind, monaco.languages.CompletionItemKind> = {
    macro: monaco.languages.CompletionItemKind.Class,
    function: monaco.languages.CompletionItemKind.Function,
    variable: monaco.languages.CompletionItemKind.Variable,
    unit: monaco.languages.CompletionItemKind.Unit,
    snippet: monaco.languages.CompletionItemKind.Snippet,
};

function toMonaco(data: CompletionData, range: monaco.IRange): monaco.languages.CompletionItem {
    return {
        label: data.label,
        kind: KIND_MAP[data.kind],
        insertText: data.insertText,
        insertTextRules: data.isSnippet
            ? monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet
            : undefined,
        detail: data.detail,
        documentation: { value: data.documentation },
        sortText: data.sortText,
        range,
    };
}
