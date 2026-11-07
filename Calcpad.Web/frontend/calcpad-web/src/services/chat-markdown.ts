function escapeHtml(text: string): string {
    return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function inline(text: string): string {
    return escapeHtml(text)
        .replace(/`([^`\n]+)`/g, '<code>$1</code>')
        .replace(/\*\*([^*\n]+)\*\*/g, '<strong>$1</strong>');
}

/** Minimal Markdown for chat replies: fences, headings, lists, inline code and bold. Output is escaped. */
export function renderChatMarkdown(source: string): string {
    const out: string[] = [];
    const lines = source.replace(/\r\n/g, '\n').split('\n');
    let list: 'ul' | 'ol' | null = null;
    const closeList = () => {
        if (list) out.push(`</${list}>`);
        list = null;
    };
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        if (line.startsWith('```')) {
            closeList();
            const code: string[] = [];
            for (i++; i < lines.length && !lines[i].startsWith('```'); i++) code.push(lines[i]);
            out.push(`<pre><code>${escapeHtml(code.join('\n'))}</code></pre>`);
            continue;
        }
        const heading = /^(#{1,4})\s+(.*)$/.exec(line);
        const bullet = /^\s*[-*]\s+(.*)$/.exec(line);
        const numbered = /^\s*\d+[.)]\s+(.*)$/.exec(line);
        if (heading) {
            closeList();
            out.push(`<p class="chat-heading">${inline(heading[2])}</p>`);
        } else if (bullet || numbered) {
            const kind = bullet ? 'ul' : 'ol';
            if (list !== kind) {
                closeList();
                out.push(`<${kind}>`);
                list = kind;
            }
            out.push(`<li>${inline((bullet ?? numbered)![1])}</li>`);
        } else if (!line.trim()) {
            closeList();
        } else {
            closeList();
            out.push(`<p>${inline(line)}</p>`);
        }
    }
    closeList();
    return out.join('');
}
