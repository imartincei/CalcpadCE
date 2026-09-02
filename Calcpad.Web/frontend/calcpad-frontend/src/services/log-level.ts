import type { CalcpadLogLevel } from '../types/settings';

/**
 * The client-side half of the log level, mirroring FileLogger.MinLevel on the server. Module
 * state rather than a parameter because every logger in every host consults it, and both hosts
 * already have exactly one place where the level changes.
 *
 * Most-severe-first, so filtering is one `<=` test.
 */
const RANK: Record<CalcpadLogLevel, number> = {
    error: 0,
    warning: 1,
    information: 2,
    verbose: 3,
};

let current: CalcpadLogLevel = 'warning';

export function setLogLevel(level: CalcpadLogLevel): void {
    current = level;
}

export function getLogLevel(): CalcpadLogLevel {
    return current;
}

/** Whether an entry at `level` should be written. Entries default to `information`. */
export function shouldLog(level: CalcpadLogLevel = 'information'): boolean {
    return RANK[level] <= RANK[current];
}
