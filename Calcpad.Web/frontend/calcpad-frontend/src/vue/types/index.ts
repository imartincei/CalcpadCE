// Type definitions for CalcpadVuePanel

// Which host is running the Vue UI: exactly one of isVSCode / isWeb / isDesktop is true.
// `isWebOrDesktop` is a convenience — true for the calcpad-web/desktop hosts, which share
// App.vue's in-page Output panel and tab strip, and false for VS Code.
export interface VersionConfig {
  isVSCode: boolean
  isWeb: boolean
  isDesktop: boolean
  isWebOrDesktop: boolean
}

export const DEFAULT_VERSION_CONFIG: VersionConfig = {
  isVSCode: true,
  isWeb: false,
  isDesktop: false,
  isWebOrDesktop: false,
}

export interface SnippetParameter {
  name: string
  description?: string
  /** Type from the backend ParameterType enum (e.g. "Scalar", "Vector", "Matrix", "Any"). */
  type?: string
  /** Human-readable type description; falls back to `type` when absent. */
  typeDescription?: string
  isOptional?: boolean
  isVariadic?: boolean
}

export interface InsertItem {
  label?: string
  tag: string
  description?: string
  /** Long-form description for docstrings. */
  documentation?: string
  /** Usage example. */
  example?: string
  categoryPath?: string
  category?: string
  quickType?: string
  keywordType?: string
  returnType?: string
  returnTypeDescription?: string
  isElementWise?: boolean
  acceptsAnyCount?: boolean
  parameters?: SnippetParameter[]
}

export interface InsertCategory {
  direct?: InsertItem[]
  [key: string]: InsertItem[] | InsertCategory | undefined
}

export interface InsertData {
  [key: string]: InsertCategory
}

// The app-level settings shape is defined once in types/settings.ts; the Vue UI
// consumes it under the name `Settings`.
export type { CalcpadSettings as Settings } from '../../types/settings'

export interface VariableItem {
  name: string
  definition?: string
  content?: string
  source?: string
  sourceFile?: string
  params?: string
  description?: string
  paramTypes?: string[]
  paramDescriptions?: string[]
  defaults?: (string | null)[]
  /** Value/expression type for variables and custom units (e.g. "Scalar", "Vector"). */
  type?: string
  /** Return type for functions. */
  returnType?: string
  /** Right-hand-side expression for functions/variables/custom units. */
  expression?: string
}

export interface VariablesData {
  macros: VariableItem[]
  variables: VariableItem[]
  functions: VariableItem[]
  customUnits: VariableItem[]
}

export interface Tab {
  id: string
  label: string
  icon?: string
}

export type ThemeKind = 'dark' | 'light'

export interface ThemeInfo {
  label: string
  id: string
  kind: ThemeKind
}

// VS Code message types
export interface VscodeMessage {
  type: string
  [key: string]: any
}

export type { PdfSettings } from '../../types/pdf-settings'
export { DEFAULT_PDF_SETTINGS } from '../../types/pdf-settings'

export interface TocHeading {
  level: number   // 1-6
  text: string    // Heading text content (stripped of markup)
  line: number    // 1-indexed line number in the source document
}

export interface FileNode {
  name: string
  path: string
  isDirectory: boolean
  children?: FileNode[]
  loaded?: boolean
}