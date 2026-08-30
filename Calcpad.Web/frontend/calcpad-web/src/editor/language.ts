import * as monaco from 'monaco-editor';

/**
 * There is deliberately no Monarch tokenizer for CalcpadCE. All highlighting comes
 * from the server's semantic tokens (as in the VS Code extension), so an unhighlighted
 * document is a visible signal that the server is not connected.
 */

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
