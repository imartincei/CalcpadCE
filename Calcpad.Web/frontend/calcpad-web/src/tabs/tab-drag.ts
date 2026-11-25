import type { TabTransfer } from './tab-manager';

export type SplitSide = 'top' | 'bottom';

interface TabSource {
    windowId: string;
    groupId: string;
    tabId: string;
    token: string;
}

interface DropTarget {
    windowId: string;
    x: number;
    y: number;
}

type DragMessage =
    | { kind: 'hover'; source: TabSource; target: DropTarget | null }
    | { kind: 'end' }
    | { kind: 'drop'; source: TabSource; target: DropTarget | null }
    | { kind: 'request'; source: TabSource; windowId: string; groupId: string; beforeId: string | null; split?: SplitSide }
    | { kind: 'transfer'; source: TabSource; windowId: string; groupId: string | null; beforeId: string | null; split?: SplitSide; document: TabTransfer }
    | { kind: 'accepted'; source: TabSource }
    | { kind: 'detach'; source: TabSource }
    | { kind: 'ready'; windowId: string };

interface TabDragHooks {
    capture(groupId: string, tabId: string): Promise<TabTransfer | null>;
    move(sourceGroupId: string, tabId: string, groupId: string, beforeId: string | null, split?: SplitSide): Promise<void>;
    receive(document: TabTransfer, groupId: string, beforeId: string | null, split?: SplitSide): Promise<boolean>;
    remove(groupId: string, tabId: string, document: TabTransfer): void;
    openWindow(): Promise<string | null>;
}

const DRAG_EVENT = 'calcpad-tab-drag';
const TAB_MIME = 'application/x-calcpad-tab';

export async function setupTabDragging(desktop: boolean, hooks: TabDragHooks): Promise<void> {
    const windowId = desktop
        ? (await import('@tauri-apps/api/window')).getCurrentWindow().label
        : crypto.randomUUID();
    let send: (message: DragMessage) => Promise<void>;
    let locate: (event: PointerEvent) => Promise<DropTarget | null>;
    let source: TabSource | null = null;
    let pending: TabTransfer | null = null;
    let claimed: string | null = null;
    let dropped: string | null = null;
    let detaching: { source: TabSource; document: TabTransfer; label: string | null } | null = null;
    const readyWindows = new Set<string>();
    let useSystemDrag = !desktop;
    const marker = document.createElement('div');
    marker.className = 'tab-drop-marker';
    marker.hidden = true;
    const overlay = document.createElement('div');
    overlay.className = 'tab-drop-overlay';
    overlay.hidden = true;
    document.body.append(marker, overlay);

    function hideIndicators(): void {
        marker.hidden = true;
        overlay.hidden = true;
    }

    function clear(): void {
        hideIndicators();
        document.querySelectorAll('.tab-dragging').forEach(tab => tab.classList.remove('tab-dragging'));
        document.body.classList.remove('dragging-tab');
    }

    function showMarker(list: HTMLElement, before: HTMLElement | undefined): void {
        const tabs = list.querySelectorAll<HTMLElement>('[data-tab-id]');
        const last = tabs[tabs.length - 1];
        const rect = (before ?? last ?? list).getBoundingClientRect();
        marker.style.left = `${before || !last ? rect.left : rect.right}px`;
        marker.style.top = `${rect.top}px`;
        marker.style.height = `${rect.height}px`;
        marker.hidden = false;
    }

    // 'ignore' is the center of the dragged tab's own editor; null means outside every drop zone.
    function targetAt(x: number, y: number, dragged: TabSource | null):
        { groupId: string; beforeId: string | null; split?: SplitSide } | 'ignore' | null {
        const element = document.elementFromPoint(x, y);
        const editor = element?.closest<HTMLElement>('[data-editor-group]');
        if (editor) {
            const groupId = editor.dataset.editorGroup!;
            const rect = editor.getBoundingClientRect();
            const edge = rect.height / 3;
            const split: SplitSide | undefined = document.querySelectorAll('[data-editor-group]').length > 1 ? undefined
                : y < rect.top + edge ? 'top' : y > rect.bottom - edge ? 'bottom' : undefined;
            if (!split && dragged?.windowId === windowId && dragged.groupId === groupId) return 'ignore';
            const half = rect.height / 2;
            Object.assign(overlay.style, {
                left: `${rect.left}px`, width: `${rect.width}px`,
                top: `${split === 'bottom' ? rect.top + half : rect.top}px`, height: `${split ? half : rect.height}px`,
            });
            overlay.hidden = false;
            return { groupId, beforeId: null, split };
        }
        const strip = element?.closest<HTMLElement>('[data-tab-group]');
        if (!strip) return null;
        const list = strip.querySelector<HTMLElement>('.tab-list')!;
        const before = [...list.querySelectorAll<HTMLElement>('[data-tab-id]')].find(tab => {
            const rect = tab.getBoundingClientRect();
            return x < rect.left + rect.width / 2;
        });
        showMarker(list, before);
        if (x < list.getBoundingClientRect().left + 24) list.scrollLeft -= 20;
        if (x > list.getBoundingClientRect().right - 24) list.scrollLeft += 20;
        return { groupId: strip.dataset.tabGroup!, beforeId: before?.dataset.tabId ?? null };
    }

    async function sendDetached(): Promise<void> {
        const { source: dragged, document: transfer, label } = detaching!;
        await send({ kind: 'transfer', source: dragged, windowId: label!, groupId: null, beforeId: null, document: transfer });
    }

    async function detach(dragged: TabSource): Promise<void> {
        if (!desktop || dragged.token !== source?.token || claimed === dragged.token
            || document.querySelectorAll('[data-tab-id]').length < 2) return;
        const transfer = await hooks.capture(dragged.groupId, dragged.tabId);
        if (!transfer || dragged.token !== source?.token) return;
        source = null;
        const current = { source: dragged, document: transfer, label: null as string | null };
        detaching = current;
        current.label = await hooks.openWindow();
        if (!current.label) {
            if (detaching === current) detaching = null;
        } else if (detaching === current && readyWindows.has(current.label)) {
            await sendDetached();
        }
    }

    async function handle(message: DragMessage): Promise<void> {
        switch (message.kind) {
            case 'hover':
            case 'drop': {
                hideIndicators();
                if (message.kind === 'drop' && message.source.windowId === windowId) {
                    if (!message.target) {
                        await detach(message.source);
                        return;
                    }
                    dropped = message.source.token;
                }
                if (message.target?.windowId !== windowId) return;
                const target = targetAt(message.target.x, message.target.y, message.source);
                if (message.kind === 'drop') {
                    clear();
                    if (!target) await send({ kind: 'detach', source: message.source });
                    else if (target !== 'ignore') await send({ kind: 'request', source: message.source, windowId, ...target });
                }
                break;
            }
            case 'detach':
                if (message.source.windowId === windowId) await detach(message.source);
                break;
            case 'request': {
                if (message.source.windowId !== windowId || message.source.token !== source?.token) return;
                claimed = message.source.token;
                if (message.windowId === windowId) {
                    await hooks.move(source.groupId, source.tabId, message.groupId, message.beforeId, message.split);
                    source = null;
                } else {
                    const document = await hooks.capture(source.groupId, source.tabId);
                    if (document) {
                        pending = document;
                        await send({ ...message, kind: 'transfer', document });
                    }
                }
                break;
            }
            case 'transfer': {
                if (message.windowId !== windowId) return;
                const groupId = message.groupId
                    ?? document.querySelector<HTMLElement>('[data-tab-group]')?.dataset.tabGroup;
                if (groupId && await hooks.receive(message.document, groupId, message.beforeId, message.split)) {
                    await send({ kind: 'accepted', source: message.source });
                }
                break;
            }
            case 'ready':
                readyWindows.add(message.windowId);
                if (detaching?.label === message.windowId) await sendDetached();
                break;
            case 'accepted':
                if (message.source.windowId !== windowId) return;
                if (message.source.token === detaching?.source.token) {
                    hooks.remove(detaching.source.groupId, detaching.source.tabId, detaching.document);
                    detaching = null;
                } else if (message.source.token === source?.token && pending) {
                    hooks.remove(source.groupId, source.tabId, pending);
                    pending = null;
                    source = null;
                }
                break;
            case 'end':
                clear();
                break;
        }
    }

    if (desktop) {
        const { emit, listen } = await import('@tauri-apps/api/event');
        const { invoke } = await import('@tauri-apps/api/core');
        await listen<DragMessage>(DRAG_EVENT, event => { void handle(event.payload); });
        send = message => emit(DRAG_EVENT, message);
        locate = () => invoke<DropTarget | null>('tab_drag_target');
        useSystemDrag = await invoke<boolean>('use_system_tab_drag');
        await send({ kind: 'ready', windowId });
    } else {
        const channel = new BroadcastChannel(DRAG_EVENT);
        channel.onmessage = event => { void handle(event.data); };
        send = async message => {
            channel.postMessage(message);
            await handle(message);
        };
    }

    function begin(tab: HTMLElement): TabSource {
        source = {
            windowId,
            groupId: tab.closest<HTMLElement>('[data-tab-group]')!.dataset.tabGroup!,
            tabId: tab.dataset.tabId!,
            token: crypto.randomUUID(),
        };
        pending = null;
        tab.classList.add('tab-dragging');
        return source;
    }

    if (useSystemDrag) {
        document.addEventListener('pointerdown', event => {
            const tab = (event.target as HTMLElement).closest<HTMLElement>('[data-tab-id]');
            if (tab) tab.draggable = !(event.target as HTMLElement).closest('button');
        });
        document.addEventListener('dragstart', event => {
            const tab = (event.target as HTMLElement).closest<HTMLElement>('[data-tab-id]');
            if (!tab) return;
            event.dataTransfer!.effectAllowed = 'move';
            event.dataTransfer!.setData(TAB_MIME, JSON.stringify(begin(tab)));
        });
        function allowDrop(event: DragEvent): void {
            if (!event.dataTransfer?.types.includes(TAB_MIME)) return;
            // Keeps Monaco from showing its text drop cursor.
            event.stopPropagation();
            hideIndicators();
            // Desktop accepts drops anywhere so any drop inside a window is reported.
            if (!targetAt(event.clientX, event.clientY, source) && !desktop) {
                event.dataTransfer.dropEffect = 'none';
                return;
            }
            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';
        }
        document.addEventListener('dragenter', allowDrop, true);
        document.addEventListener('dragover', allowDrop, true);
        document.addEventListener('dragleave', event => {
            if (!event.relatedTarget) hideIndicators();
        });
        document.addEventListener('drop', event => {
            if (!event.dataTransfer?.types.includes(TAB_MIME)) return;
            event.preventDefault();
            event.stopPropagation();
            const dragged = JSON.parse(event.dataTransfer.getData(TAB_MIME)) as TabSource;
            void send({ kind: 'drop', source: dragged, target: { windowId, x: event.clientX, y: event.clientY } });
        }, true);
        document.addEventListener('dragend', () => {
            void send({ kind: 'end' });
            const dragged = source;
            // Other apps may accept the drop, so detach unless a CalcpadCE window reported it.
            if (dragged) setTimeout(() => { if (dropped !== dragged.token) void detach(dragged); }, 300);
        });
        return;
    }

    let gesture: { tab: HTMLElement; pointerId: number; x: number; y: number; dragging: boolean } | null = null;
    let updates = Promise.resolve();
    let moving = false;
    document.addEventListener('pointerdown', event => {
        if (event.button !== 0 || (event.target as HTMLElement).closest('button')) return;
        const tab = (event.target as HTMLElement).closest<HTMLElement>('[data-tab-id]');
        if (!tab) return;
        gesture = { tab, pointerId: event.pointerId, x: event.clientX, y: event.clientY, dragging: false };
        tab.setPointerCapture(event.pointerId);
    });
    document.addEventListener('pointermove', event => {
        if (!gesture || gesture.pointerId !== event.pointerId) return;
        if (!gesture.dragging) {
            if (Math.hypot(event.clientX - gesture.x, event.clientY - gesture.y) < 5) return;
            gesture.dragging = true;
            begin(gesture.tab);
            document.body.classList.add('dragging-tab');
        }
        if (moving) return;
        moving = true;
        const dragged = source!;
        updates = updates.then(async () => {
            const target = await locate(event);
            if (gesture?.dragging) await send({ kind: 'hover', source: dragged, target });
        }).finally(() => {
            moving = false;
        });
    });
    document.addEventListener('pointerup', event => {
        if (!gesture || gesture.pointerId !== event.pointerId) return;
        const dragged = gesture.dragging ? source : null;
        gesture = null;
        clear();
        if (!dragged) return;
        updates = updates.then(async () => {
            const target = await locate(event);
            await send({ kind: 'end' });
            await send({ kind: 'drop', source: dragged, target });
        });
    });
    function cancel(): void {
        if (!gesture) return;
        const current = gesture;
        gesture = null;
        if (current.tab.hasPointerCapture(current.pointerId)) current.tab.releasePointerCapture(current.pointerId);
        updates = updates.then(() => send({ kind: 'end' }));
        clear();
        source = null;
    }
    document.addEventListener('pointercancel', cancel);
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') cancel();
    });
}
