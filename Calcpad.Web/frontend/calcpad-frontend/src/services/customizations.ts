import defaultSettingsJson from '../defaults/settings.default.json' with { type: 'json' };
import type { SnippetParameterDto } from '../types/snippets';
import type { DefinitionsResponse } from '../types/api';

/** The InsertItem fields customization reads, shared by the host and Vue item types. */
export interface SnippetLike {
    tag: string;
    label?: string;
    description?: string;
    categoryPath?: string;
    key?: string;
    customId?: string;
    keywordType?: string;
}

/** Built-in `keywordType` values a custom snippet can take; absent means plain text. */
export const CUSTOM_SNIPPET_TYPES = ['Function', 'Macro', 'Variable', 'Unit'] as const;
export type CustomSnippetType = typeof CUSTOM_SNIPPET_TYPES[number];

/** Same fields as a built-in snippet, so it is shown, inserted and completed the same way. */
export interface CustomSnippet {
    id: string;
    label: string;
    insert: string;
    description?: string;
    keywordType?: CustomSnippetType;
    documentation?: string;
    example?: string;
    returnType?: string;
    returnTypeDescription?: string;
    /** One per `§` in `insert`. */
    parameters?: SnippetParameterDto[];
}

const OPTIONAL_TEXT_FIELDS = ['description', 'documentation', 'example', 'returnType', 'returnTypeDescription'] as const;

/** Favorites, keybindings, quick types and custom snippets, each stored as a settings extra. */
export interface UserCustomizations {
    favoriteSnippets: string[];
    favoriteSettings: string[];
    customSnippets: CustomSnippet[];
    /** Command or `setting:<id>` → portable hotkey; `""` unbinds a default. */
    keybindings: Record<string, string>;
    /** Letters after `~` → inserted text; `""` disables a default. */
    quickTypes: Record<string, string>;
}

export const CUSTOMIZATION_KEYS = [
    'favoriteSnippets', 'favoriteSettings', 'customSnippets', 'keybindings', 'quickTypes',
] as const;

export const DEFAULT_KEYBINDINGS: Readonly<Record<string, string>> = defaultSettingsJson.extras.keybindings;
export const DEFAULT_QUICK_TYPES: Readonly<Record<string, string>> = defaultSettingsJson.extras.quickTypes;

export const CUSTOM_SNIPPETS_CATEGORY = 'Custom Snippets';
const CUSTOM_KEY_PREFIX = 'custom:';
export const QUICK_TYPE_MAX_LENGTH = 4;

function sanitizeParameters(raw: unknown): SnippetParameterDto[] {
    if (!Array.isArray(raw)) return [];
    const out: SnippetParameterDto[] = [];
    for (const p of raw) {
        if (!p || typeof p !== 'object') continue;
        const { name, description, type, typeDescription, isOptional, isVariadic } = p as Record<string, unknown>;
        if (typeof name !== 'string' || !name.trim()) continue;
        out.push({
            name: name.trim(),
            ...(typeof description === 'string' && description ? { description } : {}),
            ...(typeof type === 'string' && type ? { type } : {}),
            ...(typeof typeDescription === 'string' && typeDescription ? { typeDescription } : {}),
            ...(isOptional === true ? { isOptional } : {}),
            ...(isVariadic === true ? { isVariadic } : {}),
        });
    }
    return out;
}

/** Drops empty fields and unknown types so stored snippets stay minimal. */
export function sanitizeCustomSnippet(raw: unknown): CustomSnippet | null {
    if (!raw || typeof raw !== 'object') return null;
    const r = raw as Record<string, unknown>;
    if (typeof r.id !== 'string' || !r.id || typeof r.insert !== 'string' || !r.insert) return null;
    const out: CustomSnippet = {
        id: r.id,
        label: typeof r.label === 'string' && r.label.trim() ? r.label.trim() : r.insert,
        insert: r.insert,
    };
    for (const field of OPTIONAL_TEXT_FIELDS) {
        const value = r[field];
        if (typeof value === 'string' && value.trim()) out[field] = value.trim();
    }
    if (CUSTOM_SNIPPET_TYPES.includes(r.keywordType as CustomSnippetType)) out.keywordType = r.keywordType as CustomSnippetType;
    const params = sanitizeParameters(r.parameters);
    if (params.length) out.parameters = params;
    return out;
}

function parse(raw: string | undefined): unknown {
    if (!raw) return undefined;
    try { return JSON.parse(raw); } catch { return undefined; }
}

const stringList = (v: unknown): string[] =>
    Array.isArray(v) ? [...new Set(v.filter((x): x is string => typeof x === 'string' && x !== ''))] : [];

const stringMap = (v: unknown): Record<string, string> => {
    const out: Record<string, string> = {};
    if (v && typeof v === 'object' && !Array.isArray(v)) {
        for (const [k, val] of Object.entries(v)) if (typeof val === 'string') out[k] = val;
    }
    return out;
};

/** Reads customizations from the settings extras, merged over the defaults and sanitized. */
export function readCustomizations(getExtra: (key: string) => string | undefined): UserCustomizations {
    const quickTypes = { ...DEFAULT_QUICK_TYPES };
    for (const [key, text] of Object.entries(stringMap(parse(getExtra('quickTypes'))))) {
        if (!quickTypeFormatError(key)) quickTypes[key] = text;
    }
    const snippets: CustomSnippet[] = [];
    const rawSnippets = parse(getExtra('customSnippets'));
    if (Array.isArray(rawSnippets)) {
        for (const raw of rawSnippets) {
            const snippet = sanitizeCustomSnippet(raw);
            if (snippet && !snippets.some(x => x.id === snippet.id)) snippets.push(snippet);
        }
    }
    return {
        favoriteSnippets: stringList(parse(getExtra('favoriteSnippets'))),
        favoriteSettings: stringList(parse(getExtra('favoriteSettings'))),
        customSnippets: snippets,
        keybindings: { ...DEFAULT_KEYBINDINGS, ...stringMap(parse(getExtra('keybindings'))) },
        quickTypes,
    };
}

/** Each customization as the JSON string its extra stores. */
export function customizationExtras(c: UserCustomizations): Record<typeof CUSTOMIZATION_KEYS[number], string> {
    return {
        favoriteSnippets: JSON.stringify(c.favoriteSnippets),
        favoriteSettings: JSON.stringify(c.favoriteSettings),
        customSnippets: JSON.stringify(c.customSnippets),
        keybindings: JSON.stringify(c.keybindings),
        quickTypes: JSON.stringify(c.quickTypes),
    };
}

/** Stable identity for favorites. */
export function snippetKey(item: SnippetLike): string {
    if (item.customId) return CUSTOM_KEY_PREFIX + item.customId;
    return (item.categoryPath ?? '') + '|' + item.tag;
}

export function newCustomSnippetId(): string {
    return Date.now().toString(36) + Math.random().toString(36).slice(2, 8);
}

/** Built-in items tagged with their key, followed by the custom snippets. */
export function applySnippetCustomizations<T extends SnippetLike>(items: T[], c: UserCustomizations): T[] {
    const out: T[] = items.map(item => ({ ...item, key: snippetKey(item) }));
    for (const s of c.customSnippets) {
        const { id, insert, label, description, ...rest } = s;
        out.push({
            ...rest,
            tag: insert,
            label,
            description: description ?? '',
            categoryPath: CUSTOM_SNIPPETS_CATEGORY,
            customId: id,
            key: CUSTOM_KEY_PREFIX + id,
        } as unknown as T);
    }
    return out;
}

export type DefinitionKind = 'variable' | 'function' | 'macro' | 'customUnit';

export interface DefinitionLike {
    name: string;
    /** `a; b` as listed in the Variables tab. */
    params?: string;
    /** The rest come from the definition's `'<!--{...}-->` metadata comment when it has one. */
    paramTypes?: string[];
    paramDescriptions?: string[];
    description?: string;
    definition?: string;
    /** Function return type, or a variable's value type. */
    returnType?: string;
    type?: string;
}

const DEFINITION_TYPES: Record<DefinitionKind, CustomSnippetType> = {
    function: 'Function', macro: 'Macro', variable: 'Variable', customUnit: 'Unit',
};

const UNTYPED = new Set(['', 'any', 'various', 'unknown']);
const typed = (type: string | undefined): string | undefined =>
    type && !UNTYPED.has(type.trim().toLowerCase()) ? type.trim() : undefined;

/** A user definition as a custom snippet; functions and macros get `§` parameters like built-ins. */
export function customSnippetFromDefinition(kind: DefinitionKind, item: DefinitionLike): Omit<CustomSnippet, 'id'> {
    const rawParams = (item.params ?? '').split(';').map(p => p.trim()).filter(Boolean);
    const callable = kind === 'function' || (kind === 'macro' && rawParams.length > 0);
    const parameters: SnippetParameterDto[] = rawParams.map((raw, i) => {
        const eq = raw.indexOf('=');
        const type = typed(item.paramTypes?.[i]);
        const description = item.paramDescriptions?.[i]?.trim();
        return {
            name: (eq >= 0 ? raw.slice(0, eq) : raw).trim(),
            ...(type ? { type } : {}),
            ...(description ? { description } : {}),
            ...(eq >= 0 ? { isOptional: true } : {}),
        };
    });
    const signature = callable ? `${item.name}(${rawParams.join('; ')})` : item.name;
    const body = item.definition?.trim() ?? '';
    let example = '';
    if (body) {
        if (kind === 'macro') example = body.includes('\n') ? `#def ${signature}\n${body}\n#end def` : `#def ${signature} = ${body}`;
        else example = `${kind === 'customUnit' ? '.' : ''}${signature} = ${body}`;
    }
    const description = item.description?.trim() || example.split('\n')[0];
    const returnType = kind === 'function' || kind === 'variable' ? typed(item.returnType ?? item.type) : undefined;
    return {
        label: callable ? `${item.name}(${parameters.map(p => p.name).join('; ')})` : item.name,
        insert: callable ? `${item.name}(${parameters.map(() => '§').join('; ')})` : item.name,
        keywordType: DEFINITION_TYPES[kind],
        ...(description ? { description } : {}),
        ...(example ? { example } : {}),
        ...(returnType ? { returnType } : {}),
        ...(parameters.length ? { parameters } : {}),
    };
}

/** The snippet draft for a definition in the server's definitions, matched by name. */
export function snippetDraftForSymbol(
    defs: Pick<DefinitionsResponse, 'macros' | 'functions' | 'variables' | 'customUnits'>,
    kind: DefinitionKind,
    name: string,
): Omit<CustomSnippet, 'id'> | null {
    const bare = name.replace(/^\./, '');
    const same = (n: string) => n === name || n === bare || n.replace(/\$$/, '') === bare.replace(/\$$/, '');
    switch (kind) {
        case 'macro': {
            const m = defs.macros.find(d => same(d.name));
            return m ? customSnippetFromDefinition('macro', {
                name: m.name, params: m.parameters.join('; '), definition: m.content.join('\n'),
                description: m.description, paramTypes: m.paramTypes, paramDescriptions: m.paramDescriptions,
            }) : null;
        }
        case 'function': {
            const f = defs.functions.find(d => same(d.name));
            return f ? customSnippetFromDefinition('function', {
                name: f.name, params: f.parameters.join('; '), definition: f.expression, returnType: f.returnType,
                description: f.description, paramTypes: f.paramTypes, paramDescriptions: f.paramDescriptions,
            }) : null;
        }
        case 'variable': {
            const v = [...defs.variables].reverse().find(d => same(d.name));
            return v ? customSnippetFromDefinition('variable', {
                name: v.name, definition: v.expression, type: v.type, description: v.description,
            }) : null;
        }
        case 'customUnit': {
            const u = defs.customUnits.find(d => same(d.name));
            return u ? customSnippetFromDefinition('customUnit', {
                name: u.name, definition: u.expression, description: u.description,
            }) : null;
        }
    }
}

const TEXT_LABEL_MAX_LENGTH = 40;

/** A Text snippet draft for selected text, named after its first line. */
export function snippetDraftFromText(text: string): Omit<CustomSnippet, 'id'> | null {
    if (!text.trim()) return null;
    const firstLine = text.trim().split(/\r?\n/)[0].trim();
    const label = firstLine.length > TEXT_LABEL_MAX_LENGTH ? firstLine.slice(0, TEXT_LABEL_MAX_LENGTH - 1) + '…' : firstLine;
    return { label, insert: text.replace(/\r\n/g, '\n') };
}

/** The snippet draft for the symbol under the cursor, falling back to the word there (custom units aren't symbols). */
export function snippetDraftAt(
    defs: Pick<DefinitionsResponse, 'macros' | 'functions' | 'variables' | 'customUnits'>,
    symbol: { symbolName: string; kind: string } | null,
    word: string | undefined,
): Omit<CustomSnippet, 'id'> | null {
    if (symbol && (symbol.kind === 'variable' || symbol.kind === 'function' || symbol.kind === 'macro')) {
        const draft = snippetDraftForSymbol(defs, symbol.kind, symbol.symbolName);
        if (draft) return draft;
    }
    if (!word) return null;
    for (const kind of ['function', 'macro', 'variable', 'customUnit'] as const) {
        const draft = snippetDraftForSymbol(defs, kind, word);
        if (draft) return draft;
    }
    return null;
}

/** Enabled quick-type letters for an inserted text. */
export function quickTypesFor(text: string, quickTypes: Record<string, string>): string[] {
    return Object.keys(quickTypes).filter(k => quickTypes[k] !== '' && quickTypes[k] === text);
}

/** `~letters` → inserted text, skipping disabled entries. `§` placeholders are stripped unless kept. */
export function buildQuickTypeMap(quickTypes: Record<string, string>, keepPlaceholders = false): Map<string, string> {
    const map = new Map<string, string>();
    for (const [key, text] of Object.entries(quickTypes)) {
        if (text) map.set('~' + key, keepPlaceholders ? text : text.replace(/§/g, ''));
    }
    return map;
}

/** Removes a quick type: a default is kept as `""` so the merge doesn't restore it. */
export function removeQuickType(quickTypes: Record<string, string>, key: string): Record<string, string> {
    const next = { ...quickTypes };
    if (key in DEFAULT_QUICK_TYPES) next[key] = '';
    else delete next[key];
    return next;
}

function quickTypeFormatError(value: string): string | null {
    if (!value || value.length > QUICK_TYPE_MAX_LENGTH) return 'catalog.enter1ToMaxCharacters';
    if (/[\s~]/.test(value)) return 'catalog.spacesAndAreNotAllowed';
    return null;
}

export interface QuickTypeError {
    message: string;
    params: Record<string, string | number>;
}

/** Why `key` can't be added as a quick type, or null if it is free. */
export function validateQuickType(key: string, quickTypes: Record<string, string>): QuickTypeError | null {
    const formatError = quickTypeFormatError(key);
    if (formatError) return { message: formatError, params: { max: QUICK_TYPE_MAX_LENGTH } };
    const existing = quickTypes[key];
    if (existing) return { message: 'catalog.keyAlreadyInsertsText', params: { key, text: existing } };
    return null;
}
