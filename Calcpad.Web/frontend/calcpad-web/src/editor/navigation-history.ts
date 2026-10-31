import type { ISelection } from 'monaco-editor';

export interface NavigationLocation {
    groupId: string;
    tabId: string;
    position: { lineNumber: number; column: number };
    selection?: ISelection;
}

export type NavigationDirection = -1 | 1;

export function navigationDirection(
    event: Pick<KeyboardEvent, 'key' | 'code' | 'ctrlKey' | 'altKey' | 'shiftKey' | 'metaKey'>,
    platform: string,
): NavigationDirection | 0 {
    if (event.metaKey) return 0;
    if (/Mac/.test(platform)) {
        if (event.ctrlKey && !event.altKey && (event.code === 'Minus' || event.key === '-'))
            return event.shiftKey ? 1 : -1;
    } else if (/Linux/.test(platform)) {
        if (event.ctrlKey && (event.code === 'Minus' || event.key === '-')) {
            if (event.altKey && !event.shiftKey) return -1;
            if (event.shiftKey && !event.altKey) return 1;
        }
    } else if (event.altKey && !event.ctrlKey && !event.shiftKey) {
        if (event.key === 'ArrowLeft') return -1;
        if (event.key === 'ArrowRight') return 1;
    }
    return 0;
}

export class NavigationHistory {
    private entries: NavigationLocation[] = [];
    private index = -1;

    record(location: NavigationLocation, jump = false): void {
        const current = this.entries[this.index];
        const sameTab = current?.groupId === location.groupId && current.tabId === location.tabId;
        const line = (entry: NavigationLocation) => entry.selection
            ? Math.min(entry.selection.selectionStartLineNumber, entry.selection.positionLineNumber)
            : entry.position.lineNumber;

        if (sameTab && (line(current) === line(location)
            || (!jump && Math.abs(line(current) - line(location)) < 10))) {
            this.entries[this.index] = location;
        } else {
            this.entries.splice(this.index + 1);
            this.entries.push(location);
            this.index = this.entries.length - 1;
            if (this.entries.length > 50) {
                this.entries.shift();
                this.index--;
            }
        }
    }

    canGo(direction: NavigationDirection): boolean {
        return this.index + direction >= 0 && this.index + direction < this.entries.length;
    }

    go(direction: NavigationDirection): NavigationLocation | undefined {
        if (!this.canGo(direction)) return undefined;
        this.index += direction;
        return this.entries[this.index];
    }

    remove(groupId: string, tabId: string): void {
        const keep = (entry: NavigationLocation) => entry.groupId !== groupId || entry.tabId !== tabId;
        this.index = this.entries.slice(0, this.index + 1).filter(keep).length - 1;
        this.entries = this.entries.filter(keep);
    }
}
