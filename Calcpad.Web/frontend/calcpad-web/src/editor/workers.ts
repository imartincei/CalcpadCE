// Monaco worker setup for web — required for proper language features.
// Must be imported before creating any Monaco editor instances.
self.MonacoEnvironment = {
    getWorker(_workerId: string, _label: string) {
        return new Worker(
            new URL('monaco-editor/esm/vs/editor/editor.worker.js', import.meta.url),
            { type: 'module' }
        );
    },
};
