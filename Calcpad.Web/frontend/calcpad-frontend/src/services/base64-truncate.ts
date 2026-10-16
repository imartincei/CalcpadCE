/**
 * Truncates base64 data URIs in content to reduce payload size before
 * sending to the highlighter/linter. The truncated content still tokenizes
 * correctly because the HTML structure (tags, attributes, quotes) is preserved.
 */

const BASE64_DATA_URI_PATTERN = /(data:[^;]+;base64,)[A-Za-z0-9+/=]{100,}/g;

export function truncateBase64Content(content: string): string {
    return content.replace(BASE64_DATA_URI_PATTERN, '$1TRUNCATED');
}
