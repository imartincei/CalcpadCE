<template>
  <div class="insert-tab">
    <CalcpadSearchField
      ref="searchField"
      v-model="searchTerm"
      :placeholder="searchShortcut ? t('Search items... ({key})', { key: searchShortcut }) : t('Search items...')"
      class="search-container"
      @submit="focusResult(0)"
      @keydown.down.exact="enterResults($event, 0)"
      @keydown.up.exact="enterResults($event, filteredItems.length - 1)"
    />

    <div class="insert-scroll-container">
      <div v-if="!searchQuery" class="insert-actions">
        <button @click="insertImage" class="icon" :title="t('Insert an image from file')" :aria-label="t('Insert an image from file')">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.2" aria-hidden="true">
            <rect x="2" y="2" width="12" height="12" rx="1" />
            <circle cx="5.5" cy="5.5" r="1.5" />
            <path d="m14 12-4-4-3 3-2-2-3 3" />
          </svg>
        </button>
        <CollapseAllButton v-if="displayItems.length > 0" @click="collapseAll" />
      </div>

      <div v-if="!searchQuery" class="favorites-palette">
        <div class="symbols-palette-header" :title="t('Favorites')" @click="panelState.favoritesOpen = !panelState.favoritesOpen">
          <span class="symbols-palette-arrow" :class="{ open: panelState.favoritesOpen }">&#x25B6;</span>
          <span class="favorite-star active">★</span>
          <span>{{ t('Favorites') }}</span>
          <span v-if="favoriteItems.length" class="favorites-count">{{ favoriteItems.length }}</span>
        </div>
        <div v-show="panelState.favoritesOpen" class="favorites-body">
          <div v-if="favoriteItems.length === 0" class="favorites-empty">{{ t('Star an item to pin it here.') }}</div>
          <div v-for="item in favoriteItems" :key="item.key" class="item-row">
            <button class="tree-item" :title="buildTooltip(item)" @click="insertItem(item)">{{ formatDisplayText(item) }}</button>
            <button class="item-action" :title="t('Quick type')" :aria-label="t('Quick type')" @click="openQuickTypeEditor(item)">~</button>
            <button class="item-action favorite-star active" :title="t('Remove from Favorites')" :aria-label="t('Remove from Favorites')" @click="toggleFavorite(item)">★</button>
          </div>
        </div>
      </div>

      <!-- Symbols Palette -->
      <div v-if="!searchQuery && symbolGroups.length > 0" class="symbols-palette">
        <div class="symbols-palette-header" :title="t('Symbol Palette')" @click="panelState.symbolsPaletteOpen = !panelState.symbolsPaletteOpen">
          <span class="symbols-palette-arrow" :class="{ open: panelState.symbolsPaletteOpen }">&#x25B6;</span>
          <span class="symbols-palette-title" :aria-label="t('Symbol Palette')">{{ t('Symbols:') }}<span class="symbols-palette-examples">α β π Δ Ω x̄ ẋ °</span>
          </span>
        </div>
        <div v-show="panelState.symbolsPaletteOpen" class="symbols-palette-body">
          <div v-for="group in symbolGroups" :key="group.name" class="symbol-group">
            <div class="symbol-group-header" :title="group.name" @click="toggleGroup(group.name)">
              <span class="symbol-group-arrow" :class="{ open: isGroupOpen(group.name) }">&#x25B6;</span>
              <span class="symbol-group-label" :aria-label="group.name">{{ group.preview }}</span>
            </div>
            <div v-show="isGroupOpen(group.name)" class="symbol-grid">
              <button
                v-for="item in group.items"
                :key="itemKeys.get(item)"
                class="symbol-btn"
                :title="buildTooltip(item)"
                @click="insertItem(item)"
              >
                {{ item.tag }}
              </button>
            </div>
          </div>
        </div>
      </div>

      <div v-if="searchQuery && filteredItems.length === 0" class="no-items">
        No items found for "{{ searchQuery }}"
      </div>
      <div v-else-if="displayItems.length === 0" class="no-items">{{ t('No insert items available') }}</div>
      <div
        v-else-if="searchQuery"
        ref="searchResults"
        class="insert-results"
        role="listbox"
        :aria-label="t('Insert search results')"
        @keydown.down.exact.prevent.stop="focusResult(Math.min(selectedResultIndex + 1, filteredItems.length - 1))"
        @keydown.up.exact.prevent.stop="focusResult(Math.max(selectedResultIndex - 1, 0))"
        @keydown.home.exact.prevent.stop="focusResult(0)"
        @keydown.end.exact.prevent.stop="focusResult(filteredItems.length - 1)"
        @keydown.space.prevent.stop
        @keydown.enter.prevent.stop="!$event.repeat && insertSelectedResult()"
      >
        <div
          v-for="(item, index) in displayItems"
          :key="itemKeys.get(item)"
          class="insert-result"
          role="option"
          :aria-selected="selectedResultIndex === index"
          :tabindex="index === Math.max(selectedResultIndex, 0) ? 0 : -1"
          @focus="selectedResultIndex = index"
          @click="focusResult(index)"
          @dblclick.prevent="insertItem(item)"
          :title="buildTooltip(item)"
        >
          <button
            class="item-action favorite-star result-star"
            :class="{ active: isFavorite(item) }"
            tabindex="-1"
            :title="isFavorite(item) ? t('Remove from Favorites') : t('Add to Favorites')"
            :aria-label="isFavorite(item) ? t('Remove from Favorites') : t('Add to Favorites')"
            @click.stop="toggleFavorite(item)"
            @dblclick.stop
          >{{ isFavorite(item) ? '★' : '☆' }}</button>
          <CalcpadSearchHighlight class="item-display" :text="formatDisplayText(item)" :query="searchQuery" />
          <CalcpadSearchHighlight v-if="formatSecondaryText(item)" class="item-description" :text="formatSecondaryText(item)" :query="searchQuery" />
          <CalcpadSearchHighlight v-if="item.categoryPath" class="item-category" :text="item.categoryPath" :query="searchQuery" />
        </div>
      </div>
      <div v-else>
        <div id="tree-container">
          <!-- Tree structure will be built here -->
          <div class="tree-placeholder">{{ t('Loading insert items...') }}</div>
        </div>
      </div>
    </div>

    <div v-if="editor" class="snippet-editor-overlay" @keydown.esc.stop="editor = null" @click.self="editor = null">
      <div class="snippet-editor" role="dialog" :aria-label="editorTitle">
        <h4 class="snippet-editor-title">{{ editorTitle }}</h4>

        <template v-if="editor.mode === 'snippet'">
          <label class="snippet-editor-field">
            <span>{{ t('Name') }}</span>
            <input ref="firstField" v-model="editor.label" type="text" />
          </label>
          <label class="snippet-editor-field">
            <span>{{ t('Type') }}</span>
            <select v-model="editor.keywordType">
              <option value="">{{ t('Text') }}</option>
              <option v-for="type in CUSTOM_SNIPPET_TYPES" :key="type" :value="type">{{ t(SNIPPET_TYPE_LABELS[type]) }}</option>
            </select>
          </label>
          <label class="snippet-editor-field">
            <span>{{ t('Inserted text') }}</span>
            <textarea v-model="editor.insert" rows="4" spellcheck="false"></textarea>
          </label>
          <label class="snippet-editor-field">
            <span>{{ t('Description') }}</span>
            <input v-model="editor.description" type="text" />
          </label>
          <div v-if="editorHasParameters" class="snippet-editor-field">
            <span>{{ t('Parameters') }}</span>
            <div v-for="(param, index) in editor.parameters" :key="index" class="snippet-param-row">
              <input v-model="param.name" type="text" :placeholder="t('Name')" :aria-label="t('Parameter name')" />
              <input
                v-model="param.type"
                type="text"
                :list="editor.keywordType === 'Macro' ? 'calcpad-macro-param-types' : 'calcpad-function-param-types'"
                :placeholder="t('Type')"
                :aria-label="t('Parameter type')"
              />
              <input v-model="param.description" type="text" :placeholder="t('Description')" :aria-label="t('Parameter description')" />
              <button :title="t('Remove')" :aria-label="t('Remove')" @click="editor.parameters.splice(index, 1)">✕</button>
            </div>
            <button class="snippet-param-add" @click="editor.parameters.push({ name: '', type: '', description: '' })">{{ t('+ Add parameter') }}</button>
            <span class="snippet-editor-hint">{{ t('Parameters fill the § placeholders in the inserted text, in order.') }}</span>
            <datalist id="calcpad-function-param-types">
              <option v-for="type in FUNCTION_PARAM_TYPES" :key="type" :value="type" />
            </datalist>
            <datalist id="calcpad-macro-param-types">
              <option v-for="type in MACRO_PARAM_TYPES" :key="type" :value="type" />
            </datalist>
          </div>
          <label v-if="editor.keywordType === 'Function' || editor.keywordType === 'Variable'" class="snippet-editor-field">
            <span>{{ editor.keywordType === 'Function' ? t('Return type') : t('Value type') }}</span>
            <input v-model="editor.returnType" type="text" list="calcpad-function-param-types" />
          </label>
          <label v-if="editor.keywordType" class="snippet-editor-field">
            <span>{{ t('Example') }}</span>
            <textarea v-model="editor.example" rows="2" spellcheck="false"></textarea>
          </label>
        </template>
        <div v-else class="snippet-editor-preview">{{ editor.insert }}</div>

        <div class="snippet-editor-field">
          <span>{{ t('Quick type') }}</span>
          <div class="quick-type-chips">
            <span v-for="key in editor.quickTypes" :key="key" class="quick-type-chip">
              ~{{ key }}
              <button :title="t('Remove')" :aria-label="t('Remove ~{key}', { key })" @click="removeEditorQuickType(key)">✕</button>
            </span>
            <span v-if="editor.quickTypes.length === 0" class="quick-type-none">{{ t('None') }}</span>
          </div>
          <div class="quick-type-add">
            <span class="quick-type-tilde">~</span>
            <input
              :ref="el => { if (editor?.mode === 'quickType') firstField = el as HTMLInputElement | null }"
              v-model="editor.newQuickType"
              type="text"
              :maxlength="QUICK_TYPE_MAX_LENGTH"
              :class="{ 'input-invalid': !!quickTypeError }"
              :placeholder="t('letters')"
              @keydown.enter.prevent="addEditorQuickType"
            />
            <button :disabled="!editor.newQuickType || !!quickTypeError" @click="addEditorQuickType">{{ t('Add') }}</button>
          </div>
          <span v-if="quickTypeError" class="snippet-editor-error">{{ quickTypeError }}</span>
          <span v-else class="snippet-editor-hint">{{ t('Type ~ then the letters and a space to insert this item.') }}</span>
        </div>

        <span v-if="snippetError" class="snippet-editor-error">{{ snippetError }}</span>
        <div class="snippet-editor-actions">
          <button v-if="editor.mode === 'snippet' && editor.id" class="danger" @click="deleteCustomSnippet">{{ t('Delete') }}</button>
          <span class="spacer"></span>
          <button @click="editor = null">{{ t('Cancel') }}</button>
          <button :disabled="!!snippetError" @click="saveEditor">{{ t('Save') }}</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { ref, reactive, computed, watch, nextTick } from 'vue'
import CalcpadSearchField from './CalcpadSearchField.vue'
import CalcpadSearchHighlight from './CalcpadSearchHighlight.vue'
import CollapseAllButton from './CollapseAllButton.vue'
import type { InsertItem } from '../types'
import { replaceParameterPlaceholders } from '../../text/snippet-insert'
import {
  CUSTOM_SNIPPETS_CATEGORY, CUSTOM_SNIPPET_TYPES, QUICK_TYPE_MAX_LENGTH, applySnippetCustomizations, newCustomSnippetId,
  quickTypesFor, readCustomizations, removeQuickType, sanitizeCustomSnippet, snippetKey, validateQuickType,
} from '../../services/customizations'
import type { CustomSnippet, CustomSnippetType, UserCustomizations } from '../../services/customizations'

const { t } = useI18n()

// Props
interface Props {
  insertItems: InsertItem[]
  customizations?: UserCustomizations
  /** Display form of the Search Insert Items shortcut, or empty when unbound. */
  searchShortcut?: string
}

const props = withDefaults(defineProps<Props>(), {
  customizations: () => readCustomizations(() => undefined),
  searchShortcut: '',
})

// Emits
const emit = defineEmits<{
  insertText: [text: string]
  insertImage: []
  updateCustomizations: [customizations: UserCustomizations]
}>()

const allItems = computed(() => applySnippetCustomizations(props.insertItems, props.customizations))
const itemQuickTypes = (item: InsertItem): string[] => quickTypesFor(item.tag, props.customizations.quickTypes)

const updateCustomizations = (patch: Partial<UserCustomizations>) => {
  emit('updateCustomizations', { ...props.customizations, ...patch })
}

// Favorites
const favoriteItems = computed(() => {
  const byKey = new Map(allItems.value.map(item => [item.key!, item]))
  return props.customizations.favoriteSnippets.map(key => byKey.get(key)).filter((x): x is InsertItem => !!x)
})
const isFavorite = (item: InsertItem) => props.customizations.favoriteSnippets.includes(item.key ?? snippetKey(item))
const toggleFavorite = (item: InsertItem) => {
  const key = item.key ?? snippetKey(item)
  const list = props.customizations.favoriteSnippets
  updateCustomizations({ favoriteSnippets: list.includes(key) ? list.filter(k => k !== key) : [...list, key] })
}

// Quick type and custom snippet editor
const SNIPPET_TYPE_LABELS: Record<CustomSnippetType, string> = {
  Function: 'Function', Macro: 'Macro', Variable: 'Variable', Unit: 'Custom Unit',
}
const FUNCTION_PARAM_TYPES = ['value', 'vector', 'matrix', 'any']
const MACRO_PARAM_TYPES = ['Const', 'Variable', 'Function', 'Macro', 'Units', 'Operator', 'Input', 'Keyword', 'Command']

interface EditorParameter { name: string; type: string; description: string; isOptional?: boolean; isVariadic?: boolean; typeDescription?: string }
interface EditorState {
  mode: 'quickType' | 'snippet'
  id?: string
  name: string
  originalInsert: string
  label: string
  insert: string
  description: string
  keywordType: CustomSnippetType | ''
  parameters: EditorParameter[]
  returnType: string
  example: string
  /** Stored fields the editor doesn't show, kept on save. */
  base: Partial<CustomSnippet>
  quickTypes: string[]
  newQuickType: string
}

const snippetDefaults = () => ({ keywordType: '' as const, parameters: [], returnType: '', example: '', base: {} })

const editorHasParameters = computed(() =>
  editor.value?.keywordType === 'Function' || editor.value?.keywordType === 'Macro')
const editor = ref<EditorState | null>(null)
const firstField = ref<HTMLInputElement | null>(null)

const focusEditor = async () => {
  await nextTick()
  firstField.value?.focus()
}

const openQuickTypeEditor = (item: InsertItem) => {
  if (item.customId) {
    openSnippetEditor(item.customId)
    return
  }
  editor.value = {
    mode: 'quickType', name: formatTitleText(item), originalInsert: item.tag, label: '', insert: item.tag,
    description: '', ...snippetDefaults(), quickTypes: itemQuickTypes(item), newQuickType: '',
  }
  void focusEditor()
}

const openSnippetEditor = (id?: string, draft?: Omit<CustomSnippet, 'id'>) => {
  const stored = props.customizations.customSnippets.find(s => s.id === id)
  const snippet: Partial<CustomSnippet> = stored ?? draft ?? {}
  editor.value = {
    mode: 'snippet', id: stored?.id, name: snippet.label ?? '', originalInsert: stored?.insert ?? '',
    label: snippet.label ?? '', insert: snippet.insert ?? '', description: snippet.description ?? '',
    keywordType: snippet.keywordType ?? '',
    parameters: (snippet.parameters ?? []).map(p => ({ ...p, type: p.type ?? '', description: p.description ?? '' })),
    returnType: snippet.returnType ?? '',
    example: snippet.example ?? '',
    base: snippet,
    quickTypes: snippet.insert ? quickTypesFor(snippet.insert, props.customizations.quickTypes) : [], newQuickType: '',
  }
  void focusEditor()
}

/** Opens the editor filled in from a definition, or the saved snippet it matches. */
const openSnippetDraft = (draft: Omit<CustomSnippet, 'id'>) => {
  const existing = props.customizations.customSnippets.find(s => s.insert === draft.insert)
  openSnippetEditor(existing?.id, existing ? undefined : draft)
}

const editorTitle = computed(() => {
  if (!editor.value) return ''
  if (editor.value.mode === 'quickType') return t('Quick type for {name}', { name: editor.value.name })
  return editor.value.id ? t('Edit custom snippet') : t('New custom snippet')
})

// The quick-type map as it would be with the editor's changes applied.
const draftQuickTypes = (state: EditorState): Record<string, string> => {
  let next = { ...props.customizations.quickTypes }
  for (const key of quickTypesFor(state.originalInsert, next)) {
    if (!state.quickTypes.includes(key)) next = removeQuickType(next, key)
  }
  for (const key of state.quickTypes) next[key] = state.insert
  return next
}

const quickTypeError = computed(() => {
  const state = editor.value
  if (!state?.newQuickType) return ''
  if (state.quickTypes.includes(state.newQuickType)) return ''
  const error = validateQuickType(state.newQuickType, draftQuickTypes(state))
  return error ? t(error.message, error.params) : ''
})

const addEditorQuickType = () => {
  const state = editor.value
  if (!state?.newQuickType || quickTypeError.value) return
  if (!state.quickTypes.includes(state.newQuickType)) state.quickTypes.push(state.newQuickType)
  state.newQuickType = ''
}

const removeEditorQuickType = (key: string) => {
  if (editor.value) editor.value.quickTypes = editor.value.quickTypes.filter(k => k !== key)
}

const snippetError = computed(() => {
  const state = editor.value
  if (state?.mode !== 'snippet') return ''
  return state.insert.trim() ? '' : t('Enter the text to insert')
})

const saveEditor = () => {
  const state = editor.value
  if (!state || snippetError.value) return
  if (state.newQuickType && !quickTypeError.value) addEditorQuickType()
  const quickTypes = draftQuickTypes(state)
  if (state.mode === 'quickType') {
    updateCustomizations({ quickTypes })
  } else {
    const list = props.customizations.customSnippets
    const hasParams = state.keywordType === 'Function' || state.keywordType === 'Macro'
    const plainText = !state.keywordType
    const snippet = sanitizeCustomSnippet({
      ...state.base,
      ...(plainText ? { documentation: undefined, returnTypeDescription: undefined } : {}),
      id: state.id ?? newCustomSnippetId(),
      label: state.label.trim() || state.insert.trim().split('\n')[0],
      insert: state.insert,
      description: state.description,
      keywordType: state.keywordType || undefined,
      parameters: hasParams ? state.parameters : undefined,
      returnType: state.keywordType === 'Function' || state.keywordType === 'Variable' ? state.returnType : undefined,
      example: plainText ? undefined : state.example,
    })
    if (!snippet) return
    const customSnippets = state.id ? list.map(s => s.id === state.id ? snippet : s) : [...list, snippet]
    updateCustomizations({ customSnippets, quickTypes })
    panelState.openCategories = [...new Set([...panelState.openCategories, CUSTOM_SNIPPETS_CATEGORY])]
  }
  editor.value = null
}

const deleteCustomSnippet = () => {
  const state = editor.value
  if (!state?.id) return
  let quickTypes = { ...props.customizations.quickTypes }
  for (const key of quickTypesFor(state.originalInsert, quickTypes)) quickTypes = removeQuickType(quickTypes, key)
  const key = snippetKey({ tag: state.originalInsert, customId: state.id })
  updateCustomizations({
    customSnippets: props.customizations.customSnippets.filter(s => s.id !== state.id),
    favoriteSnippets: props.customizations.favoriteSnippets.filter(k => k !== key),
    quickTypes,
  })
  editor.value = null
}

// State
const searchTerm = ref('')
const searchField = ref<InstanceType<typeof CalcpadSearchField> | null>(null)
const searchResults = ref<HTMLDivElement | null>(null)
const selectedResultIndex = ref(-1)
const searchQuery = computed(() => searchTerm.value.trim())
defineExpose({ focusSearch: () => searchField.value!.focus(), openSnippetDraft })

const STORAGE_KEY = 'calcpad.insert.panels'
const panelState = reactive({
  favoritesOpen: true,
  symbolsPaletteOpen: false,
  openSymbolGroups: [] as string[],
  openCategories: [] as string[]
})
Object.assign(panelState, JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}'))

watch(panelState, () => {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(panelState))
}, { flush: 'sync' })

const toggleGroup = (name: string) => {
  const next = new Set(panelState.openSymbolGroups)
  if (next.has(name)) next.delete(name)
  else next.add(name)
  panelState.openSymbolGroups = [...next]
}

const isGroupOpen = (name: string): boolean => panelState.openSymbolGroups.includes(name)

// Symbol palette helpers
interface SymbolGroup {
  name: string
  preview: string
  items: InsertItem[]
}

const isSymbolItem = (item: InsertItem): boolean => {
  return item.categoryPath?.startsWith('Symbols') ?? false
}

const symbolGroups = computed((): SymbolGroup[] => {
  const groups = new Map<string, InsertItem[]>()
  for (const item of allItems.value) {
    if (!isSymbolItem(item)) continue
    // Use the last segment of the category path as group name
    const parts = (item.categoryPath || '').split(' > ')
    const groupName = parts[parts.length - 1] || 'Other'
    if (!groups.has(groupName)) {
      groups.set(groupName, [])
    }
    groups.get(groupName)!.push(item)
  }
  return Array.from(groups.entries()).map(([name, items]) => ({
    name,
    preview: items.slice(0, 4).map(item => item.tag).join(' '),
    items
  }))
})

// Computed
const itemKeys = computed(() => new Map(
  allItems.value.map((item, index) => [item, index])
))

const filteredItems = computed(() => {
  if (!searchQuery.value) {
    return []
  }

  const term = searchQuery.value.toLowerCase()
  const scoreMatch = (text: string) => {
    const normalizedText = text.toLowerCase()
    let matchedCharacters = 0
    let wordStart = 0
    let position = normalizedText.indexOf(term)
    while (position !== -1) {
      matchedCharacters += term.length
      if (!/[\p{L}\p{M}\p{N}_]$/u.test(normalizedText.slice(0, position))) {
        wordStart = 1
      }
      position = normalizedText.indexOf(term, position + term.length)
    }
    return {
      coverage: matchedCharacters ? matchedCharacters / normalizedText.length : 0,
      wordStart
    }
  }

  return allItems.value
    .map(item => ({
      item,
      titleScore: scoreMatch(formatTitleText(item)),
      descriptionScore: scoreMatch(formatSecondaryText(item)),
      categoryScore: scoreMatch(item.categoryPath ?? '')
    }))
    .filter(({ item, titleScore, descriptionScore, categoryScore }) =>
      titleScore.coverage > 0 || descriptionScore.coverage > 0 || categoryScore.coverage > 0 ||
      item.label?.toLowerCase().includes(term) ||
      item.tag.toLowerCase().includes(term) ||
      item.description?.toLowerCase().includes(term)
    )
    // In each field, prefer word-start matches, then higher coverage.
    // Count matches without overlap; coverage is matched characters divided by field length.
    // Move to the next field only on a tie:
    // 1. Title.
    // 2. Description.
    // 3. Category.
    // Keep the original order when all fields tie.
    .sort((a, b) =>
      b.titleScore.wordStart - a.titleScore.wordStart ||
      b.titleScore.coverage - a.titleScore.coverage ||
      b.descriptionScore.wordStart - a.descriptionScore.wordStart ||
      b.descriptionScore.coverage - a.descriptionScore.coverage ||
      b.categoryScore.wordStart - a.categoryScore.wordStart ||
      b.categoryScore.coverage - a.categoryScore.coverage
    )
    .map(({ item }) => item)
})

const displayItems = computed(() => {
  if (searchQuery.value) {
    return filteredItems.value
  }
  return allItems.value
})

watch(filteredItems, () => {
  selectedResultIndex.value = -1
})

// Types for tree structure built from flat items
interface TreeNode {
  [key: string]: TreeNode | InsertItem[]
}

// Build tree structure from flat items grouped by categoryPath
const buildTreeFromItems = (items: InsertItem[]): TreeNode => {
  const tree: TreeNode = {}

  for (const item of items) {
    const path = item.categoryPath || 'Uncategorized'
    const parts = path.split(' > ')
    let current: TreeNode = tree

    for (let i = 0; i < parts.length; i++) {
      const part = parts[i]
      const isLast = i === parts.length - 1

      if (isLast) {
        if (!current[part]) {
          current[part] = []
        }
        const arr = current[part]
        if (Array.isArray(arr)) {
          arr.push(item)
        }
      } else {
        if (!current[part]) {
          current[part] = {}
        }
        const next = current[part]
        if (!Array.isArray(next)) {
          current = next
        }
      }
    }
  }

  return tree
}

// Markup snippets insert long, multi-line code. Their buttons show the friendly
// name and the tooltip carries the code — the reverse of every other category,
// where the tag itself is short enough to be the button text.
const MARKUP_CATEGORIES = new Set(['HTML', 'Metadata Properties', 'SVG', 'Markdown', 'CSS'])

const isMarkupItem = (item: InsertItem): boolean =>
  MARKUP_CATEGORIES.has((item.categoryPath ?? '').split(' > ')[0])

const formatTitleText = (item: InsertItem): string => {
  const name = isMarkupItem(item)
    ? item.label || item.description || item.tag
    : item.label || item.tag
  return replaceParameterPlaceholders(name, item)
}

// Appends (~shortcut) for items with quick typing support.
const formatDisplayText = (item: InsertItem): string => {
  const display = formatTitleText(item)
  const keys = itemQuickTypes(item)
  return keys.length ? display + ' (' + keys.map(k => '~' + k).join(', ') + ')' : display
}

const formatInsertText = (item: InsertItem): string => {
  return replaceParameterPlaceholders(item.tag, item)
}

// Secondary line under the display text in search results. Markup items show
// their code there, since their description is already the display text.
const formatSecondaryText = (item: InsertItem): string =>
  isMarkupItem(item) ? formatInsertText(item) : (item.description ?? '')

// Helper: Build a plain-text tooltip carrying the same information the
// source-code hover shows (description, documentation, parameters with
// type/optional/variadic, return type, element-wise note, example).
const buildTooltip = (item: InsertItem): string => {
  const lines: string[] = []

  if (isMarkupItem(item)) {
    lines.push(formatInsertText(item))
    lines.push('')
  }

  if (item.description) {
    lines.push(item.description)
  }

  if (item.documentation) {
    lines.push('')
    lines.push(item.documentation)
  }

  if (item.parameters && item.parameters.length > 0) {
    lines.push('')
    lines.push('Parameters:')
    for (const param of item.parameters) {
      let paramLine = '  ' + param.name
      const type = param.typeDescription ?? (param.type && param.type !== 'Any' ? param.type : undefined)
      if (type) paramLine += ' (' + type + ')'
      if (param.description) paramLine += ' - ' + param.description
      if (param.isVariadic) paramLine += ' [variadic]'
      else if (param.isOptional) paramLine += ' [optional]'
      lines.push(paramLine)
    }
  }

  const returnLabel = item.returnTypeDescription
    ?? (item.returnType && item.returnType !== 'Any' ? item.returnType : undefined)
  if (returnLabel) {
    lines.push('')
    lines.push('Returns: ' + returnLabel)
  }

  if (item.isElementWise) {
    lines.push('')
    lines.push('Accepts a scalar, vector, or matrix — applied element-wise, returning the same shape.')
  }

  if (item.example) {
    lines.push('')
    lines.push('Example:')
    lines.push('  ' + item.example.replace(/\n/g, '\n  '))
  }

  const quickTypes = itemQuickTypes(item)
  if (quickTypes.length) {
    lines.push('')
    lines.push('Quick type: ' + quickTypes.map(k => '~' + k).join(', '))
  }

  return lines.join('\n')
}

// Methods
const focusResult = async (index: number) => {
  if (!searchQuery.value || filteredItems.value.length === 0) return
  selectedResultIndex.value = index
  await nextTick()
  const result = searchResults.value!.querySelectorAll<HTMLElement>('[role="option"]')[index]
  result.focus({ preventScroll: true })
  result.scrollIntoView({ block: 'nearest' })
}

const enterResults = (event: KeyboardEvent, index: number) => {
  if (!searchQuery.value || filteredItems.value.length === 0 || event.isComposing) return
  event.preventDefault()
  event.stopPropagation()
  void focusResult(index)
}

const insertSelectedResult = () => {
  if (selectedResultIndex.value !== -1) insertItem(filteredItems.value[selectedResultIndex.value])
}

const insertImage = () => {
  emit('insertImage')
}

const insertItem = (item: InsertItem) => {
  emit('insertText', formatInsertText(item))
}

const collapseAll = () => {
  panelState.symbolsPaletteOpen = false
  panelState.openSymbolGroups = []
  panelState.openCategories = []
  const checkboxes = document.querySelectorAll('#tree-container input[type="checkbox"]')
  checkboxes.forEach(checkbox => {
    (checkbox as HTMLInputElement).checked = false
  })
}

const buildTreeStructure = () => {
  const treeContainer = document.getElementById('tree-container')
  if (!treeContainer) return

  // Clear existing content
  treeContainer.innerHTML = ''

  const treeRoot = document.createElement('ul')
  treeRoot.className = 'tree'

  if (props.insertItems.length === 0) {
    const placeholder = document.createElement('div')
    placeholder.className = 'tree-placeholder'
    placeholder.textContent = t('No insert data available')
    treeContainer.appendChild(placeholder)
  }
  treeContainer.appendChild(treeRoot)

  const builtins = allItems.value.filter(item => !item.customId)
  buildTreeStructureRecursive(buildTreeFromItems(builtins), treeRoot, 0)

  // Custom Snippets always shows last, with its add button, even when empty.
  const { li, ul } = createTreeSection(CUSTOM_SNIPPETS_CATEGORY, 0, [CUSTOM_SNIPPETS_CATEGORY], t('Custom Snippets'))
  for (const item of allItems.value) {
    if (item.customId) ul.appendChild(createTreeItem(item))
  }
  const addLi = document.createElement('li')
  const addButton = document.createElement('button')
  addButton.className = 'tree-item add-snippet'
  addButton.textContent = '+ ' + t('Add custom snippet')
  addButton.addEventListener('click', () => openSnippetEditor())
  addLi.appendChild(addButton)
  ul.appendChild(addLi)
  treeRoot.appendChild(li)
}

const buildTreeStructureRecursive = (data: TreeNode, parentUl: HTMLUListElement, level: number, parentPath: string[] = []) => {
  if (level >= 5) return // Prevent infinite recursion

  Object.keys(data).forEach(categoryKey => {
    const categoryData = data[categoryKey]
    const categoryPath = [...parentPath, categoryKey]

    if (Array.isArray(categoryData)) {
      const { li, ul } = createTreeSection(categoryKey, level, categoryPath)

      categoryData.forEach((item: InsertItem) => {
        const itemLi = createTreeItem(item)
        ul.appendChild(itemLi)
      })

      parentUl.appendChild(li)
    } else if (typeof categoryData === 'object' && categoryData !== null) {
      const { li, ul } = createTreeSection(categoryKey, level, categoryPath)
      buildTreeStructureRecursive(categoryData as TreeNode, ul, level + 1, categoryPath)
      parentUl.appendChild(li)
    }
  })
}

const createTreeSection = (title: string, level: number, categoryPath: string[], displayTitle = title) => {
  const categoryId = categoryPath.join(' > ')
  const checkboxId = 'checkbox-' + Date.now() + '-' + Math.random()
  const levelClass = level > 0 ? ' level-' + level : ''

  const li = document.createElement('li')
  li.className = 'tree-section' + levelClass

  const checkbox = document.createElement('input')
  checkbox.type = 'checkbox'
  checkbox.id = checkboxId
  checkbox.checked = panelState.openCategories.includes(categoryId)

  const label = document.createElement('label')
  label.setAttribute('for', checkboxId)
  label.textContent = displayTitle

  // Add click handler to label to toggle checkbox
  label.addEventListener('click', (e) => {
    e.preventDefault()
    checkbox.checked = !checkbox.checked
    const next = new Set(panelState.openCategories)
    if (checkbox.checked) next.add(categoryId)
    else next.delete(categoryId)
    panelState.openCategories = [...next]
  })

  const ul = document.createElement('ul')

  li.appendChild(checkbox)
  li.appendChild(label)
  li.appendChild(ul)

  return { li, ul }
}

const createActionButton = (text: string, title: string, onClick: () => void, extraClass = '') => {
  const button = document.createElement('button')
  button.className = 'item-action' + (extraClass ? ' ' + extraClass : '')
  button.textContent = text
  button.title = title
  button.setAttribute('aria-label', title)
  button.addEventListener('click', onClick)
  return button
}

const createTreeItem = (item: InsertItem) => {
  const li = document.createElement('li')
  li.className = 'item-row'

  const button = document.createElement('button')
  button.className = 'tree-item'
  button.title = buildTooltip(item)
  button.textContent = formatDisplayText(item)
  button.addEventListener('click', () => {
    insertItem(item)
  })
  li.appendChild(button)

  li.appendChild(item.customId
    ? createActionButton('✎', t('Edit custom snippet'), () => openSnippetEditor(item.customId))
    : createActionButton('~', t('Quick type'), () => openQuickTypeEditor(item)))
  const favorite = isFavorite(item)
  li.appendChild(createActionButton(
    favorite ? '★' : '☆',
    favorite ? t('Remove from Favorites') : t('Add to Favorites'),
    () => toggleFavorite(item),
    'favorite-star' + (favorite ? ' active' : ''),
  ))
  return li
}

// Rebuild the tree when items or customizations change
watch(
  () => [allItems.value, props.customizations],
  () => {
    nextTick(() => {
      buildTreeStructure()
    })
  },
  { immediate: true }
)

// Watch for search term changes to rebuild tree when clearing search
watch(
  () => searchTerm.value,
  (newSearchTerm) => {
    // When search is cleared (empty), rebuild the tree
    if (!newSearchTerm.trim()) {
      nextTick(() => {
        buildTreeStructure()
      })
    }
  }
)
</script>

<style scoped>
.insert-tab {
  position: relative;
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}

/* Favorites */
.favorites-palette {
  margin-bottom: 12px;
  border: 1px solid var(--vscode-widget-border);
  border-radius: 3px;
  overflow: hidden;
}

.favorites-body {
  padding: 4px;
}

.favorites-count {
  margin-left: auto;
  font-weight: normal;
  color: var(--vscode-descriptionForeground);
}

.favorites-empty {
  padding: 6px;
  color: var(--vscode-descriptionForeground);
  font-style: italic;
}

.item-row,
:deep(.item-row) {
  display: flex;
  align-items: stretch;
  gap: 2px;
}

.item-row .tree-item,
:deep(.item-row .tree-item) {
  flex: 1 1 auto;
  min-width: 0;
}

.item-action,
:deep(.item-action) {
  flex-shrink: 0;
  width: 24px;
  margin: 1px 0;
  padding: 0;
  border: none;
  background: transparent;
  color: var(--vscode-descriptionForeground);
  cursor: pointer;
  border-radius: 2px;
}

.item-action:hover,
:deep(.item-action:hover) {
  background: var(--vscode-list-hoverBackground);
  color: var(--vscode-foreground);
}

.favorite-star.active,
:deep(.favorite-star.active) {
  color: var(--vscode-editorWarning-foreground, #cca700);
}

.result-star {
  float: right;
  height: 20px;
}

:deep(.add-snippet) {
  color: var(--vscode-textLink-foreground) !important;
  border-style: dashed !important;
}

/* Quick type / custom snippet editor */
.snippet-editor-overlay {
  position: absolute;
  inset: 0;
  z-index: 10;
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding: 12px;
  background: rgba(0, 0, 0, 0.35);
  overflow-y: auto;
}

.snippet-editor {
  width: 100%;
  max-width: 420px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px;
  background: var(--vscode-editorWidget-background, var(--vscode-editor-background));
  border: 1px solid var(--vscode-widget-border);
  border-radius: 4px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.3);
}

.snippet-editor-title {
  margin: 0;
  overflow-wrap: anywhere;
}

.snippet-editor-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.snippet-param-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 0.8fr) minmax(0, 1.5fr) auto;
  gap: 4px;
}

.snippet-param-row button {
  padding: 0 6px;
}

.snippet-param-add {
  align-self: flex-start;
}

.snippet-editor-field select {
  width: 100%;
  padding: 4px 6px;
  background: var(--vscode-dropdown-background, var(--vscode-input-background));
  border: 1px solid var(--vscode-dropdown-border, var(--vscode-input-border, var(--vscode-widget-border)));
  color: var(--vscode-dropdown-foreground, var(--vscode-input-foreground));
  border-radius: 3px;
  font: inherit;
}

.snippet-editor-field input,
.snippet-editor-field textarea {
  width: 100%;
  padding: 4px 6px;
  background: var(--vscode-input-background);
  border: 1px solid var(--vscode-input-border, var(--vscode-widget-border));
  color: var(--vscode-input-foreground);
  border-radius: 3px;
  font: inherit;
}

.snippet-editor-field textarea {
  font-family: monospace;
  resize: vertical;
}

.snippet-editor-preview {
  padding: 6px;
  font-family: monospace;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  max-height: 120px;
  overflow-y: auto;
  border: 1px solid var(--vscode-widget-border);
  border-radius: 3px;
}

.quick-type-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.quick-type-chip {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  padding: 1px 2px 1px 6px;
  font-family: monospace;
  border: 1px solid var(--vscode-widget-border);
  border-radius: 10px;
}

.quick-type-chip button {
  padding: 0 4px;
  border: none;
  background: transparent;
  color: var(--vscode-descriptionForeground);
  cursor: pointer;
}

.quick-type-none {
  color: var(--vscode-descriptionForeground);
  font-style: italic;
}

.quick-type-add {
  display: flex;
  align-items: center;
  gap: 4px;
}

.quick-type-add input {
  flex: 1;
  font-family: monospace;
}

.quick-type-tilde {
  font-family: monospace;
}

.input-invalid {
  border-color: var(--vscode-inputValidation-errorBorder, #f14c4c) !important;
}

.snippet-editor-error {
  color: var(--vscode-errorForeground, #f48771);
}

.snippet-editor-hint {
  color: var(--vscode-descriptionForeground);
}

.snippet-editor-actions {
  display: flex;
  gap: 6px;
  align-items: center;
}

.snippet-editor-actions .spacer {
  flex: 1;
}

.search-container {
  flex-shrink: 0;
  margin: 12px;
}

.insert-scroll-container {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 0 12px;
}

/* Symbols Palette */
.symbols-palette {
  margin-bottom: 12px;
  border: 1px solid var(--vscode-widget-border);
  border-radius: 3px;
  overflow: hidden;
}

.symbols-palette-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 8px;
  cursor: pointer;
  background: var(--vscode-sideBar-background);
  font-weight: bold;
  user-select: none;
}

.symbols-palette-header:hover {
  background: var(--vscode-list-hoverBackground);
}

.symbols-palette-examples {
  font-size: var(--calcpad-font-size-icon-large);
}

.symbols-palette-arrow {
  display: inline-block;
  transition: transform 0.2s ease;
}

.symbols-palette-arrow.open {
  transform: rotate(90deg);
}

.symbols-palette-body {
  padding: 4px;
}

.symbol-group {
  margin-bottom: 2px;
}

.symbol-group-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 3px 6px;
  cursor: pointer;
  user-select: none;
  font-weight: 600;
  color: var(--vscode-descriptionForeground);
}

.symbol-group-header:hover {
  background: var(--vscode-list-hoverBackground);
}

.symbol-group-arrow {
  display: inline-block;
  font-size: var(--calcpad-font-size-icon-small);
  transition: transform 0.2s ease;
}

.symbol-group-arrow.open {
  transform: rotate(90deg);
}

.symbol-group-label {
  flex: 1;
  font-size: var(--calcpad-font-size-icon-large);
  font-weight: normal;
  color: var(--vscode-editor-foreground);
}

.symbol-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(28px, 1fr));
  gap: 2px;
  padding: 4px 4px 6px;
}

.symbol-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  aspect-ratio: 1;
  padding: 0;
  background: var(--vscode-editor-background);
  border: 1px solid var(--vscode-widget-border);
  border-radius: 2px;
  cursor: pointer;
  font-size: var(--calcpad-font-size-icon-large);
  color: var(--vscode-editor-foreground);
  line-height: 1;
}

.symbol-btn:hover {
  background: var(--vscode-list-hoverBackground);
  border-color: var(--vscode-focusBorder);
}

.insert-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.no-items {
  padding: 20px;
  text-align: center;
  color: var(--vscode-descriptionForeground);
  font-style: italic;
}

.insert-results {
  display: block;
  overflow: visible;
  height: max-content;
  width: 100%;
  padding: 4px;
  border: none;
  color: inherit;
  background: transparent;
  font: inherit;
}

.insert-result {
  display: block;
  width: 100%;
  white-space: normal;
  overflow-wrap: anywhere;
  color: inherit;
  font: inherit;
  text-align: left;
  padding: 8px;
  margin: 2px 0;
  scroll-margin: 4px;
  border: 1px solid var(--vscode-widget-border);
  border-radius: 3px;
  cursor: default;
  background: var(--vscode-editor-background);
}

.insert-result[aria-selected="true"] {
  border-color: var(--vscode-focusBorder);
}

.insert-result:focus {
  position: relative;
  z-index: 1;
  outline: 2px solid var(--vscode-focusBorder);
  outline-offset: 2px;
}

.item-display,
.item-description,
.item-category {
  display: block;
}

.item-display {
  font-weight: bold;
  color: var(--vscode-editor-foreground);
  font-family: monospace;
}

.item-description {
  color: var(--vscode-descriptionForeground);
  margin-top: 2px;
}

.item-category {
  color: var(--vscode-descriptionForeground);
  margin-top: 2px;
  opacity: 0.7;
}

.tree-placeholder {
  padding: 20px;
  text-align: center;
  color: var(--vscode-descriptionForeground);
  font-style: italic;
}

/* Tree styles */
:deep(.tree) {
  list-style: none;
  padding: 0;
  margin: 0;
}

:deep(.tree-section) {
  margin: 2px 0;
}

:deep(.tree-section > input[type="checkbox"]) {
  display: none;
}

:deep(.tree-section > label) {
  display: block;
  padding: 4px 8px;
  cursor: pointer;
  background: var(--vscode-sideBar-background);
  border: 1px solid var(--vscode-widget-border);
  border-radius: 3px;
  font-weight: bold;
  position: relative;
}

:deep(.tree-section > label:before) {
  content: '▶';
  margin-right: 6px;
  transition: transform 0.2s ease;
  display: inline-block;
}

:deep(.tree-section > input[type="checkbox"]:checked + label:before) {
  transform: rotate(90deg);
}

:deep(.tree-section > ul) {
  list-style: none;
  padding-left: 16px;
  margin: 4px 0 0 0;
  display: none;
}

:deep(.tree-section > input[type="checkbox"]:checked + label + ul) {
  display: block;
}

:deep(.tree-item) {
  display: block;
  width: 100%;
  padding: 4px 8px;
  margin: 1px 0;
  background: var(--vscode-editor-background);
  border: 1px solid var(--vscode-widget-border);
  border-radius: 2px;
  cursor: pointer;
  color: var(--vscode-editor-foreground);
  text-align: left;
}

:deep(.tree-item:hover) {
  background: var(--vscode-list-hoverBackground);
  border-color: var(--vscode-focusBorder);
}
</style>
