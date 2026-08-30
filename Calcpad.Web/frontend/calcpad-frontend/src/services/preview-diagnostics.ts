/**
 * The two ways a rendered worksheet can fail without saying anything, both structurally
 * invisible to a console relay: a CSP violation is a *refused* fetch that throws nothing, and a
 * resource load failure raises a non-bubbling `error` event on the element itself. Together they
 * cover the whole silent class — a CDN dependency refused by a host allowlist stops the entire
 * module graph evaluating, leaving a blank diagram and an empty log.
 *
 * The detection lives here so all three front ends report the same thing; the transport does
 * not, because each host addresses its own log differently.
 */

import { consoleRelayGuardScript } from './preview-limits';

/**
 * Builds a `<script>` body (no tag) that relays both failure classes. `emit` is inlined verbatim
 * and must be a JavaScript *expression* for a function `(level, message) => void`, and
 * `maxMessages` is the per-render relay cap, which has to match whatever the console patch in
 * the same document passes — the guard installs once, and the first block to run sets it.
 */
export function previewDiagnosticsScript(emit: string, maxMessages?: number): string {
    return [
        consoleRelayGuardScript(maxMessages),
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
