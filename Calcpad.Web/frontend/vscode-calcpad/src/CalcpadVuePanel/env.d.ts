/// <reference types="vite/client" />

declare module '*.vue' {
  const component: any
  export default component
}

// VS Code webview API
declare global {
  interface Window {
    vscode: {
      postMessage(message: unknown): void
    }
    insertData?: unknown
  }

  const acquireVsCodeApi: () => {
    postMessage(message: unknown): void
  }
}

export {}