import { findUiDirectiveBlock } from '../text/ui-directive';
import { planMetadataFooter, type LineEdit } from './metadata-footer';
import type { SourceEdit } from './ui-override-hints';
import type { UiControl, UiOverrides } from './ui-overrides';

/** One `name = value` of a `#UI` line, with 0-based columns of its trimmed right hand side. */
export interface UiAssignment {
    name: string;
    rhs: string;
    rhsStart: number;
    rhsEnd: number;
}

/** A `#UI` line's assignments and the JSON options they render with. */
export interface UiLine {
    type?: string;
    keepsUnits: boolean;
    assignments: UiAssignment[];
}

/** Mirrors `UiSyntax.EnumerateAssignments`: one assignment per code segment between comments. */
export function parseUiLine(text: string): UiLine | null {
    const block = findUiDirectiveBlock([text], 0);
    if (!block?.valid || !block.data) return null;
    const assignments: UiAssignment[] = [];
    let offset = text.length - block.tail.length;
    const tail = block.tail;
    let i = 0;
    while (i < tail.length) {
        let end: number;
        const quote = tail[i];
        if (quote === "'" || quote === '"') {
            const close = tail.indexOf(quote, i + 1);
            end = close < 0 ? tail.length : close + 1;
        } else {
            const next = tail.slice(i).search(/['"]/);
            end = next < 0 ? tail.length : i + next;
            const segment = tail.slice(i, end);
            const eq = segment.indexOf('=');
            const name = eq > 0 ? segment.slice(0, eq).trim() : '';
            if (name) {
                const raw = segment.slice(eq + 1);
                const lead = raw.length - raw.trimStart().length;
                const rhs = raw.trim();
                const rhsStart = offset + i + eq + 1 + lead;
                assignments.push({ name, rhs, rhsStart, rhsEnd: rhsStart + rhs.length });
            }
        }
        i = end;
    }
    return {
        type: block.data.type || undefined,
        keepsUnits: block.data.allowExpression !== true && block.data.forceUnits !== false,
        assignments,
    };
}

function startsWithFunction(rhs: string, name: string): boolean {
    return rhs.slice(0, name.length).toLowerCase() === name && rhs.slice(name.length).trimStart().startsWith('(');
}

function isDatagridRhs(rhs: string): boolean {
    return rhs.length > 1 && rhs.startsWith('[') && rhs.endsWith(']')
        || startsWithFunction(rhs, 'vector') || startsWithFunction(rhs, 'matrix');
}

const NUMERIC_CHAR = /[0-9.+\-eE]/;

/** Port of `ExpressionParser.ApplyUiOverride` on the right hand side; null when the engine would ignore the value. */
export function applyUiValue(rhs: string, value: string, options: Pick<UiLine, 'type' | 'keepsUnits'>): string | null {
    const type = options.type ?? (isDatagridRhs(rhs) ? 'datagrid' : 'entry');
    if (type === 'datagrid') {
        const bracketEnd = rhs.lastIndexOf(']');
        return bracketEnd < 0 || rhs.indexOf('[') < 0 ? value : value + rhs.slice(bracketEnd + 1);
    }
    const isOption = type === 'dropdown' || type === 'radio';
    const bareNumber = /\d/.test(value) && /^[0-9.+\-eE ]*$/.test(value);
    if (!options.keepsUnits || isOption && !bareNumber) return value;
    let i = 0;
    while (i < rhs.length && NUMERIC_CHAR.test(rhs[i])) i++;
    if (i > 0) return value + rhs.slice(i);
    return isOption ? value : null;
}

function nameOf(key: string): string {
    return key.split(':')[0];
}

/** The line's only assignment to the control's variable, when the control is a plain `#UI` line in this file. */
function sourceAssignment(control: UiControl, text: string | undefined): { line: UiLine; assignment: UiAssignment } | null {
    // A third key part is a loop pass.
    if (text === undefined || control.key.split(':').length !== 2) return null;
    const line = parseUiLine(text);
    const name = nameOf(control.key);
    const matches = line?.assignments.filter(a => a.name === name) ?? [];
    if (!line || matches.length !== 1 || name.includes('$') || matches[0].rhs.includes('$')) return null;
    return { line, assignment: matches[0] };
}

/** Whether the control is declared on a `#UI` line of this file, outside loops, macros and includes. */
export function isSourceUiControl(control: UiControl, lineText: (line: number) => string | undefined): boolean {
    return sourceAssignment(control, lineText(control.line)) !== null;
}

/** The saved key a control renders with, narrowest first as `ExpressionParser.TryGetUiOverride` picks it. */
export function effectiveUiOverrideKey(overrides: UiOverrides, key: string): string | null {
    for (const candidate of overrideCandidates(key))
        if (Object.prototype.hasOwnProperty.call(overrides, candidate)) return candidate;
    return null;
}

function overrideCandidates(key: string): string[] {
    const [name, ordinal] = key.split(':');
    return [...new Set([key, `${name}:${ordinal}`, name])];
}

/**
 * The keys in {@link overrides} that would shadow the source value of {@link control}, or null
 * when one of them is also what another control renders with.
 */
export function uiOverrideKeysFor(overrides: UiOverrides, control: UiControl, controls: UiControl[]): string[] | null {
    const keys = overrideCandidates(control.key).filter(k => Object.prototype.hasOwnProperty.call(overrides, k));
    const shared = controls.some(c => c.key !== control.key && keys.includes(effectiveUiOverrideKey(overrides, c.key) ?? ''));
    return shared ? null : keys;
}

/** Edits, or the reason a value can't be written into the source. */
export interface UiSourcePlan {
    /** Edits to the `#UI` lines and the footer, against the lines planned from. */
    edits: LineEdit[];
    /** Saved override keys the edits remove. */
    removed: string[];
    /** Override keys that were asked for but stay. */
    skipped: string[];
}

function rewriteLine(text: string, control: UiControl, value: string): string | null {
    const found = sourceAssignment(control, text);
    if (!found) return null;
    const { line, assignment } = found;
    const rhs = applyUiValue(assignment.rhs, value, line);
    if (rhs === null) return null;
    return text.slice(0, assignment.rhsStart) + rhs + text.slice(assignment.rhsEnd);
}

function linePlan(lines: string[], rewritten: Map<number, string>, saved: UiOverrides, keep: UiOverrides): LineEdit[] {
    const edits: LineEdit[] = [...rewritten]
        .filter(([i, text]) => lines[i] !== text)
        .map(([i, text]) => ({ line: i, endLine: i, lines: [text], isNew: false }));
    if (Object.keys(keep).length !== Object.keys(saved).length)
        edits.push(...planMetadataFooter(lines, { uiOverrides: keep }));
    return edits;
}

/**
 * Writes saved override values into the `#UI` lines that declare them and drops them from the
 * footer. Only values whose control is a plain `#UI` line of this file move; the rest are skipped.
 */
export function planUiOverridesToSource(
    lines: string[],
    saved: UiOverrides,
    controls: UiControl[],
    keys: string[] = Object.keys(saved),
): UiSourcePlan {
    const rewritten = new Map<number, string>();
    const current: UiOverrides = { ...saved };
    let pending = keys.filter(k => Object.prototype.hasOwnProperty.call(saved, k));
    // Moving one value can free a broader key another control was sharing, so repeat until stuck.
    for (let progress = true; progress && pending.length;) {
        progress = false;
        for (const key of pending) {
            if (!Object.prototype.hasOwnProperty.call(current, key)) continue;
            const users = controls.filter(c => effectiveUiOverrideKey(current, c.key) === key);
            const control = users.length === 1 ? users[0] : null;
            const shadowing = control && uiOverrideKeysFor(current, control, controls);
            if (!control || !shadowing) continue;
            const index = control.line - 1;
            const next = rewriteLine(rewritten.get(index) ?? lines[index], control, current[key]);
            if (next === null) continue;
            rewritten.set(index, next);
            for (const k of shadowing) delete current[k];
            progress = true;
        }
        pending = pending.filter(k => Object.prototype.hasOwnProperty.call(current, k));
    }
    const removed = Object.keys(saved).filter(k => !Object.prototype.hasOwnProperty.call(current, k));
    return { edits: linePlan(lines, rewritten, saved, current), removed, skipped: pending };
}

/**
 * Writes a value entered into the form into its `#UI` line, removing the saved keys that would
 * shadow it. Null when the control isn't a plain `#UI` line of this file, or another control
 * shares one of those keys.
 */
export function planUiValueToSource(
    lines: string[],
    control: UiControl,
    value: string,
    saved: UiOverrides,
    controls: UiControl[],
): { edits: LineEdit[]; removed: string[] } | null {
    const shadowing = uiOverrideKeysFor(saved, control, controls);
    if (!shadowing) return null;
    const index = control.line - 1;
    const next = rewriteLine(lines[index], control, value);
    if (next === null) return null;
    const keep = Object.fromEntries(Object.entries(saved).filter(([k]) => !shadowing.includes(k)));
    return { edits: linePlan(lines, new Map([[index, next]]), saved, keep), removed: shadowing };
}

/**
 * Plain `#UI` controls whose value an edit typed into. {@link before} and {@link edits} are in
 * the coordinates before the edits, {@link lineText} reads the document after them.
 */
export function uiControlsWithEditedValues(
    before: UiControl[],
    edits: SourceEdit[],
    lineText: (line: number) => string | undefined,
): UiControl[] {
    const lineDelta = (e: SourceEdit) => e.text.split(/\r\n|\r|\n/).length - 1 - (e.endLine - e.startLine);
    const found = new Map<string, UiControl>();
    for (const edit of edits) {
        if (edit.startLine !== edit.endLine || /[\r\n]/.test(edit.text)) continue;
        const shift = edits.filter(e => e !== edit && e.endLine < edit.startLine).reduce((sum, e) => sum + lineDelta(e), 0);
        const text = lineText(edit.startLine + shift);
        const line = text === undefined ? null : parseUiLine(text);
        if (!line) continue;
        // The edit's span after it, 0-based; touching either end counts.
        const from = edit.startColumn - 1;
        const to = from + edit.text.length;
        for (const assignment of line.assignments) {
            if (to < assignment.rhsStart || from > assignment.rhsEnd) continue;
            for (const control of before) {
                if (control.line !== edit.startLine || nameOf(control.key) !== assignment.name) continue;
                if (sourceAssignment(control, text)) found.set(control.key, control);
            }
        }
    }
    return [...found.values()];
}
