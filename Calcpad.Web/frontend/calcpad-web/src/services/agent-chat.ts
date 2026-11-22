import { reactive } from 'vue';
import { applyEdit, findDoc, materialize, prepareEdit, runEditorTool, type AgentEditorContext, type PreparedEdit, type ProposedEdit } from './agent-tools';

export type ApprovalState = 'pending' | 'allowed' | 'denied' | 'expired';

export type ChatItem =
    | { kind: 'user'; text: string }
    | { kind: 'assistant'; text: string; streaming: boolean }
    | { kind: 'tool'; id: string; name: string; input: unknown; result?: string; isError?: boolean; done: boolean }
    | { kind: 'approval'; requestId: number; toolName: string; input: unknown; state: ApprovalState }
    | { kind: 'edit'; requestId: number; title: string; summary: string; state: ApprovalState; auto?: boolean }
    | { kind: 'question'; requestId: number; questions: AgentQuestion[]; state: ApprovalState; answers?: Record<string, string>; via: 'approval' | 'ask' }
    | { kind: 'notice'; text: string; error: boolean };

export type AgentStatus = 'idle' | 'starting' | 'ready' | 'busy';

/** A question an agent asks the user, from Claude's AskUserQuestion or a harness dialog. */
export interface AgentQuestion {
    question: string;
    header?: string;
    options: { label: string; description?: string }[];
    multiSelect?: boolean;
    /** Free-text answers; defaults to true. */
    allowOther?: boolean;
    placeholder?: string;
}

function toQuestions(value: unknown): AgentQuestion[] {
    if (!Array.isArray(value)) return [];
    return value
        .filter(q => q && typeof q.question === 'string')
        .map(q => ({
            question: q.question,
            header: typeof q.header === 'string' ? q.header : undefined,
            options: Array.isArray(q.options)
                ? q.options
                    .map((o: unknown) => typeof o === 'string' ? { label: o } : o as { label: string; description?: string })
                    .filter((o: { label?: unknown }) => typeof o?.label === 'string')
                : [],
            multiSelect: q.multiSelect === true,
            allowOther: q.allowOther !== false,
            placeholder: typeof q.placeholder === 'string' ? q.placeholder : undefined,
        }));
}

export type PermissionMode = string;

export interface ModelOption {
    value: string;
    label: string;
}

/** How a mode treats propose_edit: diff review, applied with a backup, or refused. */
export type EditPolicy = 'review' | 'auto' | 'blocked';

export interface PermissionModeInfo {
    value: PermissionMode;
    label: string;
    hint: string;
    edits: EditPolicy;
    /** Switching between modes with different profiles restarts the agent (the session resumes). */
    profile?: string;
}

/** A supported agent CLI, as returned by agent_harnesses. */
export interface HarnessInfo {
    id: string;
    name: string;
    assistantName: string;
    installed: boolean;
    /** Detected or configured executable; null when not found. */
    path: string | null;
    installHint: string;
    /** Fixed models; empty when `dynamicModels`. */
    models: ModelOption[];
    /** Models come from the running agent and are remembered between sessions. */
    dynamicModels: boolean;
    /** Shift+Tab cycling order; the first is the default. */
    permissionModes: PermissionModeInfo[];
}

const FALLBACK_MODE: PermissionModeInfo = { value: 'default', label: 'Default', hint: '', edits: 'review' };
const DEFAULT_MODEL: ModelOption = { value: '', label: 'Default' };

export function harnessInfo(id: string | undefined): HarnessInfo | null {
    return agentChat.harnesses.find(h => h.id === id) ?? null;
}

export function activeHarness(): HarnessInfo | null {
    return harnessInfo(activeConversation().provider);
}

export function permissionModes(harness = activeHarness()): PermissionModeInfo[] {
    return harness?.permissionModes.length ? harness.permissionModes : [FALLBACK_MODE];
}

export function modeInfo(mode: PermissionMode, harness = activeHarness()): PermissionModeInfo {
    const modes = permissionModes(harness);
    return modes.find(m => m.value === mode) ?? modes[0];
}

export function modelOptions(harness = activeHarness()): ModelOption[] {
    if (!harness) return [];
    return harness.dynamicModels ? [DEFAULT_MODEL, ...(agentChat.models[harness.id] ?? [])] : harness.models;
}

export function providerName(harness = activeHarness()): string {
    return harness?.name ?? 'Agent';
}

export function assistantName(harness = activeHarness()): string {
    return harness?.assistantName || providerName(harness);
}

function toMode(value: unknown, harness: HarnessInfo | null): PermissionMode {
    const modes = permissionModes(harness);
    return modes.some(m => m.value === value) ? value as PermissionMode : modes[0].value;
}

/** Backup of a document taken before the agent first changed it in a conversation. */
export interface ChangeRecord {
    key: string;
    path: string | null;
    title: string;
    baseline: string;
    changedAt: number;
    /** The agent created this worksheet; reverting closes it. */
    created?: boolean;
}

export interface Conversation {
    id: string;
    provider: string;
    title: string;
    createdAt: number;
    updatedAt: number;
    /** Agent session to resume when the process is restarted. */
    sessionId: string | null;
    /** Model picked for this conversation; empty is the provider's default. */
    model: string;
    permissionMode: PermissionMode;
    /** Restart with the new mode's tool set once the current turn ends. */
    restartPending: boolean;
    resolvedModel: string | null;
    version: string | null;
    status: AgentStatus;
    items: ChatItem[];
    changes: ChangeRecord[];
}

export interface HistoryMeta {
    id: string;
    title: string;
    createdAt: number;
    updatedAt: number;
}

/** Per-harness settings. */
export interface AgentSettings {
    /** Executable path that overrides auto-detection; empty to detect. */
    executable: string;
    /** Model and permission mode for new conversations. */
    model: string;
    permissionMode: PermissionMode;
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
    | { kind: 'sessionStarted'; sessionId: string; model: string | null; version: string | null; permissionMode: string | null }
    | { kind: 'textDelta'; text: string }
    | { kind: 'assistantText'; text: string }
    | { kind: 'toolUse'; id: string; name: string; input: unknown }
    | { kind: 'toolResult'; toolUseId: string; isError: boolean; content: string }
    | { kind: 'turnComplete'; isError: boolean; subtype: string; costUsd: number | null; error: string | null }
    | { kind: 'status'; text: string }
    | { kind: 'exited'; code: number | null; output: string }
    | { kind: 'error'; message: string }
    | { kind: 'models'; models: ModelOption[] };

interface ToolRequest {
    id: number;
    conversation: string;
    tool: string;
    args: Record<string, unknown>;
}

const SETTINGS_KEY = 'calcpad.agent.settings';
const HARNESS_KEY = 'calcpad.agent.harness';
const MODELS_KEY = 'calcpad.agent.models';
const PANEL_KEY = 'calcpad.agent.panelOpen';
const TABS_KEY = 'calcpad.agent.tabs';
const SAVE_DELAY_MS = 800;
const TITLE_LENGTH = 60;

function readJson(key: string): Record<string, unknown> {
    try {
        const value = JSON.parse(localStorage.getItem(key) ?? '{}');
        return value && typeof value === 'object' ? value : {};
    } catch {
        return {};
    }
}

function loadSettings(harness: HarnessInfo): AgentSettings {
    const raw = readJson(`${SETTINGS_KEY}.${harness.id}`);
    return {
        executable: String(raw.executable ?? ''),
        model: String(raw.model ?? ''),
        permissionMode: toMode(raw.permissionMode, harness),
    };
}

export function settingsFor(id: string): AgentSettings {
    return agentChat.settings[id] ?? { executable: '', model: '', permissionMode: toMode(null, harnessInfo(id)) };
}

function storeSettings(id: string): void {
    localStorage.setItem(`${SETTINGS_KEY}.${id}`, JSON.stringify(settingsFor(id)));
}

function loadModels(id: string): ModelOption[] {
    const raw = readJson(`${MODELS_KEY}.${id}`);
    return Array.isArray(raw.models) ? raw.models.filter(m => m && typeof m.value === 'string' && m.value) : [];
}

function createConversation(harnessId: string): Conversation {
    const now = Date.now();
    const settings = settingsFor(harnessId);
    return {
        id: crypto.randomUUID(),
        provider: harnessId,
        title: '',
        createdAt: now,
        updatedAt: now,
        sessionId: null,
        model: settings.model,
        permissionMode: settings.permissionMode,
        restartPending: false,
        resolvedModel: null,
        version: null,
        status: 'idle',
        items: [],
        changes: [],
    };
}

export const agentChat = reactive({
    /** Every supported harness, empty until detection finishes. */
    harnesses: [] as HarnessInfo[],
    /** Harness for new conversations. */
    harnessId: localStorage.getItem(HARNESS_KEY) ?? '',
    panelOpen: localStorage.getItem(PANEL_KEY) === 'true',
    settings: {} as Record<string, AgentSettings>,
    /** Models reported by harnesses with `dynamicModels`. */
    models: {} as Record<string, ModelOption[]>,
    conversations: [] as Conversation[],
    activeId: '',
    view: 'chat' as 'chat' | 'history' | 'changes',
    history: [] as HistoryMeta[],
    pendingEdit: null as PendingEdit | null,
    reviewChange: null as { conversationId: string; key: string; title: string; baseline: string; current: string } | null,
});

agentChat.conversations.push(createConversation(agentChat.harnessId));
agentChat.activeId = agentChat.conversations[0].id;

let editorContext: AgentEditorContext | null = null;
let listening: Promise<void> | null = null;
let restored: Promise<void> | null = null;
// Kept out of the reactive store: prepared edits hold Monaco objects.
const editQueue: { view: PendingEdit; edit: ProposedEdit }[] = [];
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
    return conversation.items.some(i => (i.kind === 'approval' || i.kind === 'edit' || i.kind === 'question') && i.state === 'pending');
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
        provider: conversation.provider,
        title: conversation.title,
        createdAt: conversation.createdAt,
        updatedAt: conversation.updatedAt,
        sessionId: conversation.sessionId,
        model: conversation.model,
        permissionMode: conversation.permissionMode,
        resolvedModel: conversation.resolvedModel,
        items: conversation.items,
        changes: conversation.changes,
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
        if ((item.kind === 'approval' || item.kind === 'edit' || item.kind === 'question') && item.state === 'pending') return { ...item, state: 'expired' as const };
        return item;
    });
    return {
        id: raw.id,
        provider: typeof raw.provider === 'string' ? raw.provider : 'claude-code',
        title: String(raw.title ?? ''),
        createdAt: Number(raw.createdAt ?? Date.now()),
        updatedAt: Number(raw.updatedAt ?? Date.now()),
        sessionId: typeof raw.sessionId === 'string' ? raw.sessionId : null,
        model: String(raw.model ?? ''),
        permissionMode: toMode(raw.permissionMode, harnessInfo(typeof raw.provider === 'string' ? raw.provider : 'claude-code')),
        restartPending: false,
        resolvedModel: typeof raw.resolvedModel === 'string' ? raw.resolvedModel : null,
        version: null,
        status: 'idle',
        items,
        changes: Array.isArray(raw.changes) ? raw.changes as ChangeRecord[] : [],
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
            if (permissionModes(harnessInfo(conversation.provider)).some(m => m.value === event.permissionMode)) conversation.permissionMode = event.permissionMode!;
            break;
        case 'models':
            agentChat.models[conversation.provider] = event.models;
            localStorage.setItem(`${MODELS_KEY}.${conversation.provider}`, JSON.stringify({ models: event.models }));
            return;
        case 'error':
            notice(conversation, event.message, true);
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
        case 'toolUse': {
            const existing = conversation.items.find(i => i.kind === 'tool' && i.id === event.id);
            if (existing?.kind === 'tool') {
                existing.name = event.name;
                existing.input = event.input;
                break;
            }
            finishStreaming(conversation);
            conversation.items.push({ kind: 'tool', id: event.id, name: event.name, input: event.input, done: false });
            break;
        }
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
            if (conversation.restartPending) void restartForMode(conversation);
            if (event.subtype === 'interrupted' && !event.error) notice(conversation, 'Stopped.');
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
                notice(conversation, `${providerName(harnessInfo(conversation.provider))} exited${event.code === null ? '' : ` with code ${event.code}`}.${detail ? '\n' + detail : ''}`, true);
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
    if (tool === 'ask_user') {
        const questions = toQuestions(args.questions);
        if (!questions.length) {
            await respond(id, { result: { cancelled: true } });
            return;
        }
        conversation.items.push({ kind: 'question', requestId: id, questions, state: 'pending', via: 'ask' });
        touch(conversation);
        openPanel();
        return;
    }
    if (tool === 'approve_tool') {
        const toolName = String(args.tool_name ?? '');
        if (toolName === 'AskUserQuestion') {
            const input = (args.input ?? {}) as Record<string, unknown>;
            approvalInputs.set(id, input);
            conversation.items.push({ kind: 'question', requestId: id, questions: toQuestions(input.questions), state: 'pending', via: 'approval' });
            touch(conversation);
            openPanel();
            return;
        }
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
        const mode = modeInfo(conversation.permissionMode, harnessInfo(conversation.provider));
        if (mode.edits === 'blocked') {
            await respond(id, { error: `${mode.label} is on, so worksheets cannot be edited. Present your plan instead.` });
            return;
        }
        try {
            const edit = await prepareEdit(editorContext, args);
            const title = 'create' in edit ? edit.create : edit.doc.tab.filePath ?? edit.doc.tab.title;
            if (mode.edits === 'auto') {
                applyProposed(conversation, edit);
                conversation.items.push({ kind: 'edit', requestId: id, title, summary: edit.summary, state: 'allowed', auto: true });
                touch(conversation);
                await respond(id, { result: 'create' in edit
                    ? 'The new worksheet was opened in the editor and not yet saved.'
                    : 'The edit was applied in the editor and not yet saved.' });
                return;
            }
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
    const item = conversation?.items.find(i => (i.kind === 'approval' || i.kind === 'edit' || i.kind === 'question') && i.requestId === requestId);
    if (conversation && item && (item.kind === 'approval' || item.kind === 'edit' || item.kind === 'question')) {
        item.state = state;
        touch(conversation);
    }
}

export async function answerApproval(conversationId: string, requestId: number, allow: boolean, forSession = false, denial = 'The user denied this action.'): Promise<void> {
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
            : { behavior: 'deny', message: denial },
    });
    const conversation = find(conversationId);
    if (allow && conversation && item.toolName === 'EnterPlanMode') {
        const plan = permissionModes(harnessInfo(conversation.provider)).find(m => m.edits === 'blocked');
        if (plan) await setPermissionMode(plan.value, conversation, false);
    }
}

/** Answers a question card; null skips it. Answers are keyed by question text. */
export async function answerQuestions(conversationId: string, requestId: number, answers: Record<string, string> | null): Promise<void> {
    const conversation = find(conversationId);
    const item = conversation?.items.find(i => i.kind === 'question' && i.requestId === requestId);
    if (!conversation || !item || item.kind !== 'question' || item.state !== 'pending') return;
    item.state = answers ? 'allowed' : 'denied';
    if (answers) item.answers = answers;
    touch(conversation);
    if (item.via === 'approval') {
        const input = approvalInputs.get(requestId) ?? {};
        approvalInputs.delete(requestId);
        await respond(requestId, {
            result: answers
                ? { behavior: 'allow', updatedInput: { ...input, answers } }
                : { behavior: 'deny', message: 'The user declined to answer.' },
        });
    } else {
        await respond(requestId, { result: answers ? { answers } : { cancelled: true } });
    }
}

/** Tool names agents use to leave their plan mode with a plan for the user to approve. */
export function isPlanTool(toolName: string): boolean {
    return toolName === 'ExitPlanMode';
}

/** Approves a plan and leaves plan mode for this conversation only. */
export async function approvePlan(conversationId: string, requestId: number): Promise<void> {
    await answerApproval(conversationId, requestId, true);
    const conversation = find(conversationId);
    if (conversation && modeInfo(conversation.permissionMode, harnessInfo(conversation.provider)).edits === 'blocked') {
        await setPermissionMode(permissionModes(harnessInfo(conversation.provider))[0].value, conversation, false);
    }
}

export function keepPlanning(conversationId: string, requestId: number): Promise<void> {
    return answerApproval(conversationId, requestId, false, false, 'The user wants to keep planning. Ask what they would like to change.');
}

/** Leaves plan mode for the active conversation. */
export function exitPlanMode(): Promise<void> {
    const conversation = activeConversation();
    return setPermissionMode(permissionModes(harnessInfo(conversation.provider))[0].value, conversation, false);
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
        const conversation = find(conversationId);
        if (conversation) applyProposed(conversation, next.edit);
        else applyEdit(materialize(editorContext!, next.edit));
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

function executablePaths(): Record<string, string> {
    const paths: Record<string, string> = {};
    for (const [id, settings] of Object.entries(agentChat.settings)) if (settings.executable) paths[id] = settings.executable;
    return paths;
}

/** Re-runs detection, e.g. after a path setting changes. */
export async function refreshHarnesses(): Promise<void> {
    try {
        const found = await invoke<HarnessInfo[]>('agent_harnesses', { paths: executablePaths() });
        if (found.length) agentChat.harnesses = found;
    } catch {
        // No agent support in this build.
    }
}

async function loadHarnesses(): Promise<void> {
    try {
        agentChat.harnesses = await invoke<HarnessInfo[]>('agent_harnesses', { paths: {} });
    } catch {
        return;
    }
    for (const harness of agentChat.harnesses) {
        agentChat.settings[harness.id] = loadSettings(harness);
        agentChat.models[harness.id] = loadModels(harness.id);
    }
    if (Object.keys(executablePaths()).length) await refreshHarnesses();
    if (!harnessInfo(agentChat.harnessId)) {
        agentChat.harnessId = (agentChat.harnesses.find(h => h.installed) ?? agentChat.harnesses[0])?.id ?? '';
    }
    for (const conversation of agentChat.conversations.filter(c => !c.items.length)) {
        Object.assign(conversation, { ...createConversation(agentChat.harnessId), id: conversation.id });
    }
}

export function registerAgentEditor(context: AgentEditorContext): void {
    editorContext = context;
    restored ??= loadHarnesses().then(() => {
        if (!agentChat.harnesses.length) return;
        void ensureListening();
        return restoreTabs();
    });
}

/**
 * Makes `id` the harness for new conversations. A conversation that has started keeps
 * its harness, so picking another opens a new tab.
 */
export function selectHarness(id: string): void {
    if (!harnessInfo(id)) return;
    agentChat.harnessId = id;
    localStorage.setItem(HARNESS_KEY, id);
    const active = activeConversation();
    if (active.provider === id) return;
    if (!active.items.length) {
        Object.assign(active, { ...createConversation(id), id: active.id });
        return;
    }
    const conversation = createConversation(id);
    agentChat.conversations.push(conversation);
    activateConversation(conversation.id);
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
    if (!trimmed || !conversation.provider || conversation.status === 'starting' || conversation.status === 'busy') return;
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
                    harness: conversation.provider,
                    executable: settingsFor(conversation.provider).executable || null,
                    model: conversation.model || null,
                    permissionMode: conversation.permissionMode,
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
        if ((item.kind === 'approval' || item.kind === 'edit' || item.kind === 'question') && item.state === 'pending') item.state = 'expired';
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
    const conversation = blank ?? createConversation(agentChat.harnessId);
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
    if (!agentChat.conversations.length) agentChat.conversations.push(createConversation(agentChat.harnessId));
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
    agentChat.settings[conversation.provider] = { ...settingsFor(conversation.provider), model: next };
    storeSettings(conversation.provider);
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

async function restartForMode(conversation: Conversation): Promise<void> {
    conversation.restartPending = false;
    await stopProcess(conversation);
    notice(conversation, `${modeInfo(conversation.permissionMode, harnessInfo(conversation.provider)).label} applies from your next message.`);
}

/** Switches the active conversation's mode and makes it the default for new ones. */
export async function setPermissionMode(mode: PermissionMode, conversation = activeConversation(), remember = true): Promise<void> {
    const harness = harnessInfo(conversation.provider);
    if (remember) {
        agentChat.settings[conversation.provider] = { ...settingsFor(conversation.provider), permissionMode: mode };
        storeSettings(conversation.provider);
    }
    const previous = conversation.permissionMode;
    if (mode === previous) return;
    conversation.permissionMode = mode;
    touch(conversation);
    if (conversation.status === 'idle') return;
    if ((modeInfo(mode, harness).profile ?? '') !== (modeInfo(previous, harness).profile ?? '')) {
        if (conversation.status === 'busy' || conversation.status === 'starting') conversation.restartPending = true;
        else await restartForMode(conversation);
        return;
    }
    try {
        await invoke('agent_set_permission_mode', { conversation: conversation.id, mode });
    } catch (err) {
        notice(conversation, errorText(err), true);
    }
}

export function cyclePermissionMode(): void {
    const modes = permissionModes().map(m => m.value);
    const current = modes.indexOf(activeConversation().permissionMode);
    void setPermissionMode(modes[(current + 1) % modes.length]);
}

// ---- Changes made by the agent (backups, diff, revert) ----

function docKey(edit: PreparedEdit): string {
    return edit.doc.tab.filePath ?? `untitled:${edit.doc.tab.id}`;
}

/** Opens a new worksheet if needed, records the backup, and applies the edit. */
function applyProposed(conversation: Conversation, proposed: ProposedEdit): void {
    const edit = materialize(editorContext!, proposed);
    recordChange(conversation, edit, 'create' in proposed);
    applyEdit(edit);
}

function recordChange(conversation: Conversation, edit: PreparedEdit, created = false): void {
    const key = docKey(edit);
    const existing = conversation.changes.find(c => c.key === key);
    if (existing) {
        existing.changedAt = Date.now();
        return;
    }
    conversation.changes.push({
        key,
        path: edit.doc.tab.filePath,
        title: edit.doc.tab.title,
        baseline: edit.original,
        changedAt: Date.now(),
        ...(created ? { created: true } : {}),
    });
}

/** Live text of a changed document, or null when it is no longer open. */
export function currentText(change: ChangeRecord): string | null {
    if (!editorContext) return null;
    try {
        return findDoc(editorContext, change.path ?? change.key.slice('untitled:'.length)).model.getValue();
    } catch {
        return null;
    }
}

export function lineStats(before: string, after: string): { added: number; removed: number } {
    const counts = new Map<string, number>();
    for (const line of before.split('\n')) counts.set(line, (counts.get(line) ?? 0) + 1);
    let added = 0;
    for (const line of after.split('\n')) {
        const n = counts.get(line) ?? 0;
        if (n > 0) counts.set(line, n - 1);
        else added++;
    }
    let removed = 0;
    for (const n of counts.values()) removed += n;
    return { added, removed };
}

export function openChangeDiff(conversationId: string, key: string): void {
    const change = find(conversationId)?.changes.find(c => c.key === key);
    const current = change ? currentText(change) : null;
    if (!change || current === null) return;
    agentChat.reviewChange = { conversationId, key, title: change.path ?? change.title, baseline: change.baseline, current };
}

export function closeChangeDiff(): void {
    agentChat.reviewChange = null;
}

export function keepChange(conversationId: string, key: string): void {
    const conversation = find(conversationId);
    if (!conversation) return;
    conversation.changes = conversation.changes.filter(c => c.key !== key);
    if (agentChat.reviewChange?.key === key) agentChat.reviewChange = null;
    touch(conversation);
}

/** Restores the backup into the editor buffer as one undoable edit; reopens the file if needed. */
export async function revertChange(conversationId: string, key: string): Promise<void> {
    const conversation = find(conversationId);
    const change = conversation?.changes.find(c => c.key === key);
    if (!conversation || !change || !editorContext) return;
    try {
        if (change.created && change.path) {
            editorContext.closeDocument(change.path);
            keepChange(conversationId, key);
            const saved = await editorContext.pathExists(change.path);
            notice(conversation, saved
                ? `Closed ${change.path}. It had been saved, so the file is still on disk.`
                : `Removed the new worksheet ${change.path}.`);
            return;
        }
        if (currentText(change) === null) {
            if (!change.path) throw new Error('This untitled worksheet was closed, so it cannot be restored.');
            await editorContext.openFile(change.path);
        }
        const doc = findDoc(editorContext, change.path ?? change.key.slice('untitled:'.length));
        const now = doc.model.getValue();
        if (now !== change.baseline) applyEdit({ doc, original: now, modified: change.baseline, summary: '' });
        keepChange(conversationId, key);
        notice(conversation, `Reverted ${change.path ?? change.title} to its backup.`);
    } catch (err) {
        notice(conversation, errorText(err), true);
    }
}

export async function revertAllChanges(conversationId: string): Promise<void> {
    for (const change of [...(find(conversationId)?.changes ?? [])]) await revertChange(conversationId, change.key);
}

export function keepAllChanges(conversationId: string): void {
    const conversation = find(conversationId);
    if (!conversation) return;
    conversation.changes = [];
    agentChat.reviewChange = null;
    touch(conversation);
}

export function showChanges(show: boolean): void {
    agentChat.view = show ? 'changes' : 'chat';
}

/**
 * Saves a harness's settings and makes it the default. A changed executable needs new
 * processes; each of its conversations resumes on its next message.
 */
export async function saveSettings(id: string, settings: Pick<AgentSettings, 'executable' | 'model'>): Promise<void> {
    const executable = settings.executable.trim();
    const changed = executable !== settingsFor(id).executable;
    agentChat.settings[id] = { ...settingsFor(id), executable, model: settings.model.trim() };
    storeSettings(id);
    selectHarness(id);
    if (activeConversation().provider === id) await setModel(settings.model);
    if (changed) {
        await Promise.all(agentChat.conversations.filter(c => c.provider === id).map(stopProcess));
        await refreshHarnesses();
    }
}

export function setPanelOpen(open: boolean): void {
    agentChat.panelOpen = open;
    localStorage.setItem(PANEL_KEY, String(open));
}

export function openPanel(): void {
    if (!agentChat.panelOpen) setPanelOpen(true);
}
