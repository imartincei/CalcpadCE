import * as monaco from 'monaco-editor';

/**
 * Calcpad code gets no local tokens: its highlighting comes from the server's semantic
 * tokens (as in the VS Code extension), so unhighlighted code is a visible signal that the
 * server is not connected. This tokenizer only hands #html/#markdown content to Monaco's
 * built-in grammars, following the same push/pop stack as ExpressionParser.
 */
const endDirective = /^\s*#end\s+(html|cpd|markdown)\b.*$/;
const openers: monaco.languages.IMonarchLanguageRule[] = [
    [/^\s*#html\b.*$/, { token: '', next: '@htmlOuter' }],
    [/^\s*#markdown\b.*$/, { token: '', next: '@mdOuter' }],
    [/^\s*#cpd\b.*$/, { token: '', next: '@cpdBlock' }],
];

function embeddedStates(language: string): [monaco.languages.IMonarchLanguageRule[], monaco.languages.IMonarchLanguageRule[]] {
    const outer: monaco.languages.IMonarchLanguageRule[] = [
        [endDirective, { token: '', next: '@pop' }],
        ...openers,
        [/^/, { token: '@rematch', next: `@${language}Body`, nextEmbedded: language }],
    ];
    const body: monaco.languages.IMonarchLanguageRule[] = [
        [/^\s*#(end\s+)?(html|cpd|markdown)\b/, { token: '@rematch', next: '@pop', nextEmbedded: '@pop' }],
        [/.+/, ''],
    ];
    return [outer, body];
}

const [htmlOuter, htmlBody] = embeddedStates('html');
const [mdOuter, markdownBody] = embeddedStates('markdown');

export const calcpadMonarchLanguage: monaco.languages.IMonarchLanguage = {
    ignoreCase: true,
    defaultToken: '',
    tokenizer: {
        root: [...openers, [/.+/, '']],
        cpdBlock: [[endDirective, { token: '', next: '@pop' }], ...openers, [/.+/, '']],
        htmlOuter,
        htmlBody,
        mdOuter,
        markdownBody,
    },
};

/**
 * Language configuration for CalcPad (brackets, auto-closing, etc.).
 */
export const calcpadLanguageConfiguration: monaco.languages.LanguageConfiguration = {
    wordPattern: /(-?\d*\.\d\w*)|([^\`\~\!\@\#\%\^\&\*\(\)\-\=\+\[\{\]\}\\\|\;\:\'\"\.\<\>\/\?\s]+)/,
    comments: {
        lineComment: "'",
        blockComment: ['"', '"'],
    },
    brackets: [
        ['(', ')'],
        ['{', '}'],
        ['[', ']'],
    ],
    autoClosingPairs: [
        { open: '(', close: ')' },
        { open: '{', close: '}' },
        { open: '[', close: ']' },
        { open: '"', close: '"' },
    ],
    surroundingPairs: [
        { open: '(', close: ')' },
        { open: '{', close: '}' },
    ],
};
