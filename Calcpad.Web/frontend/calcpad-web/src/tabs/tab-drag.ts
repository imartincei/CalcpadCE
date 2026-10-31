import type { TabTransfer } from './tab-manager';

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
    | { kind: 'request'; source: TabSource; windowId: string; groupId: string; beforeId: string | null }
    | { kind: 'transfer'; source: TabSource; windowId: string; groupId: string; beforeId: string | null; document: TabTransfer }
    | { kind: 'accepted'; source: TabSource };

interface TabDragHooks {
    capture(groupId: string, tabId: string): Promise<TabTransfer | null>;
    move(sourceGroupId: string, tabId: string, groupId: string, beforeId: string | null): Promise<void>;
    receive(document: TabTransfer, groupId: string, beforeId: string | null): Promise<boolean>;
    remove(groupId: string, tabId: string, document: TabTransfer): void;
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
    let useSystemDrag = !desktop;
    const marker = document.createElement('div');
    marker.className = 'tab-drop-marker';
    marker.hidden = true;
    document.body.append(marker);

    function clear(): void {
        marker.hidden = true;
        document.querySelectorAll('.tab-dragging').forEach(tab => tab.classList.remove('tab-dragging'));
        document.body.classList.remove('dragging-tab');
    }

    function targetAt(x: number, y: number) {
        const strip = document.elementFromPoint(x, y)?.closest<HTMLElement>('[data-tab-group]');
        if (!strip) return null;
        const list = strip.querySelector<HTMLElement>('.tab-list')!;
        const tabs = [...list.querySelectorAll<HTMLElement>('[data-tab-id]')];
        const before = tabs.find(tab => {
            const rect = tab.getBoundingClientRect();
            return x < rect.left + rect.width / 2;
        });
        const rect = (before ?? tabs.at(-1) ?? list).getBoundingClientRect();
        marker.style.left = `${before ? rect.left : tabs.length ? rect.right : rect.left}px`;
        marker.style.top = `${rect.top}px`;
        marker.style.height = `${rect.height}px`;
        marker.hidden = false;
        if (x < list.getBoundingClientRect().left + 24) list.scrollLeft -= 20;
        if (x > list.getBoundingClientRect().right - 24) list.scrollLeft += 20;
        return { groupId: strip.dataset.tabGroup!, beforeId: before?.dataset.tabId ?? null };
    }

    async function handle(message: DragMessage): Promise<void> {
        switch (message.kind) {
            case 'hover':
            case 'drop': {
                marker.hidden = true;
                if (message.target?.windowId !== windowId) return;
                const target = targetAt(message.target.x, message.target.y);
                if (message.kind === 'drop') {
                    clear();
                    if (target) await send({ kind: 'request', source: message.source, windowId, ...target });
                }
                break;
            }
            case 'request': {
                if (message.source.windowId !== windowId || message.source.token !== source?.token) return;
                if (message.windowId === windowId) {
                    await hooks.move(source.groupId, source.tabId, message.groupId, message.beforeId);
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
            case 'transfer':
                if (message.windowId === windowId
                    && await hooks.receive(message.document, message.groupId, message.beforeId)) {
                    await send({ kind: 'accepted', source: message.source });
                }
                break;
            case 'accepted':
                if (message.source.windowId === windowId && message.source.token === source?.token && pending) {
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
            marker.hidden = true;
            if (!targetAt(event.clientX, event.clientY)) {
                event.dataTransfer.dropEffect = 'none';
                return;
            }
            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';
        }
        document.addEventListener('dragenter', allowDrop);
        document.addEventListener('dragover', allowDrop);
        document.addEventListener('dragleave', event => {
            if (!event.relatedTarget) marker.hidden = true;
        });
        document.addEventListener('drop', event => {
            if (!event.dataTransfer?.types.includes(TAB_MIME)) return;
            event.preventDefault();
            event.stopPropagation();
            const dragged = JSON.parse(event.dataTransfer.getData(TAB_MIME)) as TabSource;
            void send({ kind: 'drop', source: dragged, target: { windowId, x: event.clientX, y: event.clientY } });
        }, true);
        document.addEventListener('dragend', () => { void send({ kind: 'end' }); });
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
