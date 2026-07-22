import * as vscode from 'vscode';
import { VSCodeFileSystem } from './adapters';
import { resolveIncludeDirectiveLocation } from './calcpadLocationResolver';
import { parseDirectiveLine } from './calcpadIncludeCompletionProvider';

/**
 * Renders `#include FILEPATH` paths as clickable links, using VS Code's
 * built-in link styling (underline + link color) — always visible, unlike
 * "Go to Definition" which only underlines while Ctrl is held.
 */
export class CalcpadIncludeLinkProvider implements vscode.DocumentLinkProvider {
    private fileSystem: VSCodeFileSystem;

    constructor(private outputChannel: vscode.OutputChannel) {
        this.fileSystem = new VSCodeFileSystem();
    }

    async provideDocumentLinks(document: vscode.TextDocument): Promise<vscode.DocumentLink[]> {
        const links: vscode.DocumentLink[] = [];
        for (let i = 0; i < document.lineCount; i++) {
            const line = document.lineAt(i).text;
            const parsed = parseDirectiveLine(line);
            if (!parsed || parsed.directive !== 'include') continue;
            const rawPath = parsed.partialPath.trim();
            if (!rawPath) continue;

            const location = await resolveIncludeDirectiveLocation(
                document, rawPath, this.fileSystem, this.outputChannel, '[IncludeLink]',
            );
            if (!location) continue;

            const range = new vscode.Range(i, parsed.pathStartCol, i, line.length);
            const link = new vscode.DocumentLink(range, location.uri);
            link.tooltip = `Open ${rawPath}`;
            links.push(link);
        }
        return links;
    }

    static register(outputChannel: vscode.OutputChannel): vscode.Disposable {
        return vscode.languages.registerDocumentLinkProvider(
            { language: 'calcpad' },
            new CalcpadIncludeLinkProvider(outputChannel),
        );
    }
}
