import * as monaco from 'monaco-editor';

/**
 * Monarch tokenizer for CalcpadCE — provides basic syntax highlighting
 * as a fallback before semantic tokens arrive from the server.
 */
export const calcpadLanguage: monaco.languages.IMonarchLanguage = {
    defaultToken: '',
    tokenPostfix: '.calcpad',

    keywords: [
        '#if', '#else', '#else if', '#end if',
        '#for', '#loop', '#repeat',
        '#def', '#end def',
        '#include', '#read', '#write',
        '#local', '#global', '#round', '#pause',
        '#val', '#equ', '#noc', '#show', '#hide', '#pre', '#post',
        '#end val', '#end equ', '#end noc', '#end show', '#end hide', '#end pre', '#end post',
        '#end nosub', '#end novar', '#end varsub',
        '#deg', '#rad', '#gra',
        '#md on', '#md off',
        '#const', '#break', '#continue', '#append', '#format', '#settings', '#split',
        '#wrap', '#phasor', '#complex', '#input', '#nosub', '#novar', '#varsub',
        '#cpd', '#html', '#markdown',
    ],

    operators: [
        '=', '≡', '≠', '≥', '≤', '<', '>',
        '+', '-', '*', '/', '\\', '^', '!',
        '∧', '∨', '⊕', '¬',
        '←', '⦼',
    ],

    symbols: /[=><!~?:&|+\-*\/\\^%⦼∧∨⊕¬≡≠≥≤←]+/,

    tokenizer: {
        root: [
            // Line comments (single quote)
            [/'.*$/, 'comment'],

            // Block comments (double quote)
            [/"/, 'comment', '@blockComment'],

            // Keywords starting with #
            [/#\w[\w\s]*/, {
                cases: {
                    '@keywords': 'keyword',
                    '@default': 'keyword',
                }
            }],

            // Commands starting with $
            [/\$\w+/, 'keyword'],

            // Numbers
            [/\d+\.?\d*([eE][-+]?\d+)?/, 'number'],

            // Operators
            [/@symbols/, {
                cases: {
                    '@operators': 'operator',
                    '@default': '',
                }
            }],

            // Brackets
            [/[{}()\[\]]/, 'bracket'],

            // Identifiers (commas are valid in Calcpad variable names; semicolons are argument separators)
            [/[a-zA-Z_α-ωΑ-Ω][a-zA-Z0-9_,α-ωΑ-Ω]*/, 'identifier'],

            // Whitespace
            [/\s+/, 'white'],
        ],

        blockComment: [
            [/[^"]+/, 'comment'],
            [/"/, 'comment', '@pop'],
        ],
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
