import legacyKeys from './legacy-keys.generated'

/**
 * Resolves a legacy English key to its stable message ID. IDs pass through.
 * Temporary bridge for the message-ID migration.
 */
export function resolveMessageKey(key: string): string {
    return legacyKeys[key] ?? key
}
