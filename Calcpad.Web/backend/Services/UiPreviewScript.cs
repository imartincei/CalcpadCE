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

    // Layout constants. DEF_COL is the resting width of a column nothing was declared for,
    // MIN_* the floors a proportional shrink may not go below, SINGLE_MIN the floor for the
    // lone column of a one-column grid, which is sized from its header instead of stretched.
    var DEF_COL = 80, MIN_COL = 28, DEF_ROW_HDR = 50, MIN_ROW_HDR = 24, SINGLE_MIN = 80;
    var CHROME = 8, SCROLLBAR = 10, HDR_PAD = 12, MAX_GRID_H = 420;
    var measureCtx = null;
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
        // A tall grid scrolls, so the cell that was left can be below the fold.
        var record = sheet.records && sheet.records[cell[1]] && sheet.records[cell[1]][cell[0]];
        if (record && record.element && record.element.scrollIntoView)
            record.element.scrollIntoView({ block: 'nearest', inline: 'nearest' });
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
    // assignment, so anything the parser would reject turns the line into an error - with no
    // exponent form, since MathParser reads the 'e' of 2.5e6 as a unit, and with PARTIAL
    // additionally passing the mid-typing states, which are filtered on the way in but never
    // posted.
    var NUMBER = /^[-+\u2212]?(\d+\.?\d*|\.\d+)$/;
    var PARTIAL = /^[-+\u2212]?(\d+\.?\d*|\.\d*)?$/;
    // A number with a unit, mirroring UiSyntax.IsNumber + IsUnits: unit names are letters and
    // a handful of symbols, joined by the product/division operators, each optionally raised.
    var UNIT_NAME = '[\\p{L}\u00b0%\u2030\u2031\u2032\u2033\u2127_]+';
    var UNIT_POW = '(?:\\s*\\^\\s*[-+\u2212]?\\d+(?:\\.\\d+)?)?';
    var UNIT_PART = UNIT_NAME + UNIT_POW;
    var VALUE = new RegExp(
        '^[-+\u2212]?(?:\\d+\\.?\\d*|\\.\\d+)' +
        '(?:\\s*' + UNIT_PART + '(?:\\s*[*/\u00b7\u00d7\u2219]\\s*' + UNIT_PART + ')*)?$', 'u');

    // The three input modes of the #UI directive. Default edits the number and leaves the
    // unit in the document; 'forceUnits: false' puts the unit in the control; 'allowExpression'
    // hands the right hand side over whole and leaves the parser to report what it cannot read.
    function modeOf(el) {
        if (el.getAttribute('data-ui-allow-expression') === '1') return 'expression';
        return el.getAttribute('data-ui-force-units') === '0' ? 'value' : 'number';
    }

    function accepts(mode, text) {
        if (mode === 'expression') return text.length > 0;
        return (mode === 'value' ? VALUE : NUMBER).test(text);
    }

    document.querySelectorAll('.calcpad-ui-input').forEach(function (input) {
        var mode = modeOf(input);
        if (mode === 'number') input.setAttribute('inputmode', 'decimal');
        // The text the filter last let through, and the last accepted value, which an
        // abandoned edit - a field left holding '-' - falls back to.
        var typed = input.value;
        var committed = input.value;
        // Only the number-only mode can tell a mid-typing state from a wrong one, so it is
        // the only one that filters keystrokes; the others are checked when the edit ends.
        if (mode === 'number')
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
            var value = input.value.trim();
            if (mode !== 'expression') value = value.replace(/\.$/, '');
            if (!accepts(mode, value)) {
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
            var mode = modeOf(container);
            // Present only when the cells hold numbers and the value carried units.
            var cellUnits = jsonAttr(container, 'data-ui-cell-units');

            // Calcpad literal shape: '|' separates rows, ';' separates cells within a row.
            var data;
            if (values) {
                data = values.split('|').map(function (row) { return row.split(';'); });
            } else {
                data = [];
                for (var r = 0; r < rows; r++) data.push(new Array(cols).fill('0'));
            }

            var colHeaders = jsonAttr(container, 'data-ui-col-headers');
            var rowHeaders = jsonAttr(container, 'data-ui-row-headers');
            // Measured while the container is still a plain block element: jspreadsheet turns
            // it into its own inline-block, after which clientWidth is the table's, not the page's.
            var layout = resolveWidths(container, data, cols, colHeaders);

            var columns = [];
            for (var c = 0; c < cols; c++) {
                var def = { width: layout.cols[c] };
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
                // No tableWidth/tableHeight: both do nothing but fix .jss_content at a size of
                // their own, which leaves slack around the table and, for tableHeight, an inline
                // drop shadow drawn around it. Left off, the box shrinks to the table.
                tableOverflow: true
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
            // Everything below has to stay synchronous: restoreState() runs straight after
            // hydration and paints the selection from live geometry.
            applyRowHeaderWidth(container, layout.rowHeader);
            capHeight(container, layout.width);
            showUnits(sheet, cellUnits);
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
                var grid = valuesOnly(sheet, sheet.getData ? sheet.getData() : [], mode);
                var cells = withUnits(grid, cellUnits);
                var literal = cells.length === 1 ?
                    '[' + cells[0].join('; ') + ']' :
                    '[' + cells.map(function (row) { return row.join('; '); }).join(' | ') + ']';
                change(container, literal);
            }

            function flushNow() {
                if (!idle) return;
                clearTimeout(idle);
                flush();
            }
        });
    }

    // Must be called before jspreadsheet() turns the container into its own inline-block:
    // until then it is a block element in normal flow, so its content box is the width the
    // page allows - body margins, max-width, the host pane and any indentation included.
    function availableWidth(container) {
        var w = container.clientWidth;
        if (!w && container.parentElement) w = container.parentElement.clientWidth;
        if (!w) w = document.body.clientWidth || document.documentElement.clientWidth;
        return Math.floor(w) || 640;
    }

    function measureText(container, text) {
        if (!text) return 0;
        if (!measureCtx) {
            try { measureCtx = document.createElement('canvas').getContext('2d'); } catch (e) { return 0; }
        }
        if (!measureCtx) return 0;
        var style = window.getComputedStyle(container);
        // Built field by field: the 'font' shorthand does not always serialise.
        measureCtx.font = [style.fontStyle, style.fontWeight, style.fontSize, style.fontFamily].join(' ');
        return Math.ceil(measureCtx.measureText(text).width);
    }

    function positive(n) {
        n = parseInt(n, 10);
        return isFinite(n) && n > 0 ? n : null;
    }

    // Rescale the columns by one factor so they total `target`, keeping their relative widths,
    // and settle the rounding drift on the widest one. Their declared numbers are therefore
    // read as ratios whenever a total is asked for.
    function fitColumns(cols, target) {
        var total = 0;
        for (var i = 0; i < cols.length; i++) total += cols[i];
        if (!total || !cols.length) return cols;

        var f = target / total;
        var out = cols.map(function (w) { return Math.max(MIN_COL, Math.floor(w * f)); });
        var sum = 0, widest = 0;
        for (var j = 0; j < out.length; j++) {
            sum += out[j];
            if (out[j] > out[widest]) widest = j;
        }
        // A shortfall the floors already caused cannot be taken back out again; what is
        // left over runs off the page and scrolls.
        out[widest] = Math.max(MIN_COL, out[widest] + target - sum);
        return out;
    }

    function resolveWidths(container, data, cols, colHeaders) {
        for (var r = 0; r < data.length; r++) cols = Math.max(cols, data[r].length);

        var page = availableWidth(container) - CHROME;
        if (data.length * 24 + 30 > MAX_GRID_H) page -= SCROLLBAR;

        var declared = jsonAttr(container, 'data-ui-column-widths') || [];
        var rowHeader = positive(container.getAttribute('data-ui-row-header-width')) || DEF_ROW_HDR;
        var w = [];
        for (var c = 0; c < cols; c++) w.push(positive(declared[c]) || DEF_COL);

        // A single column has no page to share, so it is sized to its header rather than
        // stretched across one.
        if (cols === 1 && !positive(declared[0]))
            w[0] = Math.max(SINGLE_MIN, measureText(container, colHeaders && colHeaders[0]) + HDR_PAD);

        var width = parseInt(container.getAttribute('data-ui-width'), 10);
        var target = null;
        if (width === -1) target = page;              // "100%"
        else if (isFinite(width) && width > 0) target = Math.min(width, page);

        var total = rowHeader;
        for (var i = 0; i < w.length; i++) total += w[i];
        // An undeclared total is left at its natural width, unless that runs off the page.
        if (target === null) {
            if (total <= page) return { rowHeader: rowHeader, cols: w, width: page };

            target = page;
        }
        // The row header is a width, not a ratio: it keeps what it was given and only gives
        // way when the columns would have no room left.
        var room = target - cols * MIN_COL;
        if (rowHeader > room) rowHeader = Math.max(MIN_ROW_HDR, room);
        return { rowHeader: rowHeader, cols: fitColumns(w, target - rowHeader), width: page };
    }

    // jspreadsheet hardcodes width="50" on the first <col> of its colgroup and offers no
    // option for it - setWidth() indexes the data columns - so it is set after the fact.
    function applyRowHeaderWidth(container, width) {
        var col = container.querySelector('colgroup > col');
        if (!col) return;

        col.setAttribute('width', width);
        col.style.width = width + 'px';
    }

    // Measured rather than predicted from the row count: wrapped rows are taller than the
    // library's default. Set on .jss_content, which is what the PDF pass already clears.
    function capHeight(container, width) {
        var content = container.querySelector('.jss_content');
        if (!content) return;

        if (content.scrollHeight > MAX_GRID_H) {
            content.style.maxHeight = MAX_GRID_H + 'px';
            content.style.overflowY = 'auto';
        }
        // Only when the floors above could not bring the table inside the page.
        if (content.scrollWidth > width) {
            content.style.width = width + 'px';
            content.style.overflowX = 'auto';
        }
    }

    // Every cell becomes an element of a matrix literal, so one the mode does not accept -
    // typed or pasted in - is put back to 0, in the grid as well as in what gets posted.
    function valuesOnly(sheet, grid, mode) {
        for (var r = 0; r < grid.length; r++) {
            for (var c = 0; c < grid[r].length; c++) {
                var cell = String(grid[r][c]).trim();
                if (accepts(mode, cell)) {
                    grid[r][c] = cell;
                    continue;
                }
                grid[r][c] = '0';
                if (sheet.setValueFromCoords) sheet.setValueFromCoords(c, r, '0');
            }
        }
        return grid;
    }

    // The unit the cell was seeded with, put back on the way out so a grid of plain numbers
    // still writes a literal in the units the document was working in. A cell outside the
    // stored grid takes the first unit there is.
    function withUnits(grid, cellUnits) {
        if (!cellUnits) return grid;

        var fallback = '';
        for (var i = 0; i < cellUnits.length && !fallback; i++)
            for (var j = 0; j < cellUnits[i].length && !fallback; j++)
                fallback = cellUnits[i][j] || '';

        return grid.map(function (row, r) {
            return row.map(function (cell, c) {
                var unit = cellUnits[r] && cellUnits[r][c] !== undefined ? cellUnits[r][c] : fallback;
                return unit ? cell + unit : cell;
            });
        });
    }

    // The unit is not in the cell, so it is shown on hover instead.
    function showUnits(sheet, cellUnits) {
        if (!cellUnits || !sheet.records) return;

        for (var r = 0; r < sheet.records.length; r++)
            for (var c = 0; c < sheet.records[r].length; c++) {
                var unit = cellUnits[r] && cellUnits[r][c];
                if (unit && sheet.records[r][c].element)
                    sheet.records[r][c].element.title = unit;
            }
    }

    function jsonAttr(el, name) {
        var value = el.getAttribute(name);
        if (!value) return null;
        try { return JSON.parse(value); } catch (e) { return null; }
    }
})();
</script>
""";
    }
}
