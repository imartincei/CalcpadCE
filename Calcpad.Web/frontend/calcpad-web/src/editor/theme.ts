import * as monaco from 'monaco-editor';

/**
 * CalcPad dark theme for Monaco. Maps semantic token types to colors matching
 * the dark-mode color rules in the VS Code extension's package.json.
 */
// `semanticHighlighting: true` is recognized by Monaco at runtime but isn't
// in IStandaloneThemeData's public types, so we cast at the property level.
export const calcpadDarkTheme: monaco.editor.IStandaloneThemeData = {
    base: 'vs-dark',
    inherit: true,
    ...({ semanticHighlighting: true } as object),
    rules: [
        // Core syntax
        { token: 'comment', foreground: '57A64A' },
        { token: 'keyword', foreground: 'FF80FF' },
        { token: 'number', foreground: 'D4D4D4' },
        { token: 'operator', foreground: 'ECC860' },
        { token: 'bracket', foreground: 'FF69B4' },
        { token: 'identifier', foreground: 'D4D4D4' },

        // Semantic token rules (matched by semantic tokens provider)
        { token: 'const', foreground: 'D4D4D4' },
        { token: 'lineContinuation', foreground: 'ECC860' },
        { token: 'variable', foreground: '6B9FFF' },
        { token: 'localVariable', foreground: '8BB4FF' },
        { token: 'function', foreground: 'D4D4D4', fontStyle: 'bold' },
        { token: 'macro', foreground: 'D355D3' },
        { token: 'macroParameter', foreground: 'E07BE0' },
        { token: 'units', foreground: '00CED1' },
        { token: 'setting', foreground: '4FC1FF' },
        { token: 'controlBlockKeyword', foreground: 'FF80FF' },
        { token: 'endKeyword', foreground: 'FF80FF' },
        { token: 'command', foreground: 'FF80FF' },
        { token: 'include', foreground: '9B6BCC' },
        { token: 'filePath', foreground: '9B6BCC' },
        { token: 'dataExchangeKeyword', foreground: 'FF80FF' },
        { token: 'htmlComment', foreground: 'A0A0A0' },
        { token: 'tag', foreground: 'CC66FF' },
        { token: 'htmlContent', foreground: '57A64A' },
        { token: 'javascript', foreground: 'ECC860' },
        { token: 'css', foreground: '6B9FFF' },
        { token: 'svg', foreground: '00CED1' },
        { token: 'input', foreground: 'FF5555' },
        { token: 'format', foreground: 'A9A9A9' },
    ],
    colors: {
        'editor.background': '#1e1e1e',
        'editor.foreground': '#d4d4d4',
        'editorLineNumber.foreground': '#858585',
        'editorLineNumber.activeForeground': '#c6c6c6',
        'editor.selectionBackground': '#264f78',
        'editor.inactiveSelectionBackground': '#3a3d41',
        'editorBracketMatch.background': '#0064001a',
        'editorBracketMatch.border': '#888888',
    },
};

/**
 * CalcPad light theme for Monaco. Colors mirror the light-mode color rules
 * in the VS Code extension's package.json — same token roles as the dark
 * theme, retuned for contrast on a white background.
 */
export const calcpadLightTheme: monaco.editor.IStandaloneThemeData = {
    base: 'vs',
    inherit: true,
    ...({ semanticHighlighting: true } as object),
    rules: [
        { token: 'comment', foreground: '008000' },
        { token: 'keyword', foreground: 'AF00DB' },
        { token: 'number', foreground: '000000' },
        { token: 'operator', foreground: '795E26' },
        { token: 'bracket', foreground: 'C71585' },
        { token: 'identifier', foreground: '000000' },

        { token: 'const', foreground: '000000' },
        { token: 'lineContinuation', foreground: '795E26' },
        { token: 'variable', foreground: '0451A5' },
        { token: 'localVariable', foreground: '2E5A9E' },
        { token: 'function', foreground: '000000', fontStyle: 'bold' },
        { token: 'macro', foreground: '8B008B' },
        { token: 'macroParameter', foreground: 'A040A0' },
        { token: 'units', foreground: '0F8080' },
        { token: 'setting', foreground: '0070C1' },
        { token: 'controlBlockKeyword', foreground: 'AF00DB' },
        { token: 'endKeyword', foreground: 'AF00DB' },
        { token: 'command', foreground: 'AF00DB' },
        { token: 'include', foreground: '6F42C1' },
        { token: 'filePath', foreground: '6F42C1' },
        { token: 'dataExchangeKeyword', foreground: 'AF00DB' },
        { token: 'htmlComment', foreground: '6A737D' },
        { token: 'tag', foreground: '800080' },
        { token: 'htmlContent', foreground: '008000' },
        { token: 'javascript', foreground: '795E26' },
        { token: 'css', foreground: '0451A5' },
        { token: 'svg', foreground: '0F8080' },
        { token: 'input', foreground: 'CD3131' },
        { token: 'format', foreground: '6A737D' },
    ],
    colors: {
        'editor.background': '#ffffff',
        'editor.foreground': '#000000',
        'editorLineNumber.foreground': '#237893',
        'editorLineNumber.activeForeground': '#0b216f',
        'editor.selectionBackground': '#add6ff',
        'editor.inactiveSelectionBackground': '#e5ebf1',
        'editorBracketMatch.background': '#0064001a',
        'editorBracketMatch.border': '#b9b9b9',
    },
};
