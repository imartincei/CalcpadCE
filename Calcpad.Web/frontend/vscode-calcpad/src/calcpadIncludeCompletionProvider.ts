import * as vscode from 'vscode';
import * as path from 'path';
import * as os from 'os';
import {
    getPathRootTokenKind,
    isUserToken,
    parseDirectiveLine,
    extensionsForDirective,
    resolveCompletionPathRoots,
    PATH_ROOT_TOKEN,
    PATH_ROOT_LABEL,
    USER_TOKEN,
    DIRECTIVE_TRIGGER_CHARACTERS,
    pathRootTokenOptions,
    hasDanglingCloseBrace,
} from 'calcpad-frontend';
import { expandEnvVars } from './calcpadLocationResolver';
import type { CalcpadDefinitionsService } from './calcpadDefinitionsService';

export class CalcpadIncludeCompletionProvider implements vscode.CompletionItemProvider {
    private outputChannel: vscode.OutputChannel;
    private definitionsService: CalcpadDefinitionsService;

    constructor(definitionsService: CalcpadDefinitionsService, outputChannel: vscode.OutputChannel) {
        this.definitionsService = definitionsService;
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

        this.outputChannel.appendLine(`[INCLUDE COMPLETION] Triggered on line: "${lineText}" (directive: #${directive})`);

        // Strip any trailing options (@sheet, type=, sep=) from the partial path for completion
        let partialPath = parsed.partialPath;
        const atIndex = partialPath.indexOf('@');
        if (atIndex !== -1) {
            partialPath = partialPath.substring(0, atIndex);
        }

        this.outputChannel.appendLine(`[INCLUDE COMPLETION] Partial path: "${partialPath}"`);

        const documentDir = path.dirname(document.uri.fsPath);
        const homeDir = os.homedir();
        const completionItems: vscode.CompletionItem[] = [];
        const addedEntries = new Set<string>();

        const extensions = extensionsForDirective(directive);

        // VS Code auto-closes a typed `{` into `{}`, leaving a `}` sitting right after the
        // cursor — swallow it so inserting a full token doesn't leave it dangling.
        const nextChar = document.lineAt(position.line).text.charAt(position.character);
        const extraChar = hasDanglingCloseBrace(partialPath, nextChar) ? 1 : 0;

        // Replace range covers from the start of the file path to the cursor
        const replaceRange = new vscode.Range(
            position.line, pathStartCol,
            position.line, position.character + extraChar
        );

        // The document's own #ProjectPath/#LibraryPath roots — the server's resolved value
        // (live regardless of where in the #include chain it was declared) when cached,
        // otherwise this document's own text scan.
        const resolvedRoots = await resolveCompletionPathRoots({
            serverRoots: this.definitionsService.getCachedPathRoots(document.uri.toString()),
            sourceText: document.getText(),
            beforeLine: position.line,
            documentDir,
            expandEnvVars,
            resolve: path.resolve,
            homeDir,
        });
        this.outputChannel.appendLine(
            `[INCLUDE COMPLETION] Resolved roots: project="${resolvedRoots.project || '(none)'}" library="${resolvedRoots.library || '(none)'}"`
        );

        try {
            const tokenKind = getPathRootTokenKind(partialPath);
            const isUser = isUserToken(partialPath);
            if (isUser) {
                // Drilling into a {user} reference: search the OS home directory, but reinsert
                // completions in token form so the reference stays portable.
                const tokenText = partialPath.slice(0, USER_TOKEN.length);
                let rest = partialPath.slice(tokenText.length);
                if (rest.startsWith('/') || rest.startsWith('\\')) rest = rest.slice(1);
                this.outputChannel.appendLine(`[INCLUDE COMPLETION] Inside {user} root, relative path: "${rest}"`);
                await this.addEntriesFromDirectory(
                    homeDir, rest, extensions, replaceRange,
                    document.uri.fsPath, completionItems, addedEntries, token,
                    undefined, '', `${tokenText}/`
                );
            } else if (tokenKind !== null) {
                // Drilling into a {project}/{library} reference: search the resolved root, but
                // reinsert completions in token form so the reference stays portable.
                const root = resolvedRoots[tokenKind];
                if (root) {
                    const tokenText = partialPath.slice(0, PATH_ROOT_TOKEN[tokenKind].length);
                    let rest = partialPath.slice(tokenText.length);
                    if (rest.startsWith('/') || rest.startsWith('\\')) rest = rest.slice(1);
                    this.outputChannel.appendLine(`[INCLUDE COMPLETION] Inside ${tokenKind} root, relative path: "${rest}"`);
                    await this.addEntriesFromDirectory(
                        root, rest, extensions, replaceRange,
                        document.uri.fsPath, completionItems, addedEntries, token,
                        PATH_ROOT_LABEL[tokenKind], '', `${tokenText}/`
                    );
                }
            } else if (partialPath.includes('/') || partialPath.includes('\\')) {
                // User is navigating local subdirectories (path has separators but isn't a token)
                this.outputChannel.appendLine(`[INCLUDE COMPLETION] Navigating local subdirectory`);

                await this.addEntriesFromDirectory(
                    documentDir, partialPath, extensions, replaceRange,
                    document.uri.fsPath, completionItems, addedEntries, token,
                    undefined, ''
                );
            } else {
                // Root level - show local files/folders + declared roots + workspace folders
                this.outputChannel.appendLine(`[INCLUDE COMPLETION] Root level - searching local + declared roots + workspace`);

                await this.addEntriesFromDirectory(
                    documentDir, partialPath, extensions, replaceRange,
                    document.uri.fsPath, completionItems, addedEntries, token,
                    undefined, ''
                );

                const docDirNorm = path.normalize(documentDir);

                // Offer {user}/{project}/{library} as pick-a-root placeholders rather than
                // eagerly flattening their contents in — selecting one inserts just the token
                // and re-triggers completion, which then drills into that root above.
                for (const opt of pathRootTokenOptions(resolvedRoots, true)) {
                    const item = new vscode.CompletionItem(opt.label, vscode.CompletionItemKind.Folder);
                    item.insertText = `${opt.token}/`;
                    item.filterText = `${opt.token}/`;
                    item.range = replaceRange;
                    item.detail = opt.detail;
                    item.sortText = '0_' + opt.label;
                    item.command = {
                        command: 'editor.action.triggerSuggest',
                        title: 'Re-trigger completions'
                    };
                    completionItems.push(item);
                }

                // Also search each open workspace folder, skipping any that coincides with the
                // doc dir or a declared root. Their entries get absolute-path insert text so
                // #include resolves regardless of the current file's location.
                const workspaceFolders = vscode.workspace.workspaceFolders ?? [];
                const rootNorms = (['project', 'library'] as const)
                    .map(k => resolvedRoots[k])
                    .filter((p): p is string => !!p)
                    .map(p => path.normalize(p));
                for (const folder of workspaceFolders) {
                    const folderPath = folder.uri.fsPath;
                    const folderNorm = path.normalize(folderPath);
                    if (folderNorm === docDirNorm || rootNorms.includes(folderNorm)) {
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
                : libraryPrefix + pathPrefix + name + path.sep;

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

    public static register(definitionsService: CalcpadDefinitionsService, outputChannel: vscode.OutputChannel): vscode.Disposable {
        const provider = new CalcpadIncludeCompletionProvider(definitionsService, outputChannel);
        return vscode.languages.registerCompletionItemProvider(
            ['calcpad', 'plaintext'],
            provider,
            ...DIRECTIVE_TRIGGER_CHARACTERS
        );
    }
}
