# Bundled UI assets

Third-party libraries that hydrate `#UI` datagrids in the preview. `BundledUiAssets`
inlines them into `<head>`, but only when the rendered document actually contains a
`calcpad-ui-datagrid` element — the payload is ~410 KB.

They are vendored rather than linked from a CDN so the preview works offline and inside
the `srcdoc` iframe, where relative URLs do not resolve.

| File | Package | Version | License |
|---|---|---|---|
| `jspreadsheet.min.js` / `jspreadsheet.min.css` | [jspreadsheet-ce](https://github.com/jspreadsheet/ce) | 5.0.4 | MIT |
| `jsuites.min.js` / `jsuites.min.css` | [jsuites](https://github.com/jsuites/jsuites) | 5.13.5 | MIT |

Fetched from jsDelivr. Both stylesheets are self-contained: every `url()` they reference
is a `data:` URI, so no request leaves the page.

Load order matters — jspreadsheet reads the `jSuites` global at parse time, so jsuites
must come first. `BundledUiAssets` hard-codes that order.

## Updating

```sh
cd Calcpad.Web/backend/UiAssets
curl -o jspreadsheet.min.js  https://cdn.jsdelivr.net/npm/jspreadsheet-ce@5/dist/index.min.js
curl -o jspreadsheet.min.css https://cdn.jsdelivr.net/npm/jspreadsheet-ce@5/dist/jspreadsheet.min.css
curl -o jsuites.min.js       https://cdn.jsdelivr.net/npm/jsuites@5/dist/jsuites.min.js
curl -o jsuites.min.css      https://cdn.jsdelivr.net/npm/jsuites@5/dist/jsuites.min.css
```

jspreadsheet-ce 5 is MIT. Version 6 and up moved to a commercial license, so the major
version is pinned in those URLs.
