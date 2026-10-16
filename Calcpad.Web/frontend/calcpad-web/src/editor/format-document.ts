import * as monaco from 'monaco-editor';
import type { EditorBridge } from './bridge';

/**
 * Wires up Shift+Alt+F (Monaco's Format Document) to call the server's prettify endpoint.
 */
export function registerFormatDocumentProvider(bridge: EditorBridge): monaco.IDisposable {
    return monaco.languages.registerDocumentFormattingEditProvider('calcpad', {
        async provideDocumentFormattingEdits(model, options) {
            const indentUnit = options.insertSpaces ? ' '.repeat(options.tabSize) : '\t';
            const response = await bridge.api.prettify(model.getValue(), indentUnit, true);
            if (!response?.content) return [];

            const fullRange = model.getFullModelRange();
            return [{ range: fullRange, text: response.content }];
        },
    });
}
