import type * as monaco from 'monaco-editor';

/**
 * Semantic tokens provide Calcpad highlighting.
 * Local tokens identify comments, literal text, and embedded HTML/Markdown.
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

function commentState(quote: string): monaco.languages.IMonarchLanguageRule[] {
    const text = {
        cases: {
            '@eos': { token: 'comment', next: '@pop' },
            '@default': 'comment',
        },
    };
    return [
        [new RegExp(`[^${quote}<]* _\\s*$`), 'comment'],
        [/<!--/, { token: 'htmlComment.comment', next: '@htmlComment' }],
        [/-->/, {
            cases: {
                '@eos': { token: 'htmlComment.comment', next: '@pop' },
                '@default': 'htmlComment.comment',
            },
        }],
        [new RegExp(quote), { token: 'comment', next: '@pop' }],
        [/<[^>]*>/, text],
        [new RegExp(`[^${quote}<]+`), text],
        [/</, text],
        [/$/, { token: 'comment', next: '@pop' }],
    ];
}

export const calcpadMonarchLanguage: monaco.languages.IMonarchLanguage = {
    ignoreCase: true,
    defaultToken: '',
    tokenizer: {
        root: [...openers, { include: '@code' }],
        cpdBlock: [[endDirective, { token: '', next: '@pop' }], ...openers, { include: '@code' }],
        code: [
            [/^(\s*#include\s+)([^#'"]*)/, ['', 'string']],
            [/^(\s*#(?:projectpath|librarypath|format)\s+)([^'"]*)/, ['', 'string']],
            [/^(\s*#(?:read|write|append)\s+.*?\s+(?:from|to)\s+)([^@'"]*?)(?=\s+(?:sep|type)\b|@|['"]|$)/, ['', 'string']],
            [/^(\s*#settings\s+)(.*)$/, ['', 'string']],
            [/^(\s*#ui\s*)(\{[^}]*\})/, ['', 'string']],
            [/'/, { cases: { '@eos': 'comment', '@default': { token: 'comment', next: '@singleComment' } } }],
            [/"/, { cases: { '@eos': 'comment', '@default': { token: 'comment', next: '@doubleComment' } } }],
            [/[^'"]+/, ''],
        ],
        singleComment: commentState("'"),
        doubleComment: commentState('"'),
        htmlComment: [
            [/-->/, { token: '@rematch', next: '@pop' }],
            [/[^-]+|-(?!->)/, 'htmlComment.comment'],
        ],
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
    colorizedBracketPairs: [['(', ')']],
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
