<template>
  <div class="insert-tab">
    <CalcpadSearchField
      ref="searchField"
      v-model="searchTerm"
      :placeholder="t('Search items... (Alt+I)')"
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

const { t } = useI18n()

// Props
interface Props {
  insertItems: InsertItem[]
}

const props = defineProps<Props>()

// Emits
const emit = defineEmits<{
  insertText: [text: string]
  insertImage: []
}>()

// State
const searchTerm = ref('')
const searchField = ref<InstanceType<typeof CalcpadSearchField> | null>(null)
const searchResults = ref<HTMLDivElement | null>(null)
const selectedResultIndex = ref(-1)
const searchQuery = computed(() => searchTerm.value.trim())
defineExpose({ focusSearch: () => searchField.value!.focus() })

const STORAGE_KEY = 'calcpad.insert.panels'
const panelState = reactive({
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
  for (const item of props.insertItems) {
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
  props.insertItems.map((item, index) => [item, index])
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

  return props.insertItems
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
  return props.insertItems
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
  if (item.quickType) {
    return display + ' (~' + item.quickType + ')'
  }
  return display
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

  if (item.quickType) {
    lines.push('')
    lines.push('Quick type: ~' + item.quickType)
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

  if (props.insertItems.length === 0) {
    treeContainer.innerHTML = '<div class="tree-placeholder">No insert data available</div>'
    return
  }

  const treeRoot = document.createElement('ul')
  treeRoot.className = 'tree'
  treeContainer.appendChild(treeRoot)

  const treeData = buildTreeFromItems(props.insertItems)
  buildTreeStructureRecursive(treeData, treeRoot, 0)
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

const createTreeSection = (title: string, level: number, categoryPath: string[]) => {
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
  label.textContent = title

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

const createTreeItem = (item: InsertItem) => {
  const li = document.createElement('li')

  const button = document.createElement('button')
  button.className = 'tree-item'

  // Use description for tooltip with parameter breakdown
  button.title = buildTooltip(item)

  // Use insert (or label) as display text with § replaced by param names
  button.textContent = formatDisplayText(item)

  button.addEventListener('click', () => {
    insertItem(item)
  })

  li.appendChild(button)
  return li
}

// Watch for insertItems changes
watch(
  () => props.insertItems,
  () => {
    nextTick(() => {
      buildTreeStructure()
    })
  },
  { immediate: true, deep: true }
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
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
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
