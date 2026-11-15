import * as vscode from 'vscode';
import type { ILogger } from 'calcpad-frontend';
import { CalcpadDefinitionsService } from './calcpadDefinitionsService';
import { VSCodeFileSystem } from './adapters';
import { resolveIncludeDirectiveLocation, resolveDocumentPathRoots } from './calcpadLocationResolver';
import { CalcpadInsertManager } from './calcpadInsertManager';
import { buildBuiltinDocMarkdown, extractFunctionName } from './calcpadBuiltinDocs';
import { getVariableTypeAt } from 'calcpad-frontend';
import type {
    MacroDefinition,
    FunctionDefinition,
    VariableDefinition,
    CustomUnitDefinition,
} from 'calcpad-frontend';

/** Where an error the document reports at a position is, in an included file named like `sourceFile`. */
export type IncludedErrorFinder = (uri: vscode.Uri, position: vscode.Position, sourceFile: string) => vscode.Uri | undefined;

/**
 * Provides hover tooltips for Calcpad symbols (macros, functions, variables, custom units).
 * Uses cached definitions from CalcpadDefinitionsService for user-defined symbols and falls
 * back to CalcpadInsertManager for built-in functions — no additional API calls on hover.
 */
export class CalcpadHoverProvider implements vscode.HoverProvider {
    private definitionsService: CalcpadDefinitionsService;
    private insertManager: CalcpadInsertManager;
    private outputChannel: ILogger;
    private findIncludedError?: IncludedErrorFinder;
    private fileSystem = new VSCodeFileSystem();

    constructor(
        definitionsService: CalcpadDefinitionsService,
        insertManager: CalcpadInsertManager,
        outputChannel: ILogger,
        findIncludedError?: IncludedErrorFinder,
    ) {
        this.definitionsService = definitionsService;
        this.insertManager = insertManager;
        this.outputChannel = outputChannel;
        this.findIncludedError = findIncludedError;
    }

    async provideHover(
        document: vscode.TextDocument,
        position: vscode.Position,
        _token: vscode.CancellationToken
    ): Promise<vscode.Hover | null> {
        // Match identifiers including $ suffix (macros) and Unicode (Greek letters etc.)
        const wordRange = document.getWordRangeAtPosition(
            position,
            /[a-zA-Z_\u0080-\uFFFF][a-zA-Z0-9_,\u0080-\uFFFF]*\$?/
        );
        if (!wordRange) {
            return null;
        }

        const word = document.getText(wordRange);

        // Search user-defined symbols first (macros, functions, variables, custom units),
        // then fall back to built-in functions from the insert manager.
        const definitions = this.definitionsService.getCachedDefinitions(document.uri.toString());
        const link = (symbol: { source: string; sourceFile?: string }) =>
            symbol.source !== 'local' && symbol.sourceFile
                ? this.sourceLink(document, position, symbol.sourceFile)
                : Promise.resolve(undefined);
        if (definitions) {
            const macro = definitions.macros.find(m => m.name === word);
            if (macro) {
                this.outputChannel.appendLine('[Hover] Macro: ' + word, 'verbose');
                return new vscode.Hover(this.buildMacroHover(macro, await link(macro)), wordRange);
            }

            // As in Core, a name is a function only when "(" directly follows it.
            const func = definitions.functions.find(f => f.name === word);
            const variable = definitions.variables.find(v => v.name === word);
            const isCall = document.lineAt(position.line).text[wordRange.end.character] === '(';
            if (func && (!variable || isCall)) {
                this.outputChannel.appendLine('[Hover] Function: ' + word, 'verbose');
                return new vscode.Hover(this.buildFunctionHover(func, await link(func)), wordRange);
            }

            if (variable) {
                this.outputChannel.appendLine('[Hover] Variable: ' + word, 'verbose');
                return new vscode.Hover(this.buildVariableHover(variable, position.line, await link(variable)), wordRange);
            }

            const unit = definitions.customUnits.find(u => u.name === word);
            if (unit) {
                this.outputChannel.appendLine('[Hover] Custom unit: ' + word, 'verbose');
                return new vscode.Hover(this.buildCustomUnitHover(unit, await link(unit)), wordRange);
            }
        }

        // Fall back to built-in functions
        if (this.insertManager.isLoaded()) {
            const builtin = this.insertManager.getAllItems().find(item => {
                if (item.keywordType !== 'Function') return false;
                return extractFunctionName(item.tag) === word;
            });
            if (builtin) {
                this.outputChannel.appendLine('[Hover] Built-in function: ' + word, 'verbose');
                return new vscode.Hover(buildBuiltinDocMarkdown(builtin), wordRange);
            }
        }

        return null;
    }

    // The error this position reports from the file, else the file itself
    private async sourceLink(document: vscode.TextDocument, position: vscode.Position, sourceFile: string): Promise<vscode.Uri | undefined> {
        const error = this.findIncludedError?.(document.uri, position, sourceFile);
        if (error) return error;
        const roots = resolveDocumentPathRoots(document, this.definitionsService.getCachedPathRoots(document.uri.toString()));
        const location = await resolveIncludeDirectiveLocation(document, sourceFile, this.fileSystem, this.outputChannel, '[Hover]', roots);
        return location?.uri;
    }

    private appendSource(md: vscode.MarkdownString, sourceFile: string, link?: vscode.Uri): void {
        md.appendMarkdown(link
            ? `Source: [\`${sourceFile}\`](<${link.toString()}>)\n\n`
            : `Source: \`${sourceFile}\`\n\n`);
    }

    private buildMacroHover(macro: MacroDefinition, link?: vscode.Uri): vscode.MarkdownString {
        const md = new vscode.MarkdownString();
        md.isTrusted = true;

        const paramStr = macro.parameters.length > 0 ? `(${macro.parameters.join('; ')})` : '';
        md.appendCodeblock(`${macro.name}${paramStr}`, 'calcpad');

        if (macro.source !== 'local' && macro.sourceFile) {
            this.appendSource(md, macro.sourceFile, link);
        }

        if (macro.description) {
            md.appendMarkdown(macro.description + '\n\n');
        }

        this.appendParameterDocs(md, macro.parameters, macro.paramTypes, macro.paramDescriptions);

        return md;
    }

    private buildFunctionHover(func: FunctionDefinition, link?: vscode.Uri): vscode.MarkdownString {
        const md = new vscode.MarkdownString();
        md.isTrusted = true;

        const paramStr = func.parameters.length > 0 ? `(${func.parameters.join('; ')})` : '()';
        md.appendCodeblock(`${func.name}${paramStr}`, 'calcpad');

        if (func.source !== 'local' && func.sourceFile) {
            this.appendSource(md, func.sourceFile, link);
        }

        if (func.description) {
            md.appendMarkdown(func.description + '\n\n');
        }

        if (func.returnType) {
            md.appendMarkdown(`Returns: *${func.returnType}*\n\n`);
        }

        this.appendParameterDocs(md, func.parameters, func.paramTypes, func.paramDescriptions);

        if (func.expression) {
            md.appendMarkdown('**Expression:**\n');
            md.appendCodeblock(func.expression, 'calcpad');
        }

        return md;
    }

    private buildVariableHover(variable: VariableDefinition, line: number, link?: vscode.Uri): vscode.MarkdownString {
        const md = new vscode.MarkdownString();
        md.isTrusted = true;

        md.appendCodeblock(`${variable.name} = ${variable.expression || ''}`, 'calcpad');

        if (variable.source !== 'local' && variable.sourceFile) {
            this.appendSource(md, variable.sourceFile, link);
        }

        const type = getVariableTypeAt(variable, line);
        if (type) {
            md.appendMarkdown(`Type: *${type}*\n\n`);
        }

        if (variable.description) {
            md.appendMarkdown(variable.description + '\n\n');
        }

        return md;
    }

    private buildCustomUnitHover(unit: CustomUnitDefinition, link?: vscode.Uri): vscode.MarkdownString {
        const md = new vscode.MarkdownString();
        md.isTrusted = true;

        md.appendCodeblock(`.${unit.name} = ${unit.expression || ''}`, 'calcpad');

        if (unit.source !== 'local' && unit.sourceFile) {
            this.appendSource(md, unit.sourceFile, link);
        }

        if (unit.description) {
            md.appendMarkdown(unit.description + '\n\n');
        }

        return md;
    }

    private appendParameterDocs(
        md: vscode.MarkdownString,
        params?: string[],
        paramTypes?: string[],
        paramDescriptions?: string[]
    ): void {
        if (!params || params.length === 0) {
            return;
        }

        const hasTypes = paramTypes && paramTypes.length > 0;
        const hasDescs = paramDescriptions && paramDescriptions.length > 0;

        if (hasTypes || hasDescs) {
            md.appendMarkdown('**Parameters:**\n');
            for (let i = 0; i < params.length; i++) {
                const name = params[i];
                const type = hasTypes && i < paramTypes.length ? paramTypes[i] : undefined;
                const desc = hasDescs && i < paramDescriptions.length ? paramDescriptions[i] : undefined;
                let line = `- \`${name}\``;
                if (type) line += ` *(${type})*`;
                if (desc) line += ` — ${desc}`;
                md.appendMarkdown(line + '\n');
            }
            md.appendMarkdown('\n');
        } else {
            md.appendMarkdown('Parameters: `' + params.join('; ') + '`\n\n');
        }
    }

    static register(
        definitionsService: CalcpadDefinitionsService,
        insertManager: CalcpadInsertManager,
        outputChannel: ILogger,
        findIncludedError?: IncludedErrorFinder,
    ): vscode.Disposable {
        const provider = new CalcpadHoverProvider(definitionsService, insertManager, outputChannel, findIncludedError);
        return vscode.languages.registerHoverProvider(
            { language: 'calcpad' },
            provider
        );
    }
}
