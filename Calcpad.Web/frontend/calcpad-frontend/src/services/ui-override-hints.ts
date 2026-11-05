import type { UiControl, UiOverrides } from './ui-overrides';

/** A saved `#UI` value (or several) shown above the source line it applies to. */
export interface UiOverrideHint {
    /** 1-based line the hint sits above. */
    line: number;
    /** Saved override keys in effect on this line. */
    keys: string[];
    /** `name = value` for a lone control declared on a `#UI` line, else null. */
    value: { name: string; value: string } | null;
}

export interface UiOverrideHintLabels {
    value: (name: string, value: string) => string;
    count: (n: number) => string;
}

const ENGLISH_LABELS: UiOverrideHintLabels = {
    value: (name, value) => `UI override: ${name} = ${value}`,
    count: n => `${n} UI override${n === 1 ? '' : 's'}`,
};

export function uiOverrideHintTitle(hint: UiOverrideHint, labels: UiOverrideHintLabels = ENGLISH_LABELS): string {
    return hint.value ? labels.value(hint.value.name, hint.value.value) : labels.count(hint.keys.length);
}

/** The saved key a control renders with, narrowest first as `ExpressionParser.ApplyUiOverride` picks it. */
function effectiveKey(overrides: UiOverrides, key: string): string | null {
    const [name, ordinal] = key.split(':');
    for (const candidate of [key, `${name}:${ordinal}`, name])
        if (Object.prototype.hasOwnProperty.call(overrides, candidate)) return candidate;
    return null;
}

const UI_LINE = /^\s*#ui\b/i;

/**
 * Hints for the lines whose controls take a saved value. A line holding one control that it
 * declares itself shows the value; a loop body, an `#include` or a macro call shows a count.
 */
export function computeUiOverrideHints(
    overrides: UiOverrides,
    controls: UiControl[],
    lineText: (line: number) => string | undefined,
): UiOverrideHint[] {
    const byLine = new Map<number, UiControl[]>();
    for (const control of controls) {
        const list = byLine.get(control.line);
        if (list) list.push(control);
        else byLine.set(control.line, [control]);
    }

    const hints: UiOverrideHint[] = [];
    for (const [line, lineControls] of byLine) {
        const keys = [...new Set(lineControls
            .map(c => effectiveKey(overrides, c.key))
            .filter((k): k is string => k !== null))];
        if (!keys.length) continue;
        const only = lineControls.length === 1 ? lineControls[0] : null;
        const simple = only && only.key.split(':').length === 2 && UI_LINE.test(lineText(line) ?? '');
        hints.push({
            line,
            keys,
            value: simple ? { name: only.key.split(':')[0], value: overrides[keys[0]] } : null,
        });
    }
    return hints.sort((a, b) => a.line - b.line);
}

/** An edit in 1-based editor coordinates, with the range it replaced. */
export interface SourceEdit {
    startLine: number;
    startColumn: number;
    endLine: number;
    endColumn: number;
    text: string;
}

/**
 * Controls moved to follow edits made since they were rendered. Edits within one event refer
 * to the document before any of them, so they are applied bottom-up.
 */
export function shiftUiControls(controls: UiControl[], edits: SourceEdit[]): UiControl[] {
    const sorted = [...edits].sort((a, b) => b.startLine - a.startLine || b.startColumn - a.startColumn);
    let result = controls;
    for (const edit of sorted) {
        const inserted = edit.text.split(/\r\n|\r|\n/).length - 1;
        const delta = inserted - (edit.endLine - edit.startLine);
        if (delta === 0) continue;
        // Text inserted at the very start of a line pushes that line down with it.
        const pushesStart = edit.startColumn === 1 && edit.startLine === edit.endLine && edit.startColumn === edit.endColumn;
        const removesStart = edit.startColumn === 1 && edit.endColumn === 1 && edit.endLine > edit.startLine;
        result = result.flatMap(c => {
            if (c.line > edit.endLine || (pushesStart && c.line === edit.startLine))
                return [{ ...c, line: c.line + delta }];
            if (c.line === edit.endLine && c.line > edit.startLine)
                return [{ ...c, line: edit.startLine + inserted }];
            // Lines the edit replaced whole take their controls with them.
            if (c.line > edit.startLine || (removesStart && c.line === edit.startLine))
                return [];
            return [c];
        });
    }
    return result;
}

export interface UiOverrideHintTrackerOptions {
    /** Renders a document's form and returns its controls, or null when it should not or could not. */
    resolve: (docKey: string) => Promise<UiControl[] | null>;
    /** A document's hints may have changed. */
    changed: (docKey: string) => void;
    /** Quiet time after an edit before the controls are rendered afresh. */
    idleMs?: number;
}

/**
 * Last-known `#UI` controls per document, for hints. Unlike the Properties tab's list these
 * survive edits, following them by line until a render made after the edit replaces them.
 */
export class UiOverrideHintTracker {
    private readonly controls = new Map<string, UiControl[]>();
    private readonly stale = new Set<string>();
    private readonly versions = new Map<string, number>();
    private readonly inFlight = new Set<string>();
    private readonly timers = new Map<string, ReturnType<typeof setTimeout>>();

    constructor(private readonly options: UiOverrideHintTrackerOptions) {}

    controlsFor(docKey: string): UiControl[] | undefined {
        return this.controls.get(docKey);
    }

    setControls(docKey: string, controls: UiControl[]): void {
        this.controls.set(docKey, controls);
        this.stale.delete(docKey);
        this.options.changed(docKey);
    }

    applyEdits(docKey: string, edits: SourceEdit[]): void {
        const current = this.controls.get(docKey);
        if (current) {
            const shifted = shiftUiControls(current, edits);
            if (shifted !== current) {
                this.controls.set(docKey, shifted);
                this.options.changed(docKey);
            }
        }
        this.stale.add(docKey);
        this.versions.set(docKey, (this.versions.get(docKey) ?? 0) + 1);
        const timer = this.timers.get(docKey);
        if (timer) clearTimeout(timer);
        this.timers.set(docKey, setTimeout(() => {
            this.timers.delete(docKey);
            void this.ensure(docKey);
        }, this.options.idleMs ?? 1500));
    }

    /** Renders the document when its controls are unknown or older than its last edit. */
    async ensure(docKey: string): Promise<void> {
        if (this.inFlight.has(docKey)) return;
        if (this.controls.has(docKey) && !this.stale.has(docKey)) return;
        this.inFlight.add(docKey);
        const version = this.versions.get(docKey);
        try {
            const controls = await this.options.resolve(docKey);
            // An edit landed mid-render; its own timer renders again.
            if (controls && this.versions.get(docKey) === version) this.setControls(docKey, controls);
        } finally {
            this.inFlight.delete(docKey);
        }
    }

    forget(docKey: string): void {
        const timer = this.timers.get(docKey);
        if (timer) clearTimeout(timer);
        this.timers.delete(docKey);
        this.controls.delete(docKey);
        this.stale.delete(docKey);
        this.versions.delete(docKey);
    }

    dispose(): void {
        for (const timer of this.timers.values()) clearTimeout(timer);
        this.timers.clear();
    }
}
