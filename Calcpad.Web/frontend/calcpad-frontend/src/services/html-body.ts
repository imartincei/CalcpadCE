// Strips the <html>/<head> wrapper so HTML-preview debug views show just the
// rendered content, not the boilerplate shared with the print/export template.
export function extractBodyHtml(html: string): string {
    // Start past </head> so `<body` inside head comments/styles/scripts (the
    // template has one in a CSS comment) can't be mistaken for the real tag.
    const headEnd = html.indexOf('</head>');
    const searchFrom = headEnd === -1 ? 0 : headEnd + '</head>'.length;

    const bodyOpen = html.indexOf('<body', searchFrom);
    const bodyClose = html.lastIndexOf('</body>');
    if (bodyOpen === -1 || bodyClose === -1) return html;

    const bodyStart = html.indexOf('>', bodyOpen) + 1;
    return html.substring(bodyStart, bodyClose);
}
