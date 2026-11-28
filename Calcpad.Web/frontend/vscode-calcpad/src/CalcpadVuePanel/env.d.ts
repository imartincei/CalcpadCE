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
    __CALCPAD_HOST_LOCALE__?: string
    __CALCPAD_CURRENT_CULTURE__?: string
  }

  const acquireVsCodeApi: () => {
    postMessage(message: unknown): void
  }
}

export {}