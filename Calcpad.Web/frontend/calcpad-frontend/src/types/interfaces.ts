import type { CalcpadLogLevel } from './settings';

/**
 * Logging adapter interface: vscode.OutputChannel in the extension, the in-app Output panel in
 * the desktop app. Implementations drop entries below the current level (see services/log-level),
 * so callers may log freely at `verbose`. Omitting the level means `information`.
 */
export interface ILogger {
    appendLine(message: string, level?: CalcpadLogLevel): void;
}

/**
 * File system adapter interface: vscode.workspace.fs in the extension, Node.js fs in the
 * desktop app.
 */
export interface IFileSystem {
    readFile(path: string): Promise<Uint8Array>;
    exists(path: string): Promise<boolean>;
}
