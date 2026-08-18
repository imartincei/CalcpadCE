// =============================================================================
// calcpad-frontend — Shared CalcPad frontend logic
// =============================================================================

// --- Types -------------------------------------------------------------------
export type {
    LintRequest,
    LintResponse,
    LintDiagnostic,
    HighlightRequest,
    HighlightResponse,
    HighlightToken,
    DefinitionsRequest,
    DefinitionsResponse,
    MacroDefinition,
    FunctionDefinition,
    VariableDefinition,
    CustomUnitDefinition,
    SymbolLocation,
    SymbolKind,
    SymbolAtPositionRequest,
    SymbolAtPositionResponse,
    PrettifyRequest,
    PrettifyResponse,
    CalcpadError,
    CalcpadErrorSource,
    ConvertResult,
    UiConvertOptions,
    ExportVariant,
    CpdzDecodeResponse,
    CpdzEncodeResponse,
    PortableBundleResult,
    PortablePackageResult,
} from './types/api';
export { CalcpadTokenType, CalcpadTypeId } from './types/api';

export type { ILogger, IFileSystem } from './types/interfaces';

export type { SettingOption, MetadataSettingKey } from './types/catalog';
export { specForKey, validateCatalogValue } from './types/catalog';

export type { PdfSettings } from './types/pdf-settings';
export {
    DEFAULT_PDF_SETTINGS,
    resolveStoredPdfSettings,
    resolveEffectivePdfSettings,
    PDF_SETTING_KEYS,
    pdfSpec,
    validatePdfValue,
} from './types/pdf-settings';

export type { CalcpadSettings, CalcpadExtras, CalcpadSettingsBlob } from './types/settings';
export {
    getDefaultSettings,
    getDefaultExtras,
    getDefaultSettingsBlob,
    deserializeSettingsBlob,
    serializeSettingsBlob,
    getExtraString,
    getExtraBool,
    getExtraNumber,
    getExtraObject,
    colorScaleToEnum,
    lightDirectionToEnum,
    buildApiSettings,
    METADATA_SETTINGS_KEYS,
    settingSpec,
    validateSettingValue,
    SETTINGS_PATH,
    DEFAULT_CALCPAD_SETTINGS,
} from './types/settings';

export type {
    SnippetParameterDto,
    SnippetDto,
    SnippetsResponse,
    InsertItem,
    InsertDataTree,
    SnippetsLoadedCallback,
} from './types/snippets';

// --- API Client --------------------------------------------------------------
export { CalcpadApiClient, API_TOKEN_HEADER, parseConvertErrorHeader, combineSignals } from './api/client';
export {
    BROWSER_NOT_FOUND,
    BrowserNotFoundError,
    isBrowserNotFound,
    pdfResponseError,
    fetchPdfBrowserStatus,
    installPdfBrowser,
} from './api/pdf-browser';
export type { PdfBrowserStatus } from './api/pdf-browser';

// --- Services ----------------------------------------------------------------
export { CalcpadLintService } from './services/linter';
export { CalcpadDefinitionsService } from './services/definitions';
export { CalcpadSnippetService } from './services/snippets';
export {
    SEMANTIC_TOKEN_TYPES,
    TOKEN_TYPE_MAP,
    mapTokenTypeToIndex,
} from './services/highlight';

// --- Base64 Truncation -------------------------------------------------------
export { truncateBase64Content } from './services/base64-truncate';

// --- Image Utilities ---------------------------------------------------------
export {
    IMAGE_EXTENSIONS,
    IMAGE_MIME_TYPES,
    mimeFromExtension,
    isImageExtension,
    buildImageCommentLine,
    bytesToBase64,
} from './services/image-utils';
export type { ImageStorageMode, PickedImage } from './services/image-utils';

// --- HTML Body Extraction ------------------------------------------------------
export { extractBodyHtml } from './services/html-body';
export { previewDiagnosticsScript } from './services/preview-diagnostics';

// --- Preview Memory Limits ---------------------------------------------------
export {
    DEFAULT_PREVIEW_SIZE_MB,
    MIN_PREVIEW_SIZE_MB,
    MAX_PREVIEW_SIZE_MB,
    MAX_INLINE_IMAGE_TOTAL_BYTES,
    MAX_HTML_MIRROR_CHARS,
    BACK_BUFFER_CLEAR_CHARS,
    previewSizeLimitChars,
    formatSize,
    truncateForOutput,
    previewLimitNoticeHtml,
    consoleRelayGuardScript,
} from './services/preview-limits';
export type { PreviewLimitNotice } from './services/preview-limits';

export type { ScrollAnchor, PreviewScrollState } from './services/scroll-anchor';
export { scrollAnchorScript, parseScrollState } from './services/scroll-anchor';

// --- Plot Extraction + ZIP ---------------------------------------------------
export { extractPlotsFromHtml } from './services/plot-extract';
export type { ExtractedPlot } from './services/plot-extract';
export { buildZip } from './services/zip-writer';
export type { ZipEntry } from './services/zip-writer';
export { UiOverrideStore, readUiOverrides, writeUiOverrides, extractUiControls, classifyUiOverrides } from './services/ui-overrides';
export { isCompiledPath, inlineImageSources, COMPILED_EXTENSION, COMPILED_MIME } from './services/cpdz';
export type { InlineImageBudget } from './services/cpdz';
export {
    pathDirname,
    pathBasename,
    pathRelative,
    pathIsAbsolute,
    pathResolve,
    pathExtension,
} from './services/paths';
export type { UiOverrides, UiValueChange, UiControl, UiOverrideRow } from './services/ui-overrides';

// --- Message Bridge (base class) --------------------------------------------
export { BaseMessageBridge, variantRender } from './services/message-bridge/base';
export type { ExportRequest, VariantRender } from './services/message-bridge/base';

// --- Headings / TOC ----------------------------------------------------------
export type { TocHeading } from './services/headings';
export { parseHeadings } from './services/headings';

export {
    decodeExitCode,
    formatCrashReportPayload,
    buildCrashRecord,
} from './services/crash-report';
export type { CrashRecordInput } from './services/crash-report';

// --- Text Analysis -----------------------------------------------------------
export {
    OPERATOR_REPLACEMENTS,
    isOperatorTriggerChar,
    isInsideStringOrComment,
    findOperatorReplacement,
} from './text/operators';
export {
    findQuickTypeReplacement,
} from './text/quick-type';
export {
    buildInsertSnippet,
    hasSnippetPlaceholders,
    replaceParameterPlaceholders,
    formatInsertLabel,
} from './text/snippet-insert';
export type { CompletionKind, CompletionData } from './text/completion-format';
export {
    buildParameterSnippet,
    buildParameterizedDoc,
    formatMacroCompletion,
    formatFunctionCompletion,
    formatVariableCompletion,
    formatCustomUnitCompletion,
    formatBuiltinSnippetCompletion,
} from './text/completion-format';
export {
    INDENT_INCREASE_PATTERNS,
    INDENT_DECREASE_PATTERNS,
    shouldIncreaseIndent,
    shouldDecreaseIndent,
    getIndentation,
    couldCompleteDedentKeyword,
    calculateExpectedIndent,
} from './text/auto-indent';
export type { InlineFormat, CommentFormat } from './text/comment-formatting';
export {
    HTML_INLINE,
    MARKDOWN_INLINE,
    getIndentLength,
    splitIndent,
    stripCommentPrefix,
    lineHasCommentPrefix,
    isColumnInTextContext,
    getCommentPrefixInsertColumn,
    buildHeadingLine,
    buildParagraphLine,
    buildListLines,
} from './text/comment-formatting';
export type {
    MetadataCommentData,
    MetadataCommentBlock,
    MetadataLayout,
    MetadataLayoutSegment,
    MetadataLineContext,
    MetadataDefKind,
    MetadataDefinition,
    DefinitionResolver,
    LintCode,
    SettingsValues,
    SettingsDirective,
    PdfCommentValues,
} from './text/metadata-comment';
export {
    FUNCTION_PARAM_TYPES,
    MACRO_PARAM_TYPES,
    LINT_CODES,
    findMetadataCommentBlock,
    computeMetadataBlock,
    serializeMetadataComment,
    hasMetadataContent,
    settingsDirectiveOnLine,
    serializeSettingsDirective,
    pdfSettingsFromDocument,
    buildDefinitionResolver,
    analyzeMetadataLine,
} from './text/metadata-comment';
export type {
    UiDirectiveData,
    UiDirectiveBlock,
    UiPropertyKey,
} from './text/ui-directive';
export {
    UI_PROPERTY_KEYS,
    findUiDirectiveBlock,
    serializeUiDirective,
    documentHasUiDirectives,
} from './text/ui-directive';
export type {
    PathRootKind,
    PathRootDeclaration,
    DeclaredPathRoots,
    ResolvedPathRoots,
} from './text/path-roots';
export {
    hasPathRootToken,
    getPathRootTokenKind,
    isUserToken,
    expandUserToken,
    parsePathRootDeclaration,
    scanDeclaredPathRoots,
    resolvePathRoot,
    resolveDeclaredPathRoots,
    expandPathRootToken,
    createReferenceResolver,
} from './text/path-roots';
export type {
    Directive,
    DirectiveParse,
    CompletionPathRootsParams,
    PathRootTokenOption,
} from './text/directives';
export {
    parseDirectiveLine,
    INCLUDE_EXTENSIONS,
    DATA_EXTENSIONS,
    extensionsForDirective,
    PATH_ROOT_TOKEN,
    PATH_ROOT_LABEL,
    USER_TOKEN,
    DIRECTIVE_TRIGGER_CHARACTERS,
    resolveCompletionPathRoots,
    pathRootTokenOptions,
    hasDanglingCloseBrace,
} from './text/directives';
