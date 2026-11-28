import { createApp } from 'vue'
import CalcpadApp from '@calcpad-vue/components/CalcpadApp.vue'
import '@calcpad-vue/styles/base.css'
import { initMessaging, postMessage } from '@calcpad-vue/services/messaging'
import { createCalcpadI18n, getActiveLocale, readLocaleSetting, LOCALE_CHANGED_EVENT, setCurrentCulture, setHostLocale } from '@calcpad-i18n'

// Initialize VS Code API before messaging service
const vscode = (window as any).acquireVsCodeApi()
;(window as any).vscode = vscode

// Initialize platform-aware messaging (defaults to VS Code when VITE_PLATFORM is unset)
initMessaging()

// Create and mount the Vue app
const app = createApp(CalcpadApp, {
  versionConfig: {
    isVSCode: true,
    isWeb: false,
    isDesktop: false,
    isWebOrDesktop: false,
  },
  appVersion: import.meta.env.VITE_APP_VERSION,
})
setCurrentCulture(window.__CALCPAD_CURRENT_CULTURE__)
setHostLocale(window.__CALCPAD_HOST_LOCALE__)
app.use(createCalcpadI18n(readLocaleSetting()))
app.mount('#app')

// Keep the extension host's Accept-Language in step with the resolved UI locale.
function announceLocale() {
  postMessage({ type: 'uiLocale', locale: getActiveLocale() })
}
announceLocale()
window.addEventListener(LOCALE_CHANGED_EVENT, announceLocale)

// Handle any global errors
app.config.errorHandler = (err, instance, info) => {
  console.error('Vue Error:', err, info)
  postMessage({
    type: 'debug',
    message: `Vue Error: ${err} - ${info}`
  })
}
