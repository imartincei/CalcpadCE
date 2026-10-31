<template>
  <div ref="rootEl" class="settings-tab" :class="{ 'search-active': searchActive }">
    <div class="settings-toolbar">
      <CalcpadSearchField
        v-model="searchQuery"
        class="settings-search"
        placeholder="Search settings..."
      />
      <CollapseAllButton v-if="!searchActive" :expand="allCollapsed" @click="setAll(!allCollapsed)" />
    </div>

    <div class="settings-container">
      <section v-show="sectionVisible('math')" class="settings-section">
        <h3 class="section-header" @click="toggle('math')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('math') }">&#x25BC;</span>
          <CalcpadSearchHighlight text="Math Settings" :query="query" />
        </h3>
        <div v-show="bodyVisible('math')" class="section-body">
          <div
            v-for="spec in MATH_KEYS"
            :key="spec.key"
            v-show="rowVisible('math', spec.key)"
            class="setting-group"
          >
            <label v-if="spec.type === 'boolean'">
              <input
                type="checkbox"
                v-model="settingModel(spec.key).value"
                @change="updateSettings"
              />
              <CalcpadSearchHighlight :text="spec.label" :query="query" />
              <span v-if="spec.detail" class="setting-info" :title="spec.detail">ⓘ</span>
            </label>
            <template v-else>
              <label :for="spec.key">
                <CalcpadSearchHighlight :text="spec.label" :query="query" />:
                <span v-if="spec.detail" class="setting-info" :title="spec.detail">ⓘ</span>
              </label>
              <select
                v-if="spec.type === 'enum'"
                :id="spec.key"
                v-model="settingModel(spec.key).value"
                @change="updateSettings"
              >
                <option v-for="opt in spec.options" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
              </select>
              <input
                v-else-if="spec.type === 'number'"
                :id="spec.key"
                type="number"
                v-model="settingModel(spec.key).value"
                :min="spec.min"
                :max="spec.max"
                :step="numberStep(spec.key)"
                :class="{ 'input-invalid': settingErrors[spec.key] }"
                :title="settingErrors[spec.key] || undefined"
                @input="updateSettings"
              />
              <input
                v-else
                :id="spec.key"
                type="text"
                v-model="settingModel(spec.key).value"
                @input="updateSettings"
              />
            </template>
          </div>
        </div>
      </section>

      <section v-show="sectionVisible('plot')" class="settings-section">
        <h3 class="section-header" @click="toggle('plot')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('plot') }">&#x25BC;</span>
          <CalcpadSearchHighlight text="Plot Settings" :query="query" />
        </h3>
        <div v-show="bodyVisible('plot')" class="section-body">
          <div
            v-for="spec in PLOT_KEYS_A"
            :key="spec.key"
            v-show="rowVisible('plot', spec.key)"
            class="setting-group"
          >
            <label>
              <input type="checkbox" v-model="settingModel(spec.key).value" @change="updateSettings" />
              <CalcpadSearchHighlight :text="spec.label" :query="query" />
              <span v-if="spec.detail" class="setting-info" :title="spec.detail">ⓘ</span>
            </label>
          </div>

          <div v-show="rowVisible('plot', 'screenScale')" class="setting-group">
            <label for="screenScaleFactor">
              <CalcpadSearchHighlight text="Screen Scale Factor:" :query="query" />
            </label>
            <input
              id="screenScaleFactor"
              v-model.number="localSettings.plot.screenScaleFactor"
              type="number"
              min="0.1"
              max="5"
              step="0.1"
              @input="updateSettings"
            />
          </div>

          <div
            v-for="spec in PLOT_KEYS_B"
            :key="spec.key"
            v-show="rowVisible('plot', spec.key)"
            class="setting-group"
          >
            <label v-if="spec.type === 'boolean'">
              <input type="checkbox" v-model="settingModel(spec.key).value" @change="updateSettings" />
              <CalcpadSearchHighlight :text="spec.label" :query="query" />
              <span v-if="spec.detail" class="setting-info" :title="spec.detail">ⓘ</span>
            </label>
            <template v-else>
              <label :for="spec.key">
                <CalcpadSearchHighlight :text="spec.label" :query="query" />:
                <span v-if="spec.detail" class="setting-info" :title="spec.detail">ⓘ</span>
              </label>
              <select
                v-if="spec.type === 'enum'"
                :id="spec.key"
                v-model="settingModel(spec.key).value"
                @change="updateSettings"
              >
                <option v-for="opt in spec.options" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
              </select>
              <input
                v-else
                :id="spec.key"
                type="text"
                v-model="settingModel(spec.key).value"
                @input="updateSettings"
              />
            </template>
          </div>

          <div v-show="rowVisible('plot', 'lightDirection')" class="setting-group">
            <label for="lightDirection">
              <CalcpadSearchHighlight text="Light Direction:" :query="query" />
            </label>
            <select
              id="lightDirection"
              v-model="localSettings.plot.lightDirection"
              @change="updateSettings"
            >
              <option value="NorthWest">NorthWest</option>
              <option value="North">North</option>
              <option value="NorthEast">NorthEast</option>
              <option value="West">West</option>
              <option value="East">East</option>
              <option value="SouthWest">SouthWest</option>
              <option value="South">South</option>
              <option value="SouthEast">SouthEast</option>
            </select>
          </div>

          <div
            v-for="spec in PLOT_KEYS_C"
            :key="spec.key"
            v-show="rowVisible('plot', spec.key)"
            class="setting-group"
          >
            <label :for="spec.key">
              <CalcpadSearchHighlight :text="spec.label" :query="query" />:
              <span v-if="spec.detail" class="setting-info" :title="spec.detail">ⓘ</span>
            </label>
            <input
              :id="spec.key"
              type="number"
              v-model="settingModel(spec.key).value"
              :min="spec.min"
              :max="spec.max"
              :class="{ 'input-invalid': settingErrors[spec.key] }"
              :title="settingErrors[spec.key] || undefined"
              @input="updateSettings"
            />
          </div>
        </div>
      </section>

      <section v-show="sectionVisible('units')" class="settings-section">
        <h3 class="section-header" @click="toggle('units')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('units') }">&#x25BC;</span>
          <CalcpadSearchHighlight text="Units" :query="query" />
        </h3>
        <div v-show="bodyVisible('units')" class="section-body">
          <div v-show="rowVisible('units', 'isUs')" class="setting-group">
            <label for="nonMetricUnits">
              <CalcpadSearchHighlight text="Non-Metric Units:" :query="query" />
              <span class="setting-info" title="Selects US or UK definitions for bare unit names that differ between the two systems (gal, ton, cwt, pt, qt, bbl, tonf, therm, etc.).">ⓘ</span>
            </label>
            <select
              id="nonMetricUnits"
              v-model="localSettings.isUs"
              @change="updateSettings"
            >
              <option :value="false">UK (Imperial)</option>
              <option :value="true">US Customary</option>
            </select>
          </div>
        </div>
      </section>

      <section v-show="sectionVisible('pdf')" class="settings-section">
        <h3 class="section-header" @click="toggle('pdf')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('pdf') }">&#x25BC;</span>
          <CalcpadSearchHighlight text="PDF Export" :query="query" />
        </h3>
        <div v-show="bodyVisible('pdf')" class="section-body">
          <div
            v-for="spec in pdfKeys"
            :key="spec.key"
            v-show="rowVisible('pdf', spec.key)"
            class="setting-group"
          >
            <label :for="'pdf-' + spec.key">
              <CalcpadSearchHighlight :text="spec.label" :query="query" />:
              <span class="setting-info" :title="spec.detail">ⓘ</span>
            </label>
            <select
              v-if="spec.type === 'boolean'"
              :id="'pdf-' + spec.key"
              v-model="localPdfSettings[spec.key]"
              @change="updatePdfSettings"
            >
              <option :value="true">Yes</option>
              <option :value="false">No</option>
            </select>
            <select
              v-else-if="spec.type === 'enum'"
              :id="'pdf-' + spec.key"
              v-model="localPdfSettings[spec.key]"
              @change="updatePdfSettings"
            >
              <option v-for="opt in spec.options" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
            </select>
            <input
              v-else
              :id="'pdf-' + spec.key"
              type="text"
              v-model="localPdfSettings[spec.key]"
              :class="{ 'input-invalid': pdfErrors[spec.key] }"
              :title="pdfErrors[spec.key] || undefined"
              @input="updatePdfSettings"
            />
            <span v-if="pdfErrors[spec.key]" class="setting-error">{{ pdfErrors[spec.key] }}</span>
          </div>

          <div v-show="rowVisible('pdf', 'reset')" class="settings-actions">
            <button @click="resetPdfSettings" class="reset-button">
              <CalcpadSearchHighlight text="Reset PDF Settings" :query="query" />
            </button>
          </div>
        </div>
      </section>

      <section v-show="sectionVisible('user')" class="settings-section">
        <h3 class="section-header" @click="toggle('user')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('user') }">&#x25BC;</span>
          User
        </h3>
        <div v-show="bodyVisible('user')" class="section-body">
          <div v-show="rowVisible('user', 'userName')" class="setting-group">
            <label for="userName">
              User Name:
              <span class="setting-info" title="Recorded in a file's info (Info tab) as its author, who last updated it, and who reviewed it.">ⓘ</span>
            </label>
            <input
              id="userName"
              v-model="userName"
              type="text"
              placeholder="e.g. jsmith"
              @change="updateUserName"
            />
          </div>
        </div>
      </section>

      <section v-show="sectionVisible('editor')" class="settings-section">
        <h3 class="section-header" @click="toggle('editor')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('editor') }">&#x25BC;</span>
          <CalcpadSearchHighlight text="Editor" :query="query" />
        </h3>
        <div v-show="bodyVisible('editor')" class="section-body">
          <div v-show="rowVisible('editor', 'quickTyping')" class="setting-group">
            <label>
              <input
                v-model="enableQuickTyping"
                type="checkbox"
                @change="updateQuickTyping"
              />
              <CalcpadSearchHighlight text="Enable Quick Typing" :query="query" />
              <span class="setting-info" title="Type shortcuts like ~a → α, ~' → ′">ⓘ</span>
            </label>
          </div>

          <div v-show="rowVisible('editor', 'commentFormat')" class="setting-group">
            <label for="commentFormat">
              <CalcpadSearchHighlight text="Comment Format:" :query="query" />
            </label>
            <select
              id="commentFormat"
              v-model="commentFormat"
              @change="updateCommentFormat"
            >
              <option value="auto">Auto (detect #md on/off)</option>
              <option value="html">HTML</option>
              <option value="markdown">Markdown</option>
            </select>
          </div>

          <div v-show="rowVisible('editor', 'formattingHotkeys')" class="setting-group">
            <label>
              <input
                v-model="enableFormattingHotkeys"
                type="checkbox"
                @change="updateFormattingHotkeys"
              />
              <CalcpadSearchHighlight text="Enable Formatting Hotkeys" :query="query" />
              <span class="setting-info" title="Ctrl+B for bold, Ctrl+I for italic, etc.">ⓘ</span>
            </label>
          </div>

          <div v-show="rowVisible('editor', 'previewCursorSync')" class="setting-group">
            <label>
              <input
                v-model="enablePreviewCursorSync"
                type="checkbox"
                @change="updatePreviewCursorSync"
              />
              <CalcpadSearchHighlight text="Sync Preview to Cursor Line" :query="query" />
              <span class="setting-info" title="Scroll the preview to follow the line the cursor is on in the editor.">ⓘ</span>
            </label>
          </div>

          <div v-show="rowVisible('editor', 'autoRun')" class="setting-group">
            <label>
              <input
                v-model="enableAutoRun"
                type="checkbox"
                @change="updateAutoRun"
              />
              <CalcpadSearchHighlight text="Auto-Run Preview" :query="query" />
              <span class="setting-info" title="When off, the preview only re-renders when it is first opened or a manual run is triggered.">ⓘ</span>
            </label>
          </div>

          <div v-if="!versionConfig.isWeb" v-show="rowVisible('editor', 'autoInputMode')" class="setting-group">
            <label>
              <input
                v-model="enableAutoInputMode"
                type="checkbox"
                @change="updateAutoInputMode"
              />
              <CalcpadSearchHighlight text="Open #UI Documents in Input Mode" :query="query" />
              <span class="setting-info" title="A document declaring #UI controls opens as its input form the first time you open it.">ⓘ</span>
            </label>
          </div>

          <div v-show="rowVisible('editor', 'previewUiOverrides')" class="setting-group">
            <label>
              <input
                v-model="enablePreviewUiOverrides"
                type="checkbox"
                @change="updatePreviewUiOverrides"
              />
              <CalcpadSearchHighlight text="Apply #UI Values in Preview" :query="query" />
              <span class="setting-info" title="Preview normally shows the document's own values. Turn this on to render it with the values entered into the input form instead, for tracking down an error that only appears once a form is filled in.">ⓘ</span>
            </label>
          </div>

          <div v-show="rowVisible('editor', 'showFileMetadata')" class="setting-group">
            <label>
              <input
                v-model="showFileMetadata"
                type="checkbox"
                @change="updateShowFileMetadata"
              />
              <CalcpadSearchHighlight text="Show File Metadata" :query="query" />
              <span class="setting-info" title="Show the file info and saved #UI values comments at the end of the file. They are hidden by default and edited in the Properties tab.">ⓘ</span>
            </label>
          </div>

          <div v-if="versionConfig.isWebOrDesktop" v-show="rowVisible('editor', 'completionDelayMs')" class="setting-group">
            <label for="completionDelayMs">
              <CalcpadSearchHighlight text="Completion Delay (ms):" :query="query" />
              <span class="setting-info" title="How long to wait before showing code suggestions while typing. Zero shows them immediately.">ⓘ</span>
            </label>
            <input
              id="completionDelayMs"
              v-model.number="completionDelayMs"
              type="number"
              min="0"
              step="1"
              required
              @change="updateCompletionDelayMs"
            />
          </div>

          <div v-if="versionConfig.isDesktop" v-show="rowVisible('editor', 'fontFamily')" class="setting-group">
            <label for="editorFontFamily">
              <CalcpadSearchHighlight text="Font Family:" :query="query" />
              <span class="setting-info" title="JuliaMono is the bundled default. Drop .woff2/.woff/.ttf/.otf files into the fonts folder to make them available here.">ⓘ</span>
            </label>
            <select
              id="editorFontFamily"
              v-model="editorFontFamily"
              @mousedown="requestFontRescan"
              @focus="requestFontRescan"
              @change="updateEditorFontFamily"
            >
              <option value="JuliaMono">JuliaMono (default)</option>
              <option value="system">System Default</option>
              <optgroup v-if="userFontOptions.length" label="From fonts folder">
                <option
                  v-for="name in userFontOptions"
                  :key="name"
                  :value="name"
                >{{ name }}</option>
              </optgroup>
              <option
                v-if="editorFontFamily && !isKnownFont"
                :value="editorFontFamily"
              >{{ editorFontFamily }} (missing)</option>
            </select>
          </div>
          <div v-if="versionConfig.isDesktop" v-show="rowVisible('editor', 'fontsFolder')" class="setting-group">
            <button
              class="diagnostics-button"
              title="Open the folder where custom fonts can be dropped. Reopen the Font Family picker to pick up new fonts."
              @click="openFontsFolder"
            >
              <CalcpadSearchHighlight text="Open Fonts Folder" :query="query" />
            </button>
          </div>

          <div v-show="rowVisible('editor', 'previewTheme')" class="setting-group">
            <label for="previewTheme">
              <CalcpadSearchHighlight text="Preview Theme:" :query="query" />
            </label>
            <select
              id="previewTheme"
              v-model="previewTheme"
              @change="updatePreviewTheme"
            >
              <option value="system">System</option>
              <option value="light">Light</option>
              <option value="dark">Dark</option>
            </select>
          </div>

          <div v-show="rowVisible('editor', 'darkBackground')" class="setting-group">
            <label for="darkBackground">
              <CalcpadSearchHighlight text="Dark Mode Background:" :query="query" />
            </label>
            <div class="color-input-row">
              <input
                id="darkBackground"
                v-model="darkBackground"
                type="text"
                placeholder="#1e1e1e"
                @input="updateDarkBackground"
              />
              <button
                title="Reset to default (#1e1e1e)"
                @click="resetDarkBackground"
              >
                Reset
              </button>
            </div>
          </div>

          <div v-show="rowVisible('editor', 'colorTheme')" class="setting-group">
            <label for="colorTheme">
              <CalcpadSearchHighlight text="Color Theme:" :query="query" />
            </label>
            <select
              id="colorTheme"
              v-model="colorTheme"
              @change="updateColorTheme"
            >
              <option
                v-if="colorTheme && !knownThemeLabels.has(colorTheme) && colorTheme !== 'System'"
                :value="colorTheme"
              >{{ colorTheme }}</option>
              <option value="System">System</option>
              <optgroup v-if="darkThemes.length" label="Dark">
                <option
                  v-for="t in darkThemes"
                  :key="t.label"
                  :value="t.label"
                >{{ t.label }}</option>
              </optgroup>
              <optgroup v-if="lightThemes.length" label="Light">
                <option
                  v-for="t in lightThemes"
                  :key="t.label"
                  :value="t.label"
                >{{ t.label }}</option>
              </optgroup>
            </select>
          </div>

          <div v-show="rowVisible('editor', 'linterMinSeverity')" class="setting-group">
            <label for="linterMinSeverity">
              <CalcpadSearchHighlight text="Linter Minimum Severity:" :query="query" />
            </label>
            <select
              id="linterMinSeverity"
              v-model="linterMinSeverity"
              @change="updateLinterMinSeverity"
            >
              <option value="error">Error</option>
              <option value="warning">Warning</option>
              <option value="information">Information (all)</option>
            </select>
          </div>
        </div>
      </section>

      <section v-show="sectionVisible('formatting')" class="settings-section">
        <h3 class="section-header" @click="toggle('formatting')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('formatting') }">&#x25BC;</span>
          <CalcpadSearchHighlight text="Formatting" :query="query" />
        </h3>
        <div v-show="bodyVisible('formatting')" class="section-body">
          <slot name="formatting"></slot>
        </div>
      </section>

      <section v-show="sectionVisible('server')" class="settings-section">
        <h3 class="section-header" @click="toggle('server')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('server') }">&#x25BC;</span>
          <CalcpadSearchHighlight text="Server Settings" :query="query" />
        </h3>
        <div v-show="bodyVisible('server')" class="section-body">
          <div v-show="rowVisible('server', 'url')" class="setting-group">
            <label for="serverUrl">
              <CalcpadSearchHighlight text="Remote Server URL:" :query="query" />
            </label>
            <input
              id="serverUrl"
              v-model="localSettings.server.url"
              type="text"
              @input="updateSettings"
            />
          </div>
        </div>
      </section>

      <section v-show="sectionVisible('diagnostics')" class="settings-section">
        <h3 class="section-header" @click="toggle('diagnostics')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('diagnostics') }">&#x25BC;</span>
          <CalcpadSearchHighlight text="Diagnostics" :query="query" />
        </h3>
        <div v-show="bodyVisible('diagnostics')" class="section-body">
          <div v-show="rowVisible('diagnostics', 'logsFolder')" class="setting-group">
            <button
              class="diagnostics-button"
              title="Opens the folder containing server logs and the most recent crash dump."
              @click="openLogsFolder"
            >
              <CalcpadSearchHighlight text="Open Logs Folder" :query="query" />
            </button>
          </div>

          <div v-show="rowVisible('diagnostics', 'logLevel')" class="setting-group">
            <label for="logLevel">
              <CalcpadSearchHighlight text="Log Level:" :query="query" />
              <span class="setting-info" :title="logLevelDetail">&#9432;</span>
            </label>
            <select
              id="logLevel"
              v-model="logLevel"
              @change="updateLogLevel"
            >
              <option
                v-for="opt in LOG_LEVEL_OPTIONS"
                :key="opt.value"
                :value="opt.value"
              >{{ opt.label }}</option>
            </select>
          </div>

          <div v-if="versionConfig.isWebOrDesktop" v-show="rowVisible('diagnostics', 'maxOutputLines')" class="setting-group">
            <label for="maxOutputLines">
              <CalcpadSearchHighlight text="Max Output Lines (per channel):" :query="query" />
              <span class="setting-info" title="Lines retained in each Output channel before older lines are dropped. Lower values reduce memory use and improve responsiveness when logs are noisy.">ⓘ</span>
            </label>
            <input
              id="maxOutputLines"
              v-model.number="maxOutputLines"
              type="number"
              min="10"
              max="100000"
              step="100"
              @change="updateMaxOutputLines"
            />
          </div>

          <div v-show="rowVisible('diagnostics', 'maxPreviewSize')" class="setting-group">
            <label for="maxPreviewSize">
              <CalcpadSearchHighlight text="Max Preview Size (MB):" :query="query" />
              <span class="setting-info" title="A worksheet that renders to more HTML than this is not shown in the preview — showing it risks running the app out of memory. Exporting to PDF, HTML or DOCX is unaffected. Raise it only if you need very large documents on screen.">ⓘ</span>
            </label>
            <input
              id="maxPreviewSize"
              v-model.number="maxPreviewSizeMB"
              type="number"
              :min="MIN_PREVIEW_SIZE_MB"
              :max="MAX_PREVIEW_SIZE_MB"
              step="8"
              @change="updateMaxPreviewSize"
            />
          </div>

          <div v-show="rowVisible('diagnostics', 'maxPreviewConsoleMessages')" class="setting-group">
            <label for="maxPreviewConsoleMessages">
              <CalcpadSearchHighlight text="Max Preview Console Messages:" :query="query" />
              <span class="setting-info" title="How many console lines one preview render may relay before the rest are dropped. A worksheet whose scripts log in a loop can otherwise flood the console channel. Raise it when debugging a script, lower it when a library is noisy. Each line is clipped to 4 KB regardless.">ⓘ</span>
            </label>
            <input
              id="maxPreviewConsoleMessages"
              v-model.number="maxPreviewConsoleMessages"
              type="number"
              :min="MIN_CONSOLE_MESSAGES_PER_DOCUMENT"
              :max="MAX_CONSOLE_MESSAGES_PER_DOCUMENT"
              step="100"
              @change="updateMaxPreviewConsoleMessages"
            />
          </div>
        </div>
      </section>

      <section v-show="sectionVisible('configuration')" class="settings-section">
        <h3 class="section-header" @click="toggle('configuration')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('configuration') }">&#x25BC;</span>
          <CalcpadSearchHighlight text="Configuration" :query="query" />
        </h3>
        <div v-show="bodyVisible('configuration')" class="section-body">
          <div v-show="rowVisible('configuration', 'activeConfig')" class="setting-group">
            <label for="activeConfig">
              <CalcpadSearchHighlight text="Active Config:" :query="query" />
            </label>
            <select
              id="activeConfig"
              :value="activeConfig"
              @change="switchConfig(($event.target as HTMLSelectElement).value)"
            >
              <option
                v-for="name in availableConfigs"
                :key="name"
                :value="name"
              >{{ name }}</option>
            </select>
          </div>

          <div v-show="rowVisible('configuration', 'saveConfig')" class="setting-group">
            <label for="configName">
              <CalcpadSearchHighlight text="Save current settings as:" :query="query" />
            </label>
            <div class="color-input-row">
              <input
                id="configName"
                v-model="newConfigName"
                type="text"
                placeholder="e.g. my-config"
                @keyup.enter="saveNamedConfig"
              />
              <button
                :disabled="!newConfigName.trim()"
                @click="saveNamedConfig"
              >
                Save
              </button>
            </div>
            <span v-if="saveError" class="setting-error">{{ saveError }}</span>
          </div>

          <div v-show="rowVisible('configuration', 'actions')" class="settings-actions">
            <button @click="openSettingsFolder" class="reset-button">
              <CalcpadSearchHighlight text="Open Settings Folder" :query="query" />
            </button>
            <button @click="resetSettings" class="reset-button">
              <CalcpadSearchHighlight text="Reset to Default" :query="query" />
            </button>
          </div>
        </div>
      </section>

      <div v-if="searchActive && !anyVisible" class="no-results">
        No settings found for "{{ searchQuery.trim() }}"
      </div>

      <div v-if="appVersion" class="about-footer">
        <span class="app-version">CalcpadCE v{{ appVersion }}</span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, computed, reactive, nextTick } from 'vue'
import CalcpadSearchField from './CalcpadSearchField.vue'
import CalcpadSearchHighlight from './CalcpadSearchHighlight.vue'
import CollapseAllButton from './CollapseAllButton.vue'
import type { WritableComputedRef } from 'vue'
import type { PdfSettings, Settings, ThemeInfo, VersionConfig } from '../types'
import { DEFAULT_PDF_SETTINGS, DEFAULT_VERSION_CONFIG } from '../types'
import { getDefaultSettings, METADATA_SETTINGS_KEYS, validateSettingValue, SETTINGS_PATH, LOG_LEVEL_OPTIONS, DEFAULT_COMPLETION_DELAY_MS } from '../../types/settings'
import { PDF_SETTING_KEYS, validatePdfValue } from '../../types/pdf-settings'
import { specForKey } from '../../types/catalog'
import {
  DEFAULT_PREVIEW_SIZE_MB, MIN_PREVIEW_SIZE_MB, MAX_PREVIEW_SIZE_MB,
  DEFAULT_CONSOLE_MESSAGES_PER_DOCUMENT, MIN_CONSOLE_MESSAGES_PER_DOCUMENT,
  MAX_CONSOLE_MESSAGES_PER_DOCUMENT,
} from '../../services/preview-limits'

// Props
interface Props {
  settings?: Settings
  initialPreviewTheme?: string
  initialColorTheme?: string
  initialAvailableThemes?: ThemeInfo[]
  initialEnableQuickTyping?: boolean
  initialCommentFormat?: string
  initialEnableFormattingHotkeys?: boolean
  initialEnablePreviewCursorSync?: boolean
  initialEnableAutoRun?: boolean
  initialEnableAutoInputMode?: boolean
  initialEnablePreviewUiOverrides?: boolean
  initialShowFileMetadata?: boolean
  initialDarkBackground?: string
  initialLinterMinSeverity?: string
  initialLogLevel?: string
  initialMaxOutputLines?: number
  initialMaxPreviewSize?: number
  initialMaxPreviewConsoleMessages?: number
  versionConfig?: VersionConfig
  initialActiveConfig?: string
  initialAvailableConfigs?: string[]
  initialEditorFontFamily?: string
  initialCompletionDelayMs?: number
  initialAvailableFonts?: string[]
  initialUserName?: string
  /** A setting to bring into view and focus, by its section and input id. */
  focusRequest?: { section: string; id: string } | null
  appVersion?: string
  pdfSettings?: PdfSettings
}

const props = withDefaults(defineProps<Props>(), {
  settings: () => getDefaultSettings(),
  initialPreviewTheme: 'system',
  initialColorTheme: '',
  initialAvailableThemes: () => [],
  initialEnableQuickTyping: true,
  initialCommentFormat: 'auto',
  initialEnableFormattingHotkeys: true,
  initialEnablePreviewCursorSync: false,
  initialEnableAutoRun: true,
  initialEnableAutoInputMode: true,
  initialEnablePreviewUiOverrides: false,
  initialShowFileMetadata: false,
  initialDarkBackground: '#1a1a2e',
  initialLinterMinSeverity: 'information',
  initialLogLevel: 'warning',
  initialMaxOutputLines: 1000,
  initialMaxPreviewSize: DEFAULT_PREVIEW_SIZE_MB,
  initialMaxPreviewConsoleMessages: DEFAULT_CONSOLE_MESSAGES_PER_DOCUMENT,
  versionConfig: () => ({ ...DEFAULT_VERSION_CONFIG }),
  initialActiveConfig: 'default',
  initialAvailableConfigs: () => ['default'],
  initialEditorFontFamily: 'JuliaMono',
  initialCompletionDelayMs: DEFAULT_COMPLETION_DELAY_MS,
  initialAvailableFonts: () => [],
  initialUserName: '',
  focusRequest: null,
  appVersion: '',
  pdfSettings: () => ({ ...DEFAULT_PDF_SETTINGS })
})

// Emits
const emit = defineEmits<{
  updateSettings: [settings: Settings]
  updatePreviewTheme: [theme: string]
  updateColorTheme: [theme: string]
  updateQuickTyping: [enabled: boolean]
  updateCommentFormat: [format: string]
  updateFormattingHotkeys: [enabled: boolean]
  updatePreviewCursorSync: [enabled: boolean]
  updateAutoRun: [enabled: boolean]
  updateAutoInputMode: [enabled: boolean]
  updatePreviewUiOverrides: [enabled: boolean]
  updateShowFileMetadata: [enabled: boolean]
  updateDarkBackground: [color: string]
  updateLinterMinSeverity: [severity: string]
  updateLogLevel: [level: string]
  updateMaxOutputLines: [value: number]
  updateMaxPreviewSize: [value: number]
  updateMaxPreviewConsoleMessages: [value: number]
  resetSettings: []
  saveNamedConfig: [name: string]
  switchConfig: [name: string]
  openSettingsFolder: []
  openLogsFolder: []
  openFontsFolder: []
  refreshFonts: []
  updateEditorFontFamily: [family: string]
  updateCompletionDelayMs: [delayMs: number]
  updateUserName: [value: string]
  focusHandled: []
  updatePdfSettings: [settings: PdfSettings]
  resetPdfSettings: []
}>()

// State
const localSettings = ref<Settings>({ ...props.settings })

// Ordered per-section key lists — METADATA_SETTINGS_KEYS' array order doesn't
// group Math/Plot/Units contiguously (isUs sits mid-array, precision/tol
// sit at the end), so the render order is spelled out here instead of sliced.
// Plot is split around screenScaleFactor/lightDirection, which have no
// #settings-directive counterpart and so stay hardcoded, to keep the visible
// field order unchanged.
const MATH_KEYS = [
  'decimals', 'degrees', 'complex', 'substitute', 'formatEquations',
  'zeroSmallMatrixElements', 'inlineMatrices', 'showHiddenOutput', 'maxOutputCount', 'precision', 'tol',
].map(k => specForKey(METADATA_SETTINGS_KEYS, k)!)
const PLOT_KEYS_A = ['adaptivePlot'].map(k => specForKey(METADATA_SETTINGS_KEYS, k)!)
const PLOT_KEYS_B = ['vectorGraphics', 'colorScale', 'smoothScale', 'shadows'].map(k => specForKey(METADATA_SETTINGS_KEYS, k)!)
const PLOT_KEYS_C = ['plotWidth', 'plotHeight', 'plotStep'].map(k => specForKey(METADATA_SETTINGS_KEYS, k)!)

/** Writable computed bound through SETTINGS_PATH's dot-path, one per key, cached. */
const settingModelCache = new Map<string, WritableComputedRef<string | number | boolean>>()
function settingModel(key: string): WritableComputedRef<string | number | boolean> {
  let model = settingModelCache.get(key)
  if (!model) {
    const [a, b] = SETTINGS_PATH[key]!.split('.')
    model = computed({
      get: () => (b ? (localSettings.value as any)[a][b] : (localSettings.value as any)[a]),
      set: (v) => {
        const current = b ? (localSettings.value as any)[a][b] : (localSettings.value as any)[a]
        const coerced = typeof current === 'number' ? Number(v) : v
        if (b) (localSettings.value as any)[a][b] = coerced
        else (localSettings.value as any)[a] = coerced
      },
    })
    settingModelCache.set(key, model)
  }
  return model
}

/** `step` for a generic number input — precision/tol are continuous, everything else is integral. */
const numberStep = (key: string) => (key === 'precision' || key === 'tol' ? 'any' : '1')

// Defaults first, so a settings blob written before a key existed still renders a
// real value rather than validating `undefined` against the key's allowed set.
const localPdfSettings = reactive<Record<string, string | number | boolean>>(
  { ...DEFAULT_PDF_SETTINGS, ...props.pdfSettings }
)
const previewTheme = ref(props.initialPreviewTheme)
const colorTheme = ref(props.initialColorTheme)
const availableThemes = ref<ThemeInfo[]>(props.initialAvailableThemes)

const darkThemes = computed(() => availableThemes.value.filter(t => t.kind === 'dark'))
const lightThemes = computed(() => availableThemes.value.filter(t => t.kind === 'light'))
const knownThemeLabels = computed(() => new Set(availableThemes.value.map(t => t.label)))
const userFontOptions = computed(() =>
  availableFonts.value.filter(f => f && f !== 'JuliaMono')
)
const isKnownFont = computed(() => {
  const v = editorFontFamily.value
  if (!v) return true
  if (v === 'JuliaMono' || v === 'system') return true
  return availableFonts.value.includes(v)
})
const enableQuickTyping = ref(props.initialEnableQuickTyping)
const commentFormat = ref(props.initialCommentFormat)
const enableFormattingHotkeys = ref(props.initialEnableFormattingHotkeys)
const enablePreviewCursorSync = ref(props.initialEnablePreviewCursorSync)
const enableAutoRun = ref(props.initialEnableAutoRun)
const enableAutoInputMode = ref(props.initialEnableAutoInputMode)
const enablePreviewUiOverrides = ref(props.initialEnablePreviewUiOverrides)
const showFileMetadata = ref(props.initialShowFileMetadata)
const darkBackground = ref(props.initialDarkBackground)
const linterMinSeverity = ref(props.initialLinterMinSeverity)
const logLevel = ref(props.initialLogLevel)
const logLevelDetail = computed(() =>
  LOG_LEVEL_OPTIONS.find(o => o.value === logLevel.value)?.detail ?? ''
)
const maxOutputLines = ref(props.initialMaxOutputLines)
const maxPreviewSizeMB = ref(props.initialMaxPreviewSize)
const maxPreviewConsoleMessages = ref(props.initialMaxPreviewConsoleMessages)
const activeConfig = ref(props.initialActiveConfig)
const availableConfigs = ref<string[]>(props.initialAvailableConfigs)
const editorFontFamily = ref(props.initialEditorFontFamily)
const completionDelayMs = ref(props.initialCompletionDelayMs)
const availableFonts = ref<string[]>(props.initialAvailableFonts)
const userName = ref(props.initialUserName)
const newConfigName = ref('')
const saveError = ref('')

// Search + collapse
const searchQuery = ref('')
const query = computed(() => searchQuery.value.trim().toLowerCase())
const searchActive = computed(() => query.value.length > 0)

// Single source of truth for section titles and per-row search keywords.
const SECTION_META: Record<string, { title: string; rows: Record<string, string> }> = {
  math: {
    title: 'Math Settings',
    rows: {
      decimals: 'decimals precision digits',
      degrees: 'angle units degrees radians gradians',
      complex: 'complex numbers imaginary',
      substitute: 'substitute variables',
      formatEquations: 'format equations professional inline',
      zeroSmallMatrixElements: 'zero small matrix elements scientific notation',
      inlineMatrices: 'inline matrices vectors grid bracket single line rendering layout cells aligned',
      showHiddenOutput: 'show hidden output hide debug debugging suppressed',
      maxOutputCount: 'max output count rows columns matrices vectors',
      precision: 'numerical precision integration root finding tolerance',
      tol: 'solver tolerance pcg eigensolver iterative'
    }
  },
  plot: {
    title: 'Plot Settings',
    rows: {
      adaptivePlot: 'adaptive plotting sample points',
      screenScale: 'screen scale factor resolution',
      vectorGraphics: 'vector graphics svg png raster',
      colorScale: 'color scale none gray grayscale rainbow terrain violet green blues blue yellow red purple',
      smoothScale: 'smooth scale',
      shadows: 'shadows',
      lightDirection: 'light direction',
      plotWidth: 'plot width pixels size',
      plotHeight: 'plot height pixels size',
      plotStep: 'map mesh step surface plotting'
    }
  },
  units: {
    title: 'Units',
    rows: {
      isUs: 'non-metric units us uk imperial customary'
    }
  },
  pdf: {
    title: 'PDF Export',
    rows: {
      format: 'pdf paper size page format letter legal tabloid ledger a4 a3',
      orientation: 'pdf page orientation portrait landscape',
      marginTop: 'pdf top margin page',
      marginBottom: 'pdf bottom margin page',
      marginLeft: 'pdf left margin page',
      marginRight: 'pdf right margin page',
      showPageNumbers: 'pdf page numbers footer',
      showDate: 'pdf date timestamp header',
      showTitle: 'pdf document title header show hide',
      dateTimeFormat: 'pdf timestamp date time format',
      headerFooterLines: 'pdf header footer separator lines none both',
      lineColor: 'pdf line color separator header footer',
      reset: 'reset pdf settings default'
    }
  },
  user: {
    title: 'User',
    rows: {
      userName: 'user name username author reviewer file info'
    }
  },
  editor: {
    title: 'Editor',
    rows: {
      quickTyping: 'enable quick typing shortcuts symbols',
      commentFormat: 'comment format html markdown auto',
      formattingHotkeys: 'formatting hotkeys bold italic',
      previewCursorSync: 'sync preview cursor line scroll',
      autoRun: 'auto-run preview render',
      autoInputMode: 'auto input mode ui form first open',
      previewUiOverrides: 'apply ui values preview entered overrides debug troubleshoot',
      showFileMetadata: 'show hide file metadata info ui overrides comment footer',
      fontFamily: 'editor font family juliamono system',
      completionDelayMs: 'completion delay autocomplete code suggestions milliseconds ms',
      fontsFolder: 'open fonts folder custom',
      previewTheme: 'preview theme system light dark',
      darkBackground: 'dark mode background color',
      colorTheme: 'color theme syntax',
      linterMinSeverity: 'linter minimum severity error warning information'
    }
  },
  formatting: {
    title: 'Formatting',
    rows: { prettify: 'prettify document auto indent style tab space spaces per level trim trailing whitespace' }
  },
  server: {
    title: 'Server Settings',
    rows: {
      url: 'remote server url endpoint'
    }
  },
  diagnostics: {
    title: 'Diagnostics',
    rows: {
      logsFolder: 'open logs folder crash dump',
      logLevel: 'log level verbosity error warning information verbose noisy quiet logging output channel',
      maxOutputLines: 'max output lines channel',
      maxPreviewSize: 'max preview size memory limit mb blocked too large',
      maxPreviewConsoleMessages: 'max preview console messages javascript js log flood suppressed'
    }
  },
  configuration: {
    title: 'Configuration',
    rows: {
      activeConfig: 'active config',
      saveConfig: 'save current settings config name',
      actions: 'open settings folder reset default'
    }
  }
}

const STORAGE_KEY = 'calcpad.settings.collapsed'
const collapsed = reactive<Record<string, boolean>>({})
try {
  Object.assign(collapsed, JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}'))
} catch {
  // ignore unavailable/corrupt storage
}
watch(collapsed, () => {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(collapsed))
  } catch {
    // ignore unavailable storage
  }
}, { deep: true })

const isCollapsed = (id: string) => !!collapsed[id]
const toggle = (id: string) => { collapsed[id] = !collapsed[id] }
const bodyVisible = (id: string) => searchActive.value || !collapsed[id]

const setAll = (value: boolean) => {
  for (const id of Object.keys(SECTION_META)) collapsed[id] = value
}
const allCollapsed = computed(() => Object.keys(SECTION_META).every(id => collapsed[id]))

const rowVisible = (id: string, key: string) => {
  if (!searchActive.value) return true
  const meta = SECTION_META[id]
  if (!meta) return true
  if (meta.title.toLowerCase().includes(query.value)) return true
  return (meta.rows[key] || '').includes(query.value)
}

const sectionVisible = (id: string) => {
  if (!searchActive.value) return true
  const meta = SECTION_META[id]
  if (!meta) return true
  if (meta.title.toLowerCase().includes(query.value)) return true
  return Object.values(meta.rows).some(r => r.includes(query.value))
}

const anyVisible = computed(() =>
  Object.keys(SECTION_META).some(id => sectionVisible(id))
)

// Methods
// Per-field validation against the ranges Calcpad.Core enforces. Invalid fields
// are highlighted and block the settings from being applied (rather than clamped).
const settingErrors = computed<Record<string, string | null>>(() => {
  const out: Record<string, string | null> = {}
  for (const spec of [...MATH_KEYS, ...PLOT_KEYS_A, ...PLOT_KEYS_B, ...PLOT_KEYS_C])
    if (spec.type === 'number') out[spec.key] = validateSettingValue(spec.key, settingModel(spec.key).value)
  return out
})
const hasSettingErrors = computed(() => Object.values(settingErrors.value).some(Boolean))

const updateSettings = () => {
  if (hasSettingErrors.value) return
  emit('updateSettings', localSettings.value)
}

// These are the host-level defaults. A document can override any of them for its
// own export via the `pdf` key of a metadata comment (see the Properties tab).
const pdfKeys = PDF_SETTING_KEYS

const pdfErrors = computed<Record<string, string | null>>(() => {
  const out: Record<string, string | null> = {}
  for (const spec of PDF_SETTING_KEYS)
    out[spec.key] = validatePdfValue(spec.key, localPdfSettings[spec.key])
  return out
})

const updatePdfSettings = () => {
  if (Object.values(pdfErrors.value).some(Boolean)) return
  emit('updatePdfSettings', { ...localPdfSettings } as unknown as PdfSettings)
}

const resetPdfSettings = () => {
  emit('resetPdfSettings')
}

const updatePreviewTheme = () => {
  emit('updatePreviewTheme', previewTheme.value)
}

const updateColorTheme = () => {
  emit('updateColorTheme', colorTheme.value)
}

const updateQuickTyping = () => {
  emit('updateQuickTyping', enableQuickTyping.value)
}

const updateCommentFormat = () => {
  emit('updateCommentFormat', commentFormat.value)
}

const updateFormattingHotkeys = () => {
  emit('updateFormattingHotkeys', enableFormattingHotkeys.value)
}

const updatePreviewCursorSync = () => {
  emit('updatePreviewCursorSync', enablePreviewCursorSync.value)
}

const updateAutoRun = () => {
  emit('updateAutoRun', enableAutoRun.value)
}

const updateAutoInputMode = () => {
  emit('updateAutoInputMode', enableAutoInputMode.value)
}

const updatePreviewUiOverrides = () => {
  emit('updatePreviewUiOverrides', enablePreviewUiOverrides.value)
}

const updateShowFileMetadata = () => {
  emit('updateShowFileMetadata', showFileMetadata.value)
}

const updateDarkBackground = () => {
  emit('updateDarkBackground', darkBackground.value)
}

const resetDarkBackground = () => {
  darkBackground.value = '#1e1e1e'
  updateDarkBackground()
}

const updateLinterMinSeverity = () => {
  emit('updateLinterMinSeverity', linterMinSeverity.value)
}

const updateLogLevel = () => {
  emit('updateLogLevel', logLevel.value)
}

const updateMaxOutputLines = () => {
  const n = Number(maxOutputLines.value)
  if (!Number.isFinite(n) || n < 10) return
  emit('updateMaxOutputLines', Math.floor(n))
}

const updateMaxPreviewSize = () => {
  const n = Number(maxPreviewSizeMB.value)
  if (!Number.isFinite(n)) return
  const clamped = Math.min(MAX_PREVIEW_SIZE_MB, Math.max(MIN_PREVIEW_SIZE_MB, Math.floor(n)))
  maxPreviewSizeMB.value = clamped
  emit('updateMaxPreviewSize', clamped)
}

const updateMaxPreviewConsoleMessages = () => {
  const n = Number(maxPreviewConsoleMessages.value)
  if (!Number.isFinite(n)) return
  const clamped = Math.min(MAX_CONSOLE_MESSAGES_PER_DOCUMENT,
    Math.max(MIN_CONSOLE_MESSAGES_PER_DOCUMENT, Math.floor(n)))
  maxPreviewConsoleMessages.value = clamped
  emit('updateMaxPreviewConsoleMessages', clamped)
}

const resetSettings = () => {
  emit('resetSettings')
}

const saveNamedConfig = () => {
  const name = newConfigName.value.trim()
  if (!name) return
  if (name.toLowerCase() === 'default') {
    saveError.value = 'The "default" config is protected and cannot be overridden.'
    return
  }
  saveError.value = ''
  emit('saveNamedConfig', name)
  newConfigName.value = ''
}

const switchConfig = (name: string) => {
  if (!name) return
  emit('switchConfig', name)
}

const openSettingsFolder = () => {
  emit('openSettingsFolder')
}

const openLogsFolder = () => {
  emit('openLogsFolder')
}

const openFontsFolder = () => {
  emit('openFontsFolder')
}

const requestFontRescan = () => {
  emit('refreshFonts')
}

const updateEditorFontFamily = () => {
  emit('updateEditorFontFamily', editorFontFamily.value)
}

const updateCompletionDelayMs = (event: Event) => {
  const input = event.target as HTMLInputElement
  if (input.reportValidity()) emit('updateCompletionDelayMs', input.valueAsNumber)
}

const updateUserName = () => {
  userName.value = userName.value.trim()
  emit('updateUserName', userName.value)
}

// Watch for prop changes
watch(
  () => props.settings,
  (newSettings) => {
    if (newSettings) {
      localSettings.value = { ...newSettings }
    }
  },
  { deep: true }
)

watch(
  () => props.initialPreviewTheme,
  (newTheme) => {
    previewTheme.value = newTheme
  }
)

watch(
  () => props.initialColorTheme,
  (newValue) => {
    colorTheme.value = newValue
  }
)

watch(
  () => props.initialAvailableThemes,
  (newValue) => {
    availableThemes.value = newValue
  }
)

watch(
  () => props.initialEnableQuickTyping,
  (newValue) => {
    enableQuickTyping.value = newValue
  }
)

watch(
  () => props.initialCommentFormat,
  (newValue) => {
    commentFormat.value = newValue
  }
)

watch(
  () => props.initialEnableFormattingHotkeys,
  (newValue) => {
    enableFormattingHotkeys.value = newValue
  }
)

watch(
  () => props.initialEnablePreviewCursorSync,
  (newValue) => {
    enablePreviewCursorSync.value = newValue
  }
)

watch(
  () => props.initialEnableAutoInputMode,
  (newValue) => {
    enableAutoInputMode.value = newValue
  }
)

watch(
  () => props.initialEnablePreviewUiOverrides,
  (newValue) => {
    enablePreviewUiOverrides.value = newValue
  }
)

watch(
  () => props.initialShowFileMetadata,
  (newValue) => {
    showFileMetadata.value = newValue
  }
)

watch(
  () => props.initialEnableAutoRun,
  (newValue) => {
    enableAutoRun.value = newValue
  }
)

watch(
  () => props.initialDarkBackground,
  (newValue) => {
    darkBackground.value = newValue
  }
)

watch(
  () => props.initialLinterMinSeverity,
  (newValue) => {
    linterMinSeverity.value = newValue
  }
)

watch(
  () => props.initialLogLevel,
  (newValue) => {
    logLevel.value = newValue
  }
)

watch(
  () => props.initialMaxOutputLines,
  (newValue) => {
    maxOutputLines.value = newValue
  }
)

watch(
  () => props.initialMaxPreviewSize,
  (newValue) => {
    maxPreviewSizeMB.value = newValue
  }
)

watch(
  () => props.initialMaxPreviewConsoleMessages,
  (newValue) => {
    maxPreviewConsoleMessages.value = newValue
  }
)

watch(
  () => props.initialActiveConfig,
  (newValue) => {
    activeConfig.value = newValue
  }
)

watch(
  () => props.initialAvailableConfigs,
  (newValue) => {
    availableConfigs.value = newValue
  }
)

watch(
  () => props.initialEditorFontFamily,
  (newValue) => {
    editorFontFamily.value = newValue
  }
)

watch(
  () => props.initialCompletionDelayMs,
  (delayMs) => {
    completionDelayMs.value = delayMs
  }
)

watch(
  () => props.initialUserName,
  (newValue) => {
    userName.value = newValue
  }
)

// A search or a collapsed section would otherwise leave the requested field hidden.
const rootEl = ref<HTMLElement | null>(null)
watch(
  () => props.focusRequest,
  async (request) => {
    if (!request) return
    searchQuery.value = ''
    collapsed[request.section] = false
    await nextTick()
    const input = rootEl.value?.querySelector<HTMLElement>(`#${request.id}`)
    input?.scrollIntoView({ block: 'center' })
    input?.focus()
    emit('focusHandled')
  },
  { immediate: true }
)

watch(
  () => props.initialAvailableFonts,
  (newValue) => {
    availableFonts.value = newValue
  }
)

watch(
  () => props.pdfSettings,
  (newSettings) => {
    if (newSettings) Object.assign(localPdfSettings, DEFAULT_PDF_SETTINGS, newSettings)
  },
  { deep: true }
)

</script>

<style scoped>
.settings-tab {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.settings-toolbar {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 12px;
  /* Pinned above .settings-container, which scrolls under it. */
  flex: 0 0 auto;
  border-bottom: 1px solid var(--vscode-panel-border);
  background: var(--vscode-editor-background);
}

.settings-search {
  flex: 1;
}

.settings-container {
  padding: 12px;
  overflow-y: auto;
  flex: 1 1 auto;
  min-height: 0;
}

.section-header {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 12px 0 8px 0;
  color: var(--vscode-sideBarSectionHeader-foreground);
  font-weight: bold;
  border-bottom: 1px solid var(--vscode-panel-border);
  padding-bottom: 4px;
  cursor: pointer;
  user-select: none;
}

.settings-section:first-child .section-header {
  margin-top: 0;
}

.section-header:hover {
  color: var(--vscode-foreground);
}

.expand-icon {
  transition: transform 0.2s;
}

.expand-icon.collapsed {
  transform: rotate(-90deg);
}

.search-active .expand-icon {
  visibility: hidden;
}

.setting-group {
  margin-bottom: 12px;
}

.setting-group label {
  display: block;
  margin-bottom: 4px;
  color: var(--vscode-input-foreground);
  font-weight: normal;
}

.setting-group input[type="number"],
.setting-group input[type="text"],
.setting-group select {
  width: 100%;
  padding: 6px 8px;
  background: var(--vscode-input-background);
  border: 1px solid var(--vscode-input-border);
  color: var(--vscode-input-foreground);
  border-radius: 3px;
}

.setting-group input[type="checkbox"] {
  margin-right: 8px;
  background: var(--vscode-checkbox-background);
  border: 1px solid var(--vscode-checkbox-border);
}

.setting-group label:has(input[type="checkbox"]) {
  display: flex;
  align-items: center;
  cursor: pointer;
}

.color-input-row {
  display: flex;
  gap: 6px;
  align-items: center;
}

.color-input-row input[type="text"] {
  flex: 1;
}

.setting-info {
  margin-left: 4px;
  color: var(--vscode-descriptionForeground);
  cursor: help;
}

.input-invalid {
  border-color: var(--vscode-inputValidation-errorBorder, #f14c4c) !important;
  outline: 1px solid var(--vscode-inputValidation-errorBorder, #f14c4c);
}

.setting-error {
  display: block;
  margin-top: 4px;
  color: var(--vscode-errorForeground, #f48771);
}

.settings-actions {
  display: flex;
  gap: 8px;
  margin-top: 16px;
}

.settings-actions .reset-button {
  flex: 1;
  margin-top: 0;
}

.reset-button {
  width: 100%;
  margin-top: 16px;
}

.diagnostics-button {
  width: 100%;
}

.app-version {
  color: var(--vscode-descriptionForeground);
}

.about-footer {
  margin-top: 24px;
  padding-top: 12px;
  border-top: 1px solid var(--vscode-panel-border);
  text-align: center;
}

.no-results {
  padding: 16px 4px;
  color: var(--vscode-descriptionForeground);
  text-align: center;
}
</style>
