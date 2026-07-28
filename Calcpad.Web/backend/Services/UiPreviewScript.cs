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

    function change(el, value) {
        post('uiValueChange', el.getAttribute('data-ui-var'), value, lineOf(el));
    }

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

    var grids = document.querySelectorAll('.calcpad-ui-datagrid');
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
            ondeleterow: emit
        });
        var sheet = Array.isArray(created) ? created[0] : created;

        function emit() {
            var grid = sheet.getData ? sheet.getData() : [];
            var literal = grid.length === 1 ?
                '[' + grid[0].join('; ') + ']' :
                '[' + grid.map(function (row) { return row.join('; '); }).join(' | ') + ']';
            change(container, literal);
        }
    });

    function splitAttr(el, name) {
        var value = el.getAttribute(name);
        return value ? value.split(',') : null;
    }
})();
</script>
""";
    }
}
