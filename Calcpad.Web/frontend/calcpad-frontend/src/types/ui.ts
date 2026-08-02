// Type definitions shared between VS Code webview and Tauri UI

export interface SnippetParameter {
  name: string;
  description?: string;
}

export interface UiInsertItem {
  label?: string;
  tag: string;
  description?: string;
  categoryPath?: string;
  category?: string;
  quickType?: string;
  parameters?: SnippetParameter[];
}

export interface InsertCategory {
  direct?: UiInsertItem[];
  [key: string]: UiInsertItem[] | InsertCategory | undefined;
}

export interface InsertData {
  [key: string]: InsertCategory;
}

export interface VariableItem {
  name: string;
  definition?: string;
  content?: string;
  source?: string;
  params?: string;
}

export interface VariablesData {
  macros: VariableItem[];
  variables: VariableItem[];
  functions: VariableItem[];
  customUnits: VariableItem[];
}

export interface Tab {
  id: string;
  label: string;
  icon?: string;
}

export interface VscodeMessage {
  type: string;
  [key: string]: unknown;
}

export type { PdfSettings } from './pdf-settings';
export { DEFAULT_PDF_SETTINGS } from './pdf-settings';
