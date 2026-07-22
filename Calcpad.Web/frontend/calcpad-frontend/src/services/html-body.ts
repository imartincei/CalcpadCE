// Strips the <html>/<head> wrapper so HTML-preview debug views show just the
// rendered content, not the boilerplate shared with the print/export template.
export function extractBodyHtml(html: string): string {
    const bodyOpen = html.indexOf('<body');
    const bodyClose = html.lastIndexOf('</body>');
    if (bodyOpen === -1 || bodyClose === -1) return html;

    const bodyStart = html.indexOf('>', bodyOpen) + 1;
    return html.substring(bodyStart, bodyClose);
}
