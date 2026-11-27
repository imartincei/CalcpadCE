<template>
  <aside class="agent-panel" :aria-label="t('{name} chat', { name: providerName() })">
    <header class="agent-header">
      <div class="agent-brand">
        <span class="agent-logo" aria-hidden="true">
          <svg viewBox="0 0 16 16"><path d="M6.5 3.25c.45 3.3 2.2 5.4 6 6.25-3.8.85-5.55 2.95-6 6.25-.45-3.3-2.2-5.4-6-6.25 3.8-.85 5.55-2.95 6-6.25M12.25.5c.25 1.75 1.05 2.75 3 3.25-1.95.5-2.75 1.5-3 3.25-.25-1.75-1.05-2.75-3-3.25 1.95-.5 2.75-1.5 3-3.25"/></svg>
        </span>
        <div class="agent-brand-text">
          <span class="agent-name">{{ providerName() }}</span>
          <span v-if="subtitle" class="agent-subtitle" :title="subtitle">{{ subtitle }}</span>
        </div>
      </div>
      <div class="agent-header-actions">
        <button
          class="agent-icon-btn agent-changes-btn"
          :class="{ active: agentChat.view === 'changes' }"
          :title="t('Changes')"
          :aria-label="t('Changes')"
          :aria-pressed="agentChat.view === 'changes'"
          @click="showChanges(agentChat.view !== 'changes')"
        >
          <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4.5 1.5a2 2 0 0 1 .75 3.85v5.3a2 2 0 1 1-1.5 0v-5.3A2 2 0 0 1 4.5 1.5m0 1.25a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5m0 9.5a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5M11.5 14.5a2 2 0 0 1-.75-3.85V7a1.25 1.25 0 0 0-1.25-1.25H8.06l.97.97a.75.75 0 0 1-1.06 1.06L5.72 5.53a.75.75 0 0 1 0-1.06l2.25-2.25a.75.75 0 0 1 1.06 1.06l-.97.97H9.5A2.75 2.75 0 0 1 12.25 7v3.65a2 2 0 0 1-.75 3.85m0-1.25a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5"/></svg>
          <span v-if="changeRows.length" class="agent-badge">{{ changeRows.length }}</span>
        </button>
        <button
          class="agent-icon-btn"
          :class="{ active: agentChat.view === 'history' }"
          :title="t('Conversation history')"
          :aria-label="t('Conversation history')"
          :aria-pressed="agentChat.view === 'history'"
          @click="showHistory(agentChat.view !== 'history')"
        >
          <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 1.5a6.5 6.5 0 1 1-6.1 8.73.75.75 0 1 1 1.4-.52A5 5 0 1 0 4.1 4.5h1.65a.75.75 0 0 1 0 1.5h-3.5a.75.75 0 0 1-.75-.75v-3.5a.75.75 0 0 1 1.5 0v1.42A6.48 6.48 0 0 1 8 1.5m0 3a.75.75 0 0 1 .75.75v2.44l1.78 1.78a.75.75 0 1 1-1.06 1.06l-2-2A.75.75 0 0 1 7.25 8V5.25A.75.75 0 0 1 8 4.5"/></svg>
        </button>
        <button class="agent-icon-btn" :title="t('New conversation')" :aria-label="t('New conversation')" @click="newConversation">
          <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 2a.75.75 0 0 1 .75.75v4.5h4.5a.75.75 0 0 1 0 1.5h-4.5v4.5a.75.75 0 0 1-1.5 0v-4.5h-4.5a.75.75 0 0 1 0-1.5h4.5v-4.5A.75.75 0 0 1 8 2"/></svg>
        </button>
        <button
          class="agent-icon-btn"
          :class="{ active: showSettings }"
          :title="t('Settings')"
          :aria-label="t('Settings')"
          :aria-expanded="showSettings"
          @click="toggleSettings"
        >
          <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2 4.25h6.1a2 2 0 0 1 3.8 0H14v1.5h-2.1a2 2 0 0 1-3.8 0H2zm8 1.5a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5M2 10.25h2.1a2 2 0 0 1 3.8 0H14v1.5H7.9a2 2 0 0 1-3.8 0H2zm4 1.5a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5"/></svg>
        </button>
        <button class="agent-icon-btn" :title="t('Close')" :aria-label="t('Close')" @click="emit('close')">
          <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M3.72 3.72a.75.75 0 0 1 1.06 0L8 6.94l3.22-3.22a.75.75 0 1 1 1.06 1.06L9.06 8l3.22 3.22a.75.75 0 1 1-1.06 1.06L8 9.06l-3.22 3.22a.75.75 0 0 1-1.06-1.06L6.94 8 3.72 4.78a.75.75 0 0 1 0-1.06"/></svg>
        </button>
      </div>
    </header>

    <div v-if="agentChat.view !== 'history'" class="agent-tabs" role="tablist" @wheel.passive="onTabsWheel">
      <div
        v-for="tab in agentChat.conversations"
        :key="tab.id"
        class="agent-tab"
        :class="{ active: tab.id === conversation.id }"
        role="tab"
        :aria-selected="tab.id === conversation.id"
        :title="tab.title || t('New conversation')"
        @click="activateConversation(tab.id)"
        @mousedown.middle.prevent="closeConversation(tab.id)"
      >
        <span
          class="agent-tab-dot"
          :class="{ busy: tab.status === 'busy' || tab.status === 'starting', attention: needsAttention(tab) }"
          aria-hidden="true"
        ></span>
        <span class="agent-tab-title">{{ tab.title || t('New conversation') }}</span>
        <button class="agent-tab-close" :title="t('Close')" :aria-label="t('Close')" @click.stop="closeConversation(tab.id)">
          <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4.47 4.47a.75.75 0 0 1 1.06 0L8 6.94l2.47-2.47a.75.75 0 1 1 1.06 1.06L9.06 8l2.47 2.47a.75.75 0 1 1-1.06 1.06L8 9.06l-2.47 2.47a.75.75 0 0 1-1.06-1.06L6.94 8 4.47 5.53a.75.75 0 0 1 0-1.06"/></svg>
        </button>
      </div>
    </div>

    <form v-if="showSettings" class="agent-settings" @submit.prevent="applySettings">
      <label class="agent-field">
        <span class="agent-field-label">{{ t('Agent') }}</span>
        <select :value="draftSettings.harness" class="agent-select" @change="onDraftHarness">
          <option v-for="harness in agentChat.harnesses" :key="harness.id" :value="harness.id">{{ harnessLabel(harness) }}</option>
        </select>
      </label>
      <label class="agent-field">
        <span class="agent-field-label">{{ t('{name} executable', { name: draftHarness?.name ?? '' }) }}</span>
        <input
          v-model="draftSettings.executable"
          type="text"
          spellcheck="false"
          :placeholder="draftHarness?.installed && !draftSettings.executable ? draftHarness.path ?? '' : t('Found automatically')"
        />
        <span v-if="draftHarness && !draftHarness.installed" class="agent-field-hint">
          {{ t('Not found. Install it ({hint}) or enter the full path to its executable.', { hint: draftHarness.installHint }) }}
        </span>
      </label>
      <label v-if="draftModelOptions.length > 1" class="agent-field">
        <span class="agent-field-label">{{ t('Model') }}</span>
        <select v-model="draftSettings.model" class="agent-select">
          <option v-for="option in draftModelOptions" :key="option.value" :value="option.value">{{ modelLabel(option) }}</option>
        </select>
      </label>
      <div class="agent-actions">
        <button type="submit" class="agent-btn primary">{{ t('Save') }}</button>
        <button type="button" class="agent-btn ghost" @click="showSettings = false">{{ t('Cancel') }}</button>
      </div>
    </form>

    <div v-if="agentChat.view === 'changes'" class="agent-history agent-changes">
      <div class="agent-changes-head">
        <span class="agent-history-title">{{ t('Changes in open conversations') }}</span>
        <template v-if="changeRows.length">
          <button class="agent-icon-btn" :title="t('Revert all')" :aria-label="t('Revert all')" @click="revertAllChanges()">
            <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M5.53 2.47a.75.75 0 0 1 0 1.06L4.06 5H9.5a4.5 4.5 0 1 1 0 9H6a.75.75 0 0 1 0-1.5h3.5a3 3 0 1 0 0-6H4.06l1.47 1.47a.75.75 0 0 1-1.06 1.06L1.72 6.28a.75.75 0 0 1 0-1.06l2.75-2.75a.75.75 0 0 1 1.06 0"/></svg>
          </button>
          <button class="agent-icon-btn" :title="t('Keep all')" :aria-label="t('Keep all')" @click="keepAllChanges()">
            <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M13.78 4.22a.75.75 0 0 1 0 1.06l-7.25 7.25a.75.75 0 0 1-1.06 0L2.22 9.28a.75.75 0 1 1 1.06-1.06L6 10.94l6.72-6.72a.75.75 0 0 1 1.06 0"/></svg>
          </button>
        </template>
      </div>
      <div v-if="!changeRows.length" class="agent-empty">
        <p>{{ t('No open conversation has changed a worksheet. Each worksheet is backed up before its first change.') }}</p>
      </div>
      <div
        v-for="change in changeRows"
        :key="change.key"
        class="agent-history-item agent-change-item"
        :class="{ closed: change.current === null }"
        role="button"
        tabindex="0"
        :title="change.current === null ? t('Closed; reverting reopens it') : t('Show changes')"
        @click="openChangeDiff(change.key)"
        @keydown.enter="openChangeDiff(change.key)"
      >
        <svg class="agent-change-icon" viewBox="0 0 16 16" aria-hidden="true"><path d="M4 1.5h5.3L13 5.2v8.3a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-11a1 1 0 0 1 1-1m.25 1.25v10.5h7.5V6H8.5V2.75zM9.75 3.3v1.45h1.45z"/></svg>
        <div class="agent-history-text">
          <span class="agent-history-name">{{ baseName(change.path ?? change.title) }}</span>
          <span class="agent-history-meta" :title="change.path ?? ''">{{ change.path ? dirName(change.path) : t('Untitled') }}</span>
        </div>
        <span v-if="change.current === null" class="agent-change-stat">{{ t('Closed') }}</span>
        <span v-else class="agent-change-stat">
          <span class="added">+{{ change.added }}</span>
          <span class="removed">−{{ change.removed }}</span>
        </span>
        <button
          class="agent-icon-btn agent-history-delete"
          :title="change.locked ? t('Running in another window') : t('Revert to backup')"
          :aria-label="t('Revert to backup')"
          :disabled="change.locked || (change.current === null && !change.path)"
          @click.stop="revertChange(change.key)"
        >
          <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M5.53 2.47a.75.75 0 0 1 0 1.06L4.06 5H9.5a4.5 4.5 0 1 1 0 9H6a.75.75 0 0 1 0-1.5h3.5a3 3 0 1 0 0-6H4.06l1.47 1.47a.75.75 0 0 1-1.06 1.06L1.72 6.28a.75.75 0 0 1 0-1.06l2.75-2.75a.75.75 0 0 1 1.06 0"/></svg>
        </button>
        <button
          class="agent-icon-btn agent-history-delete"
          :title="change.locked ? t('Running in another window') : t('Keep changes')"
          :aria-label="t('Keep changes')"
          :disabled="change.locked"
          @click.stop="keepChange(change.key)"
        >
          <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M13.78 4.22a.75.75 0 0 1 0 1.06l-7.25 7.25a.75.75 0 0 1-1.06 0L2.22 9.28a.75.75 0 1 1 1.06-1.06L6 10.94l6.72-6.72a.75.75 0 0 1 1.06 0"/></svg>
        </button>
      </div>
    </div>

    <div v-else-if="agentChat.view === 'history'" class="agent-history">
      <div class="agent-history-title">{{ t('Conversation history') }}</div>
      <div v-if="!agentChat.history.length" class="agent-empty">
        <p>{{ t('No saved conversations yet.') }}</p>
      </div>
      <div
        v-for="entry in agentChat.history"
        :key="entry.id"
        class="agent-history-item"
        :class="{ open: isOpen(entry.id) }"
        role="button"
        tabindex="0"
        @click="openFromHistory(entry.id)"
        @keydown.enter="openFromHistory(entry.id)"
      >
        <div class="agent-history-text">
          <span class="agent-history-name">{{ entry.title || t('Untitled conversation') }}</span>
          <span class="agent-history-meta">
            {{ relativeTime(entry.updatedAt) }}<template v-if="isOpen(entry.id)"> · {{ t('Open') }}</template>
          </span>
        </div>
        <button
          class="agent-icon-btn agent-history-delete"
          :title="t('Delete')"
          :aria-label="t('Delete')"
          @click.stop="deleteFromHistory(entry.id)"
        >
          <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M6.5 1.75h3a.75.75 0 0 1 .75.75V3h2.5a.75.75 0 0 1 0 1.5h-.4l-.6 8.4A1.75 1.75 0 0 1 10 14.5H6a1.75 1.75 0 0 1-1.75-1.6l-.6-8.4h-.4a.75.75 0 0 1 0-1.5h2.5v-.5a.75.75 0 0 1 .75-.75m-1.35 2.75.58 8.3c.01.13.12.2.25.2h4.04c.13 0 .24-.07.25-.2l.58-8.3z"/></svg>
        </button>
      </div>
    </div>

    <template v-else>
    <div ref="scroller" class="agent-messages" :class="{ elsewhere }">
      <div v-if="!conversation.items.length" class="agent-empty">
        <span class="agent-empty-logo" aria-hidden="true">
          <svg viewBox="0 0 16 16"><path d="M6.5 3.25c.45 3.3 2.2 5.4 6 6.25-3.8.85-5.55 2.95-6 6.25-.45-3.3-2.2-5.4-6-6.25 3.8-.85 5.55-2.95 6-6.25M12.25.5c.25 1.75 1.05 2.75 3 3.25-1.95.5-2.75 1.5-3 3.25-.25-1.75-1.05-2.75-3-3.25 1.95-.5 2.75-1.5 3-3.25"/></svg>
        </span>
        <template v-if="harness && !harness.installed">
          <p>{{ t('{name} was not found. Install it ({hint}), or set the path to its executable.', { name: harness.name, hint: harness.installHint }) }}</p>
          <button class="agent-btn" @click="openSettings">{{ t('Set path') }}</button>
        </template>
        <p v-else>{{ t('Ask {name} about your open worksheets. It can read them, check their errors, and propose edits for you to review.', { name: assistantName() }) }}</p>
      </div>

      <template v-for="(item, index) in conversation.items" :key="conversation.id + ':' + index">
        <div v-if="item.kind === 'user'" class="agent-msg user">{{ item.text }}</div>

        <div
          v-else-if="item.kind === 'assistant'"
          class="agent-msg assistant"
          :class="{ streaming: item.streaming }"
          v-html="renderChatMarkdown(item.text)"
        ></div>

        <details v-else-if="item.kind === 'tool'" class="agent-tool" :class="{ error: item.isError, running: !item.done }">
          <summary>
            <span class="agent-tool-dot" aria-hidden="true"></span>
            <span class="agent-tool-name">{{ toolLabel(item.name) }}</span>
            <span class="agent-tool-detail">{{ toolDetail(item.input) }}</span>
            <svg class="agent-chevron" viewBox="0 0 16 16" aria-hidden="true"><path d="M6 4l4 4-4 4"/></svg>
          </summary>
          <div class="agent-tool-body">
            <ul v-if="todos(item.input)" class="agent-todos">
              <li v-for="(todo, ti) in todos(item.input)" :key="ti" :class="todo.status">{{ todo.content }}</li>
            </ul>
            <pre v-else>{{ formatInput(item.input) }}</pre>
            <pre v-if="item.result" class="agent-tool-result">{{ item.result }}</pre>
          </div>
        </details>

        <div v-else-if="item.kind === 'question'" class="agent-card agent-question" :class="item.state">
          <div class="agent-card-head">
            <svg class="agent-card-icon" viewBox="0 0 16 16" aria-hidden="true"><path d="M8 1.5a6.5 6.5 0 1 1 0 13 6.5 6.5 0 0 1 0-13m0 1.25a5.25 5.25 0 1 0 0 10.5 5.25 5.25 0 0 0 0-10.5m0 7.5a.85.85 0 1 1 0 1.7.85.85 0 0 1 0-1.7M8 4.25c1.3 0 2.25.86 2.25 2 0 .9-.55 1.38-1.03 1.73-.4.3-.47.4-.47.62v.15a.75.75 0 0 1-1.5 0V8.6c0-.98.6-1.45 1.07-1.8.37-.27.43-.36.43-.55 0-.3-.28-.5-.75-.5-.4 0-.68.17-.85.5a.75.75 0 1 1-1.33-.68c.42-.83 1.2-1.32 2.18-1.32"/></svg>
            <span class="agent-card-title">{{ item.questions.length > 1 ? t('Questions') : t('Question') }}</span>
          </div>
          <div v-for="(q, qi) in item.questions" :key="qi" class="agent-question-block">
            <div class="agent-question-text">
              <span v-if="q.header" class="agent-chip">{{ q.header }}</span>
              <span>{{ q.question }}</span>
            </div>
            <template v-if="item.state === 'pending'">
              <div v-if="q.options.length" class="agent-question-options">
                <button
                  v-for="option in q.options"
                  :key="option.label"
                  class="agent-option"
                  :class="{ selected: isPicked(item, qi, option.label) }"
                  :aria-pressed="isPicked(item, qi, option.label)"
                  @click="pickOption(item, qi, option.label)"
                >
                  <span class="agent-option-label">{{ option.label }}</span>
                  <span v-if="option.description" class="agent-option-desc">{{ option.description }}</span>
                </button>
              </div>
              <input
                v-if="q.allowOther !== false"
                v-model="questionDraft(item).other[qi]"
                class="agent-question-other"
                type="text"
                :placeholder="q.placeholder || (q.options.length ? t('Other answer…') : t('Your answer…'))"
                @keydown.enter.prevent="canSubmit(item) && submitQuestions(item)"
              />
            </template>
            <div v-else-if="item.answers?.[q.question]" class="agent-question-answer">{{ item.answers[q.question] }}</div>
          </div>
          <div v-if="item.state === 'pending'" class="agent-actions">
            <button class="agent-btn primary" :disabled="!canSubmit(item)" @click="submitQuestions(item)">{{ t('Submit') }}</button>
            <button class="agent-btn ghost" @click="answerQuestions(conversation.id, item.requestId, null)">{{ t('Skip') }}</button>
          </div>
          <div v-else-if="item.state !== 'allowed'" class="agent-card-state">{{ item.state === 'denied' ? t('Skipped') : t('No longer pending') }}</div>
        </div>

        <div v-else-if="item.kind === 'approval' && isPlanTool(item.toolName)" class="agent-card agent-plan" :class="item.state">
          <div class="agent-card-head">
            <svg class="agent-card-icon" viewBox="0 0 16 16" aria-hidden="true"><path d="M5.5 1.5h5a1 1 0 0 1 1 1V3h1a1 1 0 0 1 1 1v9.5a1 1 0 0 1-1 1h-9a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h1v-.5a1 1 0 0 1 1-1m.25 1.25v1h4.5v-1zM4.25 4.25v9h7.5v-9h-.25V5a.75.75 0 0 1-.75.75h-5.5A.75.75 0 0 1 4.5 5v-.75zm6.28 2.97a.75.75 0 0 1 0 1.06l-2.5 2.5a.75.75 0 0 1-1.06 0l-1.5-1.5a.75.75 0 1 1 1.06-1.06l.97.97 1.97-1.97a.75.75 0 0 1 1.06 0"/></svg>
            <span class="agent-card-title">{{ t('Plan') }}</span>
          </div>
          <div class="agent-plan-body agent-msg assistant" v-html="renderChatMarkdown(planText(item.input))"></div>
          <div v-if="item.state === 'pending'" class="agent-actions">
            <button class="agent-btn primary" @click="approvePlan(conversation.id, item.requestId)">{{ t('Approve plan') }}</button>
            <button class="agent-btn ghost" @click="keepPlanning(conversation.id, item.requestId)">{{ t('Keep planning') }}</button>
          </div>
          <div v-else class="agent-card-state">{{ planStateLabel(item.state) }}</div>
        </div>

        <div v-else-if="item.kind === 'approval'" class="agent-card" :class="item.state">
          <div class="agent-card-head">
            <svg class="agent-card-icon" viewBox="0 0 16 16" aria-hidden="true"><path d="M8 1 2.5 3v4.2c0 3.3 2.3 6.3 5.5 7.3 3.2-1 5.5-4 5.5-7.3V3zm4.25 6.2c0 2.6-1.8 5-4.25 5.9-2.45-.9-4.25-3.3-4.25-5.9V3.9L8 2.33l4.25 1.57z"/></svg>
            <span class="agent-card-title">{{ t('Allow this action?') }}</span>
            <span class="agent-chip">{{ toolLabel(item.toolName) }}</span>
          </div>
          <pre class="agent-card-code">{{ formatInput(item.input) }}</pre>
          <div v-if="item.state === 'pending'" class="agent-actions">
            <button class="agent-btn primary" @click="answerApproval(conversation.id, item.requestId, true)">{{ t('Allow') }}</button>
            <button class="agent-btn" @click="answerApproval(conversation.id, item.requestId, true, true)">{{ t('Allow for this conversation') }}</button>
            <button class="agent-btn ghost" @click="answerApproval(conversation.id, item.requestId, false)">{{ t('Deny') }}</button>
          </div>
          <div v-else class="agent-card-state">{{ stateLabel(item.state, false) }}</div>
        </div>

        <div v-else-if="item.kind === 'edit'" class="agent-card" :class="item.state">
          <div class="agent-card-head">
            <svg class="agent-card-icon" viewBox="0 0 16 16" aria-hidden="true"><path d="M4 1.5h5.3L13 5.2v8.3a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-11a1 1 0 0 1 1-1m.25 1.25v10.5h7.5V6H8.5V2.75zM9.75 3.3v1.45h1.45z"/></svg>
            <span class="agent-card-title">{{ t('Proposed edit') }}</span>
            <span class="agent-chip" :title="item.title">{{ baseName(item.title) }}</span>
          </div>
          <div v-if="item.summary" class="agent-card-summary">{{ item.summary }}</div>
          <div class="agent-card-state">
            <template v-if="item.state === 'pending'">{{ t('Review the diff to accept or reject it.') }}</template>
            <template v-else-if="item.auto">
              {{ t('Applied automatically') }} ·
              <a href="#" class="agent-link" @click.prevent="showChanges(true)">{{ t('View changes') }}</a>
            </template>
            <template v-else>{{ stateLabel(item.state, true) }}</template>
          </div>
        </div>

        <div v-else class="agent-notice" :class="{ error: item.error }">{{ item.text }}</div>
      </template>

      <div v-if="conversation.status === 'starting' || (conversation.status === 'busy' && !isStreaming)" class="agent-thinking">
        <span class="agent-dots" aria-hidden="true"><i></i><i></i><i></i></span>
        {{ conversation.status === 'starting' ? t('Starting {name}…', { name: providerName() }) : t('Working…') }}
      </div>
    </div>

    <div class="agent-composer-wrap">
      <div v-if="elsewhere" class="agent-plan-banner">
        <span class="agent-plan-banner-text">{{ t('Running in another window. You can continue here when it finishes.') }}</span>
      </div>
      <div v-if="currentMode.edits === 'blocked'" class="agent-plan-banner">
        <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 1 2.5 3v4.2c0 3.3 2.3 6.3 5.5 7.3 3.2-1 5.5-4 5.5-7.3V3zm4.25 6.2c0 2.6-1.8 5-4.25 5.9-2.45-.9-4.25-3.3-4.25-5.9V3.9L8 2.33l4.25 1.57z"/></svg>
        <span class="agent-plan-banner-text">{{ t('Plan mode: {name} can read and plan, but not change anything.', { name: assistantName() }) }}</span>
        <button class="agent-btn ghost" @click="exitPlanMode">{{ t('Exit plan mode') }}</button>
      </div>
      <div class="agent-composer">
        <textarea
          ref="input"
          v-model="draft"
          rows="1"
          :disabled="elsewhere"
          :placeholder="t('Message {name}…', { name: assistantName() })"
          :title="t('Enter to send · Shift+Enter for a new line')"
          @input="autoSize"
          @keydown.enter.exact.prevent="submit"
          @keydown.shift.tab.prevent="cyclePermissionMode"
        ></textarea>
        <button
          v-if="isWorking"
          class="agent-send stop"
          :disabled="elsewhere"
          :title="t('Stop')"
          :aria-label="t('Stop')"
          @click="interrupt"
        >
          <svg viewBox="0 0 16 16" aria-hidden="true"><rect x="4" y="4" width="8" height="8" rx="1.5"/></svg>
        </button>
        <button
          v-else
          class="agent-send"
          :disabled="!draft.trim()"
          :title="t('Send')"
          :aria-label="t('Send')"
          @click="submit"
        >
          <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 2.5a.75.75 0 0 1 .53.22l4 4a.75.75 0 1 1-1.06 1.06L8.75 5.06v7.69a.75.75 0 0 1-1.5 0V5.06L4.53 7.78a.75.75 0 0 1-1.06-1.06l4-4A.75.75 0 0 1 8 2.5"/></svg>
        </button>
      </div>
      <div class="agent-composer-footer">
        <label class="agent-model-picker" :title="t('Agent')">
          <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2.5 3h11a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1h-11a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1m.25 1.25v7.5h10.5v-7.5zm1.72 1.47a.75.75 0 0 1 1.06 0l1.75 1.75a.75.75 0 0 1 0 1.06l-1.75 1.75a.75.75 0 1 1-1.06-1.06L5.69 8 4.47 6.78a.75.75 0 0 1 0-1.06M8 9.25h3a.75.75 0 0 1 0 1.5H8a.75.75 0 0 1 0-1.5"/></svg>
          <span class="agent-picker-label">{{ providerName() }}</span>
          <select :value="conversation.provider" :aria-label="t('Agent')" @change="onHarnessPicked">
            <option v-for="h in agentChat.harnesses" :key="h.id" :value="h.id">{{ harnessLabel(h) }}</option>
          </select>
          <svg class="agent-model-caret" viewBox="0 0 16 16" aria-hidden="true"><path d="M4 6l4 4 4-4"/></svg>
        </label>
        <label v-if="modelOptions.length > 1" class="agent-model-picker agent-model-name" :title="t('Model') + ': ' + currentModelLabel">
          <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M6.5 3.25c.45 3.3 2.2 5.4 6 6.25-3.8.85-5.55 2.95-6 6.25-.45-3.3-2.2-5.4-6-6.25 3.8-.85 5.55-2.95 6-6.25M12.25.5c.25 1.75 1.05 2.75 3 3.25-1.95.5-2.75 1.5-3 3.25-.25-1.75-1.05-2.75-3-3.25 1.95-.5 2.75-1.5 3-3.25"/></svg>
          <span class="agent-picker-label">{{ currentModelLabel }}</span>
          <select :value="conversation.model" :aria-label="t('Model')" @change="onModelPicked">
            <option v-for="option in modelOptions" :key="option.value" :value="option.value">{{ modelLabel(option) }}</option>
          </select>
          <svg class="agent-model-caret" viewBox="0 0 16 16" aria-hidden="true"><path d="M4 6l4 4 4-4"/></svg>
        </label>
        <label v-if="permissionModes().length > 1" class="agent-model-picker agent-mode-picker" :class="'edits-' + currentMode.edits" :title="modeHint + ' (Shift+Tab)'">
          <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 1 2.5 3v4.2c0 3.3 2.3 6.3 5.5 7.3 3.2-1 5.5-4 5.5-7.3V3zm4.25 6.2c0 2.6-1.8 5-4.25 5.9-2.45-.9-4.25-3.3-4.25-5.9V3.9L8 2.33l4.25 1.57z"/></svg>
          <span class="agent-picker-label">{{ t(currentMode.label) }}</span>
          <select :value="conversation.permissionMode" :aria-label="t('Permission mode')" @change="onModePicked">
            <option v-for="mode in permissionModes()" :key="mode.value" :value="mode.value">{{ t(mode.label) }}</option>
          </select>
          <svg class="agent-model-caret" viewBox="0 0 16 16" aria-hidden="true"><path d="M4 6l4 4 4-4"/></svg>
        </label>
      </div>
    </div>
    </template>
  </aside>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  activateConversation,
  activeConversation,
  activeHarness,
  agentChat,
  answerApproval,
  answerQuestions,
  approvePlan,
  exitPlanMode,
  isPlanTool,
  keepPlanning,
  harnessInfo,
  selectHarness,
  settingsFor,
  type HarnessInfo,
  closeConversation,
  changeText,
  pollChangeTexts,
  pooledChanges,
  cyclePermissionMode,
  deleteFromHistory,
  interrupt,
  keepAllChanges,
  keepChange,
  lineStats,
  modeInfo,
  modelOptions as providerModels,
  openChangeDiff,
  permissionModes,
  providerName,
  assistantName,
  revertAllChanges,
  revertChange,
  runningElsewhere,
  setPermissionMode,
  showChanges,
  type PermissionMode,
  needsAttention,
  newConversation,
  openFromHistory,
  saveSettings,
  sendMessage,
  setModel,
  showHistory,
  type ApprovalState,
  type ChatItem,
} from '../services/agent-chat'
import { renderChatMarkdown } from '../services/chat-markdown'

const emit = defineEmits<{ close: [] }>()
const { t, locale } = useI18n()

const MAX_INPUT_HEIGHT = 200
const drafts = reactive<Record<string, string>>({})
const scroller = ref<HTMLElement | null>(null)
const input = ref<HTMLTextAreaElement | null>(null)
const showSettings = ref(false)
const draftSettings = reactive({ harness: '', executable: '', model: '' })

const conversation = computed(() => activeConversation())
const draft = computed({
  get: () => drafts[conversation.value.id] ?? '',
  set: (value: string) => { drafts[conversation.value.id] = value },
})
const elsewhere = computed(() => runningElsewhere(conversation.value))
const isWorking = computed(() => conversation.value.status === 'busy' || conversation.value.status === 'starting')
const isStreaming = computed(() => {
  const items = conversation.value.items
  const last = items[items.length - 1]
  return last?.kind === 'assistant' && last.streaming
})
/** Keeps a model that is no longer listed selectable. */
function withCurrent(models: { value: string; label: string }[], current: string) {
  if (!models.length || !current || models.some(o => o.value === current)) return models
  return [...models, { value: current, label: current }]
}
const harness = computed(() => activeHarness())
const modelOptions = computed(() => withCurrent(providerModels(harness.value), conversation.value.model))
const draftHarness = computed(() => harnessInfo(draftSettings.harness))
const draftModelOptions = computed(() => withCurrent(providerModels(draftHarness.value), draftSettings.model))
const subtitle = computed(() => [conversation.value.resolvedModel, conversation.value.version && `v${conversation.value.version}`].filter(Boolean).join(' · '))

function modelLabel(option: { value: string; label: string }): string {
  return option.value === '' ? t('Default') : option.label
}

const currentMode = computed(() => modeInfo(conversation.value.permissionMode))
const modeHint = computed(() => t(currentMode.value.hint))
const currentModelLabel = computed(() => {
  const option = modelOptions.value.find(o => o.value === conversation.value.model)
  return option ? modelLabel(option) : conversation.value.model
})

function onModePicked(event: Event): void {
  void setPermissionMode((event.target as HTMLSelectElement).value as PermissionMode)
}

const liveTick = ref(0)
let liveTimer: ReturnType<typeof setInterval> | undefined
watch(() => agentChat.view === 'changes', open => {
  clearInterval(liveTimer)
  if (!open) return
  void pollChangeTexts()
  liveTimer = setInterval(() => {
    liveTick.value++
    void pollChangeTexts()
  }, 1000)
}, { immediate: true })
onBeforeUnmount(() => clearInterval(liveTimer))

const changeRows = computed(() => (void liveTick.value, pooledChanges()).map(change => {
  const current = changeText(change)
  return { ...change, current, ...(current === null ? { added: 0, removed: 0 } : lineStats(change.baseline, current)) }
}))

function dirName(path: string): string {
  const cut = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'))
  return cut > 0 ? path.slice(0, cut) : path
}

function onModelPicked(event: Event): void {
  void setModel((event.target as HTMLSelectElement).value)
}

function stateLabel(state: ApprovalState, isEdit: boolean): string {
  if (state === 'expired') return t('No longer pending')
  if (state === 'allowed') return isEdit ? t('Accepted') : t('Allowed')
  return isEdit ? t('Rejected') : t('Denied')
}

function isOpen(id: string): boolean {
  return agentChat.conversations.some(c => c.id === id)
}

function relativeTime(timestamp: number): string {
  const seconds = Math.round((timestamp - Date.now()) / 1000)
  const format = new Intl.RelativeTimeFormat(locale.value, { numeric: 'auto' })
  const steps: [Intl.RelativeTimeFormatUnit, number][] = [['second', 60], ['minute', 60], ['hour', 24], ['day', 7]]
  let value = seconds
  for (const [unit, size] of steps) {
    if (Math.abs(value) < size) return format.format(value, unit)
    value = Math.round(value / size)
  }
  return new Date(timestamp).toLocaleDateString(locale.value)
}

function onTabsWheel(event: WheelEvent): void {
  const el = event.currentTarget as HTMLElement
  if (Math.abs(event.deltaY) > Math.abs(event.deltaX)) el.scrollLeft += event.deltaY
}

function autoSize(): void {
  const el = input.value
  if (!el) return
  el.style.height = 'auto'
  el.style.height = Math.min(el.scrollHeight, MAX_INPUT_HEIGHT) + 'px'
}

function submit(): void {
  if (isWorking.value || !draft.value.trim()) return
  const text = draft.value
  draft.value = ''
  void nextTick(autoSize)
  void sendMessage(text)
}

function loadDraft(id: string): void {
  const settings = settingsFor(id)
  Object.assign(draftSettings, { harness: id, executable: settings.executable, model: settings.model })
}

function openSettings(): void {
  showSettings.value = true
  loadDraft(conversation.value.provider || agentChat.harnessId)
}

function toggleSettings(): void {
  if (showSettings.value) showSettings.value = false
  else openSettings()
}

function onDraftHarness(event: Event): void {
  loadDraft((event.target as HTMLSelectElement).value)
}

function applySettings(): void {
  void saveSettings(draftSettings.harness, { executable: draftSettings.executable, model: draftSettings.model })
  showSettings.value = false
}

function harnessLabel(h: HarnessInfo): string {
  return h.installed ? h.name : t('{name} (not found)', { name: h.name })
}

function onHarnessPicked(event: Event): void {
  selectHarness((event.target as HTMLSelectElement).value)
}

type QuestionItem = Extract<ChatItem, { kind: 'question' }>
const questionDrafts = reactive<Record<number, { picked: string[][]; other: string[] }>>({})

function questionDraft(item: QuestionItem) {
  return (questionDrafts[item.requestId] ??= { picked: item.questions.map(() => []), other: item.questions.map(() => '') })
}

function isPicked(item: QuestionItem, qi: number, label: string): boolean {
  return questionDraft(item).picked[qi]?.includes(label) ?? false
}

function answerFor(item: QuestionItem, qi: number): string {
  const draft = questionDraft(item)
  return draft.other[qi]?.trim() || draft.picked[qi].join(', ')
}

function canSubmit(item: QuestionItem): boolean {
  return item.questions.every((_, qi) => answerFor(item, qi))
}

function submitQuestions(item: QuestionItem): void {
  const answers: Record<string, string> = {}
  item.questions.forEach((q, qi) => { answers[q.question] = answerFor(item, qi) })
  void answerQuestions(conversation.value.id, item.requestId, answers)
}

/** A lone single-choice question is answered by its click. */
function pickOption(item: QuestionItem, qi: number, label: string): void {
  const draft = questionDraft(item)
  const question = item.questions[qi]
  draft.other[qi] = ''
  if (question.multiSelect) {
    const picked = draft.picked[qi]
    draft.picked[qi] = picked.includes(label) ? picked.filter(l => l !== label) : [...picked, label]
    return
  }
  draft.picked[qi] = [label]
  if (item.questions.length === 1) submitQuestions(item)
}

function todos(input: unknown): { content: string; status: string }[] | null {
  const list = input && typeof input === 'object' ? (input as Record<string, unknown>).todos : null
  return Array.isArray(list) && list.length ? list as { content: string; status: string }[] : null
}

function planText(input: unknown): string {
  const plan = input && typeof input === 'object' ? (input as Record<string, unknown>).plan : null
  return typeof plan === 'string' ? plan : JSON.stringify(input, null, 2)
}

function planStateLabel(state: ApprovalState): string {
  if (state === 'allowed') return t('Plan approved')
  if (state === 'denied') return t('Kept planning')
  return t('No longer pending')
}

function toolLabel(name: string): string {
  if (isPlanTool(name)) return t('Plan')
  if (name === 'TodoWrite') return t('Todos')
  if (name === 'Task' || name === 'Agent') return t('Subagent')
  if (name === 'AskUserQuestion') return t('Question')
  if (name === 'EnterPlanMode') return t('Enter plan mode')
  return name.replace(/^mcp__calcpad__/, '').replace(/^mcp__([^_]+)__/, '$1: ')
}

function toolDetail(input: unknown): string {
  if (!input || typeof input !== 'object') return ''
  const o = input as Record<string, unknown>
  const list = todos(input)
  if (list) return t('{done} of {total} done', { done: list.filter(td => td.status === 'completed').length, total: list.length })
  const value = o.command ?? o.file_path ?? o.filePath ?? o.path ?? o.pattern ?? o.url ?? o.query ?? o.description ?? o.summary
  return typeof value === 'string' ? value.split('\n')[0] : ''
}

/** Write and edit tools (Claude's snake_case, opencode's camelCase) shown as the file plus text. */
function fileChangeText(o: Record<string, unknown>): string | null {
  const str = (...keys: string[]) => keys.map(k => o[k]).find((v): v is string => typeof v === 'string')
  const path = str('file_path', 'filePath', 'path')
  if (!path) return null
  const content = str('content')
  if (content !== undefined) return `${path}\n\n${content}`
  const before = str('old_string', 'oldString')
  const after = str('new_string', 'newString')
  if (before === undefined || after === undefined) return null
  const lines = (text: string, mark: string) => text.split('\n').map(l => `${mark} ${l}`).join('\n')
  return `${path}\n\n${lines(before, '-')}\n${lines(after, '+')}`
}

function formatInput(input: unknown): string {
  if (input && typeof input === 'object' && typeof (input as Record<string, unknown>).plan === 'string') {
    return (input as Record<string, string>).plan
  }
  const fileChange = input && typeof input === 'object' ? fileChangeText(input as Record<string, unknown>) : null
  if (fileChange) return fileChange
  if (input && typeof input === 'object' && typeof (input as Record<string, unknown>).command === 'string') {
    return (input as Record<string, string>).command
  }
  return JSON.stringify(input, null, 2)
}

function baseName(path: string): string {
  return path.split(/[\\/]/).pop() || path
}

function scrollToBottom(): void {
  void nextTick(() => {
    if (scroller.value) scroller.value.scrollTop = scroller.value.scrollHeight
  })
}

watch(
  () => {
    const items = conversation.value.items
    return [items.length, (items[items.length - 1] as { text?: string } | undefined)?.text]
  },
  async () => {
    const el = scroller.value
    if (!el) return
    const nearBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 80
    await nextTick()
    if (nearBottom) el.scrollTop = el.scrollHeight
  },
)

watch(() => [conversation.value.id, agentChat.view], () => {
  scrollToBottom()
  void nextTick(autoSize)
})

defineExpose({ focus: () => input.value?.focus() })
</script>

<style scoped>
.agent-panel {
  --agent-accent: var(--vscode-button-background, #0e639c);
  --agent-accent-fg: var(--vscode-button-foreground, #fff);
  /* CalcpadCE logo blues */
  --agent-brand: #118bd1;
  --agent-brand-light: #22a7ef;
  --agent-brand-dark: #016fb3;
  --agent-border: var(--vscode-widget-border, rgba(128, 128, 128, 0.25));
  --agent-muted: var(--vscode-descriptionForeground, rgba(204, 204, 204, 0.7));
  --agent-surface: color-mix(in srgb, var(--vscode-foreground) 5%, transparent);
  --agent-surface-strong: color-mix(in srgb, var(--vscode-foreground) 9%, transparent);
  --agent-radius: 10px;
  --agent-mono: var(--vscode-editor-font-family, ui-monospace, monospace);
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  min-width: 0;
  height: 100%;
  background: var(--vscode-sideBar-background, var(--vscode-editor-background));
  color: var(--vscode-foreground);
  border-left: 1px solid var(--agent-border);
  font-size: 13px;
}

/* Opts out of the app's global button padding/background without outranking the rules below. */
:where(.agent-panel) button {
  padding: 0;
  border: none;
  background: none;
  color: inherit;
  font: inherit;
}
:where(.agent-panel) svg {
  flex-shrink: 0;
  fill: currentColor;
}

.agent-header {
  display: flex;
  align-items: center;
  gap: 8px;
  min-height: 44px;
  padding: 6px 8px 6px 14px;
  border-bottom: 1px solid var(--agent-border);
}
.agent-brand {
  display: flex;
  flex: 1;
  align-items: center;
  gap: 10px;
  min-width: 0;
}
.agent-logo {
  display: grid;
  place-items: center;
  width: 26px;
  height: 26px;
  border-radius: 8px;
  background: linear-gradient(135deg, var(--agent-brand-light), var(--agent-brand-dark));
  color: #fff;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.25);
}
.agent-logo svg {
  width: 14px;
  height: 14px;
}
.agent-brand-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
  line-height: 1.2;
}
.agent-name {
  font-weight: 600;
  letter-spacing: 0.01em;
}
.agent-subtitle {
  overflow: hidden;
  color: var(--agent-muted);
  font-size: 11px;
  white-space: nowrap;
  text-overflow: ellipsis;
}
.agent-header-actions {
  display: flex;
  gap: 2px;
}
.agent-icon-btn {
  display: grid;
  place-items: center;
  width: 28px;
  height: 28px;
  border-radius: 6px;
  color: var(--agent-muted);
  cursor: pointer;
  transition: background-color 0.15s ease, color 0.15s ease;
}
.agent-icon-btn svg {
  width: 16px;
  height: 16px;
}
.agent-panel .agent-icon-btn:hover,
.agent-panel .agent-icon-btn.active {
  background: var(--agent-surface-strong);
  color: var(--vscode-foreground);
}

.agent-settings {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin: 10px 12px 0;
  padding: 12px;
  border: 1px solid var(--agent-border);
  border-radius: var(--agent-radius);
  background: var(--agent-surface);
}
.agent-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.agent-field-label {
  color: var(--agent-muted);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}
.agent-field-hint {
  color: var(--agent-muted);
  font-size: 11px;
  line-height: 1.4;
}
.agent-field input {
  width: 100%;
  padding: 6px 8px;
  border: 1px solid var(--vscode-input-border, var(--agent-border));
  border-radius: 6px;
  outline: none;
  background: var(--vscode-input-background, transparent);
  color: var(--vscode-input-foreground, inherit);
  font: inherit;
}
.agent-field input:focus {
  border-color: var(--vscode-focusBorder, var(--agent-accent));
}

.agent-messages {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 14px;
  min-height: 0;
  padding: 16px 14px;
  overflow-y: auto;
}

.agent-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  margin: auto 12px;
  color: var(--agent-muted);
  text-align: center;
  line-height: 1.5;
}
.agent-empty-logo {
  display: grid;
  place-items: center;
  width: 44px;
  height: 44px;
  border-radius: 14px;
  background: linear-gradient(135deg, var(--agent-brand-light), var(--agent-brand-dark));
  color: #fff;
  box-shadow: 0 4px 14px rgba(1, 111, 179, 0.35);
}
.agent-empty-logo svg {
  width: 22px;
  height: 22px;
}

.agent-msg {
  overflow-wrap: anywhere;
  line-height: 1.55;
  -webkit-user-select: text;
  user-select: text;
}
.agent-msg.user {
  align-self: flex-end;
  max-width: 88%;
  padding: 8px 12px;
  border-radius: 14px 14px 4px 14px;
  background: color-mix(in srgb, var(--agent-accent) 22%, transparent);
  border: 1px solid color-mix(in srgb, var(--agent-accent) 35%, transparent);
  white-space: pre-wrap;
}
.agent-msg.assistant :deep(p) {
  margin: 0 0 8px;
}
.agent-msg.assistant :deep(p:last-child),
.agent-msg.assistant :deep(ul:last-child),
.agent-msg.assistant :deep(ol:last-child),
.agent-msg.assistant :deep(pre:last-child) {
  margin-bottom: 0;
}
.agent-msg.assistant :deep(ul),
.agent-msg.assistant :deep(ol) {
  margin: 0 0 8px;
  padding-left: 20px;
}
.agent-msg.assistant :deep(li) {
  margin: 2px 0;
}
.agent-msg.assistant :deep(.chat-heading) {
  margin-top: 4px;
  font-weight: 600;
}
.agent-msg.assistant :deep(code) {
  padding: 1px 5px;
  border-radius: 4px;
  background: var(--agent-surface-strong);
  font-family: var(--agent-mono);
  font-size: 0.92em;
}
.agent-msg.assistant :deep(pre) {
  margin: 0 0 8px;
  padding: 10px 12px;
  overflow-x: auto;
  border: 1px solid var(--agent-border);
  border-radius: 8px;
  background: var(--vscode-textCodeBlock-background, var(--agent-surface));
}
.agent-msg.assistant :deep(pre code) {
  padding: 0;
  background: none;
}
.agent-msg.streaming::after {
  content: '';
  display: inline-block;
  width: 7px;
  height: 14px;
  margin-left: 2px;
  vertical-align: -2px;
  border-radius: 1px;
  background: currentColor;
  animation: agent-blink 1s steps(2) infinite;
}
.agent-msg.streaming :deep(p:last-child) {
  display: inline;
}

.agent-tool {
  border-radius: 8px;
  font-size: 12px;
}
.agent-tool + .agent-tool {
  margin-top: -12px;
}
.agent-tool summary {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 5px 8px;
  border-radius: 8px;
  color: var(--agent-muted);
  cursor: pointer;
  list-style: none;
  transition: background-color 0.15s ease;
}
.agent-tool summary::-webkit-details-marker {
  display: none;
}
.agent-tool summary:hover,
.agent-tool[open] summary {
  background: var(--agent-surface);
}
.agent-tool-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--vscode-gitDecoration-addedResourceForeground, #73c991);
  flex-shrink: 0;
}
.agent-tool.running .agent-tool-dot {
  background: var(--vscode-progressBar-background, var(--agent-accent));
  animation: agent-pulse 1.2s ease-in-out infinite;
}
.agent-tool.error .agent-tool-dot {
  background: var(--vscode-errorForeground, #f48771);
}
.agent-tool-name {
  color: var(--vscode-foreground);
  font-weight: 600;
  white-space: nowrap;
}
.agent-tool-detail {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  font-family: var(--agent-mono);
  white-space: nowrap;
  text-overflow: ellipsis;
}
.agent-chevron {
  width: 12px;
  height: 12px;
  fill: none !important;
  stroke: currentColor;
  stroke-width: 1.6;
  stroke-linecap: round;
  stroke-linejoin: round;
  transition: transform 0.15s ease;
}
.agent-tool[open] .agent-chevron {
  transform: rotate(90deg);
}
.agent-tool-body {
  padding: 4px 0 2px 23px;
}
.agent-tool pre,
.agent-card-code {
  max-height: 220px;
  margin: 4px 0 0;
  padding: 8px 10px;
  overflow: auto;
  border: 1px solid var(--agent-border);
  border-radius: 8px;
  background: var(--vscode-textCodeBlock-background, var(--agent-surface));
  font-family: var(--agent-mono);
  font-size: 12px;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  -webkit-user-select: text;
  user-select: text;
}
.agent-tool-result {
  color: var(--agent-muted);
}

.agent-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px;
  border: 1px solid color-mix(in srgb, var(--agent-accent) 55%, transparent);
  border-radius: var(--agent-radius);
  background: color-mix(in srgb, var(--agent-accent) 8%, transparent);
  box-shadow: 0 2px 10px rgba(0, 0, 0, 0.12);
}
.agent-card.allowed,
.agent-card.denied {
  border-color: var(--agent-border);
  background: var(--agent-surface);
  box-shadow: none;
}
.agent-card-head {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.agent-card-icon {
  width: 16px;
  height: 16px;
  color: var(--vscode-textLink-foreground, var(--agent-accent));
}
.agent-card-title {
  font-weight: 600;
  white-space: nowrap;
}
.agent-chip {
  min-width: 0;
  overflow: hidden;
  padding: 1px 8px;
  border-radius: 999px;
  background: var(--agent-surface-strong);
  font-family: var(--agent-mono);
  font-size: 11.5px;
  white-space: nowrap;
  text-overflow: ellipsis;
}
.agent-card-code {
  margin: 0;
}
.agent-card-summary {
  line-height: 1.45;
}
.agent-question-block {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.agent-question-text {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 6px;
  line-height: 1.45;
  overflow-wrap: anywhere;
}
.agent-question-options {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.agent-option {
  display: flex;
  flex-direction: column;
  gap: 1px;
  padding: 6px 10px;
  border: 1px solid var(--agent-border);
  border-radius: 8px;
  background: var(--agent-surface);
  color: var(--vscode-foreground);
  font: inherit;
  text-align: left;
  cursor: pointer;
}
.agent-option:hover {
  background: var(--agent-surface-strong);
}
.agent-option.selected {
  border-color: var(--agent-brand);
  background: color-mix(in srgb, var(--agent-brand) 14%, transparent);
}
.agent-option-label {
  font-weight: 600;
}
.agent-option-desc {
  color: var(--agent-muted);
  font-size: 12px;
}
.agent-question-other {
  padding: 5px 8px;
  border: 1px solid var(--vscode-input-border, var(--agent-border));
  border-radius: 6px;
  background: var(--vscode-input-background, transparent);
  color: var(--vscode-input-foreground, inherit);
  font: inherit;
}
.agent-question-other:focus {
  outline: none;
  border-color: var(--vscode-focusBorder, var(--agent-brand));
}
.agent-question-answer {
  padding-left: 10px;
  border-left: 2px solid var(--agent-brand);
  font-weight: 600;
  overflow-wrap: anywhere;
}
.agent-todos {
  margin: 4px 0 0;
  padding-left: 0;
  list-style: none;
}
.agent-todos li {
  position: relative;
  padding: 1px 0 1px 18px;
  line-height: 1.45;
}
.agent-todos li::before {
  content: '';
  position: absolute;
  top: 6px;
  left: 2px;
  width: 9px;
  height: 9px;
  border: 1.5px solid var(--agent-muted);
  border-radius: 3px;
}
.agent-todos li.in_progress::before {
  border-color: var(--agent-brand-light);
  background: color-mix(in srgb, var(--agent-brand) 35%, transparent);
}
.agent-todos li.completed {
  color: var(--agent-muted);
  text-decoration: line-through;
}
.agent-todos li.completed::before {
  border-color: var(--agent-brand);
  background: var(--agent-brand);
}
.agent-plan-body {
  max-height: 360px;
  overflow: auto;
  padding: 2px 2px 2px 0;
  line-height: 1.5;
  overflow-wrap: anywhere;
}
.agent-plan-body :deep(pre) {
  white-space: pre-wrap;
}
.agent-plan-banner {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
  padding: 4px 4px 4px 10px;
  border: 1px solid color-mix(in srgb, var(--agent-brand) 45%, transparent);
  border-radius: 8px;
  background: color-mix(in srgb, var(--agent-brand) 10%, transparent);
  font-size: 12px;
}
.agent-plan-banner svg {
  flex-shrink: 0;
  width: 12px;
  height: 12px;
  fill: var(--agent-brand-light);
}
.agent-plan-banner-text {
  flex: 1;
  min-width: 0;
}
.agent-plan-banner .agent-btn {
  flex-shrink: 0;
}
.agent-messages.elsewhere :is(.agent-actions, .agent-question-options, .agent-question-other) {
  pointer-events: none;
  opacity: 0.5;
}
.agent-card-state {
  color: var(--agent-muted);
  font-size: 12px;
}
.agent-card.allowed .agent-card-state::before {
  content: '✓ ';
  color: var(--vscode-gitDecoration-addedResourceForeground, #73c991);
}
.agent-card.denied .agent-card-state::before {
  content: '✕ ';
  color: var(--vscode-errorForeground, #f48771);
}

.agent-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.agent-panel .agent-btn {
  padding: 5px 12px;
  border: 1px solid var(--agent-border);
  border-radius: 6px;
  background: var(--agent-surface-strong);
  cursor: pointer;
  transition: background-color 0.15s ease, border-color 0.15s ease, filter 0.15s ease;
}
.agent-panel .agent-btn:hover {
  background: color-mix(in srgb, var(--vscode-foreground) 14%, transparent);
}
.agent-panel .agent-btn.primary {
  border-color: transparent;
  background: var(--agent-accent);
  color: var(--agent-accent-fg);
}
.agent-panel .agent-btn.primary:hover {
  background: var(--vscode-button-hoverBackground, var(--agent-accent));
}
.agent-panel .agent-btn.ghost {
  border-color: transparent;
  background: transparent;
}
.agent-panel .agent-btn.ghost:hover {
  background: var(--agent-surface-strong);
}

.agent-notice {
  align-self: center;
  max-width: 100%;
  color: var(--agent-muted);
  font-size: 12px;
  text-align: center;
  white-space: pre-wrap;
}
.agent-notice.error {
  align-self: stretch;
  padding: 8px 12px;
  border: 1px solid color-mix(in srgb, var(--vscode-errorForeground, #f48771) 45%, transparent);
  border-radius: 8px;
  background: color-mix(in srgb, var(--vscode-errorForeground, #f48771) 10%, transparent);
  color: var(--vscode-errorForeground, #f48771);
  text-align: left;
}

.agent-thinking {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--agent-muted);
  font-size: 12px;
}
.agent-dots {
  display: inline-flex;
  gap: 3px;
}
.agent-dots i {
  width: 5px;
  height: 5px;
  border-radius: 50%;
  background: currentColor;
  animation: agent-bounce 1.2s ease-in-out infinite;
}
.agent-dots i:nth-child(2) {
  animation-delay: 0.15s;
}
.agent-dots i:nth-child(3) {
  animation-delay: 0.3s;
}

.agent-composer-wrap {
  padding: 10px 12px 8px;
}
.agent-composer {
  display: flex;
  align-items: flex-end;
  gap: 8px;
  padding: 8px 8px 8px 12px;
  border: 1px solid var(--vscode-input-border, var(--agent-border));
  border-radius: 12px;
  background: var(--vscode-input-background, var(--agent-surface));
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}
.agent-composer:focus-within {
  border-color: var(--vscode-focusBorder, var(--agent-accent));
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--vscode-focusBorder, #007acc) 22%, transparent);
}
.agent-composer textarea {
  flex: 1;
  min-height: 22px;
  max-height: 200px;
  padding: 3px 0;
  border: none;
  outline: none;
  background: transparent;
  color: var(--vscode-input-foreground, inherit);
  font: inherit;
  line-height: 1.45;
  resize: none;
}
.agent-composer-footer {
  display: flex;
  align-items: center;
  justify-content: flex-start;
  gap: 8px;
  min-width: 0;
  margin-top: 6px;
  padding: 0 2px;
}
.agent-model-picker {
  position: relative;
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  gap: 5px;
  padding: 2px 6px;
  border-radius: 6px;
  color: var(--agent-muted);
  cursor: pointer;
  transition: background-color 0.15s ease, color 0.15s ease;
}
.agent-model-picker:hover,
.agent-model-picker:focus-within {
  background: var(--agent-surface-strong);
  color: var(--vscode-foreground);
}
.agent-model-picker svg {
  width: 11px;
  height: 11px;
}
.agent-model-picker .agent-model-caret {
  width: 10px;
  height: 10px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.8;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.agent-picker-label {
  font-size: 12px;
  font-weight: 600;
  white-space: nowrap;
}
.agent-model-name {
  flex-shrink: 1;
  min-width: 0;
}
.agent-model-name .agent-picker-label {
  max-width: 16ch;
  overflow: hidden;
  text-overflow: ellipsis;
}
.agent-model-picker select {
  position: absolute;
  inset: 0;
  width: 100%;
  opacity: 0;
  appearance: none;
  padding: 0;
  border: none;
  outline: none;
  background: transparent;
  color: inherit;
  font: inherit;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
}
.agent-model-picker select:not([multiple]):not([size]) {
  appearance: none;
}
.agent-model-picker select option {
  background: var(--vscode-dropdown-background, var(--vscode-editor-background));
  color: var(--vscode-dropdown-foreground, var(--vscode-foreground));
}
.agent-select {
  width: 100%;
  padding: 6px 8px;
  border: 1px solid var(--vscode-input-border, var(--agent-border));
  border-radius: 6px;
  outline: none;
  background: var(--vscode-dropdown-background, var(--vscode-input-background, transparent));
  color: var(--vscode-dropdown-foreground, inherit);
  font: inherit;
}
.agent-select:focus {
  border-color: var(--vscode-focusBorder, var(--agent-accent));
}
.agent-composer textarea::placeholder {
  color: var(--agent-muted);
}
.agent-panel .agent-send {
  display: grid;
  place-items: center;
  width: 30px;
  height: 30px;
  border-radius: 8px;
  background: var(--agent-accent);
  color: var(--agent-accent-fg);
  cursor: pointer;
  transition: background-color 0.15s ease, opacity 0.15s ease;
}
.agent-panel .agent-send:hover:not(:disabled) {
  background: var(--vscode-button-hoverBackground, var(--agent-accent));
}
.agent-panel .agent-send:disabled {
  background: var(--agent-surface-strong);
  color: var(--agent-muted);
  opacity: 1;
}
.agent-panel .agent-send.stop {
  background: var(--agent-surface-strong);
  color: var(--vscode-foreground);
}
.agent-panel .agent-send.stop:disabled {
  color: var(--agent-muted);
  cursor: default;
}
.agent-composer textarea:disabled {
  cursor: not-allowed;
  opacity: 0.6;
}
.agent-send svg {
  width: 16px;
  height: 16px;
}

.agent-tabs {
  display: flex;
  gap: 2px;
  padding: 6px 8px 0;
  overflow-x: auto;
  border-bottom: 1px solid var(--agent-border);
  scrollbar-width: none;
}
.agent-tabs::-webkit-scrollbar {
  display: none;
}
.agent-tab {
  display: flex;
  flex: 0 1 auto;
  align-items: center;
  gap: 6px;
  min-width: 72px;
  max-width: 180px;
  margin-bottom: -1px;
  padding: 5px 4px 6px 10px;
  border: 1px solid transparent;
  border-bottom: none;
  border-radius: 8px 8px 0 0;
  color: var(--agent-muted);
  font-size: 12px;
  cursor: pointer;
  transition: background-color 0.15s ease, color 0.15s ease;
}
.agent-tab:hover {
  background: var(--agent-surface);
  color: var(--vscode-foreground);
}
.agent-tab.active {
  border-color: var(--agent-border);
  background: var(--vscode-sideBar-background, var(--vscode-editor-background));
  color: var(--vscode-foreground);
}
.agent-tab-dot {
  flex-shrink: 0;
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: transparent;
}
.agent-tab-dot.busy {
  background: var(--vscode-progressBar-background, var(--agent-accent));
  animation: agent-pulse 1.2s ease-in-out infinite;
}
.agent-tab-dot.attention {
  background: var(--agent-brand);
  animation: none;
}
.agent-tab-title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}
.agent-panel .agent-tab-close {
  display: grid;
  flex-shrink: 0;
  place-items: center;
  width: 18px;
  height: 18px;
  border-radius: 4px;
  color: var(--agent-muted);
  opacity: 0;
  cursor: pointer;
}
.agent-tab-close svg {
  width: 12px;
  height: 12px;
}
.agent-tab:hover .agent-tab-close,
.agent-tab.active .agent-tab-close {
  opacity: 1;
}
.agent-panel .agent-tab-close:hover {
  background: var(--agent-surface-strong);
  color: var(--vscode-foreground);
}

.agent-history {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 2px;
  min-height: 0;
  padding: 12px 8px;
  overflow-y: auto;
}
.agent-history-title {
  padding: 0 8px 8px;
  color: var(--agent-muted);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}
.agent-history-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 6px 8px 10px;
  border-radius: 8px;
  cursor: pointer;
  outline: none;
  transition: background-color 0.15s ease;
}
.agent-history-item:hover,
.agent-history-item:focus-visible {
  background: var(--agent-surface-strong);
}
.agent-history-item.open {
  box-shadow: inset 2px 0 0 var(--agent-accent);
}
.agent-history-text {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.agent-history-name {
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}
.agent-history-meta {
  color: var(--agent-muted);
  font-size: 11px;
}
.agent-history-delete {
  opacity: 0;
}
.agent-history-item:hover .agent-history-delete,
.agent-history-item:focus-within .agent-history-delete {
  opacity: 1;
}
.agent-card.expired {
  border-color: var(--agent-border);
  background: var(--agent-surface);
  box-shadow: none;
  opacity: 0.75;
}

.agent-changes-btn {
  position: relative;
}
.agent-badge {
  position: absolute;
  top: 1px;
  right: 0;
  min-width: 14px;
  height: 14px;
  padding: 0 3px;
  border-radius: 7px;
  background: var(--agent-brand);
  color: #fff;
  font-size: 9.5px;
  font-weight: 700;
  line-height: 14px;
  text-align: center;
}
.agent-changes-head {
  display: flex;
  align-items: center;
  gap: 2px;
  padding-right: 4px;
}
.agent-changes-head .agent-history-title {
  flex: 1;
  padding-bottom: 0;
}
.agent-changes-head + .agent-empty,
.agent-changes-head + .agent-change-item {
  margin-top: 6px;
}
.agent-change-icon {
  width: 14px;
  height: 14px;
  color: var(--vscode-gitDecoration-modifiedResourceForeground, #e2c08d);
}
.agent-change-item.closed .agent-change-icon,
.agent-change-item.closed .agent-history-name {
  opacity: 0.6;
}
.agent-change-stat {
  display: flex;
  gap: 6px;
  color: var(--agent-muted);
  font-family: var(--agent-mono);
  font-size: 11px;
  white-space: nowrap;
}
.agent-change-stat .added {
  color: var(--vscode-gitDecoration-addedResourceForeground, #73c991);
}
.agent-change-stat .removed {
  color: var(--vscode-errorForeground, #f48771);
}
.agent-link {
  color: var(--vscode-textLink-foreground, #3794ff);
  text-decoration: none;
}
.agent-link:hover {
  text-decoration: underline;
}
.agent-mode-picker.edits-auto {
  color: var(--agent-brand-light);
}
.agent-mode-picker.edits-blocked {
  color: var(--vscode-textLink-foreground, #3794ff);
}

@keyframes agent-blink {
  to {
    visibility: hidden;
  }
}
@keyframes agent-pulse {
  50% {
    opacity: 0.35;
  }
}
@keyframes agent-bounce {
  0%, 80%, 100% {
    opacity: 0.35;
    transform: translateY(0);
  }
  40% {
    opacity: 1;
    transform: translateY(-3px);
  }
}
@media (prefers-reduced-motion: reduce) {
  .agent-msg.streaming::after,
  .agent-tool.running .agent-tool-dot,
  .agent-dots i {
    animation: none;
  }
}
</style>
