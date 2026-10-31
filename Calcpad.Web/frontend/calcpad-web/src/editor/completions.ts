import * as monaco from 'monaco-editor';
import {
    completionRangeStart,
    formatBuiltinSnippetCompletion,
    formatFunctionCompletion,
    formatMacroCompletion,
    formatVariableCompletion,
    isCompletableBuiltin,
} from 'calcpad-frontend/text/completion-format';
import type { CompletionData, CompletionKind } from 'calcpad-frontend/text/completion-format';
import { getActiveDocumentKey, type EditorBridge } from './bridge';
import { parseDirectiveLine } from 'calcpad-frontend';

/**
 * Register a CompletionItemProvider that surfaces:
 *   1. User-defined macros, functions, and variables (including those pulled
 *      in via #include) from the cached definitions for the active document.
 *   2. Built-in snippets (functions, commands, constants, etc.) from the
 *      snippet service. Units are excluded.
 */
export function registerCompletionProvider(bridge: EditorBridge): monaco.IDisposable {
    const provider: monaco.languages.CompletionItemProvider = {
        triggerCharacters: ['#', '$', '.'],

        provideCompletionItems(model, position) {
            // Inside a #include/#read/#write/#append/#ProjectPath/#LibraryPath
            // path, only registerIncludeCompletionProvider's file suggestions
            // should show.
            const line = model.getLineContent(position.lineNumber);
            if (parseDirectiveLine(line.substring(0, position.column - 1))) return { suggestions: [] };

            const word = model.getWordUntilPosition(position);
            const range: monaco.IRange = {
                startLineNumber: position.lineNumber,
                startColumn: completionRangeStart(line, word.startColumn - 1) + 1,
                endLineNumber: position.lineNumber,
                endColumn: word.endColumn,
            };

            const suggestions: monaco.languages.CompletionItem[] = [];

            const defs = bridge.definitions.getCachedDefinitions(getActiveDocumentKey());
            if (defs) {
                for (const macro of defs.macros) suggestions.push(toMonaco(formatMacroCompletion(macro), range));
                for (const func of defs.functions) suggestions.push(toMonaco(formatFunctionCompletion(func), range));
                for (const variable of defs.variables) suggestions.push(toMonaco(formatVariableCompletion(variable), range));
            }

            for (const item of bridge.snippets.getAllItems()) {
                if (!isCompletableBuiltin(item)) continue;
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
