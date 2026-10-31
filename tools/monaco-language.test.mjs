import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from '../Calcpad.Web/frontend/calcpad-web/node_modules/typescript/lib/typescript.js';
import { compile } from '../Calcpad.Web/frontend/calcpad-web/node_modules/monaco-editor/esm/vs/editor/standalone/common/monarch/monarchCompile.js';
import { MonarchTokenizer } from '../Calcpad.Web/frontend/calcpad-web/node_modules/monaco-editor/esm/vs/editor/standalone/common/monarch/monarchLexer.js';
import { toStandardTokenType } from '../Calcpad.Web/frontend/calcpad-web/node_modules/monaco-editor/esm/vs/editor/common/languages/supports/tokenization.js';

const source = readFileSync(new URL('../Calcpad.Web/frontend/calcpad-web/src/editor/language.ts', import.meta.url), 'utf8');
const { outputText } = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext } });
const { calcpadMonarchLanguage } = await import(`data:text/javascript;base64,${Buffer.from(outputText).toString('base64')}`);

function parentheses(lines) {
    const tokenizer = new MonarchTokenizer({}, {}, 'calcpad', compile('calcpad', calcpadMonarchLanguage), {
        getValue: () => 20000,
        onDidChangeConfiguration: () => ({ dispose() {} }),
    });
    let state = tokenizer.getInitialState();
    try {
        return lines.map(line => {
            const result = tokenizer.tokenize(line, true, state);
            state = result.endState;
            return result.tokens.flatMap((token, index) => {
                const text = line.slice(token.offset, result.tokens[index + 1]?.offset);
                return [...text.matchAll(/[()]/g)].map(() => toStandardTokenType(token.type));
            });
        });
    } finally {
        tokenizer.dispose();
    }
}

test('comments and headings exclude parentheses, while inline expressions remain code', () => {
    assert.deepEqual(parentheses([
        "'test ((()))",
        '"Heading (text)"',
        "x = (1) ' text () ' + (2)",
        '"Text (a) and \'(b)\'" (3)',
        "'",
        'x = (4)',
    ]), [
        [1, 1, 1, 1, 1, 1],
        [1, 1],
        [0, 0, 1, 1, 0, 0],
        [1, 1, 1, 1, 0, 0],
        [],
        [0, 0],
    ]);
});

test('HTML comments and attributes do not leak comment state into following expressions', () => {
    assert.deepEqual(parentheses([
        "'<!-- test ((())) -->",
        'x = (1)',
        "'<!-- (start)",
        '"quoted (body)"',
        '(end) -->',
        'x = (2)',
        "'<span title=\"don't (color)\">text ()</span>' + (3)",
    ]), [
        [1, 1, 1, 1, 1, 1],
        [0, 0],
        [1, 1],
        [1, 1],
        [1, 1],
        [0, 0],
        [1, 1, 1, 1, 0, 0],
    ]);
});

test('continued comments end at a closing quote or a line without continuation', () => {
    assert.deepEqual(parentheses([
        "'comment () _",
        '(continued) _',
        "(end)' + (1)",
        '"heading () _',
        '(last line)',
        'x = (2)',
        "'comment () _",
        '',
        'x = (3)',
    ]), [
        [1, 1], [1, 1], [1, 1, 0, 0],
        [1, 1], [1, 1], [0, 0],
        [1, 1], [], [0, 0],
    ]);
});

test('file paths exclude parentheses without excluding include arguments or data expressions', () => {
    assert.deepEqual(parentheses([
        '#include (((foo))).cpd',
        '#include folder (old)/lib.cpd #{(1 + 2)}',
        '#include "lib (old).cpd" #{(1)}',
        '#ProjectPath C:/projects (old)',
        '#LibraryPath C:/libraries (old)',
        '#read a from data (old).csv @ (1)',
        '#write a.(1) to data (old).csv',
        '#append a.(2) to data (old).csv sep ";"',
        'x = (3)',
    ]), [
        [2, 2, 2, 2, 2, 2],
        [2, 2, 0, 0],
        [1, 1, 0, 0],
        [2, 2],
        [2, 2],
        [2, 2, 0, 0],
        [0, 0, 2, 2],
        [0, 0, 2, 2],
        [0, 0],
    ]);
});

test('literal directive payloads exclude parentheses, and nested CalcPad blocks use the same rules', () => {
    assert.deepEqual(parentheses([
        '#settings {"text":"(literal)"}',
        '#UI {"caption":"(literal)"} + (1)',
        '#format (literal)',
        '#html',
        '#cpd',
        "'text ()' + (2)",
        '#end cpd',
        '#end html',
        'x = (3)',
    ]), [
        [2, 2], [2, 2, 0, 0], [2, 2], [], [],
        [1, 1, 0, 0], [], [], [0, 0],
    ]);
});
