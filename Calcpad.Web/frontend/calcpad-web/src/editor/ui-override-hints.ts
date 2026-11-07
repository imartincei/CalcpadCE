import * as monaco from 'monaco-editor';
import { computeUiOverrideHints, readUiOverrides, uiOverrideHintTitle } from 'calcpad-frontend';
import type { UiControl, UiOverrideHintLabels } from 'calcpad-frontend';

const REVEAL_COMMAND = 'calcpad.revealUiOverrides';

export interface UiOverrideHintsHost {
    enabled(): boolean;
    /** Last-known controls of the document in this model, or undefined when never rendered. */
    controlsFor(model: monaco.editor.ITextModel): UiControl[] | undefined;
    /** Asked when a model with saved values has no known controls yet. */
    requestControls(model: monaco.editor.ITextModel): void;
    reveal(uri: string, line: number, keys: string[]): void;
    /** Translator from the shared i18n messages. */
    t(key: string, named?: Record<string, unknown>, plural?: number): string;
}

/** CodeLens above each line whose `#UI` controls take a saved value, linking to the Properties tab. */
export function registerUiOverrideHints(host: UiOverrideHintsHost): monaco.IDisposable & { refresh(): void } {
    const labels: UiOverrideHintLabels = {
        value: (name, value) => host.t('UI override: {name} = {value}', { name, value }),
        count: n => host.t('{n} UI override | {n} UI overrides', { n }, n),
    };
    const changed = new monaco.Emitter<monaco.languages.CodeLensProvider>();
    const provider: monaco.languages.CodeLensProvider = {
        onDidChange: changed.event,
        provideCodeLenses(model) {
            const none = { lenses: [], dispose() {} };
            if (!host.enabled()) return none;
            const overrides = readUiOverrides(model.getValue());
            if (!overrides || !Object.keys(overrides).length) return none;
            const controls = host.controlsFor(model);
            if (!controls) {
                host.requestControls(model);
                return none;
            }
            const lineCount = model.getLineCount();
            const hints = computeUiOverrideHints(overrides, controls,
                line => line >= 1 && line <= lineCount ? model.getLineContent(line) : undefined);
            return {
                lenses: hints.filter(h => h.line <= lineCount).map(h => ({
                    range: new monaco.Range(h.line, 1, h.line, 1),
                    command: {
                        id: REVEAL_COMMAND,
                        title: uiOverrideHintTitle(h, labels),
                        tooltip: host.t('Edit in the Properties tab'),
                        arguments: [model.uri.toString(), h.line, h.keys],
                    },
                })),
                dispose() {},
            };
        },
    };

    const disposables = [
        monaco.languages.registerCodeLensProvider('calcpad', provider),
        monaco.editor.registerCommand(REVEAL_COMMAND, (_accessor, uri: string, line: number, keys: string[]) =>
            host.reveal(uri, line, keys)),
        changed,
    ];
    return {
        refresh: () => changed.fire(provider),
        dispose: () => disposables.forEach(d => d.dispose()),
    };
}
