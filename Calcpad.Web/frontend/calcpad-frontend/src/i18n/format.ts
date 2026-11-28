/** Fills `{name}` placeholders in a message template. */
export function formatMessage(template: string, params?: Record<string, unknown>): string {
    if (!params) return template
    return template.replace(/\{(\w+)\}/g, (match, key) =>
        params[key] === undefined ? match : String(params[key]))
}
