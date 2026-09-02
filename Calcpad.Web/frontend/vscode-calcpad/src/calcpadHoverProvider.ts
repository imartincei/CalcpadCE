import * as vscode from 'vscode';
import type { ILogger } from 'calcpad-frontend';
import { CalcpadDefinitionsService } from './calcpadDefinitionsService';
import { CalcpadInsertManager } from './calcpadInsertManager';
import { buildBuiltinDocMarkdown, extractFunctionName } from './calcpadBuiltinDocs';
import type {
    MacroDefinition,
    FunctionDefinition,
    VariableDefinition,
    CustomUnitDefinition,
} from 'calcpad-frontend';

/**
 * Provides hover tooltips for Calcpad symbols (macros, functions, variables, custom units).
 * Uses cached definitions from CalcpadDefinitionsService for user-defined symbols and falls
 * back to CalcpadInsertManager for built-in functions — no additional API calls on hover.
 */
export class CalcpadHoverProvider implements vscode.HoverProvider {
    private definitionsService: CalcpadDefinitionsService;
    private insertManager: CalcpadInsertManager;
    private outputChannel: ILogger;

    constructor(
        definitionsService: CalcpadDefinitionsService,
        insertManager: CalcpadInsertManager,
        outputChannel: ILogger
    ) {
        this.definitionsService = definitionsService;
        this.insertManager = insertManager;
        this.outputChannel = outputChannel;
    }

    provideHover(
        document: vscode.TextDocument,
        position: vscode.Position,
        _token: vscode.CancellationToken
    ): vscode.Hover | null {
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
        if (definitions) {
            const macro = definitions.macros.find(m => m.name === word);
            if (macro) {
                this.outputChannel.appendLine('[Hover] Macro: ' + word, 'verbose');
                return new vscode.Hover(this.buildMacroHover(macro), wordRange);
            }

            const func = definitions.functions.find(f => f.name === word);
            if (func) {
                this.outputChannel.appendLine('[Hover] Function: ' + word, 'verbose');
                return new vscode.Hover(this.buildFunctionHover(func), wordRange);
            }

            const variable = definitions.variables.find(v => v.name === word);
            if (variable) {
                this.outputChannel.appendLine('[Hover] Variable: ' + word, 'verbose');
                return new vscode.Hover(this.buildVariableHover(variable), wordRange);
            }

            const unit = definitions.customUnits.find(u => u.name === word);
            if (unit) {
                this.outputChannel.appendLine('[Hover] Custom unit: ' + word, 'verbose');
                return new vscode.Hover(this.buildCustomUnitHover(unit), wordRange);
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

    private buildMacroHover(macro: MacroDefinition): vscode.MarkdownString {
        const md = new vscode.MarkdownString();
        md.isTrusted = true;

        const paramStr = macro.parameters.length > 0 ? `(${macro.parameters.join('; ')})` : '';
        md.appendCodeblock(`${macro.name}${paramStr}`, 'calcpad');

        if (macro.source !== 'local' && macro.sourceFile) {
            md.appendMarkdown(`Source: \`${macro.sourceFile}\`\n\n`);
        }

        if (macro.description) {
            md.appendMarkdown(macro.description + '\n\n');
        }

        this.appendParameterDocs(md, macro.parameters, macro.paramTypes, macro.paramDescriptions, macro.defaults);

        return md;
    }

    private buildFunctionHover(func: FunctionDefinition): vscode.MarkdownString {
        const md = new vscode.MarkdownString();
        md.isTrusted = true;

        const paramStr = func.parameters.length > 0 ? `(${func.parameters.join('; ')})` : '()';
        md.appendCodeblock(`${func.name}${paramStr}`, 'calcpad');

        if (func.source !== 'local' && func.sourceFile) {
            md.appendMarkdown(`Source: \`${func.sourceFile}\`\n\n`);
        }

        if (func.description) {
            md.appendMarkdown(func.description + '\n\n');
        }

        if (func.returnType) {
            md.appendMarkdown(`Returns: *${func.returnType}*\n\n`);
        }

        this.appendParameterDocs(md, func.parameters, func.paramTypes, func.paramDescriptions, func.defaults);

        if (func.expression) {
            md.appendMarkdown('**Expression:**\n');
            md.appendCodeblock(func.expression, 'calcpad');
        }

        return md;
    }

    private buildVariableHover(variable: VariableDefinition): vscode.MarkdownString {
        const md = new vscode.MarkdownString();
        md.isTrusted = true;

        md.appendCodeblock(`${variable.name} = ${variable.expression || ''}`, 'calcpad');

        if (variable.source !== 'local' && variable.sourceFile) {
            md.appendMarkdown(`Source: \`${variable.sourceFile}\`\n\n`);
        }

        if (variable.type) {
            md.appendMarkdown(`Type: *${variable.type}*\n\n`);
        }

        if (variable.description) {
            md.appendMarkdown(variable.description + '\n\n');
        }

        return md;
    }

    private buildCustomUnitHover(unit: CustomUnitDefinition): vscode.MarkdownString {
        const md = new vscode.MarkdownString();
        md.isTrusted = true;

        md.appendCodeblock(`.${unit.name} = ${unit.expression || ''}`, 'calcpad');

        if (unit.source !== 'local' && unit.sourceFile) {
            md.appendMarkdown(`Source: \`${unit.sourceFile}\`\n\n`);
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
        paramDescriptions?: string[],
        defaults?: (string | null)[]
    ): void {
        if (!params || params.length === 0) {
            return;
        }

        const hasTypes = paramTypes && paramTypes.length > 0;
        const hasDescs = paramDescriptions && paramDescriptions.length > 0;
        const hasDefaults = defaults && defaults.length > 0;

        if (hasTypes || hasDescs || hasDefaults) {
            md.appendMarkdown('**Parameters:**\n');
            for (let i = 0; i < params.length; i++) {
                const name = params[i];
                const type = hasTypes && i < paramTypes.length ? paramTypes[i] : undefined;
                const desc = hasDescs && i < paramDescriptions.length ? paramDescriptions[i] : undefined;
                const def = hasDefaults && i < defaults.length ? defaults[i] : undefined;
                let line = `- \`${name}\``;
                if (type) line += ` *(${type})*`;
                if (desc) line += ` — ${desc}`;
                if (def !== undefined && def !== null) {
                    line += ` *(default: ${def})*`;
                } else if (hasDefaults) {
                    line += ` *(required)*`;
                }
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
        outputChannel: ILogger
    ): vscode.Disposable {
        const provider = new CalcpadHoverProvider(definitionsService, insertManager, outputChannel);
        return vscode.languages.registerHoverProvider(
            { language: 'calcpad' },
            provider
        );
    }
}
