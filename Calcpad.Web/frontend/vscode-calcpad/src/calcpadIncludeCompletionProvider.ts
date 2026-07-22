import * as vscode from 'vscode';
import * as path from 'path';
import { CalcpadSettingsManager } from './calcpadSettings';

const DIRECTIVES = ['include', 'read', 'write', 'append'] as const;
type Directive = typeof DIRECTIVES[number];

const INCLUDE_EXTENSIONS = ['cpd', 'txt'];

export interface DirectiveParse {
    directive: Directive;
    /** Column index where the file path begins */
    pathStartCol: number;
    /** The partial file path typed so far (everything from pathStartCol to end of line) */
    partialPath: string;
}

/**
 * Parse a line to extract the directive type and file path portion.
 * Syntax:
 *   #include FILEPATH
 *   #read varName from FILEPATH[@options...]
 *   #write varName to FILEPATH[@options...]
 *   #append varName to FILEPATH[@options...]
 *
 * Returns undefined if the line isn't a recognized directive or
 * the cursor hasn't reached the file path portion yet.
 */
export function parseDirectiveLine(lineText: string): DirectiveParse | undefined {
    let i = 0;

    // Skip leading whitespace
    while (i < lineText.length && (lineText[i] === ' ' || lineText[i] === '\t')) { i++; }

    // Expect '#'
    if (i >= lineText.length || lineText[i] !== '#') { return undefined; }
    i++;

    // Read the directive keyword
    const keywordStart = i;
    while (i < lineText.length && lineText[i] !== ' ' && lineText[i] !== '\t') { i++; }
    const keyword = lineText.substring(keywordStart, i).toLowerCase() as Directive;

    if (!DIRECTIVES.includes(keyword)) { return undefined; }

    // Skip whitespace after directive keyword
    const afterKeyword = i;
    while (i < lineText.length && (lineText[i] === ' ' || lineText[i] === '\t')) { i++; }
    // Need at least one space after the directive keyword
    if (i === afterKeyword) { return undefined; }

    if (keyword === 'include') {
        // #include FILEPATH — path starts here
        return { directive: keyword, pathStartCol: i, partialPath: lineText.substring(i) };
    }

    // For #read/#write/#append: skip variable name, then expect 'from' or 'to'
    // Skip variable name (non-whitespace token)
    while (i < lineText.length && lineText[i] !== ' ' && lineText[i] !== '\t') { i++; }

    // Skip whitespace after variable name
    const afterVar = i;
    while (i < lineText.length && (lineText[i] === ' ' || lineText[i] === '\t')) { i++; }
    if (i === afterVar) { return undefined; } // No space after variable name yet

    // Read the connecting keyword ('from' for #read, 'to' for #write/#append)
    const connStart = i;
    while (i < lineText.length && lineText[i] !== ' ' && lineText[i] !== '\t') { i++; }
    const connector = lineText.substring(connStart, i).toLowerCase();

    const expectedConnector = keyword === 'read' ? 'from' : 'to';
    if (connector !== expectedConnector) { return undefined; }

    // Skip whitespace after connector — path starts after this
    const afterConn = i;
    while (i < lineText.length && (lineText[i] === ' ' || lineText[i] === '\t')) { i++; }
    if (i === afterConn) { return undefined; } // No space after 'from'/'to' yet

    return { directive: keyword, pathStartCol: i, partialPath: lineText.substring(i) };
}

export class CalcpadIncludeCompletionProvider implements vscode.CompletionItemProvider {
    private outputChannel: vscode.OutputChannel;

    constructor(outputChannel: vscode.OutputChannel) {
        this.outputChannel = outputChannel;
    }

    async provideCompletionItems(
        document: vscode.TextDocument,
        position: vscode.Position,
        token: vscode.CancellationToken,
        context: vscode.CompletionContext
    ): Promise<vscode.CompletionList | undefined> {
        const lineText = document.lineAt(position.line).text.substring(0, position.character);

        const parsed = parseDirectiveLine(lineText);
        if (!parsed) {
            return undefined;
        }

        const { directive, pathStartCol } = parsed;
        const isInclude = directive === 'include';

        this.outputChannel.appendLine(`[INCLUDE COMPLETION] Triggered on line: "${lineText}" (directive: #${directive})`);

        // Strip any trailing options (@sheet, type=, sep=) from the partial path for completion
        let partialPath = parsed.partialPath;
        const atIndex = partialPath.indexOf('@');
        if (atIndex !== -1) {
            partialPath = partialPath.substring(0, atIndex);
        }

        this.outputChannel.appendLine(`[INCLUDE COMPLETION] Partial path: "${partialPath}"`);

        const documentDir = path.dirname(document.uri.fsPath);
        const completionItems: vscode.CompletionItem[] = [];
        const addedEntries = new Set<string>();

        const extensions = isInclude
            ? INCLUDE_EXTENSIONS
            : [...INCLUDE_EXTENSIONS, 'csv', 'tsv', 'xlsx', 'xlsm', 'xls'];

        // Replace range covers from the start of the file path to the cursor
        const replaceRange = new vscode.Range(
            position.line, pathStartCol,
            position.line, position.character
        );

        // Get library configuration
        const libraryPath = CalcpadSettingsManager.getInstance().getExtra('libraryPath', '');
        const libraryPrefix = libraryPath
            ? (libraryPath.endsWith('/') || libraryPath.endsWith('\\') ? libraryPath : libraryPath + '\\')
            : '';
        const resolvedLibraryPath = libraryPath ? this.resolveLibraryPath(libraryPath) : undefined;

        this.outputChannel.appendLine(`[INCLUDE COMPLETION] Library: raw="${libraryPath}" prefix="${libraryPrefix}" resolved="${resolvedLibraryPath || '(none)'}"`);

        try {
            // Determine if the user is navigating inside a library path
            // (i.e. they already selected a library folder and are drilling deeper)
            const isInLibrary = libraryPrefix && partialPath.startsWith(libraryPrefix);

            if (isInLibrary && resolvedLibraryPath) {
                // User is inside a library path - strip the prefix and search the library subdirectory
                const libraryRelativePath = partialPath.substring(libraryPrefix.length);
                this.outputChannel.appendLine(`[INCLUDE COMPLETION] Inside library, relative path: "${libraryRelativePath}"`);

                await this.addEntriesFromDirectory(
                    resolvedLibraryPath, libraryRelativePath, extensions, replaceRange,
                    document.uri.fsPath, completionItems, addedEntries, token,
                    'Library', '', libraryPrefix
                );
            } else if (partialPath.includes('/') || partialPath.includes('\\')) {
                // User is navigating local subdirectories (path has separators but isn't library)
                this.outputChannel.appendLine(`[INCLUDE COMPLETION] Navigating local subdirectory`);

                await this.addEntriesFromDirectory(
                    documentDir, partialPath, extensions, replaceRange,
                    document.uri.fsPath, completionItems, addedEntries, token,
                    undefined, ''
                );
            } else {
                // Root level - show local files/folders + library + workspace folders
                this.outputChannel.appendLine(`[INCLUDE COMPLETION] Root level - searching local + library + workspace`);

                await this.addEntriesFromDirectory(
                    documentDir, partialPath, extensions, replaceRange,
                    document.uri.fsPath, completionItems, addedEntries, token,
                    undefined, ''
                );

                // Also search library root if configured and different from document dir
                if (resolvedLibraryPath && path.normalize(resolvedLibraryPath) !== path.normalize(documentDir)) {
                    this.outputChannel.appendLine(`[INCLUDE COMPLETION] Also searching library root: ${resolvedLibraryPath}`);
                    await this.addEntriesFromDirectory(
                        resolvedLibraryPath, '', extensions, replaceRange,
                        document.uri.fsPath, completionItems, addedEntries, token,
                        'Library', '', libraryPrefix
                    );
                }

                // Also search each open workspace folder. Skip any folder that
                // is the doc dir or the library — those are already covered.
                // Workspace-folder entries get absolute-path insert text so
                // #include resolves regardless of the current file's location.
                const workspaceFolders = vscode.workspace.workspaceFolders ?? [];
                const docDirNorm = path.normalize(documentDir);
                const libNorm = resolvedLibraryPath ? path.normalize(resolvedLibraryPath) : '';
                for (const folder of workspaceFolders) {
                    const folderPath = folder.uri.fsPath;
                    const folderNorm = path.normalize(folderPath);
                    if (folderNorm === docDirNorm || (libNorm && folderNorm === libNorm)) {
                        continue;
                    }
                    this.outputChannel.appendLine(`[INCLUDE COMPLETION] Also searching workspace folder: ${folderPath}`);
                    await this.addEntriesFromDirectory(
                        folderPath, '', extensions, replaceRange,
                        document.uri.fsPath, completionItems, addedEntries, token,
                        'Workspace', '', ''
                    );
                }
            }

            this.outputChannel.appendLine(`[INCLUDE COMPLETION] Returning ${completionItems.length} items (isIncomplete=true)`);
        } catch (error) {
            this.outputChannel.appendLine(`[INCLUDE COMPLETION ERROR] ${error}`);
        }

        // Return as incomplete so VS Code re-invokes the provider on each keystroke
        return new vscode.CompletionList(completionItems, true);
    }

    private async addEntriesFromDirectory(
        baseDir: string,
        relativePath: string,
        extensions: string[],
        replaceRange: vscode.Range,
        currentFilePath: string,
        completionItems: vscode.CompletionItem[],
        addedEntries: Set<string>,
        token: vscode.CancellationToken,
        sourceLabel?: string,
        insertPrefix: string = '',
        libraryPrefix: string = ''
    ): Promise<void> {
        let searchDir: string;
        let pathPrefix: string;

        if (relativePath.includes('/') || relativePath.includes('\\')) {
            // User has typed a subdirectory path - navigate into it
            const lastSep = Math.max(relativePath.lastIndexOf('/'), relativePath.lastIndexOf('\\'));
            pathPrefix = relativePath.substring(0, lastSep + 1);
            searchDir = path.resolve(baseDir, pathPrefix);
        } else {
            pathPrefix = '';
            searchDir = baseDir;
        }

        this.outputChannel.appendLine(`[INCLUDE COMPLETION]   Reading directory: ${searchDir} (pathPrefix="${pathPrefix}", libraryPrefix="${libraryPrefix}")`);

        let entries: [string, vscode.FileType][];
        try {
            entries = await vscode.workspace.fs.readDirectory(vscode.Uri.file(searchDir));
        } catch (err) {
            this.outputChannel.appendLine(`[INCLUDE COMPLETION]   Could not read directory: ${searchDir} (${err})`);
            return;
        }

        if (token.isCancellationRequested) {
            return;
        }

        this.outputChannel.appendLine(`[INCLUDE COMPLETION]   Found ${entries.length} entries in ${searchDir}`);

        // Insert workspace-folder entries with absolute paths so #include
        // resolves regardless of the current file's location.
        const useAbsolute = sourceLabel === 'Workspace';

        // Add subdirectory entries
        for (const [name, fileType] of entries) {
            if (fileType !== vscode.FileType.Directory || name.startsWith('.')) {
                continue;
            }

            const absPath = path.resolve(searchDir, name);
            const absKey = 'abs:' + path.normalize(absPath).toLowerCase();
            if (addedEntries.has(absKey)) {
                continue;
            }

            const insertPath = useAbsolute
                ? absPath + path.sep
                : libraryPrefix + pathPrefix + name + '\\';

            if (addedEntries.has(insertPath)) {
                continue;
            }
            addedEntries.add(insertPath);
            addedEntries.add(absKey);

            const item = new vscode.CompletionItem(name, vscode.CompletionItemKind.Folder);
            item.insertText = insertPrefix + insertPath;
            item.filterText = insertPath;
            item.range = replaceRange;
            // Re-trigger completions after inserting a folder so the user can keep drilling
            item.command = {
                command: 'editor.action.triggerSuggest',
                title: 'Re-trigger completions'
            };
            item.sortText = '0_' + name;
            if (sourceLabel) {
                item.detail = sourceLabel;
            }
            completionItems.push(item);
        }

        // Add file entries
        for (const [name, fileType] of entries) {
            if (fileType !== vscode.FileType.File) {
                continue;
            }

            const ext = path.extname(name).toLowerCase().replace('.', '');
            if (!extensions.includes(ext)) {
                continue;
            }

            // Don't suggest the current file
            const absPath = path.resolve(searchDir, name);
            if (absPath === currentFilePath) {
                continue;
            }

            const absKey = 'abs:' + path.normalize(absPath).toLowerCase();
            if (addedEntries.has(absKey)) {
                continue;
            }

            const insertPath = useAbsolute ? absPath : libraryPrefix + pathPrefix + name;
            if (addedEntries.has(insertPath)) {
                continue;
            }
            addedEntries.add(insertPath);
            addedEntries.add(absKey);

            const item = new vscode.CompletionItem(name, vscode.CompletionItemKind.File);
            item.insertText = insertPrefix + insertPath;
            item.filterText = insertPath;
            item.range = replaceRange;
            item.detail = sourceLabel ? `${ext.toUpperCase()} file (${sourceLabel})` : ext.toUpperCase() + ' file';
            item.sortText = '1_' + name;
            completionItems.push(item);
        }
    }

    private resolveLibraryPath(libraryPath: string): string | undefined {
        this.outputChannel.appendLine(`[INCLUDE COMPLETION] Resolving library path: "${libraryPath}"`);

        // Expand environment variables (%VAR% on Windows, $VAR on Unix)
        let resolved = libraryPath.replace(/%([^%]+)%/g, (_, varName) => {
            const value = process.env[varName];
            this.outputChannel.appendLine(`[INCLUDE COMPLETION]   ENV %${varName}% = "${value || '(not set)'}"`);
            return value || '';
        });
        resolved = resolved.replace(/\$([A-Za-z_][A-Za-z0-9_]*)/g, (_, varName) => {
            const value = process.env[varName];
            this.outputChannel.appendLine(`[INCLUDE COMPLETION]   ENV $${varName} = "${value || '(not set)'}"`);
            return value || '';
        });

        this.outputChannel.appendLine(`[INCLUDE COMPLETION]   After expansion: "${resolved}"`);

        if (!path.isAbsolute(resolved)) {
            // Resolve relative to the first workspace folder
            const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
            if (workspaceFolder) {
                resolved = path.resolve(workspaceFolder.uri.fsPath, resolved);
                this.outputChannel.appendLine(`[INCLUDE COMPLETION]   Resolved relative to workspace: "${resolved}"`);
            } else {
                this.outputChannel.appendLine(`[INCLUDE COMPLETION]   Cannot resolve relative path - no workspace folder`);
                return undefined;
            }
        }

        return resolved;
    }

    public static register(outputChannel: vscode.OutputChannel): vscode.Disposable {
        const provider = new CalcpadIncludeCompletionProvider(outputChannel);
        return vscode.languages.registerCompletionItemProvider(
            ['calcpad', 'plaintext'],
            provider,
            ' ', '/', '\\'  // space (after 'from'/'to'/#include), path separators
        );
    }
}
