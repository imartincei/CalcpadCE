/**
 * The two ways a rendered worksheet can fail without saying anything.
 *
 * Both are invisible to a console relay, for structural reasons rather than by
 * oversight:
 *
 *  - A CSP violation is a *refused* fetch, not a failed one. Nothing throws, so
 *    `window.onerror` never fires and `console.*` is never called. The browser writes
 *    it to devtools and nowhere else.
 *  - A resource that fails to load raises an `error` event on the element itself.
 *    It does not bubble, and `window.onerror` only sees uncaught exceptions, so
 *    without capture-phase interception it is lost too.
 *
 * Together they cover the whole silent class. Their absence cost a full
 * investigation once already: the DXF module imports `dxf-viewer` from jsdelivr,
 * which resolves `opentype.js` from `cdn.skypack.dev` at runtime — a host present in
 * no worksheet. A CSP host allowlist refused it silently, and because a failed
 * dependency means the whole module graph never evaluates, not one statement of the
 * worksheet's own script ran. The symptom was a blank diagram and an empty log.
 *
 * The detection lives here so all three front ends report the same thing; the
 * transport does not, because each host addresses its own log differently.
 */

import { consoleRelayGuardScript } from './preview-limits';

/**
 * Builds a `<script>` body (no tag) that relays both failure classes.
 *
 * `emit` is a JavaScript *expression* for a function `(level, message) => void` —
 * whatever puts a line in the host's log. It is inlined verbatim, so it must be an
 * expression and not a statement:
 *
 * ```ts
 * // A frame that posts to its embedder
 * previewDiagnosticsScript("function (l, m) { send({ type: 'consoleMessage', level: l, message: m }); }")
 * ```
 */
export function previewDiagnosticsScript(emit: string): string {
    return [
        consoleRelayGuardScript(),
        '(function () {',
        '  var raw = ' + emit + ';',
        // Both classes report a URL the document controls, so both are clipped and counted
        // like any other relayed line. A suppressed one is dropped rather than emitted.
        '  var emit = function (level, message) {',
        '    var line = window.__calcpadRelayLine(message);',
        '    if (line !== null) raw(level, line);',
        '  };',
        // Guard: a document may be handed several injected blocks, and reporting the
        // same violation twice reads as two separate failures.
        '  if (window.__calcpadDiagnosticsReady) return;',
        '  window.__calcpadDiagnosticsReady = true;',
        "  document.addEventListener('securitypolicyviolation', function (e) {",
        "    emit('error', '[CSP] blocked ' + (e.blockedURI || '(inline)')",
        "      + ' - violated ' + (e.effectiveDirective || e.violatedDirective));",
        '  });',
        "  window.addEventListener('error', function (e) {",
        '    var el = e.target;',
        // An uncaught exception targets the window and is already reported elsewhere;
        // only element targets are resource failures.
        '    if (!el || el === window || !el.tagName) return;',
        "    emit('error', '[load failed] <' + el.tagName.toLowerCase() + '> '",
        "      + (el.src || el.href || '(inline)'));",
        '  }, true);',
        '})();',
    ].join('\n');
}
