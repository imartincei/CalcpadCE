import assert from 'node:assert/strict';
import { test } from 'node:test';
import { checkResult, renderResult, reportText } from '../Calcpad.Web/frontend/calcpad-web/src/services/agent-report.ts';

test('report text drops scripts, styles and markup', () => {
    const html = '<html><head><style>p{}</style><script>var x = "<p>";</script></head><body>'
        + '<h1>Beam</h1><!-- c --><p>L = 6&#160;m &lt; 7&nbsp;m</p><table><tr><td>a</td><td>b</td></tr></table>'
        + '<p><span class="err">Error in "b" on line [3]</span></p></body></html>';
    assert.equal(reportText(html), 'Beam\nL = 6 m < 7 m\na | b |\nError in "b" on line [3]');
});

test('check results use 1-based lines and count linter and runtime errors', () => {
    const lint = {
        errorCount: 1, warningCount: 1,
        diagnostics: [
            { line: 0, column: 4, endColumn: 5, code: 'CPD-1', message: 'Undefined', severity: 'Error', severityId: 0, source: 'L' },
            { line: 2, column: 0, endColumn: 1, code: 'CPD-2', message: 'Unused', severity: 'Warning', severityId: 1, source: 'L' },
        ],
    };
    const run = { html: '', errors: [
        { sourceLine: 5, outputLine: 9, message: 'Inconsistent units', source: 'Expression', code: 'CPD-3' },
        { sourceLine: 1, outputLine: 1, message: 'Undefined', source: 'Expression', code: 'CPD-1' },
    ] };
    const result = checkResult('/w/a.cpd', lint, run);
    assert.equal(result.errors, 2);
    assert.equal(result.warnings, 1);
    assert.deepEqual(result.diagnostics.map(d => [d.source, d.line]), [['linter', 1], ['linter', 3], ['runtime', 5]]);
    assert.deepEqual(result.diagnostics[0], { source: 'linter', severity: 'error', line: 1, column: 5, code: 'CPD-1', message: 'Undefined' });
    assert.deepEqual(result.diagnostics[2], { source: 'runtime', severity: 'error', line: 5, code: 'CPD-3', message: 'Inconsistent units' });
    assert.equal(result.note, undefined);
    assert.match(checkResult('/w/a.cpd', lint, null).note, /could not be run/);
    assert.throws(() => checkResult('/w/a.cpd', null, null), /did not respond/);
});

test('long reports are truncated with a note', () => {
    const result = renderResult('a.cpd', { html: `<p>${'x'.repeat(50)}</p>`, errors: [] }, 10);
    assert.equal(result.report, 'x'.repeat(10));
    assert.match(result.note, /50 characters/);
    assert.equal(renderResult('a.cpd', { html: '<p>ok</p>', errors: [] }).note, undefined);
});
