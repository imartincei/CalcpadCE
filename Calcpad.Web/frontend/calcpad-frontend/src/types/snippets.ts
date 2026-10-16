// API response types matching the server schema
export interface SnippetParameterDto {
    name: string;
    description?: string;
    /** Type from the backend ParameterType enum (e.g. "Scalar", "Vector", "Matrix", "Any"). */
    type?: string;
    /** Human-readable type description (e.g. "Angle in radians"). Falls back to type when absent. */
    typeDescription?: string;
    isOptional?: boolean;
    isVariadic?: boolean;
}

export interface SnippetDto {
    insert: string;
    description: string;
    /** Long-form Markdown description for hover/completion docstrings. */
    documentation?: string;
    /** Optional Calcpad usage example, rendered as a fenced code block. */
    example?: string;
    label?: string;
    category: string;
    quickType?: string;
    keywordType?: string;
    /** Return type name from the backend CalcpadType enum. */
    returnType?: string;
    /** Human-readable description of the return value. */
    returnTypeDescription?: string;
    isElementWise?: boolean;
    acceptsAnyCount?: boolean;
    parameters?: SnippetParameterDto[];
}

export interface SnippetsResponse {
    count: number;
    snippets: SnippetDto[];
}

// Internal InsertItem used throughout the extension and desktop app
export interface InsertItem {
    tag: string;
    label?: string;
    description: string;
    documentation?: string;
    example?: string;
    categoryPath?: string;
    quickType?: string;
    keywordType?: string;
    returnType?: string;
    returnTypeDescription?: string;
    isElementWise?: boolean;
    acceptsAnyCount?: boolean;
    parameters?: SnippetParameterDto[];
}

// Hierarchical structure for tree view
export interface InsertDataTree {
    [category: string]: InsertDataTree | InsertItem[];
}

export type SnippetsLoadedCallback = () => void;
