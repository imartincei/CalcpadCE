import * as monaco from 'monaco-editor';
import { MenuId, MenuRegistry } from 'monaco-editor/esm/vs/platform/actions/common/actions.js';
import { translate } from 'calcpad-frontend/i18n';
import { __calcpadSetLocale, __calcpadTranslate } from 'virtual:calcpad-monaco-nls';

type Localized = string | { value?: string; original?: string } | undefined;

const CALCPAD_ACTIONS: Record<string, string> = {
    'calcpad.saveAsCustomSnippet': 'editor.saveAsCustomSnippet',
    'calcpad.focusPreviewToLine': 'editor.focusPreviewToLine',
    'calcpad.runPreview': 'editor.runPreview',
    'calcpad.editMetadata': 'editor.editMetadataProperties',
};

interface RegistryInternals {
    _commands: Map<string, { title?: Localized; shortTitle?: Localized }>;
    _menuItems: Map<unknown, Iterable<{ command?: { id?: string; title?: Localized; shortTitle?: Localized }; alt?: { title?: Localized }; title?: Localized }>>;
    appendMenuItem(id: unknown, item: unknown): { dispose(): void };
}

function retranslate(value: Localized, locale: string): Localized {
    if (typeof value === 'string') return __calcpadTranslate(value, locale) ?? value;
    if (value && typeof value === 'object' && typeof value.value === 'string')
        value.value = __calcpadTranslate(value.value, locale) ?? value.value;
    return value;
}

function retranslateCommand(command: { title?: Localized; shortTitle?: Localized } | undefined, locale: string): void {
    if (!command) return;
    command.title = retranslate(command.title, locale);
    command.shortTitle = retranslate(command.shortTitle, locale);
}

function updateCalcpadEditorActions(editor: monaco.editor.ICodeEditor): void {
    for (const [actionId, messageId] of Object.entries(CALCPAD_ACTIONS)) {
        const label = translate(messageId);
        const action = editor.getAction(actionId);
        if (!action) continue;
        (action as { label: string }).label = label;
        (action as { alias: string }).alias = label;
    }
}

function updateCalcpadActions(registry: RegistryInternals | undefined): void {
    for (const editor of monaco.editor.getEditors()) {
        updateCalcpadEditorActions(editor);
        if (!registry) continue;
        for (const [actionId, messageId] of Object.entries(CALCPAD_ACTIONS)) {
            const label = translate(messageId);
            const commandId = `${editor.getId()}:${actionId}`;
            const command = registry._commands.get(commandId);
            if (command) command.title = label;
            for (const items of registry._menuItems.values()) {
                for (const item of items) {
                    if (item.command?.id === commandId) item.command.title = label;
                }
            }
        }
    }
}

function retranslateEditor(editor: monaco.editor.ICodeEditor, locale: string): void {
    for (const action of editor.getSupportedActions()) {
        const label = __calcpadTranslate(action.label, locale);
        if (label) (action as { label: string }).label = label;
        const alias = __calcpadTranslate(action.alias, locale);
        if (alias) (action as { alias: string }).alias = alias;
    }
    updateCalcpadEditorActions(editor);
}

// Keep this to widgets whose text is UI chrome. Suggest items, parameter hints
// and peek views can contain user symbols, so they stay out of the walk.
const MONACO_UI_SELECTOR = [
    '.find-widget',
    '.suggest-widget .message',
    '.quick-input-widget',
    '.monaco-menu',
    '.monaco-dialog-box',
    '.dialog-box',
    '.rename-box',
].join(',');

let activeLocale = 'en';
let translatedValues = new WeakMap<Node, string>();
let observer: MutationObserver | undefined;
let editorListener: monaco.IDisposable | undefined;

function ensureEditorListener(): void {
    if (editorListener) return;
    editorListener = monaco.editor.onDidCreateEditor(editor => {
        retranslateEditor(editor, activeLocale);
    });
}

function translateText(node: Text): void {
    const value = node.nodeValue;
    if (!value) return;
    if (translatedValues.get(node) === value) return;
    const leading = value.match(/^\s*/)?.[0] ?? '';
    const trailing = value.match(/\s*$/)?.[0] ?? '';
    const core = value.slice(leading.length, value.length - trailing.length);
    if (!core) return;
    const translated = __calcpadTranslate(core, activeLocale);
    if (!translated || translated === core) return;
    node.nodeValue = leading + translated + trailing;
    translatedValues.set(node, node.nodeValue);
}

function translateAttributes(element: Element): void {
    for (const name of ['title', 'aria-label', 'placeholder']) {
        const value = element.getAttribute(name);
        if (!value) continue;
        const translated = __calcpadTranslate(value, activeLocale);
        if (translated && translated !== value) element.setAttribute(name, translated);
    }
}

function translateSubtree(root: Element): void {
    translateAttributes(root);
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode(); node; node = walker.nextNode())
        translateText(node as Text);
    for (const element of root.querySelectorAll('[title], [aria-label], [placeholder]'))
        translateAttributes(element);
}

function translateUi(root: ParentNode = document): void {
    for (const element of root.querySelectorAll(MONACO_UI_SELECTOR))
        translateSubtree(element);
}

function ensureObserver(): void {
    if (observer || typeof MutationObserver === 'undefined') return;
    observer = new MutationObserver(records => {
        for (const record of records) {
            if (record.type === 'characterData') {
                const text = record.target as Text;
                if (text.parentElement?.closest(MONACO_UI_SELECTOR)) translateText(text);
            } else if (record.type === 'attributes') {
                const element = record.target as Element;
                if (element.closest(MONACO_UI_SELECTOR)) translateAttributes(element);
            } else {
                for (const node of record.addedNodes) {
                    if (!(node instanceof Element)) continue;
                    if (node.matches(MONACO_UI_SELECTOR)) translateSubtree(node);
                    for (const child of node.querySelectorAll(MONACO_UI_SELECTOR))
                        translateSubtree(child);
                }
            }
        }
    });
    observer.observe(document.body, {
        childList: true,
        subtree: true,
        characterData: true,
        attributes: true,
        attributeFilter: ['title', 'aria-label', 'placeholder'],
    });
}

/** Rewrites labels that Monaco copied into its global registries at startup. */
export function applyMonacoLocale(locale: string): void {
    __calcpadSetLocale(locale);
    activeLocale = locale;
    translatedValues = new WeakMap();
    const registry = MenuRegistry as unknown as RegistryInternals;
    const usableRegistry = !!(registry._commands && registry._menuItems && typeof registry.appendMenuItem === 'function');
    updateCalcpadActions(usableRegistry ? registry : undefined);
    if (usableRegistry) {
        for (const command of registry._commands.values())
            retranslateCommand(command, locale);
        const menus = new Set<unknown>(registry._menuItems.keys());
        for (const items of registry._menuItems.values()) {
            for (const item of items) {
                retranslateCommand(item.command, locale);
                retranslateCommand(item.alt, locale);
                if (item.title !== undefined)
                    item.title = retranslate(item.title, locale);
            }
        }
        menus.add(MenuId.CommandPalette);
        for (const menu of menus) {
            const disposable = registry.appendMenuItem(menu, {
                command: { id: '__calcpad_locale_refresh__', title: '' },
            });
            disposable.dispose();
        }
    }
    for (const editor of monaco.editor.getEditors())
        retranslateEditor(editor, locale);
    ensureEditorListener();
    translateUi();
    ensureObserver();
}
