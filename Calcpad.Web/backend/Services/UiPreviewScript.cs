namespace Calcpad.Server.Services
{
    /// <summary>
    /// The client side half of the <c>#UI</c> feature: wires change events on the controls
    /// that <c>ExpressionParser</c> emitted and hydrates datagrid containers with
    /// jspreadsheet. Every edit is posted to the host as a <c>uiValueChange</c> message
    /// carrying the control key from <c>data-ui-var</c>, which the host stores and sends
    /// back as a <c>uiOverrides</c> entry on the next convert.
    ///
    /// Emitted server side rather than per frontend so the web editor, the Tauri desktop
    /// app and the VS Code webview all share one implementation; the script picks its
    /// message channel at runtime instead of being built per host.
    ///
    /// Each edit makes the host re-render and rewrite the whole document, so the script
    /// also stashes where the user was - the focused control, the datagrid's selected
    /// cell, the scroll position - and reapplies it once the replacement has hydrated.
    /// </summary>
    internal static class UiPreviewScript
    {
        public static string GetScriptTag() => ScriptTag;

        private const string ScriptTag = """
<script>
(function () {
    var vscode = null;
    function post(type, varName, newValue, sourceLine) {
        var msg = {
            type: type, varName: varName, newValue: newValue, sourceLine: sourceLine,
            // Set by the web editor when it has several preview panes; absent
            // elsewhere, where the host routes to whatever document it owns.
            groupId: window.__calcpadGroupId
        };
        if (typeof acquireVsCodeApi !== 'undefined') {
            // acquireVsCodeApi may only be called once per webview.
            if (!vscode) vscode = window.__calcpadVsCode || (window.__calcpadVsCode = acquireVsCodeApi());
            vscode.postMessage(msg);
        } else if (window.parent !== window) {
            window.parent.postMessage(msg, '*');
        }
    }

    function lineOf(el) {
        return parseInt(el.getAttribute('data-ui-line') || '0');
    }

    // Every control the user can be sitting in. The element wrapping the whole line
    // carries data-ui-var too, so matching on the classes is what distinguishes the
    // control itself from its row.
    var CONTROLS = '.calcpad-ui-input, .calcpad-ui-dropdown, .calcpad-ui-checkbox, .calcpad-ui-radio, .calcpad-ui-datagrid';
    var STATE_KEY = 'calcpadUiPosition';
    // How long a datagrid waits for data entry to stop before it posts.
    var GRID_IDLE_MS = 400;
    var sheetsByKey = {};
    // Set once an edit has been posted. Until then nothing is persisted, so opening
    // the document afresh - rather than having it re-rendered - never moves focus.
    var armed = false;

    // The iframe hosts reuse the window across document.write, the VS Code webview
    // reloads it; sessionStorage covers both, with the window as the fallback for
    // when it is unreachable.
    function writeState(state) {
        try {
            sessionStorage.setItem(STATE_KEY, JSON.stringify(state));
            return;
        } catch (e) { }
        window.__calcpadUiPosition = state;
    }

    function readState() {
        var raw = null;
        try {
            raw = sessionStorage.getItem(STATE_KEY);
            sessionStorage.removeItem(STATE_KEY);
        } catch (e) { }
        var held = window.__calcpadUiPosition;
        window.__calcpadUiPosition = null;
        if (!raw) return held || null;
        try { return JSON.parse(raw); } catch (e2) { return held || null; }
    }

    // Consumed once, up front: a position left over from an earlier session must not
    // steal focus when the document is opened again rather than re-rendered.
    var pending = readState();

    function saveState() {
        var state = { scrollX: window.scrollX, scrollY: window.scrollY };
        var active = document.activeElement;
        var control = active && active.closest ? active.closest(CONTROLS) : null;
        var sheet = typeof jspreadsheet !== 'undefined' ? jspreadsheet.current : null;
        if (control && !control.classList.contains('calcpad-ui-datagrid')) {
            state.key = control.getAttribute('data-ui-var');
            if (active.setSelectionRange && active.type === 'text')
                state.caret = [active.selectionStart, active.selectionEnd];
        } else if (sheet && sheet.calcpadUiKey && sheet.selectedCell) {
            // A grid keeps its position in the library rather than in the focused
            // element, and jspreadsheet.current is the one taking keystrokes.
            state.key = sheet.calcpadUiKey;
            state.cell = [sheet.selectedCell[0], sheet.selectedCell[1], sheet.selectedCell[2], sheet.selectedCell[3]];
        }
        writeState(state);
    }

    function restoreState() {
        if (!pending) return;
        if (pending.key && pending.cell)
            restoreCell(sheetsByKey[pending.key], pending.cell);
        else if (pending.key)
            restoreFocus(pending.key, pending.caret);

        scrollBack();
        // Late-loading images reflow the page after this runs, which clamps the
        // scroll; the position is only final once everything has laid out.
        window.addEventListener('load', scrollBack);
    }

    function scrollBack() {
        window.scrollTo(pending.scrollX || 0, pending.scrollY || 0);
    }

    function restoreCell(sheet, cell) {
        if (!sheet) return;
        jspreadsheet.current = sheet;
        sheet.updateSelectionFromCoords(cell[0], cell[1], cell[2], cell[3]);
    }

    function restoreFocus(key, caret) {
        var target = null;
        document.querySelectorAll(CONTROLS).forEach(function (el) {
            if (!target && el.getAttribute('data-ui-var') === key) target = el;
        });
        // A radio group is a span wrapping the buttons, so focus the selected one.
        if (target && target.tagName === 'SPAN')
            target = target.querySelector('input[type="radio"]:checked') || target.querySelector('input[type="radio"]');
        if (!target || !target.focus) return;

        target.focus({ preventScroll: true });
        if (!caret || !target.setSelectionRange) return;
        try { target.setSelectionRange(caret[0], caret[1]); } catch (e) { }
    }

    // The re-render lands well after the edit that triggered it, by which time the
    // user has usually moved on, so the position keeps being written until the
    // document is actually replaced rather than being captured once at post time.
    function trackPosition() {
        if (armed) saveState();
    }

    function change(el, value) {
        post('uiValueChange', el.getAttribute('data-ui-var'), value, lineOf(el));
        armed = true;
        // Committing a cell or leaving a field moves focus on, so where to come back
        // to is only settled on the next tick.
        setTimeout(saveState, 0);
    }

    document.addEventListener('focusin', trackPosition);
    document.addEventListener('mouseup', trackPosition);

    var scrollQueued = false;
    window.addEventListener('scroll', function () {
        if (!armed || scrollQueued) return;
        scrollQueued = true;
        requestAnimationFrame(function () {
            scrollQueued = false;
            saveState();
        });
    });

    document.querySelectorAll('.calcpad-ui-input').forEach(function (input) {
        input.addEventListener('change', function () { change(input, input.value); });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') input.blur();
        });
    });

    document.querySelectorAll('.calcpad-ui-dropdown').forEach(function (select) {
        select.addEventListener('change', function () { change(select, select.value); });
    });

    document.querySelectorAll('.calcpad-ui-radio').forEach(function (group) {
        group.querySelectorAll('input[type="radio"]').forEach(function (radio) {
            radio.addEventListener('change', function () {
                if (radio.checked) change(group, radio.value);
            });
        });
    });

    document.querySelectorAll('.calcpad-ui-checkbox').forEach(function (cb) {
        cb.addEventListener('change', function () { change(cb, cb.checked ? '1' : '0'); });
    });

    hydrateGrids(document.querySelectorAll('.calcpad-ui-datagrid'));
    restoreState();

    function hydrateGrids(grids) {
        if (!grids.length) return;

        if (typeof jspreadsheet === 'undefined') {
            // The libraries are inlined into <head> whenever a datagrid is present, so this
            // only happens if the bundled assets are missing from the deployment.
            grids.forEach(function (container) {
                container.textContent = 'Datagrid library is not available.';
            });
            return;
        }

        grids.forEach(function (container) {
            var rows = parseInt(container.getAttribute('data-ui-rows') || '1');
            var cols = parseInt(container.getAttribute('data-ui-columns') || '1');
            var values = container.getAttribute('data-ui-values') || '';

            // Calcpad literal shape: '|' separates rows, ';' separates cells within a row.
            var data;
            if (values) {
                data = values.split('|').map(function (row) { return row.split(';'); });
            } else {
                data = [];
                for (var r = 0; r < rows; r++) data.push(new Array(cols).fill('0'));
            }

            var colHeaders = splitAttr(container, 'data-ui-col-headers');
            var rowHeaders = splitAttr(container, 'data-ui-row-headers');

            var columns = [];
            for (var c = 0; c < cols; c++) {
                var def = { width: 80 };
                if (colHeaders && c < colHeaders.length) def.title = colHeaders[c];
                columns.push(def);
            }

            var worksheet = {
                data: data,
                minDimensions: [cols, rows],
                columns: columns,
                tableOverflow: true,
                tableWidth: Math.min(cols * 85 + 50, 600) + 'px',
                tableHeight: Math.min(rows * 28 + 30, 400) + 'px'
            };
            if (rowHeaders) {
                worksheet.rows = {};
                for (var i = 0; i < rowHeaders.length; i++) worksheet.rows[i] = { title: rowHeaders[i] };
            }

            var created = jspreadsheet(container, {
                worksheets: [worksheet],
                onchange: emit,
                onpaste: emit,
                oninsertrow: emit,
                ondeleterow: emit,
                onselection: trackPosition,
                onblur: flushNow
            });
            var sheet = Array.isArray(created) ? created[0] : created;
            var key = container.getAttribute('data-ui-var');
            sheet.calcpadUiKey = key;
            sheetsByKey[key] = sheet;
            var idle = null;

            // Tabbing across a row fires one change per cell and each one has the host
            // rewrite the whole document, which would land on top of whatever is being
            // typed next. Filling the grid in is treated as one edit instead: the post
            // waits for a pause in data entry, or for the grid to be left.
            function emit() {
                if (idle) clearTimeout(idle);
                idle = setTimeout(flush, GRID_IDLE_MS);
            }

            function flush() {
                idle = null;
                // An open cell editor means the pause was only a slow typist.
                if (sheet.edition) {
                    emit();
                    return;
                }
                var grid = sheet.getData ? sheet.getData() : [];
                var literal = grid.length === 1 ?
                    '[' + grid[0].join('; ') + ']' :
                    '[' + grid.map(function (row) { return row.join('; '); }).join(' | ') + ']';
                change(container, literal);
            }

            function flushNow() {
                if (!idle) return;
                clearTimeout(idle);
                flush();
            }
        });
    }

    function splitAttr(el, name) {
        var value = el.getAttribute(name);
        return value ? value.split(',') : null;
    }
})();
</script>
""";
    }
}
