import type { CalcpadError } from '../types/api';

/** A 1-based source line linked to a run error. */
export interface RunErrorLink {
    line: number;
    message: string;
    messageId?: string;
    params?: Record<string, string | number>;
}

export interface RunErrorMark extends CalcpadError {
    related: RunErrorLink[];
    messageId?: string;
    params?: Record<string, string | number>;
}

/**
 * Links a run error raised inside custom functions to their bodies, and marks each body
 * once, from the first call that failed in it.
 */
export function expandRunErrors(errors: CalcpadError[]): RunErrorMark[] {
    const marks: RunErrorMark[] = [];
    const bodies = new Set<string>();
    for (const err of errors) {
        const frames = err.functions ?? [];
        if (frames.length === 0) {
            marks.push({ ...err, related: [] });
            continue;
        }
        const inner = frames[0];
        const outer = frames[frames.length - 1];
        marks.push({
            ...err,
            message: `${err.message} In the body of ${inner.name} on line ${inner.sourceLine}.`,
            messageId: 'run.errorInBody',
            params: { message: err.message, name: inner.name, line: inner.sourceLine },
            related: frames.map((f, i) => ({
                line: f.sourceLine,
                message: i === 0 ? `Error in the body of ${f.name}` : `Called in the body of ${f.name}`,
                messageId: i === 0 ? 'run.errorBody' : 'run.calledInBody',
                params: { name: f.name },
            })),
        });
        frames.forEach((f, i) => {
            const key = `${f.sourceLine}|${err.code}|${err.message}`;
            if (bodies.has(key)) return;
            bodies.add(key);
            marks.push({
                ...err,
                sourceLine: f.sourceLine,
                startColumn: -1,
                endColumn: -1,
                additionalRanges: undefined,
                functions: undefined,
                message: i === 0
                    ? `${err.message} At the call on line ${err.sourceLine}.`
                    : `${err.message} In ${inner.name}, at the call on line ${err.sourceLine}.`,
                messageId: i === 0 ? 'run.atCall' : 'run.inAtCall',
                params: { message: err.message, name: inner.name, line: err.sourceLine },
                related: [{
                    line: err.sourceLine,
                    message: `Call to ${outer.name}`,
                    messageId: 'run.callTo',
                    params: { name: outer.name },
                }],
            });
        });
    }
    return marks;
}
