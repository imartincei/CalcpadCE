namespace Calcpad.Server.Services
{
    /// <summary>
    /// The client side half of the <c>#UI</c> feature: wires change events on the controls
    /// <c>ExpressionParser</c> emitted and hydrates datagrid containers with jspreadsheet, each
    /// edit posted to the host as a <c>uiValueChange</c> message carrying the
    /// <c>data-ui-var</c> key. Emitted server side rather than per frontend so all three hosts
    /// share one implementation, which is also all they need: each renders the document into a
    /// sandboxed frame it replaces wholesale, so there is one way out — the parent — and one way
    /// back to where the user was, whose focused control, caret and selected cell are posted to
    /// the host for it to seed into the replacement.
    /// </summary>
    internal static class UiPreviewScript
    {
        public static string GetScriptTag() => ScriptTag;

        private const string ScriptTag = """
<script>
(function () {
    // The document is always sandboxed inside the host - a preview pane in the web and
    // desktop editors, the shell's frame in VS Code - so the parent is the only way out.
    // An exported input form opened on its own is the exception: the post reaches a window
    // with no listener, leaving the controls static, as that export says they are.
    function send(msg) {
        try { window.parent.postMessage(msg, '*'); } catch (e) { }
    }

    function post(type, varName, newValue, sourceLine) {
        send({
            type: type, varName: varName, newValue: newValue, sourceLine: sourceLine,
            // Set by the web editor when it has several preview panes; absent
            // elsewhere, where the host routes to whatever document it owns.
            groupId: window.__calcpadGroupId
        });
    }

    function lineOf(el) {
        return parseInt(el.getAttribute('data-ui-line') || '0');
    }

    // Every control the user can be sitting in. The element wrapping the whole line
    // carries data-ui-var too, so matching on the classes is what distinguishes the
    // control itself from its row.
    var CONTROLS = '.calcpad-ui-input, .calcpad-ui-dropdown, .calcpad-ui-checkbox, .calcpad-ui-radio, .calcpad-ui-datagrid';
    // How long a datagrid waits for data entry to stop before it posts.
    var GRID_IDLE_MS = 400;
    var sheetsByKey = {};
    // Set once an edit has been posted. Until then nothing is persisted, so opening
    // the document afresh - rather than having it re-rendered - never moves focus.
    var armed = false;

    // Handing the state to the host is what carries it across a re-render: every host swaps
    // the document by assigning srcdoc, so this window does not survive, and the frame's
    // opaque origin denies it sessionStorage. Both the web editor and the VS Code shell hold
    // what is posted here and seed it back into the replacement as __calcpadUiPosition.
    function postState(state) {
        send({ type: 'cpdUiState', state: state, groupId: window.__calcpadGroupId });
    }

    // Consumed once, up front: a position left over from an earlier session must not
    // steal focus when the document is opened again rather than re-rendered.
    var pending = window.__calcpadUiPosition || null;
    window.__calcpadUiPosition = null;

    function saveState() {
        var state = {};
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
        postState(state);
    }

    function restoreState() {
        if (!pending) return;
        if (pending.key && pending.cell)
            restoreCell(sheetsByKey[pending.key], pending.cell);
        else if (pending.key)
            restoreFocus(pending.key, pending.caret);
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

    // What a control may produce. The entered text replaces the right hand side of the
    // assignment and #UI only annotates values, so anything that is not a number would turn the
    // line into an error — with no exponent form, since MathParser reads the 'e' of 2.5e6 as a
    // unit, and with PARTIAL additionally passing the mid-typing states, which are filtered on
    // the way in but never posted.
    var NUMBER = /^[-+]?(\d+\.?\d*|\.\d+)$/;
    var PARTIAL = /^[-+]?(\d+\.?\d*|\.\d*)?$/;

    document.querySelectorAll('.calcpad-ui-input').forEach(function (input) {
        input.setAttribute('inputmode', 'decimal');
        // The text the filter last let through, and the last complete number, which an
        // abandoned edit - a field left holding '-' - falls back to.
        var typed = input.value;
        var committed = input.value;
        input.addEventListener('input', function () {
            if (PARTIAL.test(input.value)) {
                typed = input.value;
                return;
            }
            var caret = input.selectionStart - (input.value.length - typed.length);
            input.value = typed;
            try { input.setSelectionRange(caret, caret); } catch (e) { }
        });
        input.addEventListener('change', function () {
            // '12.' is a number as far as the field is concerned, but not as the right
            // hand side of the assignment it is written into.
            var value = input.value.trim().replace(/\.$/, '');
            if (!NUMBER.test(value)) {
                input.value = committed;
                typed = committed;
                return;
            }
            input.value = value;
            committed = value;
            typed = value;
            change(input, value);
        });
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
                // The grid is sized by the #UI directive's row/col count, so the ways a user
                // could resize or annotate it are taken out of the context menu and keyboard.
                allowInsertRow: false,
                allowManualInsertRow: false,
                allowDeleteRow: false,
                allowInsertColumn: false,
                allowManualInsertColumn: false,
                allowDeleteColumn: false,
                allowComments: false,
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
                about: false,
                allowExport: false,
                onchange: emit,
                onpaste: emit,
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
                var grid = numbersOnly(sheet, sheet.getData ? sheet.getData() : []);
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

    // Every cell becomes an element of a matrix literal, so one holding text - typed or
    // pasted in - is put back to 0, in the grid as well as in what gets posted.
    function numbersOnly(sheet, grid) {
        for (var r = 0; r < grid.length; r++) {
            for (var c = 0; c < grid[r].length; c++) {
                var cell = String(grid[r][c]).trim();
                if (NUMBER.test(cell)) {
                    grid[r][c] = cell;
                    continue;
                }
                grid[r][c] = '0';
                if (sheet.setValueFromCoords) sheet.setValueFromCoords(c, r, '0');
            }
        }
        return grid;
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
