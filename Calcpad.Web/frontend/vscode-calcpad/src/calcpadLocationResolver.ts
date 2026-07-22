import * as vscode from 'vscode';
import * as path from 'path';
import type { SymbolLocation } from 'calcpad-frontend';
import { VSCodeFileSystem } from './adapters';

/** Expands %VAR% (Windows) and $VAR / ${VAR} (POSIX) references against process.env. */
export function expandEnvVars(input: string): string {
    return input
        .replace(/%([^%]+)%/g, (_, n) => process.env[n] ?? '')
        .replace(/\$\{([A-Za-z_][A-Za-z0-9_]*)\}/g, (_, n) => process.env[n] ?? '')
        .replace(/\$([A-Za-z_][A-Za-z0-9_]*)/g, (_, n) => process.env[n] ?? '');
}

/**
 * Resolve a raw `#include FILEPATH` path (as typed, possibly with %VAR%/$VAR
 * env references) to a concrete `vscode.Location` pointing at the start of
 * that file. Same lookup order as resolveSymbolLocation: document dir first,
 * then a workspace-wide search; null if neither finds it.
 */
export async function resolveIncludeDirectiveLocation(
    document: vscode.TextDocument,
    rawPath: string,
    fileSystem: VSCodeFileSystem,
    outputChannel: vscode.OutputChannel,
    logPrefix: string,
): Promise<vscode.Location | null> {
    const expanded = expandEnvVars(rawPath.trim());
    if (!expanded) return null;
    const start = new vscode.Position(0, 0);

    const documentDir = path.dirname(document.uri.fsPath);
    const relativeResolved = path.resolve(documentDir, expanded);
    if (await fileSystem.exists(relativeResolved)) {
        outputChannel.appendLine(`${logPrefix} Resolved include via document dir: ${relativeResolved}`);
        return new vscode.Location(vscode.Uri.file(relativeResolved), start);
    }

    if (path.isAbsolute(expanded) && await fileSystem.exists(expanded)) {
        outputChannel.appendLine(`${logPrefix} Resolved include via absolute path: ${expanded}`);
        return new vscode.Location(vscode.Uri.file(expanded), start);
    }

    const pattern = '**/' + expanded.replace(/\\/g, '/');
    const foundFiles = await vscode.workspace.findFiles(pattern, '**/node_modules/**', 1);
    if (foundFiles.length > 0) {
        outputChannel.appendLine(`${logPrefix} Resolved include via workspace search: ${foundFiles[0].fsPath}`);
        return new vscode.Location(foundFiles[0], start);
    }

    outputChannel.appendLine(`${logPrefix} Include target not found: ${rawPath} (tried ${relativeResolved} and workspace search)`);
    return null;
}

/**
 * Resolve a `SymbolLocation` (from the symbol-at-position endpoint) into a
 * concrete `vscode.Location` the editor can jump to.
 *
 * Resolution rules for `source: 'include'` locations:
 *   1. Resolve `loc.sourceFile` against the active document's directory.
 *      That matches how the backend's `#include` resolver finds files, so
 *      we land on the same physical file the linter saw.
 *   2. If that file doesn't exist, fall back to a workspace-wide glob —
 *      handles cases where the user opened a loose file outside any folder
 *      that contains its include sibling.
 *   3. If both miss, return null so the caller can drop the location
 *      instead of synthesizing a phantom location in the active document.
 */
export async function resolveSymbolLocation(
    document: vscode.TextDocument,
    loc: SymbolLocation,
    fileSystem: VSCodeFileSystem,
    outputChannel: vscode.OutputChannel,
    logPrefix: string,
): Promise<vscode.Location | null> {
    const line = Math.max(0, loc.line);
    const range = new vscode.Range(
        new vscode.Position(line, loc.column),
        new vscode.Position(line, loc.column + loc.length),
    );

    if (!loc.sourceFile || loc.source === 'local') {
        return new vscode.Location(document.uri, range);
    }

    const documentDir = path.dirname(document.uri.fsPath);
    const relativeResolved = path.resolve(documentDir, loc.sourceFile);
    if (await fileSystem.exists(relativeResolved)) {
        outputChannel.appendLine(`${logPrefix} Resolved include via document dir: ${relativeResolved}`);
        return new vscode.Location(vscode.Uri.file(relativeResolved), range);
    }

    const pattern = '**/' + loc.sourceFile;
    const foundFiles = await vscode.workspace.findFiles(pattern, '**/node_modules/**', 1);
    if (foundFiles.length > 0) {
        outputChannel.appendLine(`${logPrefix} Resolved include via workspace search: ${foundFiles[0].fsPath}`);
        return new vscode.Location(foundFiles[0], range);
    }

    outputChannel.appendLine(`${logPrefix} Source file not found: ${loc.sourceFile} (tried ${relativeResolved} and workspace search)`);
    return null;
}
