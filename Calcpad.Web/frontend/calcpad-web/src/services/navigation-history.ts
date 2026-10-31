import { NavigationHistory, type NavigationDirection, type NavigationLocation } from '../editor/navigation-history';

export interface NavigationHistoryClient {
    record(location: NavigationLocation, jump?: boolean): void;
    remove(groupId: string, tabId: string): void;
    go(direction: NavigationDirection): Promise<void>;
}

interface NavigationCallbacks {
    confirm(): Promise<boolean>;
    restore(location: NavigationLocation): boolean;
}

function navigationCommand(run: NavigationHistoryClient['go']): NavigationHistoryClient['go'] {
    let running = false;
    return async direction => {
        if (running) return;
        running = true;
        try { await run(direction); }
        finally { running = false; }
    };
}

export function createBrowserNavigationHistory({ confirm, restore }: NavigationCallbacks): NavigationHistoryClient {
    const history = new NavigationHistory();
    let restoring = false;
    return {
        record(location, jump) { if (!restoring) history.record(location, jump); },
        remove(groupId, tabId) { history.remove(groupId, tabId); },
        go: navigationCommand(async direction => {
            if (!history.canGo(direction) || !await confirm()) return;
            const location = history.go(direction);
            if (!location) return;
            restoring = true;
            try { restore(location); }
            finally { restoring = false; }
        }),
    };
}

export async function createDesktopNavigationHistory({ confirm, restore }: NavigationCallbacks): Promise<NavigationHistoryClient> {
    const [{ invoke }, { listen }, { getCurrentWindow }] = await Promise.all([
        import('@tauri-apps/api/core'), import('@tauri-apps/api/event'), import('@tauri-apps/api/window'),
    ]);
    let updates = Promise.resolve();
    const send = (command: string, args: Record<string, unknown>) =>
        updates = updates.then(() => invoke<void>(command, args));

    await listen<{ id: number; location: NavigationLocation }>('navigate-history', async ({ payload }) => {
        const accepted = await confirm() && restore(payload.location);
        await send('complete_navigation', { id: payload.id, accepted });
    }, { target: getCurrentWindow().label });

    return {
        record(location, jump = false) {
            if (document.hasFocus()) void send('record_navigation', { location, jump });
        },
        remove(groupId, tabId) { void send('remove_navigation', { groupId, tabId }); },
        go: async direction => {
            if (await confirm()) await send('navigate_history', { direction });
        },
    };
}
