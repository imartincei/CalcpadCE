/**
 * Shared crash-report formatting for the bundled Calcpad.Server.
 *
 * Both hosts capture the same two artifacts when the server dies of an
 * unrecoverable fault (StackOverflow / FailFast / access violation) that
 * bypasses the in-process FileLogger: a .NET `createdump` minidump and its
 * sibling `<dump>.crashreport.json`. This module renders them into one
 * human-readable `last-crash.txt` so the VS Code extension and the Tauri
 * desktop shell produce an identical record.
 *
 * Everything here is pure — no fs/path/Tauri APIs. Each host does its own IO
 * (reading the crashreport.json, writing the record) with whatever file API it
 * has (Node `fs`, or `@tauri-apps/plugin-fs`).
 */

export interface CrashRecordInput {
    /** ISO 8601 crash timestamp. */
    timestampIso: string;
    /** Process exit code, if the host observed one. */
    code?: number | null;
    /** POSIX signal name, if any (Node exposes this; Tauri does not). */
    signal?: string | null;
    /** Tail of the process's stderr / combined stdio at crash time. */
    stderrTail?: string;
    /** Raw contents of `<dump>.crashreport.json`, if createdump produced one. */
    reportJson?: string | null;
}

/**
 * Map a .NET runtime exit code to a human-readable label. Codes come back as
 * signed 32-bit ints, so mask to unsigned before comparing.
 */
export function decodeExitCode(code: number | null): string {
    if (code === null) return '';
    const u = code >>> 0;
    switch (u) {
        case 0x00000000: return '(success)';
        case 0xC0000005: return '(STATUS_ACCESS_VIOLATION)';
        case 0xC00000FD: return '(STATUS_STACK_OVERFLOW)';
        case 0xC000013A: return '(STATUS_CONTROL_C_EXIT — Ctrl+C)';
        case 0x80131623: return '(COR_E_FAILFAST — Environment.FailFast)';
        case 0x80131506: return '(COR_E_EXECUTIONENGINE)';
        case 0x80131500: return '(CLR generic exception)';
        default: return `(0x${u.toString(16).toUpperCase()})`;
    }
}

interface StackFrame {
    is_managed?: string;
    method_name?: string;
    filename?: string;
    unmanaged_name?: string;
    native_module?: string;
}
interface CrashThread {
    crashed?: string;
    native_thread_id?: string;
    managed_exception_type?: string;
    managed_exception_hresult?: string;
    stack_frames?: StackFrame[];
}
interface CrashPayload {
    process_name?: string;
    configuration?: { version?: string; architecture?: string };
    threads?: CrashThread[];
}

/**
 * Parse a .NET createdump `*.crashreport.json` string and render the crashed
 * thread's managed exception + stack as a traceback-style block. Returns null
 * if the JSON is missing/malformed or has no crashed thread.
 *
 * Frames are in createdump's order (most-recent-first). Managed frames show the
 * method name (+ defining file when known); native frames show the unmanaged
 * symbol, else the module.
 */
export function formatCrashReportPayload(rawJson: string): string | null {
    let parsed: { payload?: CrashPayload };
    try {
        parsed = JSON.parse(rawJson);
    } catch {
        return null;
    }
    const payload = parsed?.payload;
    if (!payload || !Array.isArray(payload.threads)) return null;

    const crashed = payload.threads.find(t => t?.crashed === 'true');
    if (!crashed) return null;

    const out: string[] = [];
    out.push(`Process: ${payload.process_name ?? '(unknown)'}`);
    const cfg = payload.configuration;
    if (cfg) out.push(`Runtime: ${cfg.version ?? '(unknown)'} (${cfg.architecture ?? '?'})`);

    if (crashed.managed_exception_type) {
        out.push('');
        const hr = crashed.managed_exception_hresult ? ` (HRESULT ${crashed.managed_exception_hresult})` : '';
        out.push(`Exception: ${crashed.managed_exception_type}${hr}`);
    }

    const frames = Array.isArray(crashed.stack_frames) ? crashed.stack_frames : [];
    out.push('');
    out.push(`Stack trace (most recent call first, thread ${crashed.native_thread_id ?? '?'}):`);
    if (frames.length === 0) {
        out.push('  (no frames)');
    }
    for (const f of frames) {
        if (f?.is_managed === 'true' && f.method_name) {
            out.push(`  at ${f.method_name}${f.filename ? `  [${f.filename}]` : ''}`);
        } else if (f?.unmanaged_name) {
            out.push(`  at ${f.unmanaged_name}  [${f.native_module ?? 'native'}]`);
        } else if (f?.native_module) {
            out.push(`  at <native>  [${f.native_module}]`);
        }
    }
    return out.join('\n');
}

/**
 * Assemble the full `last-crash.txt` record: header, exit code / signal, the
 * captured stderr tail, and the managed traceback parsed from the crashreport
 * JSON (when present).
 */
export function buildCrashRecord(input: CrashRecordInput): string {
    const { timestampIso, code = null, signal = null, stderrTail = '', reportJson = null } = input;
    const sections: string[] = [];
    sections.push('Calcpad.Server crash record');
    sections.push(`Timestamp: ${timestampIso}`);
    if (code !== null || signal !== null) {
        const hex = ((code ?? 0) >>> 0).toString(16).toUpperCase();
        const decoded = decodeExitCode(code);
        // decodeExitCode falls back to the bare hex for unknown codes — drop it
        // then so the line doesn't read "(0x86) (0x86)".
        const label = decoded && decoded !== `(0x${hex})` ? ` ${decoded}` : '';
        sections.push(`Exit code: ${code} (0x${hex})${label}`);
        sections.push(`Signal: ${signal ?? '(none)'}`);
    }
    sections.push('');
    sections.push('--- last stderr ---');
    sections.push(stderrTail || '(empty)');
    sections.push('');

    const formatted = reportJson ? formatCrashReportPayload(reportJson) : null;
    if (formatted) {
        sections.push('--- managed traceback ---', formatted, '');
    }
    return sections.join('\n');
}
