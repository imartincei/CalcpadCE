<template>
  <div class="insert-tab">
    <div class="search-container">
      <input
        v-model="searchTerm"
        type="text"
        placeholder="Search items..."
        class="search-input"
      />
    </div>

    <!-- Symbols Palette -->
    <div v-if="symbolGroups.length > 0" class="symbols-palette">
      <div class="symbols-palette-header" @click="symbolsPaletteOpen = !symbolsPaletteOpen">
        <span class="symbols-palette-arrow" :class="{ open: symbolsPaletteOpen }">&#x25B6;</span>
        <span class="symbols-palette-title">Symbol Palette</span>
      </div>
      <div v-show="symbolsPaletteOpen" class="symbols-palette-body">
        <div v-for="group in symbolGroups" :key="group.name" class="symbol-group">
          <div class="symbol-group-header" @click="toggleGroup(group.name)">
            <span class="symbol-group-arrow" :class="{ open: isGroupOpen(group.name) }">&#x25B6;</span>
            <span class="symbol-group-label">{{ group.name }}</span>
          </div>
          <div v-show="isGroupOpen(group.name)" class="symbol-grid">
            <button
              v-for="item in group.items"
              :key="item.tag"
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

    <div class="image-insert-container">
      <button @click="insertImage" class="image-insert-btn" title="Insert an image from file">
        Insert Image
      </button>
    </div>

    <div v-if="searchTerm && filteredItems.length === 0" class="no-items">
      No items found for "{{ searchTerm }}"
    </div>
    <div v-else-if="displayItems.length === 0" class="no-items">
      No insert items available
    </div>
    <div v-else-if="searchTerm" class="search-results">
      <div
        v-for="item in displayItems"
        :key="item.tag"
        @click="insertItem(item)"
        class="insert-item"
        :title="buildTooltip(item)"
      >
        <div class="item-display">{{ formatDisplayText(item) }}</div>
        <div v-if="formatSecondaryText(item)" class="item-description">
          {{ formatSecondaryText(item) }}
        </div>
        <div v-if="item.categoryPath" class="item-category">
          {{ item.categoryPath }}
        </div>
      </div>
    </div>
    <div v-else class="tree-view">
      <div class="tree-actions">
        <button @click="collapseAll" class="collapse-btn">
          Collapse All
        </button>
      </div>
      <div id="tree-container" class="tree-container">
        <!-- Tree structure will be built here -->
        <div class="tree-placeholder">
          Loading insert items...
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import type { InsertItem } from '../types'
import { replaceParameterPlaceholders } from '../../text/snippet-insert'

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
const symbolsPaletteOpen = ref(false)
// All symbol groups start collapsed; users open them on demand.
const openSymbolGroups = ref<Set<string>>(new Set())

const toggleGroup = (name: string) => {
  const next = new Set(openSymbolGroups.value)
  if (next.has(name)) next.delete(name)
  else next.add(name)
  openSymbolGroups.value = next
}

const isGroupOpen = (name: string): boolean => openSymbolGroups.value.has(name)

// Symbol palette helpers
interface SymbolGroup {
  name: string
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
  return Array.from(groups.entries())
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([name, items]) => ({
      name,
      items: items.sort((a, b) => (a.description || '').localeCompare(b.description || ''))
    }))
})

// Computed
const filteredItems = computed(() => {
  if (!searchTerm.value.trim()) {
    return []
  }

  const term = searchTerm.value.toLowerCase()

  const itemMatches = props.insertItems.filter((item: InsertItem) =>
    item.label?.toLowerCase().includes(term) ||
    item.tag?.toLowerCase().includes(term) ||
    item.description?.toLowerCase().includes(term)
  )

  const categoryMatches = props.insertItems.filter((item: InsertItem) =>
    item.categoryPath?.toLowerCase().includes(term) &&
    !itemMatches.includes(item)
  )

  return [...itemMatches, ...categoryMatches]
})

const displayItems = computed(() => {
  if (searchTerm.value.trim()) {
    return filteredItems.value
  }
  return props.insertItems
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
const MARKUP_CATEGORIES = new Set(['HTML', 'Metadata Properties', 'SVG', 'Markdown'])

const isMarkupItem = (item: InsertItem): boolean =>
  MARKUP_CATEGORIES.has((item.categoryPath ?? '').split(' > ')[0])

// Format display text (uses label if available, falls back to tag).
// Appends (~shortcut) for items with quick typing support.
const formatDisplayText = (item: InsertItem): string => {
  const name = isMarkupItem(item)
    ? item.label || item.description || item.tag
    : item.label || item.tag
  const display = replaceParameterPlaceholders(name, item)
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
const insertImage = () => {
  emit('insertImage')
}

const insertItem = (item: InsertItem) => {
  emit('insertText', formatInsertText(item))
}

const collapseAll = () => {
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

const buildTreeStructureRecursive = (data: TreeNode, parentUl: HTMLUListElement, level: number) => {
  if (level >= 5) return // Prevent infinite recursion

  Object.keys(data).sort((a, b) => a.localeCompare(b)).forEach(categoryKey => {
    const categoryData = data[categoryKey]

    if (Array.isArray(categoryData)) {
      const { li, ul } = createTreeSection(categoryKey, level)

      const sorted = [...categoryData].sort((a, b) =>
        formatDisplayText(a).localeCompare(formatDisplayText(b))
      )
      sorted.forEach((item: InsertItem) => {
        const itemLi = createTreeItem(item)
        ul.appendChild(itemLi)
      })

      parentUl.appendChild(li)
    } else if (typeof categoryData === 'object' && categoryData !== null) {
      const { li, ul } = createTreeSection(categoryKey, level)
      buildTreeStructureRecursive(categoryData as TreeNode, ul, level + 1)
      parentUl.appendChild(li)
    }
  })
}

const createTreeSection = (title: string, level: number) => {
  const checkboxId = 'checkbox-' + Date.now() + '-' + Math.random()
  const levelClass = level > 0 ? ' level-' + level : ''

  const li = document.createElement('li')
  li.className = 'tree-section' + levelClass

  const checkbox = document.createElement('input')
  checkbox.type = 'checkbox'
  checkbox.id = checkboxId
  checkbox.checked = false // Default to collapsed

  const label = document.createElement('label')
  label.setAttribute('for', checkboxId)
  label.textContent = title

  // Add click handler to label to toggle checkbox
  label.addEventListener('click', (e) => {
    e.preventDefault()
    checkbox.checked = !checkbox.checked
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
  padding: 12px;
  height: 100%;
  display: flex;
  flex-direction: column;
}

.search-container {
  margin-bottom: 12px;
}

.search-input {
  width: 100%;
  padding: 8px;
  background: var(--vscode-input-background);
  border: 1px solid var(--vscode-input-border);
  color: var(--vscode-input-foreground);
  border-radius: 3px;
  font-size: 12px;
  font-family: var(--vscode-font-family);
}

.search-input:focus {
  outline: none;
  border-color: var(--vscode-focusBorder);
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
  font-size: 12px;
  user-select: none;
}

.symbols-palette-header:hover {
  background: var(--vscode-list-hoverBackground);
}

.symbols-palette-arrow {
  display: inline-block;
  font-size: 8px;
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
  font-size: 11px;
  font-weight: 600;
  color: var(--vscode-descriptionForeground);
}

.symbol-group-header:hover {
  background: var(--vscode-list-hoverBackground);
}

.symbol-group-arrow {
  display: inline-block;
  font-size: 8px;
  transition: transform 0.2s ease;
}

.symbol-group-arrow.open {
  transform: rotate(90deg);
}

.symbol-group-label {
  flex: 1;
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
  font-size: 14px;
  color: var(--vscode-editor-foreground);
  line-height: 1;
}

.symbol-btn:hover {
  background: var(--vscode-list-hoverBackground);
  border-color: var(--vscode-focusBorder);
}

.image-insert-container {
  margin-bottom: 12px;
}

.image-insert-btn {
  width: 100%;
  padding: 6px 8px;
  background: var(--vscode-button-background);
  color: var(--vscode-button-foreground);
  border: none;
  border-radius: 3px;
  cursor: pointer;
  font-size: 12px;
  font-family: var(--vscode-font-family);
}

.image-insert-btn:hover {
  background: var(--vscode-button-hoverBackground);
}

.no-items {
  padding: 20px;
  text-align: center;
  color: var(--vscode-descriptionForeground);
  font-style: italic;
}

.search-results {
  flex: 1;
  overflow-y: auto;
}

.insert-item {
  padding: 8px;
  margin: 2px 0;
  border: 1px solid var(--vscode-widget-border);
  border-radius: 3px;
  cursor: pointer;
  background: var(--vscode-editor-background);
  transition: border-color 0.2s ease;
}

.insert-item:hover {
  border-color: var(--vscode-focusBorder);
  background: var(--vscode-list-hoverBackground);
}

.item-display {
  font-weight: bold;
  color: var(--vscode-editor-foreground);
  font-size: 12px;
  font-family: monospace;
}

.item-description {
  font-size: 11px;
  color: var(--vscode-descriptionForeground);
  margin-top: 2px;
}

.item-category {
  font-size: 10px;
  color: var(--vscode-descriptionForeground);
  margin-top: 2px;
  opacity: 0.7;
}

.tree-view {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.tree-actions {
  display: flex;
  justify-content: flex-end;
  margin-bottom: 8px;
}

.collapse-btn {
  background: var(--vscode-button-background);
  color: var(--vscode-button-foreground);
  border: none;
  padding: 4px 8px;
  border-radius: 3px;
  cursor: pointer;
  font-size: 11px;
}

.collapse-btn:hover {
  background: var(--vscode-button-hoverBackground);
}

.tree-container {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
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
  font-size: 11px;
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
  font-size: 11px;
  color: var(--vscode-editor-foreground);
  text-align: left;
}

:deep(.tree-item:hover) {
  background: var(--vscode-list-hoverBackground);
  border-color: var(--vscode-focusBorder);
}
</style>
