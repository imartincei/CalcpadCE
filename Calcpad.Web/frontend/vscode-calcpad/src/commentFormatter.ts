import * as vscode from 'vscode';
import { CalcpadSettingsManager } from './calcpadSettings';
import {
    HTML_INLINE,
    MARKDOWN_INLINE,
    stripCommentPrefix,
    getCommentPrefixInsertColumn,
    buildHeadingLine,
    buildParagraphLine,
    buildListLines,
    type InlineFormat,
    type CommentFormat,
} from 'calcpad-frontend';

type CommentFormatSetting = 'html' | 'markdown' | 'auto';

/**
 * Handles text formatting hotkeys for Calcpad comment lines.
 * Supports HTML and Markdown modes via the calcpad.commentFormat setting.
 */
export class CommentFormatter {
    private outputChannel: vscode.OutputChannel;

    constructor(outputChannel: vscode.OutputChannel) {
        this.outputChannel = outputChannel;
    }

    public registerCommands(): vscode.Disposable[] {
        return [
            // Inline formatting
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatBold', (e) => this.wrapInline(e, 'bold')),
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatItalic', (e) => this.wrapInline(e, 'italic')),
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatUnderline', (e) => this.wrapInline(e, 'underline')),
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatSubscript', (e) => this.wrapInline(e, 'subscript')),
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatSuperscript', (e) => this.wrapInline(e, 'superscript')),

            // Headings
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatHeading1', (e) => this.insertHeading(e, 1)),
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatHeading2', (e) => this.insertHeading(e, 2)),
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatHeading3', (e) => this.insertHeading(e, 3)),
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatHeading4', (e) => this.insertHeading(e, 4)),
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatHeading5', (e) => this.insertHeading(e, 5)),
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatHeading6', (e) => this.insertHeading(e, 6)),

            // Block elements
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatParagraph', (e) => this.insertParagraph(e)),
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatLineBreak', (e) => this.insertLineBreak(e)),
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatBulletedList', (e) => this.insertBulletedList(e)),
            vscode.commands.registerTextEditorCommand('vscode-calcpad.formatNumberedList', (e) => this.insertNumberedList(e)),

            // Comments
            vscode.commands.registerTextEditorCommand('vscode-calcpad.toggleComment', (e) => this.toggleComment(e)),
            vscode.commands.registerTextEditorCommand('vscode-calcpad.uncomment', (e) => this.uncomment(e)),
            vscode.commands.registerTextEditorCommand('vscode-calcpad.pasteAsComment', (e) => this.pasteAsComment(e))
        ];
    }

    private getFormat(): CommentFormat {
        const setting = CalcpadSettingsManager.getInstance().getExtra('commentFormat', 'html');

        if (setting === 'markdown') {
            return 'markdown';
        }
        if (setting === 'auto') {
            return this.detectFormatAtCursor();
        }
        return 'html';
    }

    /**
     * Scan from the top of the document to the cursor position,
     * tracking #md on / #md off directives to determine the active format.
     * Default is HTML when no directive is found.
     */
    private detectFormatAtCursor(): CommentFormat {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
            return 'html';
        }

        const cursorLine = editor.selection.active.line;
        let mdActive = false;

        for (let i = 0; i <= cursorLine; i++) {
            const lineText = editor.document.lineAt(i).text.trim();
            if (lineText === '#md on') {
                mdActive = true;
            } else if (lineText === '#md off') {
                mdActive = false;
            }
        }

        return mdActive ? 'markdown' : 'html';
    }

    /**
     * Wrap selected text with inline formatting tags, ensuring every touched line starts with a
     * comment quote right after its indentation, since the tags only render as HTML inside a
     * comment. Run as its own edit first so VS Code auto-adjusts `editor.selections` before the
     * wrap edit reads them.
     */
    private async wrapInline(editor: vscode.TextEditor, type: InlineFormat): Promise<void> {
        const format = this.getFormat();
        const [prefix, suffix] = format === 'html' ? HTML_INLINE[type] : MARKDOWN_INLINE[type];

        const startCharByLine = new Map<number, number>();
        for (const sel of editor.selections) {
            const prev = startCharByLine.get(sel.start.line);
            if (prev === undefined || sel.start.character < prev) startCharByLine.set(sel.start.line, sel.start.character);
        }
        const prefixInserts: Array<[line: number, col: number]> = [];
        for (const [line, startChar] of startCharByLine) {
            const insertCol = getCommentPrefixInsertColumn(editor.document.lineAt(line).text, startChar);
            if (insertCol !== null) prefixInserts.push([line, insertCol - 1]);
        }
        if (prefixInserts.length > 0) {
            await editor.edit(editBuilder => {
                for (const [line, col] of prefixInserts) {
                    editBuilder.insert(new vscode.Position(line, col), "'");
                }
            });
        }

        const wasEmpty = editor.selections.map(s => s.isEmpty);

        await editor.edit(editBuilder => {
            for (const selection of editor.selections) {
                const selectedText = editor.document.getText(selection);
                if (selectedText) {
                    editBuilder.replace(selection, prefix + selectedText + suffix);
                } else {
                    editBuilder.insert(selection.active, prefix + suffix);
                }
            }
        });

        // For cursors that had no selection, place cursor between prefix and suffix
        const newSelections: vscode.Selection[] = [];
        for (let i = 0; i < editor.selections.length; i++) {
            if (wasEmpty[i]) {
                const pos = editor.selections[i].active;
                const newPos = new vscode.Position(pos.line, pos.character - suffix.length);
                newSelections.push(new vscode.Selection(newPos, newPos));
            } else {
                newSelections.push(editor.selections[i]);
            }
        }
        editor.selections = newSelections;
    }

    /**
     * Wrap the current line(s) in a heading tag.
     * Strips any existing heading tags/markdown headings first.
     */
    private async insertHeading(editor: vscode.TextEditor, level: number): Promise<void> {
        const format = this.getFormat();
        const contentWasEmpty: boolean[] = [];

        await editor.edit(editBuilder => {
            for (const selection of editor.selections) {
                const line = editor.document.lineAt(selection.active.line);
                const [, content] = stripCommentPrefix(line.text);
                const strippedContent = content
                    .replace(/^<h[1-6]>(.*)<\/h[1-6]>$/, '$1')
                    .replace(/^#{1,6}\s+(.*)$/, '$1');
                contentWasEmpty.push(strippedContent.trim().length === 0);

                editBuilder.replace(line.range, buildHeadingLine(line.text, level, format));
            }
        });

        // Place cursor between tags when content was empty
        if (format === 'html') {
            const closingTag = `</h${level}>`;
            const newSelections: vscode.Selection[] = [];
            for (let i = 0; i < editor.selections.length; i++) {
                if (contentWasEmpty[i]) {
                    const pos = editor.selections[i].active;
                    const lineEnd = editor.document.lineAt(pos.line).range.end;
                    const newPos = new vscode.Position(lineEnd.line, lineEnd.character - closingTag.length);
                    newSelections.push(new vscode.Selection(newPos, newPos));
                } else {
                    newSelections.push(editor.selections[i]);
                }
            }
            editor.selections = newSelections;
        }
    }

    /**
     * Wrap selected lines in <p>...</p> tags.
     */
    private async insertParagraph(editor: vscode.TextEditor): Promise<void> {
        await editor.edit(editBuilder => {
            for (const selection of editor.selections) {
                const line = editor.document.lineAt(selection.active.line);
                editBuilder.replace(line.range, buildParagraphLine(line.text));
            }
        });
    }

    /**
     * Insert a <br/> line break at the cursor position.
     */
    private async insertLineBreak(editor: vscode.TextEditor): Promise<void> {
        await editor.edit(editBuilder => {
            for (const selection of editor.selections) {
                editBuilder.insert(selection.active, "'<br/>\n");
            }
        });
    }

    /**
     * Wrap selected lines as a bulleted list.
     */
    private async insertBulletedList(editor: vscode.TextEditor): Promise<void> {
        const format = this.getFormat();

        await editor.edit(editBuilder => {
            const startLine = editor.selection.start.line;
            const endLine = editor.selection.end.line;
            const lineTexts: string[] = [];
            for (let i = startLine; i <= endLine; i++) lineTexts.push(editor.document.lineAt(i).text);

            const fullRange = new vscode.Range(
                editor.document.lineAt(startLine).range.start,
                editor.document.lineAt(endLine).range.end
            );
            editBuilder.replace(fullRange, buildListLines(lineTexts, format, false).join('\n'));
        });
    }

    /**
     * Wrap selected lines as a numbered list.
     */
    private async insertNumberedList(editor: vscode.TextEditor): Promise<void> {
        const format = this.getFormat();

        await editor.edit(editBuilder => {
            const startLine = editor.selection.start.line;
            const endLine = editor.selection.end.line;
            const lineTexts: string[] = [];
            for (let i = startLine; i <= endLine; i++) lineTexts.push(editor.document.lineAt(i).text);

            const fullRange = new vscode.Range(
                editor.document.lineAt(startLine).range.start,
                editor.document.lineAt(endLine).range.end
            );
            editBuilder.replace(fullRange, buildListLines(lineTexts, format, true).join('\n'));
        });
    }

    /**
     * Toggle comment prefix (') on selected lines.
     */
    private async toggleComment(editor: vscode.TextEditor): Promise<void> {
        await editor.edit(editBuilder => {
            const startLine = editor.selection.start.line;
            const endLine = editor.selection.end.line;

            // Check if all lines are already commented
            let allCommented = true;
            for (let i = startLine; i <= endLine; i++) {
                if (!editor.document.lineAt(i).text.startsWith("'")) {
                    allCommented = false;
                    break;
                }
            }

            for (let i = startLine; i <= endLine; i++) {
                const line = editor.document.lineAt(i);
                if (allCommented) {
                    // Remove comment prefix
                    if (line.text.startsWith("'")) {
                        editBuilder.delete(new vscode.Range(
                            new vscode.Position(i, 0),
                            new vscode.Position(i, 1)
                        ));
                    }
                } else {
                    // Add comment prefix
                    editBuilder.insert(new vscode.Position(i, 0), "'");
                }
            }
        });
    }

    /**
     * Remove comment prefix (') from selected lines.
     */
    private async uncomment(editor: vscode.TextEditor): Promise<void> {
        await editor.edit(editBuilder => {
            const startLine = editor.selection.start.line;
            const endLine = editor.selection.end.line;

            for (let i = startLine; i <= endLine; i++) {
                const line = editor.document.lineAt(i);
                if (line.text.startsWith("'")) {
                    editBuilder.delete(new vscode.Range(
                        new vscode.Position(i, 0),
                        new vscode.Position(i, 1)
                    ));
                }
            }
        });
    }

    /**
     * Paste clipboard contents as comment lines (each line prefixed with ').
     */
    private async pasteAsComment(editor: vscode.TextEditor): Promise<void> {
        const clipboardText = await vscode.env.clipboard.readText();
        if (!clipboardText) {
            return;
        }

        const commentedLines = clipboardText
            .split('\n')
            .map(line => "'" + line)
            .join('\n');

        await editor.edit(editBuilder => {
            if (editor.selection.isEmpty) {
                editBuilder.insert(editor.selection.active, commentedLines);
            } else {
                editBuilder.replace(editor.selection, commentedLines);
            }
        });
    }
}
