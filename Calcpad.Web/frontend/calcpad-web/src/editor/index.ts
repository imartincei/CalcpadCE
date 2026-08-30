// =============================================================================
// Monaco editor integration for CalcPad
// Consolidated from the former calcpad-monaco package.
// =============================================================================

export { calcpadLanguageConfiguration } from './language';
export { calcpadDarkTheme } from './theme';
export {
    registerCalcpadLanguage,
    registerCalcpadTheme,
    createCalcpadEditor,
} from './setup';
export type { CalcpadEditorOptions } from './setup';
export { registerSemanticTokensProvider } from './semantic-tokens';
export { setupDiagnostics } from './diagnostics';
export { registerCompletionProvider } from './completions';
export { registerHoverProvider } from './hover';
export {
    registerDefinitionProvider,
    registerReferenceProvider,
    registerRenameProvider,
} from './references';
export { attachQuickTyper } from './quick-type';
export { attachOperatorReplacer } from './operator-replacer';
export { attachAutoIndenter } from './auto-indent';
export { registerFormattingCommands } from './formatting-commands';
export { registerFormatDocumentProvider } from './format-document';
export type { EditorBridge } from './bridge';
export { EDITOR_DOCUMENT_KEY } from './bridge';
