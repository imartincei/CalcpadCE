# Table of Contents

> Calcpad.Web only (web editor, desktop app, and VS Code extension). Not available in the standalone WPF desktop application for Windows.

Calcpad.Web can build a navigable table of contents from your document's headings, with links that jump straight to each section.

## Writing headings

Write a heading as a comment line that starts with `'` followed by one to six `#` characters — just like Markdown:

```text
'# Section 1
'Some content
'## Subsection 1.1
'More content
'### Deeper subsection
'# Section 2
```

These become `<h1>`–`<h6>` headings in your report.
Every heading automatically gets a unique link target, so you can link to any section — the table of contents uses these targets to jump around the document.

## Building the list

Add a small script block to your document to generate the nested list.
Point it at the container where the list should appear:

```js
window.addEventListener('load', () => {
  makeList({ target: '#toc', parent: 'article' });
});
```

- `target` — the element the list is placed into (here, an element with `id="toc"`)
- `parent` — the region whose headings are collected (here, the report's `article`)

Deeper headings become nested sub-lists, and each entry is a link that scrolls to its section.

## Example

The demo file [Calcpad.Cli/Examples/Demos/toc.cpd](../Calcpad.Cli/Examples/Demos/toc.cpd) shows the whole pattern, including turning on Markdown comments with `#md on`, a bit of custom CSS, and the script block above.

## See also

- [Reporting](reporting.md) · [Writing Math](writing-math.md)
