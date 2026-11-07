import { reactive } from 'vue';
import { applyEdit, prepareEdit, runEditorTool, type AgentEditorContext, type PreparedEdit } from './agent-tools';

export type ApprovalState = 'pending' | 'allowed' | 'denied' | 'expired';

export type ChatItem =
    | { kind: 'user'; text: string }
    | { kind: 'assistant'; text: string; streaming: boolean }
    | { kind: 'tool'; id: string; name: string; input: unknown; result?: string; isError?: boolean; done: boolean }
    | { kind: 'approval'; requestId: number; toolName: string; input: unknown; state: ApprovalState }
    | { kind: 'edit'; requestId: number; title: string; summary: string; state: ApprovalState }
    | { kind: 'notice'; text: string; error: boolean };

export type AgentStatus = 'idle' | 'starting' | 'ready' | 'busy';

export interface Conversation {
    id: string;
    title: string;
    createdAt: number;
    updatedAt: number;
    /** Claude Code session to resume when the process is restarted. */
    sessionId: string | null;
    /** Model alias picked for this conversation; empty is Claude Code's default. */
    model: string;
    resolvedModel: string | null;
    version: string | null;
    status: AgentStatus;
    items: ChatItem[];
}

export interface HistoryMeta {
    id: string;
    title: string;
    createdAt: number;
    updatedAt: number;
}

export interface AgentSettings {
    executable: string;
    /** Model for new conversations. */
    model: string;
}

export interface PendingEdit {
    conversationId: string;
    requestId: number;
    title: string;
    summary: string;
    original: string;
    modified: string;
}

type AgentEvent =
    | { kind: 'sessionStarted'; sessionId: string; model: string | null; version: string | null }
    | { kind: 'textDelta'; text: string }
    | { kind: 'assistantText'; text: string }
    | { kind: 'toolUse'; id: string; name: string; input: unknown }
    | { kind: 'toolResult'; toolUseId: string; isError: boolean; content: string }
    | { kind: 'turnComplete'; isError: boolean; subtype: string; costUsd: number | null; error: string | null }
    | { kind: 'status'; text: string }
    | { kind: 'exited'; code: number | null; output: string };

interface ToolRequest {
    id: number;
    conversation: string;
    tool: string;
    args: Record<string, unknown>;
}

/** Claude Code aliases that always resolve to the latest model of each family. Empty = Claude Code's default. */
export const MODEL_OPTIONS: { value: string; label: string }[] = [
    { value: '', label: 'Default' },
    { value: 'fable', label: 'Fable' },
    { value: 'opus', label: 'Opus' },
    { value: 'sonnet', label: 'Sonnet' },
    { value: 'haiku', label: 'Haiku' },
];

const SETTINGS_KEY = 'calcpad.agent.settings';
const PANEL_KEY = 'calcpad.agent.panelOpen';
const TABS_KEY = 'calcpad.agent.tabs';
const SAVE_DELAY_MS = 800;
const TITLE_LENGTH = 60;

function loadSettings(): AgentSettings {
    try {
        const raw = JSON.parse(localStorage.getItem(SETTINGS_KEY) ?? '{}');
        return { executable: String(raw.executable ?? ''), model: String(raw.model ?? '') };
    } catch {
        return { executable: '', model: '' };
    }
}

function createConversation(model: string): Conversation {
    const now = Date.now();
    return {
        id: crypto.randomUUID(),
        title: '',
        createdAt: now,
        updatedAt: now,
        sessionId: null,
        model,
        resolvedModel: null,
        version: null,
        status: 'idle',
        items: [],
    };
}

const initialSettings = loadSettings();
const initialConversation = createConversation(initialSettings.model);

export const agentChat = reactive({
    panelOpen: localStorage.getItem(PANEL_KEY) === 'true',
    settings: initialSettings,
    conversations: [initialConversation] as Conversation[],
    activeId: initialConversation.id,
    view: 'chat' as 'chat' | 'history',
    history: [] as HistoryMeta[],
    pendingEdit: null as PendingEdit | null,
});

let editorContext: AgentEditorContext | null = null;
let listening: Promise<void> | null = null;
let restored: Promise<void> | null = null;
// Kept out of the reactive store: prepared edits hold Monaco objects.
const editQueue: { view: PendingEdit; edit: PreparedEdit }[] = [];
const sessionAllowed = new Map<string, Set<string>>();
const approvalInputs = new Map<number, unknown>();
const saveTimers = new Map<string, ReturnType<typeof setTimeout>>();

async function invoke<T>(cmd: string, args?: Record<string, unknown>): Promise<T> {
    const { invoke } = await import('@tauri-apps/api/core');
    return invoke<T>(cmd, args);
}

function errorText(err: unknown): string {
    return err instanceof Error ? err.message : String(err);
}

export function activeConversation(): Conversation {
    return agentChat.conversations.find(c => c.id === agentChat.activeId) ?? agentChat.conversations[0];
}

function find(id: string): Conversation | undefined {
    return agentChat.conversations.find(c => c.id === id);
}

export function needsAttention(conversation: Conversation): boolean {
    return conversation.items.some(i => (i.kind === 'approval' || i.kind === 'edit') && i.state === 'pending');
}

// ---- Persistence ----

function persistTabs(): void {
    localStorage.setItem(TABS_KEY, JSON.stringify({
        ids: agentChat.conversations.filter(c => c.items.length).map(c => c.id),
        active: agentChat.activeId,
    }));
}

function snapshot(conversation: Conversation): string {
    return JSON.stringify({
        version: 1,
        id: conversation.id,
        title: conversation.title,
        createdAt: conversation.createdAt,
        updatedAt: conversation.updatedAt,
        sessionId: conversation.sessionId,
        model: conversation.model,
        resolvedModel: conversation.resolvedModel,
        items: conversation.items,
    });
}

async function saveNow(conversation: Conversation): Promise<void> {
    saveTimers.delete(conversation.id);
    if (!conversation.items.some(i => i.kind === 'user')) return;
    const meta: HistoryMeta = {
        id: conversation.id,
        title: conversation.title,
        createdAt: conversation.createdAt,
        updatedAt: conversation.updatedAt,
    };
    try {
        await invoke('agent_history_write', { meta, data: snapshot(conversation) });
        agentChat.history = [meta, ...agentChat.history.filter(h => h.id !== meta.id)];
    } catch {
        // History is best-effort.
    }
}

function touch(conversation: Conversation): void {
    conversation.updatedAt = Date.now();
    clearTimeout(saveTimers.get(conversation.id));
    saveTimers.set(conversation.id, setTimeout(() => void saveNow(conversation), SAVE_DELAY_MS));
}

function flush(conversation: Conversation): Promise<void> {
    if (!saveTimers.has(conversation.id)) return Promise.resolve();
    clearTimeout(saveTimers.get(conversation.id));
    return saveNow(conversation);
}

function revive(raw: Record<string, unknown>): Conversation | null {
    if (typeof raw.id !== 'string' || !Array.isArray(raw.items)) return null;
    const items = (raw.items as ChatItem[]).map(item => {
        if (item.kind === 'assistant') return { ...item, streaming: false };
        if (item.kind === 'tool') return { ...item, done: true };
        if ((item.kind === 'approval' || item.kind === 'edit') && item.state === 'pending') return { ...item, state: 'expired' as const };
        return item;
    });
    return {
        id: raw.id,
        title: String(raw.title ?? ''),
        createdAt: Number(raw.createdAt ?? Date.now()),
        updatedAt: Number(raw.updatedAt ?? Date.now()),
        sessionId: typeof raw.sessionId === 'string' ? raw.sessionId : null,
        model: String(raw.model ?? ''),
        resolvedModel: typeof raw.resolvedModel === 'string' ? raw.resolvedModel : null,
        version: null,
        status: 'idle',
        items,
    };
}

async function load(id: string): Promise<Conversation | null> {
    try {
        const text = await invoke<string | null>('agent_history_read', { id });
        return text ? revive(JSON.parse(text)) : null;
    } catch {
        return null;
    }
}

export async function refreshHistory(): Promise<void> {
    try {
        agentChat.history = await invoke<HistoryMeta[]>('agent_history_list');
    } catch {
        agentChat.history = [];
    }
}

async function restoreTabs(): Promise<void> {
    let saved: { ids?: string[]; active?: string } = {};
    try {
        saved = JSON.parse(localStorage.getItem(TABS_KEY) ?? '{}');
    } catch {
        // Start fresh.
    }
    const loaded = (await Promise.all((saved.ids ?? []).map(load))).filter((c): c is Conversation => !!c);
    if (loaded.length) {
        const blank = agentChat.conversations.filter(c => c.items.length);
        agentChat.conversations = [...loaded, ...blank];
        agentChat.activeId = loaded.some(c => c.id === saved.active) ? saved.active! : loaded[0].id;
    }
    await refreshHistory();
}

// ---- Events from the agent ----

function notice(conversation: Conversation, text: string, error = false): void {
    conversation.items.push({ kind: 'notice', text, error });
}

function lastStreaming(conversation: Conversation): Extract<ChatItem, { kind: 'assistant' }> | null {
    const last = conversation.items[conversation.items.length - 1];
    return last?.kind === 'assistant' && last.streaming ? last : null;
}

function finishStreaming(conversation: Conversation): void {
    const last = lastStreaming(conversation);
    if (last) last.streaming = false;
}

function onEvent(conversation: Conversation, event: AgentEvent): void {
    switch (event.kind) {
        case 'sessionStarted':
            conversation.sessionId = event.sessionId;
            conversation.resolvedModel = event.model;
            conversation.version = event.version;
            break;
        case 'textDelta': {
            const last = lastStreaming(conversation);
            if (last) last.text += event.text;
            else conversation.items.push({ kind: 'assistant', text: event.text, streaming: true });
            return;
        }
        case 'assistantText': {
            const last = lastStreaming(conversation);
            if (last) {
                last.text = event.text;
                last.streaming = false;
            } else {
                conversation.items.push({ kind: 'assistant', text: event.text, streaming: false });
            }
            break;
        }
        case 'toolUse':
            finishStreaming(conversation);
            conversation.items.push({ kind: 'tool', id: event.id, name: event.name, input: event.input, done: false });
            break;
        case 'toolResult': {
            const item = conversation.items.find(i => i.kind === 'tool' && i.id === event.toolUseId);
            if (item?.kind === 'tool') {
                item.result = event.content;
                item.isError = event.isError;
                item.done = true;
            }
            break;
        }
        case 'turnComplete':
            finishStreaming(conversation);
            conversation.status = 'ready';
            if (event.subtype === 'error_during_execution' && !event.error) notice(conversation, 'Stopped.');
            else if (event.isError) notice(conversation, event.error || `The turn ended early (${event.subtype}).`, true);
            break;
        case 'status':
            notice(conversation, event.text);
            break;
        case 'exited':
            finishStreaming(conversation);
            conversation.status = 'idle';
            dropQueuedEdits(conversation.id);
            if (event.code !== 0) {
                const detail = event.output.trim().split('\n').slice(-6).join('\n');
                notice(conversation, `Claude Code exited${event.code === null ? '' : ` with code ${event.code}`}.${detail ? '\n' + detail : ''}`, true);
            }
            break;
    }
    touch(conversation);
}

async function respond(id: number, answer: { result?: unknown; error?: string }): Promise<void> {
    try {
        await invoke('agent_tool_response', { id, answer });
    } catch {
        // The session is gone; nothing is waiting.
    }
}

function showNextEdit(): void {
    agentChat.pendingEdit = editQueue[0]?.view ?? null;
}

function dropQueuedEdits(conversationId: string): void {
    for (let i = editQueue.length - 1; i >= 0; i--) {
        if (editQueue[i].view.conversationId === conversationId) editQueue.splice(i, 1);
    }
    showNextEdit();
}

async function onToolRequest(request: ToolRequest): Promise<void> {
    const { id, tool, args } = request;
    const conversation = find(request.conversation);
    if (!conversation) {
        await respond(id, { error: 'This conversation was closed.' });
        return;
    }
    if (tool === 'approve_tool') {
        const toolName = String(args.tool_name ?? '');
        if (sessionAllowed.get(conversation.id)?.has(toolName)) {
            await respond(id, { result: { behavior: 'allow', updatedInput: args.input ?? {} } });
            return;
        }
        approvalInputs.set(id, args.input ?? {});
        conversation.items.push({ kind: 'approval', requestId: id, toolName, input: args.input, state: 'pending' });
        touch(conversation);
        openPanel();
        return;
    }
    if (!editorContext) {
        await respond(id, { error: 'The Calcpad editor is not ready.' });
        return;
    }
    if (tool === 'propose_edit') {
        try {
            const edit = prepareEdit(editorContext, args);
            const title = edit.doc.tab.filePath ?? edit.doc.tab.title;
            conversation.items.push({ kind: 'edit', requestId: id, title, summary: edit.summary, state: 'pending' });
            touch(conversation);
            editQueue.push({
                edit,
                view: { conversationId: conversation.id, requestId: id, title, summary: edit.summary, original: edit.original, modified: edit.modified },
            });
            showNextEdit();
        } catch (err) {
            await respond(id, { error: errorText(err) });
        }
        return;
    }
    try {
        await respond(id, { result: await runEditorTool(editorContext, tool, args) });
    } catch (err) {
        await respond(id, { error: errorText(err) });
    }
}

function setItemState(conversationId: string, requestId: number, state: ApprovalState): void {
    const conversation = find(conversationId);
    const item = conversation?.items.find(i => (i.kind === 'approval' || i.kind === 'edit') && i.requestId === requestId);
    if (conversation && item && (item.kind === 'approval' || item.kind === 'edit')) {
        item.state = state;
        touch(conversation);
    }
}

export async function answerApproval(conversationId: string, requestId: number, allow: boolean, forSession = false): Promise<void> {
    const item = find(conversationId)?.items.find(i => i.kind === 'approval' && i.requestId === requestId);
    if (!item || item.kind !== 'approval' || item.state !== 'pending') return;
    if (allow && forSession) {
        if (!sessionAllowed.has(conversationId)) sessionAllowed.set(conversationId, new Set());
        sessionAllowed.get(conversationId)!.add(item.toolName);
    }
    setItemState(conversationId, requestId, allow ? 'allowed' : 'denied');
    const input = approvalInputs.get(requestId) ?? {};
    approvalInputs.delete(requestId);
    await respond(requestId, {
        result: allow
            ? { behavior: 'allow', updatedInput: input }
            : { behavior: 'deny', message: 'The user denied this action.' },
    });
}

export async function resolveEdit(accept: boolean): Promise<void> {
    const next = editQueue.shift();
    showNextEdit();
    if (!next) return;
    const { conversationId, requestId } = next.view;
    if (!accept) {
        setItemState(conversationId, requestId, 'denied');
        await respond(requestId, { result: 'The user rejected this edit. Ask what they would like instead.' });
        return;
    }
    try {
        applyEdit(next.edit);
        setItemState(conversationId, requestId, 'allowed');
        await respond(requestId, { result: 'The user accepted the edit. It is applied in the editor and not yet saved.' });
    } catch (err) {
        setItemState(conversationId, requestId, 'denied');
        await respond(requestId, { error: errorText(err) });
    }
}

function ensureListening(): Promise<void> {
    listening ??= (async () => {
        const [{ listen }, { getCurrentWindow }] = await Promise.all([
            import('@tauri-apps/api/event'),
            import('@tauri-apps/api/window'),
        ]);
        const target = getCurrentWindow().label;
        await listen<{ conversation: string; event: AgentEvent }>('agent-event', e => {
            const conversation = find(e.payload.conversation);
            if (conversation) onEvent(conversation, e.payload.event);
        }, { target });
        await listen<ToolRequest>('agent-tool-request', e => void onToolRequest(e.payload), { target });
    })();
    return listening;
}

export function registerAgentEditor(context: AgentEditorContext): void {
    editorContext = context;
    void ensureListening();
    restored ??= restoreTabs();
}

// ---- Commands from the panel ----

function cwdForActiveDocument(): string | null {
    const path = editorContext?.activeGroup().tabs.activeTab?.filePath;
    if (!path) return null;
    const cut = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'));
    return cut > 0 ? path.slice(0, cut) : null;
}

export async function sendMessage(text: string): Promise<void> {
    const conversation = activeConversation();
    const trimmed = text.trim();
    if (!trimmed || conversation.status === 'starting' || conversation.status === 'busy') return;
    conversation.items.push({ kind: 'user', text: trimmed });
    if (!conversation.title) conversation.title = trimmed.replace(/\s+/g, ' ').slice(0, TITLE_LENGTH);
    touch(conversation);
    persistTabs();
    try {
        await ensureListening();
        if (conversation.status === 'idle') {
            conversation.status = 'starting';
            await invoke('agent_start', {
                conversation: conversation.id,
                options: {
                    executable: agentChat.settings.executable || null,
                    model: conversation.model || null,
                    cwd: cwdForActiveDocument(),
                    resume: conversation.sessionId,
                },
            });
        }
        conversation.status = 'busy';
        await invoke('agent_send', { conversation: conversation.id, text: trimmed });
    } catch (err) {
        conversation.status = 'idle';
        notice(conversation, errorText(err), true);
    }
}

export async function interrupt(): Promise<void> {
    const conversation = activeConversation();
    try {
        await invoke('agent_interrupt', { conversation: conversation.id });
    } catch (err) {
        notice(conversation, errorText(err), true);
    }
}

async function stopProcess(conversation: Conversation): Promise<void> {
    if (conversation.status === 'idle') return;
    try {
        await invoke('agent_stop', { conversation: conversation.id });
    } catch {
        // Already stopped.
    }
    conversation.status = 'idle';
    finishStreaming(conversation);
    dropQueuedEdits(conversation.id);
    for (const item of conversation.items) {
        if ((item.kind === 'approval' || item.kind === 'edit') && item.state === 'pending') item.state = 'expired';
    }
}

export function activateConversation(id: string): void {
    if (!find(id)) return;
    agentChat.activeId = id;
    agentChat.view = 'chat';
    persistTabs();
}

export function newConversation(): void {
    const blank = agentChat.conversations.find(c => !c.items.length);
    const conversation = blank ?? createConversation(agentChat.settings.model);
    if (!blank) agentChat.conversations.push(conversation);
    activateConversation(conversation.id);
}

export async function closeConversation(id: string): Promise<void> {
    const index = agentChat.conversations.findIndex(c => c.id === id);
    if (index < 0) return;
    const conversation = agentChat.conversations[index];
    await stopProcess(conversation);
    await flush(conversation);
    sessionAllowed.delete(id);
    agentChat.conversations.splice(index, 1);
    if (!agentChat.conversations.length) agentChat.conversations.push(createConversation(agentChat.settings.model));
    if (agentChat.activeId === id) {
        agentChat.activeId = agentChat.conversations[Math.min(index, agentChat.conversations.length - 1)].id;
    }
    persistTabs();
}

export async function openFromHistory(id: string): Promise<void> {
    if (find(id)) {
        activateConversation(id);
        return;
    }
    const conversation = await load(id);
    if (!conversation) {
        agentChat.history = agentChat.history.filter(h => h.id !== id);
        return;
    }
    const blankActive = activeConversation();
    if (!blankActive.items.length) {
        agentChat.conversations.splice(agentChat.conversations.indexOf(blankActive), 1, conversation);
    } else {
        agentChat.conversations.push(conversation);
    }
    activateConversation(conversation.id);
}

export async function deleteFromHistory(id: string): Promise<void> {
    if (find(id)) await closeConversation(id);
    try {
        await invoke('agent_history_delete', { id });
    } catch {
        // Already gone.
    }
    agentChat.history = agentChat.history.filter(h => h.id !== id);
}

export function showHistory(show: boolean): void {
    agentChat.view = show ? 'history' : 'chat';
    if (show) void (restored ?? Promise.resolve()).then(refreshHistory);
}

/** Switches the active conversation's model in place and makes it the default for new ones. */
export async function setModel(model: string): Promise<void> {
    const next = model.trim();
    const conversation = activeConversation();
    agentChat.settings = { ...agentChat.settings, model: next };
    localStorage.setItem(SETTINGS_KEY, JSON.stringify(agentChat.settings));
    if (next === conversation.model) return;
    conversation.model = next;
    touch(conversation);
    if (conversation.status === 'idle') return;
    try {
        await invoke('agent_set_model', { conversation: conversation.id, model: next });
    } catch (err) {
        notice(conversation, errorText(err), true);
    }
}

/** A new executable needs new processes; each conversation resumes on its next message. */
export async function saveSettings(settings: AgentSettings): Promise<void> {
    const executable = settings.executable.trim();
    const executableChanged = executable !== agentChat.settings.executable;
    agentChat.settings = { ...agentChat.settings, executable };
    localStorage.setItem(SETTINGS_KEY, JSON.stringify(agentChat.settings));
    await setModel(settings.model);
    if (executableChanged) await Promise.all(agentChat.conversations.map(stopProcess));
}

export function setPanelOpen(open: boolean): void {
    agentChat.panelOpen = open;
    localStorage.setItem(PANEL_KEY, String(open));
}

export function openPanel(): void {
    if (!agentChat.panelOpen) setPanelOpen(true);
}
