import type { LintDiagnostic, LintRelatedLocation } from '../types/api';
import { pathBasename } from './paths';

/** A base file's lint diagnostic, placed in a file it includes. */
export interface IncludedLintDiagnostic {
    /** The host's key for the base file the lint ran on. */
    base: string;
    basePath: string;
    /** The included file. */
    filePath: string;
    line: number;
    /** -1 for the whole line. */
    column: number;
    endColumn: number;
    code: string;
    message: string;
    severityId: number;
    source: string;
    /** Locations in the base file, starting with where it reports the error. */
    related: LintRelatedLocation[];
}

/** Compares file paths, ignoring separators, and case on Windows paths. */
export function includePathKey(path: string): string {
    const slashed = path.replace(/\\/g, '/');
    return /^[a-zA-Z]:\//.test(slashed) ? slashed.toLowerCase() : slashed;
}

/** Each base file's latest lint diagnostics, grouped by the included file they belong in. */
export class IncludedLintStore {
    private byBase = new Map<string, Map<string, IncludedLintDiagnostic[]>>();

    /** Replaces a base file's diagnostics. */
    public set(base: string, basePath: string, diagnostics: LintDiagnostic[]): void {
        const baseName = pathBasename(basePath);
        const byFile = new Map<string, IncludedLintDiagnostic[]>();
        for (const d of diagnostics) {
            for (const origin of d.includeOrigins ?? []) {
                const key = includePathKey(origin.filePath);
                const list = byFile.get(key) ?? [];
                list.push({
                    base,
                    basePath,
                    filePath: origin.filePath,
                    line: origin.line,
                    column: origin.column,
                    endColumn: origin.endColumn,
                    code: d.code,
                    message: `From ${baseName}: ${origin.message}`,
                    severityId: d.severityId,
                    source: d.source,
                    related: [
                        { line: d.line, column: d.column, endColumn: d.endColumn, message: `Reported in ${baseName}` },
                        ...(d.related ?? []),
                    ],
                });
                byFile.set(key, list);
            }
        }
        if (byFile.size > 0) this.byBase.set(base, byFile);
        else this.byBase.delete(base);
    }

    public delete(base: string): void {
        this.byBase.delete(base);
    }

    /** Drops the base files `keep` rejects. */
    public prune(keep: (base: string) => boolean): void {
        for (const base of [...this.byBase.keys()])
            if (!keep(base)) this.byBase.delete(base);
    }

    /** An error a base file reports at a 0-based position that is in a file named like `fileName`. */
    public errorAt(base: string, line: number, column: number, fileName: string): IncludedLintDiagnostic | undefined {
        const name = pathBasename(fileName);
        for (const list of this.byBase.get(base)?.values() ?? [])
            for (const d of list) {
                const at = d.related[0];
                if (at.line === line && column >= at.column && column <= at.endColumn && pathBasename(d.filePath) === name)
                    return d;
            }
        return undefined;
    }

    /** The diagnostics for the included file at `path` from the base files `isOpen` accepts. */
    public get(path: string, isOpen: (base: string) => boolean): IncludedLintDiagnostic[] {
        const key = includePathKey(path);
        return [...this.byBase].flatMap(([base, byFile]) => isOpen(base) ? byFile.get(key) ?? [] : []);
    }
}
