namespace Calcpad.Server.Services
{
    /// <summary>
    /// The client side half of the <c>#UI</c> feature: wires the controls <c>ExpressionParser</c>
    /// emitted, hydrates datagrids with jspreadsheet and posts each edit to the host as a
    /// <c>uiValueChange</c>. Emitted server side so all three hosts share one implementation.
    /// </summary>
    internal static class UiPreviewScript
    {
        public static string GetScriptTag() => ScriptTag;

        private const string ScriptTag = """
<script>
(function () {
    // The document is sandboxed inside the host, so the parent is the only way out. An exported
    // form opened on its own posts to a window with no listener, leaving it static.
    function send(msg) {
        try { window.parent.postMessage(msg, '*'); } catch (e) { }
    }

    function post(type, varName, newValue, sourceLine) {
        send({
            type: type, varName: varName, newValue: newValue, sourceLine: sourceLine,
            groupId: window.__calcpadGroupId
        });
    }

    function lineOf(el) {
        return parseInt(el.getAttribute('data-ui-line') || '0');
    }

    // The line wrapper carries data-ui-var too, so the classes pick out the control itself.
    var CONTROLS = '.calcpad-ui-input, .calcpad-ui-dropdown, .calcpad-ui-checkbox, .calcpad-ui-radio, .calcpad-ui-datagrid';
    // How long a datagrid waits for data entry to stop before it posts.
    var GRID_IDLE_MS = 400;

    // MIN_* floor a proportional shrink, DEF_* a measured one. No column, measured or
    // shrunk, passes MAX_SHARE of the grid - one long word must not crowd out the rest.
    var DEF_COL = 80, MIN_COL = 28, DEF_ROW_HDR = 50, MIN_ROW_HDR = 24, MAX_SHARE = 0.25;
    var CHROME = 8, SCROLLBAR = 10, HDR_PAD = 12, CELL_PAD = 10, MAX_GRID_H = 420;
    // The table's own left and right border, outside every column.
    var BORDERS = 2;
    var measureCtx = null;
    var sheetsByKey = {};
    // Nothing is persisted until an edit is posted, so opening a document afresh never moves focus.
    var armed = false;

    // Assigning srcdoc does not leave this window standing and its opaque origin denies it
    // storage, so the host holds the state and seeds it back as __calcpadUiPosition.
    function postState(state) {
        send({ type: 'cpdUiState', state: state, groupId: window.__calcpadGroupId });
    }

    // Consumed once: a stale position must not steal focus when a document is merely opened.
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
            // A grid keeps its position in the library; jspreadsheet.current takes the keystrokes.
            state.key = sheet.calcpadUiKey;
            state.cell = [sheet.selectedCell[0], sheet.selectedCell[1], sheet.selectedCell[2], sheet.selectedCell[3]];
            state.scroll = [sheet.content.scrollLeft, sheet.content.scrollTop];
        }
        postState(state);
    }

    function restoreState() {
        if (!pending) return;
        if (pending.key && pending.cell)
            restoreCell(sheetsByKey[pending.key], pending.cell, pending.scroll);
        else if (pending.key)
            restoreFocus(pending.key, pending.caret);
    }

    // The offset goes back verbatim: selecting the cell only scrolls far enough to reveal it,
    // which loses the position whenever the cell was already in view at the left.
    function restoreCell(sheet, cell, scroll) {
        if (!sheet) return;
        jspreadsheet.current = sheet;
        sheet.updateSelectionFromCoords(cell[0], cell[1], cell[2], cell[3]);
        sheet.content.scrollLeft = scroll[0];
        sheet.content.scrollTop = scroll[1];
    }

    function restoreFocus(key, caret) {
        var target = null;
        document.querySelectorAll(CONTROLS).forEach(function (el) {
            if (!target && el.getAttribute('data-ui-var') === key) target = el;
        });
        if (target && target.tagName === 'SPAN')
            target = target.querySelector('input[type="radio"]:checked') || target.querySelector('input[type="radio"]');
        if (!target || !target.focus) return;

        target.focus({ preventScroll: true });
        if (!caret || !target.setSelectionRange) return;
        try { target.setSelectionRange(caret[0], caret[1]); } catch (e) { }
    }

    // The re-render lands long after the edit, so the position is rewritten until then.
    function trackPosition() {
        if (armed) saveState();
    }

    function change(el, value) {
        post('uiValueChange', el.getAttribute('data-ui-var'), value, lineOf(el));
        armed = true;
        // Committing moves focus on, so where to come back to settles on the next tick.
        setTimeout(saveState, 0);
    }

    document.addEventListener('focusin', trackPosition);
    document.addEventListener('mouseup', trackPosition);

    // The entered text replaces the right hand side, so anything the parser would reject turns
    // the line into an error - no exponent form, since MathParser reads the 'e' of 2.5e6 as a
    // unit. PARTIAL also passes the mid-typing states, which are never posted.
    var NUMBER = /^[-+\u2212]?(\d+\.?\d*|\.\d+)$/;
    var PARTIAL = /^[-+\u2212]?(\d+\.?\d*|\.\d*)?$/;
    // A number with a unit, mirroring UiSyntax.IsNumber + IsUnits.
    var UNIT_NAME = '[\\p{L}\u00b0%\u2030\u2031\u2032\u2033\u2127_]+';
    var UNIT_POW = '(?:\\s*\\^\\s*[-+\u2212]?\\d+(?:\\.\\d+)?)?';
    var UNIT_PART = UNIT_NAME + UNIT_POW;
    var VALUE = new RegExp(
        '^[-+\u2212]?(?:\\d+\\.?\\d*|\\.\\d+)' +
        '(?:\\s*' + UNIT_PART + '(?:\\s*[*/\u00b7\u00d7\u2219]\\s*' + UNIT_PART + ')*)?$', 'u');

    // Default edits the number and leaves the unit in the document; 'forceUnits: false' puts the
    // unit in the control; 'allowExpression' hands the right hand side over whole.
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
        var typed = input.value;
        var committed = input.value;
        // Only the number-only mode can tell a mid-typing state from a wrong one.
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
            // '12.' passes as a number in the field, but not as a right hand side.
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
            // A unit reaching a numeric cell is rejected on the next edit and takes the value
            // with it, so it is put aside here rather than shown.
            if (mode === 'number') cellUnits = stripUnits(data, cellUnits);

            var colHeaders = jsonAttr(container, 'data-ui-col-headers');
            var rowHeaders = jsonAttr(container, 'data-ui-row-headers');
            var layout = resolveWidths(container, data, cols, colHeaders, rowHeaders);

            var columns = [];
            for (var c = 0; c < cols; c++) {
                var def = { width: layout.cols[c] };
                if (colHeaders && c < colHeaders.length) def.title = colHeaders[c];
                columns.push(def);
            }

            // No tableOverflow: it only gates tableWidth/tableHeight, which size .jss_content at a
            // size of its own, and it has the library close an open cell editor on any scroll.
            var worksheet = {
                data: data,
                minDimensions: [cols, rows],
                columns: columns,
                // The #UI directive sizes the grid, so resizing and annotating are taken out.
                allowInsertRow: false,
                allowManualInsertRow: false,
                allowDeleteRow: false,
                allowInsertColumn: false,
                allowManualInsertColumn: false,
                allowDeleteColumn: false,
                allowComments: false,
                // The directive owns the headers and the geometry.
                allowRenameColumn: false,
                columnResize: false,
                rowResize: false,
                // Reordering would permute the matrix written back to the document.
                columnSorting: false,
                columnDrag: false,
                rowDrag: false
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
            // Must stay synchronous: restoreState() paints the selection from live geometry.
            applyRowHeaderWidth(container, layout.rowHeader);
            capHeight(container, layout.width);
            showUnits(sheet, cellUnits);
            var idle = null;

            // Each committed cell would have the host rewrite the document on top of whatever
            // is typed next, so filling the grid in is treated as one edit.
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

    // Called before jspreadsheet() makes the container an inline-block: until then its content
    // box is the width the page allows.
    function availableWidth(container) {
        var w = container.clientWidth;
        if (!w && container.parentElement) w = container.parentElement.clientWidth;
        if (!w) w = document.body.clientWidth || document.documentElement.clientWidth;
        return Math.floor(w) || 640;
    }

    function measureText(container, text, bold) {
        if (!text) return 0;
        if (!measureCtx) {
            try { measureCtx = document.createElement('canvas').getContext('2d'); } catch (e) { return 0; }
        }
        if (!measureCtx) return 0;
        var style = window.getComputedStyle(container);
        // The 'font' shorthand does not always serialise.
        measureCtx.font = [style.fontStyle, bold ? 'bold' : style.fontWeight, style.fontSize, style.fontFamily].join(' ');
        return Math.ceil(measureCtx.measureText(text).width);
    }

    // Breaking only at spaces, the longest word is the width below which a line starts to clip.
    function widestWord(container, text, bold, cap) {
        if (text === null || text === undefined) return 0;
        var words = String(text).split(/\s+/), max = 0;
        for (var i = 0; i < words.length; i++) {
            max = Math.max(max, measureText(container, words[i], bold));
            if (max >= cap) return cap;
        }
        return max;
    }

    function positive(n) {
        n = parseInt(n, 10);
        return isFinite(n) && n > 0 ? n : null;
    }

    // The declared total, in pixels or as a percentage of the line, never wider than the page.
    function targetWidth(container, page) {
        var raw = (container.getAttribute('data-ui-width') || '').trim();
        if (raw.slice(-1) === '%') {
            var percent = parseFloat(raw);
            return isFinite(percent) && percent > 0 ? Math.min(page, Math.round(page * percent / 100)) : null;
        }
        var px = positive(raw);
        return px === null ? null : Math.min(px, page);
    }

    // One factor with the drift settled on the widest, so declared widths are read as ratios.
    // A column pinned to its floor leaves the pool, and the rest share what is left of the
    // target; when the floors alone outgrow it the grid overflows and scrolls.
    function fitColumns(cols, floors, target) {
        var n = cols.length, out = cols.slice(), pinned = [], i;
        if (!n) return out;

        for (var pass = 0; pass <= n; pass++) {
            var room = target, pool = 0;
            for (i = 0; i < n; i++) {
                if (pinned[i]) room -= out[i];
                else pool += cols[i];
            }
            if (!pool) break;

            var hit = false;
            for (i = 0; i < n; i++) {
                if (pinned[i]) continue;
                out[i] = Math.floor(cols[i] * room / pool);
                if (out[i] < floors[i]) {
                    out[i] = floors[i];
                    pinned[i] = true;
                    hit = true;
                }
            }
            if (!hit) break;
        }

        var sum = 0, widest = -1;
        for (i = 0; i < n; i++) {
            sum += out[i];
            if (!pinned[i] && (widest < 0 || out[i] > out[widest])) widest = i;
        }
        if (widest >= 0) out[widest] = Math.max(floors[widest], out[widest] + target - sum);
        return out;
    }

    // The longest word a column has to show, header included. Capped, not floored: the
    // caller floors a natural width at DEF_COL and a shrunk one at MIN_COL.
    function measureColumn(container, data, c, header, cap) {
        var max = widestWord(container, header, true, cap) + HDR_PAD;
        for (var r = 0; r < data.length && max < cap; r++)
            max = Math.max(max, widestWord(container, data[r][c], false, cap) + CELL_PAD);

        return Math.min(cap, max);
    }

    function resolveWidths(container, data, cols, colHeaders, rowHeaders) {
        for (var r = 0; r < data.length; r++) cols = Math.max(cols, data[r].length);

        var page = availableWidth(container) - CHROME;
        if (data.length * 24 + 30 > MAX_GRID_H) page -= SCROLLBAR;

        var target = targetWidth(container, page);
        var cap = Math.max(DEF_COL, Math.round((target === null ? page : target) * MAX_SHARE));

        var declared = jsonAttr(container, 'data-ui-column-widths') || [];
        var rowHeader = positive(container.getAttribute('data-ui-row-header-width'));
        if (rowHeader === null) {
            rowHeader = DEF_ROW_HDR;
            for (var h = 0; rowHeaders && h < rowHeaders.length; h++)
                rowHeader = Math.max(rowHeader, Math.min(cap, widestWord(container, rowHeaders[h], true, cap) + HDR_PAD));
        }
        // A declared width is a ratio, but its content still sets the floor it cannot shrink past.
        var w = [], floors = [];
        for (var c = 0; c < cols; c++) {
            var word = measureColumn(container, data, c, colHeaders && colHeaders[c], cap);
            w.push(positive(declared[c]) || Math.max(DEF_COL, word));
            floors.push(Math.max(MIN_COL, word));
        }

        // An undeclared total keeps its natural width - capHeight scrolls it, shrinking would clip.
        if (target === null) return { rowHeader: rowHeader, cols: w, width: page };

        var floorTotal = 0;
        for (var i = 0; i < floors.length; i++) floorTotal += floors[i];
        // The row header is a width, not a ratio: it gives way only where that buys a fit.
        // Once the floors alone outgrow the target, narrowing it just loses the row titles too.
        var room = target - BORDERS - floorTotal;
        if (rowHeader > room && room >= MIN_ROW_HDR) rowHeader = room;
        return { rowHeader: rowHeader, cols: fitColumns(w, floors, target - rowHeader - BORDERS), width: page };
    }

    // jspreadsheet hardcodes width="50" on the first <col> and offers no option for it.
    function applyRowHeaderWidth(container, width) {
        var col = container.querySelector('colgroup > col');
        if (!col) return;

        col.setAttribute('width', width);
        col.style.width = width + 'px';
    }

    // Measured, not predicted: wrapped rows are taller than the library's default. Set on
    // .jss_content, which is what the PDF pass already clears.
    function capHeight(container, width) {
        var content = container.querySelector('.jss_content');
        if (!content) return;

        if (content.scrollHeight > MAX_GRID_H) {
            content.style.maxHeight = MAX_GRID_H + 'px';
            content.style.overflowY = 'auto';
        }
        if (content.scrollWidth > width) {
            content.style.width = width + 'px';
            content.style.overflowX = 'auto';
        }
    }

    // Every cell becomes a matrix element, so one the mode rejects goes back to 0.
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

    // Mirrors SplitCell server side: the numeric prefix, then whatever unit follows it.
    function splitUnit(cell) {
        var s = String(cell), i = 0;
        while (i < s.length && '0123456789.-+−'.indexOf(s.charAt(i)) !== -1) ++i;
        return i === 0 ? [s, ''] : [s.slice(0, i), s.slice(i)];
    }

    // Takes any unit out of the cells and into the stash, so the grid edits numbers only.
    function stripUnits(data, cellUnits) {
        for (var r = 0; r < data.length; r++)
            for (var c = 0; c < data[r].length; c++) {
                var parts = splitUnit(data[r][c]);
                if (!parts[1]) continue;
                data[r][c] = parts[0];
                if (!cellUnits) cellUnits = data.map(function (row) { return row.map(function () { return ''; }); });
                if (!cellUnits[r]) cellUnits[r] = [];
                cellUnits[r][c] = parts[1];
            }
        return cellUnits;
    }

    // The seeded unit goes back on, so a grid of plain numbers still writes the document's units.
    function withUnits(grid, cellUnits) {
        if (!cellUnits) return grid;

        return grid.map(function (row, r) {
            return row.map(function (cell, c) {
                var unit = (cellUnits[r] && cellUnits[r][c]) || '';
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
