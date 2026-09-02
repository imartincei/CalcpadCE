import * as vscode from 'vscode';
import type { ILogger, IFileSystem, CalcpadLogLevel } from 'calcpad-frontend';
import { shouldLog } from 'calcpad-frontend';

/**
 * An output channel behind the shared level filter. Entries below the current level are dropped
 * rather than written, so the channel stays readable during normal editing; `show`/`clear`/
 * `dispose` are forwarded so this can stand in for the channel itself.
 */
export class VSCodeLogger implements ILogger {
    constructor(private channel: vscode.OutputChannel) {}
    appendLine(msg: string, level?: CalcpadLogLevel): void {
        if (!shouldLog(level)) return;
        this.channel.appendLine(msg);
    }
    show(preserveFocus?: boolean): void { this.channel.show(preserveFocus); }
    clear(): void { this.channel.clear(); }
    dispose(): void { this.channel.dispose(); }
}

export class VSCodeFileSystem implements IFileSystem {
    async readFile(filePath: string): Promise<Uint8Array> {
        return vscode.workspace.fs.readFile(vscode.Uri.file(filePath));
    }
    async exists(filePath: string): Promise<boolean> {
        try {
            await vscode.workspace.fs.stat(vscode.Uri.file(filePath));
            return true;
        } catch {
            return false;
        }
    }
}
