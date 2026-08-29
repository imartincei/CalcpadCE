/**
 * Logging adapter interface: vscode.OutputChannel in the extension, console or a file logger
 * in the Electron app.
 */
export interface ILogger {
    appendLine(message: string): void;
}

/**
 * File system adapter interface: vscode.workspace.fs in the extension, Node.js fs in the
 * Electron app.
 */
export interface IFileSystem {
    readFile(path: string): Promise<Uint8Array>;
    exists(path: string): Promise<boolean>;
}
