<template>
  <div ref="rootEl" class="settings-tab" :class="{ 'search-active': searchActive }">
    <div class="settings-toolbar">
      <CalcpadSearchField
        v-model="searchQuery"
        class="settings-search"
        :placeholder="t('settings.searchSettingsEllipsis')"
      />
      <CollapseAllButton v-if="!searchActive" :expand="allCollapsed" @click="setAll(!allCollapsed)" />
    </div>

    <div class="settings-container">
      <section v-show="favoritesVisible" class="settings-section favorites-section">
        <h3 class="section-header" @click="toggle('favorites')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('favorites') }">&#x25BC;</span>
          <span class="favorites-star">★</span> {{ t('insert.favorites') }}
        </h3>
        <div v-show="bodyVisible('favorites')" class="section-body">
          <p v-if="favoriteSettings.length === 0" class="favorites-empty">{{ t('settings.starASettingToPinItHere') }}</p>
          <div v-for="id in favoriteSettings" :id="favoriteSlotId(id)" :key="id"></div>
        </div>
      </section>

      <section v-show="sectionVisible('language')" class="settings-section">
        <h3 class="section-header" @click="toggle('language')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('language') }">&#x25BC;</span>
          {{ t('settings.language') }}
        </h3>
        <div v-show="bodyVisible('language')" class="section-body">
          <SettingRow section="language" row="uiLanguage">
            <label for="uiLanguage">{{ t('settings.languageLabel') }}</label>
            <select id="uiLanguage" v-model="localeSetting" @change="changeLocale">
              <option v-for="opt in LOCALE_OPTIONS" :key="opt.value" :value="opt.value">{{ opt.translatable ? tt(opt.label) : opt.label }}</option>
            </select>
          </SettingRow>
        </div>
      </section>
      <section v-show="sectionVisible('math')" class="settings-section">
        <h3 class="section-header" @click="toggle('math')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('math') }">&#x25BC;</span>
          <CalcpadSearchHighlight :text="t('settings.mathSettings')" :query="query" />
        </h3>
        <div v-show="bodyVisible('math')" class="section-body">
          <SettingRow v-for="spec in MATH_KEYS" :key="spec.key" section="math" :row="spec.key">
            <label v-if="spec.type === 'boolean'">
              <input
                type="checkbox"
                v-model="settingModel(spec.key).value"
                @change="updateSettings"
              />
              <CalcpadSearchHighlight :text="tt(spec.label)" :query="query" />
              <span v-if="spec.detail" class="setting-info" :title="tt(spec.detail)">ⓘ</span>
            </label>
            <template v-else>
              <label :for="spec.key">
                <CalcpadSearchHighlight :text="tt(spec.label)" :query="query" />:
                <span v-if="spec.detail" class="setting-info" :title="tt(spec.detail)">ⓘ</span>
              </label>
              <select
                v-if="spec.type === 'enum'"
                :id="spec.key"
                v-model="settingModel(spec.key).value"
                @change="updateSettings"
              >
                <option v-for="opt in spec.options" :key="opt.value" :value="opt.value">{{ tt(opt.label) }}</option>
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
          </SettingRow>
        </div>
      </section>

      <section v-show="sectionVisible('plot')" class="settings-section">
        <h3 class="section-header" @click="toggle('plot')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('plot') }">&#x25BC;</span>
          <CalcpadSearchHighlight :text="t('settings.plotSettings')" :query="query" />
        </h3>
        <div v-show="bodyVisible('plot')" class="section-body">
          <SettingRow v-for="spec in PLOT_KEYS_A" :key="spec.key" section="plot" :row="spec.key">
            <label>
              <input type="checkbox" v-model="settingModel(spec.key).value" @change="updateSettings" />
              <CalcpadSearchHighlight :text="tt(spec.label)" :query="query" />
              <span v-if="spec.detail" class="setting-info" :title="tt(spec.detail)">ⓘ</span>
            </label>
          </SettingRow>

          <SettingRow section="plot" row="screenScale">
            <label for="screenScaleFactor">
              <CalcpadSearchHighlight :text="t('settings.screenScaleFactorLabel')" :query="query" />
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
          </SettingRow>

          <SettingRow v-for="spec in PLOT_KEYS_B" :key="spec.key" section="plot" :row="spec.key">
            <label v-if="spec.type === 'boolean'">
              <input type="checkbox" v-model="settingModel(spec.key).value" @change="updateSettings" />
              <CalcpadSearchHighlight :text="tt(spec.label)" :query="query" />
              <span v-if="spec.detail" class="setting-info" :title="tt(spec.detail)">ⓘ</span>
            </label>
            <template v-else>
              <label :for="spec.key">
                <CalcpadSearchHighlight :text="tt(spec.label)" :query="query" />:
                <span v-if="spec.detail" class="setting-info" :title="tt(spec.detail)">ⓘ</span>
              </label>
              <select
                v-if="spec.type === 'enum'"
                :id="spec.key"
                v-model="settingModel(spec.key).value"
                @change="updateSettings"
              >
                <option v-for="opt in spec.options" :key="opt.value" :value="opt.value">{{ tt(opt.label) }}</option>
              </select>
              <input
                v-else
                :id="spec.key"
                type="text"
                v-model="settingModel(spec.key).value"
                @input="updateSettings"
              />
            </template>
          </SettingRow>

          <SettingRow section="plot" row="lightDirection">
            <label for="lightDirection">
              <CalcpadSearchHighlight :text="t('settings.lightDirectionLabel')" :query="query" />
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
          </SettingRow>

          <SettingRow v-for="spec in PLOT_KEYS_C" :key="spec.key" section="plot" :row="spec.key">
            <label :for="spec.key">
              <CalcpadSearchHighlight :text="tt(spec.label)" :query="query" />:
              <span v-if="spec.detail" class="setting-info" :title="tt(spec.detail)">ⓘ</span>
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
          </SettingRow>
        </div>
      </section>

      <section v-show="sectionVisible('units')" class="settings-section">
        <h3 class="section-header" @click="toggle('units')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('units') }">&#x25BC;</span>
          <CalcpadSearchHighlight :text="t('settings.units')" :query="query" />
        </h3>
        <div v-show="bodyVisible('units')" class="section-body">
          <SettingRow section="units" row="isUs">
            <label for="nonMetricUnits">
              <CalcpadSearchHighlight :text="t('settings.nonMetricUnitsLabel')" :query="query" />
              <span class="setting-info" :title="t('settings.selectsUsOrUkDefinitionsForBareUnit')">ⓘ</span>
            </label>
            <select
              id="nonMetricUnits"
              v-model="localSettings.isUs"
              @change="updateSettings"
            >
              <option :value="false">{{ t('settings.ukImperial') }}</option>
              <option :value="true">{{ t('settings.usCustomary') }}</option>
            </select>
          </SettingRow>
        </div>
      </section>

      <section v-show="sectionVisible('pdf')" class="settings-section">
        <h3 class="section-header" @click="toggle('pdf')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('pdf') }">&#x25BC;</span>
          <CalcpadSearchHighlight :text="t('settings.pdfExport')" :query="query" />
        </h3>
        <div v-show="bodyVisible('pdf')" class="section-body">
          <SettingRow v-for="spec in pdfKeys" :key="spec.key" section="pdf" :row="spec.key">
            <label :for="'pdf-' + spec.key">
              <CalcpadSearchHighlight :text="tt(spec.label)" :query="query" />:
              <span class="setting-info" :title="tt(spec.detail)">ⓘ</span>
            </label>
            <select
              v-if="spec.type === 'boolean'"
              :id="'pdf-' + spec.key"
              v-model="localPdfSettings[spec.key]"
              @change="updatePdfSettings"
            >
              <option :value="true">{{ t('settings.yes') }}</option>
              <option :value="false">{{ t('settings.no') }}</option>
            </select>
            <select
              v-else-if="spec.type === 'enum'"
              :id="'pdf-' + spec.key"
              v-model="localPdfSettings[spec.key]"
              @change="updatePdfSettings"
            >
              <option v-for="opt in spec.options" :key="opt.value" :value="opt.value">{{ tt(opt.label) }}</option>
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
          </SettingRow>

          <SettingRow section="pdf" row="reset" row-class="settings-actions">
            <button @click="resetPdfSettings" class="reset-button">
              <CalcpadSearchHighlight :text="t('settings.resetPdfSettings')" :query="query" />
            </button>
          </SettingRow>
        </div>
      </section>

      <section v-show="sectionVisible('user')" class="settings-section">
        <h3 class="section-header" @click="toggle('user')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('user') }">&#x25BC;</span>
          {{ t('settings.user') }}
        </h3>
        <div v-show="bodyVisible('user')" class="section-body">
          <SettingRow section="user" row="userName">
            <label for="userName">
              {{ t('settings.userNameLabel') }}
              <span class="setting-info" :title="t('settings.recordedInAFilesInfoInfoTabAs')">ⓘ</span>
            </label>
            <input
              id="userName"
              v-model="userName"
              type="text"
              :placeholder="t('settings.exampleJsmith')"
              @change="updateUserName"
            />
          </SettingRow>
        </div>
      </section>

      <section v-show="sectionVisible('editor')" class="settings-section">
        <h3 class="section-header" @click="toggle('editor')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('editor') }">&#x25BC;</span>
          <CalcpadSearchHighlight :text="t('settings.editor')" :query="query" />
        </h3>
        <div v-show="bodyVisible('editor')" class="section-body">
          <SettingRow section="editor" row="quickTyping">
            <label>
              <input
                v-model="enableQuickTyping"
                type="checkbox"
                @change="updateQuickTyping"
              />
              <CalcpadSearchHighlight :text="t('settings.enableQuickTyping')" :query="query" />
              <span class="setting-info" :title="t('settings.typeShortcutsLikeA')">ⓘ</span>
            </label>
          </SettingRow>

          <SettingRow section="editor" row="commentFormat">
            <label for="commentFormat">
              <CalcpadSearchHighlight :text="t('settings.commentFormatLabel')" :query="query" />
            </label>
            <select
              id="commentFormat"
              v-model="commentFormat"
              @change="updateCommentFormat"
            >
              <option value="auto">{{ t('settings.autoDetectHashMdOnOff') }}</option>
              <option value="html">{{ t('settings.html') }}</option>
              <option value="markdown">{{ t('settings.markdown') }}</option>
            </select>
          </SettingRow>

          <SettingRow section="editor" row="formattingHotkeys">
            <label>
              <input
                v-model="enableFormattingHotkeys"
                type="checkbox"
                @change="updateFormattingHotkeys"
              />
              <CalcpadSearchHighlight :text="t('settings.enableFormattingHotkeys')" :query="query" />
              <span class="setting-info" :title="t('settings.ctrlBForBoldCtrlIForItalic')">ⓘ</span>
            </label>
          </SettingRow>

          <SettingRow section="editor" row="previewCursorSync">
            <label>
              <input
                v-model="enablePreviewCursorSync"
                type="checkbox"
                @change="updatePreviewCursorSync"
              />
              <CalcpadSearchHighlight :text="t('settings.syncPreviewToCursorLine')" :query="query" />
              <span class="setting-info" :title="t('settings.scrollThePreviewToFollowTheLineThe')">ⓘ</span>
            </label>
          </SettingRow>

          <SettingRow section="editor" row="autoRun">
            <label>
              <input
                v-model="enableAutoRun"
                type="checkbox"
                @change="updateAutoRun"
              />
              <CalcpadSearchHighlight :text="t('service.autoRunPreview')" :query="query" />
              <span class="setting-info" :title="t('settings.whenOffThePreviewOnlyReRendersWhen')">ⓘ</span>
            </label>
          </SettingRow>

          <SettingRow v-if="!versionConfig.isWeb" section="editor" row="autoInputMode">
            <label>
              <input
                v-model="enableAutoInputMode"
                type="checkbox"
                @change="updateAutoInputMode"
              />
              <CalcpadSearchHighlight :text="t('settings.openHashUiDocumentsInInputMode')" :query="query" />
              <span class="setting-info" :title="t('settings.aDocumentDeclaringHashUiControlsOpensAs')">ⓘ</span>
            </label>
          </SettingRow>

          <SettingRow section="editor" row="previewUiOverrides">
            <label>
              <input
                v-model="enablePreviewUiOverrides"
                type="checkbox"
                @change="updatePreviewUiOverrides"
              />
              <CalcpadSearchHighlight :text="t('service.applyHashUiValuesInPreview')" :query="query" />
              <span class="setting-info" :title="t('settings.previewNormallyShowsTheDocumentsOwnValuesTurn')">ⓘ</span>
            </label>
          </SettingRow>

          <SettingRow section="editor" row="previewPdf">
            <label>
              <input
                v-model="enablePreviewPdf"
                type="checkbox"
                @change="updatePreviewPdf"
              />
              <CalcpadSearchHighlight :text="t('settings.renderPreviewAsPdf')" :query="query" />
              <span class="setting-info" :title="t('settings.previewAndReportShowThePdfExportItself')">ⓘ</span>
            </label>
          </SettingRow>

          <SettingRow section="editor" row="showFileMetadata">
            <label>
              <input
                v-model="showFileMetadata"
                type="checkbox"
                @change="updateShowFileMetadata"
              />
              <CalcpadSearchHighlight :text="t('settings.showFileMetadata')" :query="query" />
              <span class="setting-info" :title="t('settings.showTheFileInfoAndSavedHashUi')">ⓘ</span>
            </label>
          </SettingRow>

          <SettingRow section="editor" row="showUiOverrideHints">
            <label>
              <input
                v-model="showUiOverrideHints"
                type="checkbox"
                @change="updateShowUiOverrideHints"
              />
              <CalcpadSearchHighlight :text="t('settings.showUiOverrideHints')" :query="query" />
              <span class="setting-info" :title="t('settings.showSavedHashUiValuesAboveTheLines')">ⓘ</span>
            </label>
          </SettingRow>

          <SettingRow section="editor" row="autoSyncUiValues">
            <label>
              <input
                v-model="autoSyncUiValues"
                type="checkbox"
                @change="updateAutoSyncUiValues"
              />
              <CalcpadSearchHighlight :text="t('settings.autoSyncHashUiValues')" :query="query" />
              <span class="setting-info" :title="t('settings.forHashUiLinesInThisFileExcluding')">ⓘ</span>
            </label>
          </SettingRow>

          <SettingRow v-if="versionConfig.isWebOrDesktop" section="editor" row="completionDelayMs">
            <label for="completionDelayMs">
              <CalcpadSearchHighlight :text="t('settings.completionDelayMsLabel')" :query="query" />
              <span class="setting-info" :title="t('settings.howLongToWaitBeforeShowingCodeSuggestions')">ⓘ</span>
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
          </SettingRow>

          <SettingRow v-if="versionConfig.isDesktop" section="editor" row="fontFamily">
            <label for="editorFontFamily">
              <CalcpadSearchHighlight :text="t('settings.fontFamilyLabel')" :query="query" />
              <span class="setting-info" :title="t('settings.juliamonoIsTheBundledDefaultDropWoff2Woff')">ⓘ</span>
            </label>
            <select
              id="editorFontFamily"
              v-model="editorFontFamily"
              @mousedown="requestFontRescan"
              @focus="requestFontRescan"
              @change="updateEditorFontFamily"
            >
              <option value="JuliaMono">{{ t('settings.juliamonoDefault') }}</option>
              <option value="system">{{ t('settings.systemDefault') }}</option>
              <optgroup v-if="userFontOptions.length" :label="t('settings.fromFontsFolder')">
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
          </SettingRow>
          <SettingRow v-if="versionConfig.isDesktop" section="editor" row="fontsFolder">
            <button
              class="diagnostics-button"
              :title="t('settings.openTheFolderWhereCustomFontsCanBe')"
              @click="openFontsFolder"
            >
              <CalcpadSearchHighlight :text="t('settings.openFontsFolder')" :query="query" />
            </button>
          </SettingRow>

          <SettingRow section="editor" row="previewTheme">
            <label for="previewTheme">
              <CalcpadSearchHighlight :text="t('settings.previewThemeLabel')" :query="query" />
            </label>
            <select
              id="previewTheme"
              v-model="previewTheme"
              @change="updatePreviewTheme"
            >
              <option value="system">{{ t('common.system') }}</option>
              <option value="light">{{ t('settings.light') }}</option>
              <option value="dark">{{ t('settings.dark') }}</option>
            </select>
          </SettingRow>

          <SettingRow section="editor" row="darkBackground">
            <label for="darkBackground">
              <CalcpadSearchHighlight :text="t('settings.darkModeBackgroundLabel')" :query="query" />
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
                :title="t('settings.resetToDefaultHash1e1e1e')"
                @click="resetDarkBackground"
              >
                {{ t('common.reset') }}
              </button>
            </div>
          </SettingRow>

          <SettingRow section="editor" row="colorTheme">
            <label for="colorTheme">
              <CalcpadSearchHighlight :text="t('settings.colorThemeLabel')" :query="query" />
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
              <option value="System">{{ t('common.system') }}</option>
              <optgroup v-if="darkThemes.length" :label="t('settings.dark')">
                <option
                  v-for="t in darkThemes"
                  :key="t.label"
                  :value="t.label"
                >{{ tt(t.label) }}</option>
              </optgroup>
              <optgroup v-if="lightThemes.length" :label="t('settings.light')">
                <option
                  v-for="t in lightThemes"
                  :key="t.label"
                  :value="t.label"
                >{{ tt(t.label) }}</option>
              </optgroup>
            </select>
          </SettingRow>

          <SettingRow section="editor" row="linterMinSeverity">
            <label for="linterMinSeverity">
              <CalcpadSearchHighlight :text="t('settings.linterMinimumSeverityLabel')" :query="query" />
            </label>
            <select
              id="linterMinSeverity"
              v-model="linterMinSeverity"
              @change="updateLinterMinSeverity"
            >
              <option value="error">{{ t('common.error') }}</option>
              <option value="warning">{{ t('common.warning') }}</option>
              <option value="information">{{ t('settings.informationAll') }}</option>
            </select>
          </SettingRow>
        </div>
      </section>

      <section v-show="sectionVisible('formatting')" class="settings-section">
        <h3 class="section-header" @click="toggle('formatting')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('formatting') }">&#x25BC;</span>
          <CalcpadSearchHighlight :text="t('common.formatting')" :query="query" />
        </h3>
        <div v-show="bodyVisible('formatting')" class="section-body">
          <SettingRow section="formatting" row="prettify">
            <slot name="formatting"></slot>
          </SettingRow>
        </div>
      </section>

      <section v-show="sectionVisible('shortcuts')" class="settings-section">
        <h3 class="section-header" @click="toggle('shortcuts')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('shortcuts') }">&#x25BC;</span>
          <CalcpadSearchHighlight :text="t('settings.keyboardShortcuts')" :query="query" />
        </h3>
        <div v-show="bodyVisible('shortcuts')" class="section-body">
          <div
            v-for="entry in shortcutEntries"
            v-show="rowVisible('shortcuts', entry.id)"
            :key="entry.id"
            class="shortcut-row"
          >
            <div class="shortcut-line">
              <CalcpadSearchHighlight class="shortcut-label" :text="entry.label" :query="query" />
              <button
                class="shortcut-key"
                :class="{ modified: entry.modified, unassigned: !entry.hotkey }"
                :title="t('settings.changeShortcut')"
                @click="recordingShortcut = recordingShortcut === entry.id ? null : entry.id"
              >{{ entry.hotkey ? formatHotkey(normalizeHotkey(entry.hotkey) ?? entry.hotkey) : t('settings.unassigned') }}</button>
            </div>
            <HotkeyRecorder
              v-if="recordingShortcut === entry.id"
              :binding-id="entry.id"
              :current="entry.hotkey"
              :default-value="defaultKeybinding(entry.id)"
              :keybindings="keybindings"
              :extra-reserved="hostHotkeys"
              :label-for="labelFor"
              @save="saveShortcut(entry.id, $event)"
              @cancel="recordingShortcut = null"
            />
          </div>
        </div>
      </section>

      <section v-show="sectionVisible('server')" class="settings-section">
        <h3 class="section-header" @click="toggle('server')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('server') }">&#x25BC;</span>
          <CalcpadSearchHighlight :text="t('settings.serverSettings')" :query="query" />
        </h3>
        <div v-show="bodyVisible('server')" class="section-body">
          <SettingRow section="server" row="url">
            <label for="serverUrl">
              <CalcpadSearchHighlight :text="t('settings.remoteServerUrlLabel')" :query="query" />
            </label>
            <input
              id="serverUrl"
              v-model="localSettings.server.url"
              type="text"
              @input="updateSettings"
            />
          </SettingRow>
        </div>
      </section>

      <section v-show="sectionVisible('diagnostics')" class="settings-section">
        <h3 class="section-header" @click="toggle('diagnostics')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('diagnostics') }">&#x25BC;</span>
          <CalcpadSearchHighlight text="Diagnostics" :query="query" />
        </h3>
        <div v-show="bodyVisible('diagnostics')" class="section-body">
          <SettingRow section="diagnostics" row="logsFolder">
            <button
              class="diagnostics-button"
              :title="t('settings.opensTheFolderContainingServerLogsAndThe')"
              @click="openLogsFolder"
            >
              <CalcpadSearchHighlight :text="t('settings.openLogsFolder')" :query="query" />
            </button>
          </SettingRow>

          <SettingRow section="diagnostics" row="logLevel">
            <label for="logLevel">
              <CalcpadSearchHighlight :text="t('settings.logLevelLabel')" :query="query" />
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
              >{{ tt(opt.label) }}</option>
            </select>
          </SettingRow>

          <SettingRow v-if="versionConfig.isWebOrDesktop" section="diagnostics" row="maxOutputLines">
            <label for="maxOutputLines">
              <CalcpadSearchHighlight :text="t('settings.maxOutputLinesPerChannelLabel')" :query="query" />
              <span class="setting-info" :title="t('settings.linesRetainedInEachOutputChannelBeforeOlder')">ⓘ</span>
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
          </SettingRow>

          <SettingRow section="diagnostics" row="maxPreviewSize">
            <label for="maxPreviewSize">
              <CalcpadSearchHighlight :text="t('settings.maxPreviewSizeMbLabel')" :query="query" />
              <span class="setting-info" :title="t('settings.aWorksheetThatRendersToMoreHtmlThan')">ⓘ</span>
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
          </SettingRow>

          <SettingRow section="diagnostics" row="maxPreviewConsoleMessages">
            <label for="maxPreviewConsoleMessages">
              <CalcpadSearchHighlight :text="t('settings.maxPreviewConsoleMessagesLabel')" :query="query" />
              <span class="setting-info" :title="t('settings.howManyConsoleLinesOnePreviewRenderMay')">ⓘ</span>
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
          </SettingRow>
        </div>
      </section>

      <section v-show="sectionVisible('configuration')" class="settings-section">
        <h3 class="section-header" @click="toggle('configuration')">
          <span class="expand-icon" :class="{ collapsed: isCollapsed('configuration') }">&#x25BC;</span>
          <CalcpadSearchHighlight :text="t('settings.configuration')" :query="query" />
        </h3>
        <div v-show="bodyVisible('configuration')" class="section-body">
          <SettingRow section="configuration" row="activeConfig">
            <label for="activeConfig">
              <CalcpadSearchHighlight :text="t('settings.activeConfigLabel')" :query="query" />
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
          </SettingRow>

          <SettingRow section="configuration" row="saveConfig">
            <label for="configName">
              <CalcpadSearchHighlight :text="t('settings.saveCurrentSettingsAsLabel')" :query="query" />
            </label>
            <div class="color-input-row">
              <input
                id="configName"
                v-model="newConfigName"
                type="text"
                :placeholder="t('settings.exampleMyConfig')"
                @keyup.enter="saveNamedConfig"
              />
              <button
                :disabled="!newConfigName.trim()"
                @click="saveNamedConfig"
              >
                {{ t('common.save') }}
              </button>
            </div>
            <span v-if="saveError" class="setting-error">{{ saveError }}</span>
          </SettingRow>

          <SettingRow section="configuration" row="actions" row-class="settings-actions">
            <button @click="openSettingsFolder" class="reset-button">
              <CalcpadSearchHighlight :text="t('settings.openSettingsFolder')" :query="query" />
            </button>
            <button @click="resetSettings" class="reset-button">
              <CalcpadSearchHighlight :text="t('settings.resetToDefault')" :query="query" />
            </button>
          </SettingRow>
        </div>
      </section>

      <div v-if="searchActive && !anyVisible" class="no-results">
        {{ t('settings.noSettingsFoundForQuery', { query: searchQuery.trim() }) }}
      </div>

      <div v-if="appVersion" class="about-footer">
        <span class="app-version">CalcpadCE v{{ appVersion }}</span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, computed, reactive, nextTick, provide, onMounted } from 'vue'
import { useI18n } from '../../i18n'
import CalcpadSearchField from './CalcpadSearchField.vue'
import CalcpadSearchHighlight from './CalcpadSearchHighlight.vue'
import CollapseAllButton from './CollapseAllButton.vue'
import SettingRow from './SettingRow.vue'
import HotkeyRecorder from './HotkeyRecorder.vue'
import { SETTING_ROW_CONTEXT } from './setting-row-context'
import { readCustomizations } from '../../services/customizations'
import type { UserCustomizations } from '../../services/customizations'
import { formatHotkey, normalizeHotkey } from '../../services/hotkeys'
import type { HotkeyBinding } from '../../services/hotkeys'
import { KEYBINDING_COMMANDS, SETTING_KEYBINDING_PREFIX, defaultKeybinding, keybindingCommand } from '../../services/keybindings'
import { SETTING_TOGGLES } from '../../services/setting-actions'
import type { WritableComputedRef } from 'vue'
import type { PdfSettings, Settings, ThemeInfo, VersionConfig } from '../types'
import { DEFAULT_PDF_SETTINGS, DEFAULT_VERSION_CONFIG } from '../types'
import { getDefaultSettings, METADATA_SETTINGS_KEYS, validateSettingValue, SETTINGS_PATH, LOG_LEVEL_OPTIONS, DEFAULT_COMPLETION_DELAY_MS } from '../../types/settings'
import { PDF_SETTING_KEYS, validatePdfValue } from '../../types/pdf-settings'
import { specForKey } from '../../types/catalog'
import { LOCALE_OPTIONS, readLocaleSetting, setLocale, type LocaleSetting } from '../../i18n'
import en from '../../i18n/messages/en'
import {
  DEFAULT_PREVIEW_SIZE_MB, MIN_PREVIEW_SIZE_MB, MAX_PREVIEW_SIZE_MB,
  DEFAULT_CONSOLE_MESSAGES_PER_DOCUMENT, MIN_CONSOLE_MESSAGES_PER_DOCUMENT,
  MAX_CONSOLE_MESSAGES_PER_DOCUMENT,
} from '../../services/preview-limits'

const { t, t: tt } = useI18n()
const localeSetting = ref<LocaleSetting>(readLocaleSetting())

function changeLocale() {
  setLocale(localeSetting.value)
}

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
  initialEnablePreviewPdf?: boolean
  initialShowFileMetadata?: boolean
  initialShowUiOverrideHints?: boolean
  initialAutoSyncUiValues?: boolean
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
  /** A setting to bring into view and focus, by its section and input id, or by its setting id. */
  focusRequest?: { section: string; id: string; settingId?: string } | null
  appVersion?: string
  pdfSettings?: PdfSettings
  customizations?: UserCustomizations
  hostHotkeys?: HotkeyBinding[]
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
  initialEnablePreviewPdf: false,
  initialShowFileMetadata: false,
  initialShowUiOverrideHints: false,
  initialAutoSyncUiValues: false,
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
  pdfSettings: () => ({ ...DEFAULT_PDF_SETTINGS }),
  customizations: () => readCustomizations(() => undefined),
  hostHotkeys: () => []
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
  updatePreviewPdf: [enabled: boolean]
  updateShowFileMetadata: [enabled: boolean]
  updateShowUiOverrideHints: [enabled: boolean]
  updateAutoSyncUiValues: [enabled: boolean]
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
  updateCustomizations: [customizations: UserCustomizations]
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
  'decimals', 'degrees', 'returnAngleUnits', 'complex', 'substitute', 'formatEquations',
  'zeroSmallMatrixElements', 'showHiddenOutput', 'calculatePost', 'maxOutputCount', 'precision', 'tol', 'macroCalls',
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
const enablePreviewPdf = ref(props.initialEnablePreviewPdf)
const showFileMetadata = ref(props.initialShowFileMetadata)
const showUiOverrideHints = ref(props.initialShowUiOverrideHints)
const autoSyncUiValues = ref(props.initialAutoSyncUiValues)
const darkBackground = ref(props.initialDarkBackground)
const linterMinSeverity = ref(props.initialLinterMinSeverity)
const logLevel = ref(props.initialLogLevel)
const logLevelDetail = computed(() =>
  tt(LOG_LEVEL_OPTIONS.find(o => o.value === logLevel.value)?.detail ?? '')
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
      returnAngleUnits: 'return angle units inverse trig asin acos atan atan2 phase degrees radians',
      complex: 'complex numbers imaginary',
      substitute: 'substitute variables',
      formatEquations: 'format equations professional inline',
      zeroSmallMatrixElements: 'zero small matrix elements scientific notation',
      showHiddenOutput: 'show hidden output hide debug debugging suppressed',
      calculatePost: 'calculate post input form ui slow lag performance',
      maxOutputCount: 'max output count rows columns matrices vectors',
      precision: 'numerical precision integration root finding tolerance',
      tol: 'solver tolerance pcg eigensolver iterative',
      macroCalls: 'macro calls dollar $ def expansion'
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
      previewPdf: 'render preview as pdf print preview pages viewer',
      showFileMetadata: 'show hide file metadata info ui overrides comment footer',
      showUiOverrideHints: 'show ui override hints codelens saved values above #ui line loop macro include',
      autoSyncUiValues: 'auto sync ui values overrides input form write source #ui line bidirectional',
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
  shortcuts: {
    title: 'Keyboard Shortcuts',
    rows: Object.fromEntries(KEYBINDING_COMMANDS.map(c => [c.id, ('keyboard shortcut hotkey keybinding ' + (en[c.label] ?? c.label)).toLowerCase()]))
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

const COLLAPSIBLE = ['favorites', ...Object.keys(SECTION_META)]
const setAll = (value: boolean) => {
  for (const id of COLLAPSIBLE) collapsed[id] = value
}
const allCollapsed = computed(() => COLLAPSIBLE.every(id => collapsed[id]))

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

// Favorites, hotkeys and shortcuts
const favoriteSettings = computed(() => props.customizations.favoriteSettings)
const keybindings = computed(() => props.customizations.keybindings)
const recordingShortcut = ref<string | null>(null)

const splitSettingId = (id: string): [string, string] => {
  const dot = id.indexOf('.')
  return [id.slice(0, dot), id.slice(dot + 1)]
}

const favoritesVisible = computed(() => {
  if (!searchActive.value) return true
  return favoriteSettings.value.some(id => rowVisible(...splitSettingId(id)))
})

const favoriteSlotId = (id: string) => 'calcpad-fav-slot-' + id.replace(/[^\w-]/g, '-')

const updateCustomizations = (patch: Partial<UserCustomizations>) => {
  emit('updateCustomizations', { ...props.customizations, ...patch })
}

// Rows carry their labels in markup only, so the label is read back from the rendered row.
const domReady = ref(false)
onMounted(() => { domReady.value = true })
const settingLabel = (id: string): string => {
  if (SETTING_TOGGLES[id]) return tt(SETTING_TOGGLES[id].label)
  const row = domReady.value
    ? rootEl.value?.querySelector(`[data-setting-id="${CSS.escape(id)}"] .setting-row-main`)
    : null
  const text = row?.querySelector('label, button')?.textContent ?? ''
  const cleaned = text.replace(/ⓘ/g, '').replace(/\s+/g, ' ').trim().replace(/:$/, '')
  return cleaned || splitSettingId(id)[1].replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, c => c.toUpperCase())
}

const labelFor = (bindingId: string): string => {
  if (bindingId.startsWith(SETTING_KEYBINDING_PREFIX)) {
    return t('settings.settingName', { name: settingLabel(bindingId.slice(SETTING_KEYBINDING_PREFIX.length)) })
  }
  const command = keybindingCommand(bindingId)
  return command ? tt(command.label) : bindingId
}

const shortcutEntries = computed(() => {
  const commands = KEYBINDING_COMMANDS.map(c => ({
    id: c.id,
    label: tt(c.label),
    hotkey: keybindings.value[c.id] ?? '',
    modified: (keybindings.value[c.id] ?? '') !== defaultKeybinding(c.id),
  }))
  const settings = Object.entries(keybindings.value)
    .filter(([id, hotkey]) => id.startsWith(SETTING_KEYBINDING_PREFIX) && hotkey)
    .map(([id, hotkey]) => ({ id, label: labelFor(id), hotkey, modified: true }))
  return [...commands, ...settings]
})

const saveShortcut = (id: string, hotkey: string) => {
  const next = { ...keybindings.value }
  if (!hotkey && id.startsWith(SETTING_KEYBINDING_PREFIX)) delete next[id]
  else next[id] = hotkey
  updateCustomizations({ keybindings: next })
  recordingShortcut.value = null
}

provide(SETTING_ROW_CONTEXT, {
  rowVisible,
  isFavorite: id => favoriteSettings.value.includes(id),
  toggleFavorite: id => {
    const list = favoriteSettings.value
    updateCustomizations({ favoriteSettings: list.includes(id) ? list.filter(x => x !== id) : [...list, id] })
  },
  favoriteSlotSelector: id => '#' + favoriteSlotId(id),
  hotkeyFor: id => keybindings.value[SETTING_KEYBINDING_PREFIX + id] ?? '',
  setHotkey: (id, hotkey) => saveShortcut(SETTING_KEYBINDING_PREFIX + id, hotkey),
  keybindings: () => keybindings.value,
  extraReserved: () => props.hostHotkeys,
  labelFor,
})

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

const updatePreviewPdf = () => {
  emit('updatePreviewPdf', enablePreviewPdf.value)
}

const updateShowFileMetadata = () => {
  emit('updateShowFileMetadata', showFileMetadata.value)
}

const updateShowUiOverrideHints = () => {
  emit('updateShowUiOverrideHints', showUiOverrideHints.value)
}

const updateAutoSyncUiValues = () => {
  emit('updateAutoSyncUiValues', autoSyncUiValues.value)
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
  () => props.initialEnablePreviewPdf,
  (newValue) => {
    enablePreviewPdf.value = newValue
  }
)

watch(
  () => props.initialShowFileMetadata,
  (newValue) => {
    showFileMetadata.value = newValue
  }
)

watch(
  () => props.initialShowUiOverrideHints,
  (newValue) => {
    showUiOverrideHints.value = newValue
  }
)

watch(
  () => props.initialAutoSyncUiValues,
  (newValue) => {
    autoSyncUiValues.value = newValue
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
    const input = request.settingId
      ? rootEl.value?.querySelector<HTMLElement>(
        `[data-setting-id="${CSS.escape(request.settingId)}"]:not([data-favorite-copy]) .setting-row-main :is(input, select, textarea, button)`)
      : rootEl.value?.querySelector<HTMLElement>(`#${request.id}`)
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

/* Unwraps the Formatting slot so its inputs span the row like other settings. */
.settings-container :deep([data-setting-id="formatting.prettify"] :is(.formatting-tab, .formatting-container)) {
  display: contents;
}

.settings-container :deep([data-setting-id="formatting.prettify"] .formatting-container > *) {
  grid-column: 1 / -1;
}

.settings-container :deep([data-setting-id="formatting.prettify"] .formatting-container > :first-child) {
  grid-column: 1;
  grid-row: 1;
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

.favorites-star {
  color: var(--vscode-editorWarning-foreground, #cca700);
}

.favorites-empty {
  margin: 0 0 12px;
  color: var(--vscode-descriptionForeground);
  font-style: italic;
}

.shortcut-row {
  margin-bottom: 6px;
}

.shortcut-line {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.shortcut-label {
  flex: 1 1 auto;
  min-width: 0;
}

.shortcut-key {
  flex-shrink: 0;
  padding: 2px 8px;
  font-family: monospace;
  background: var(--vscode-editor-background);
  color: var(--vscode-foreground);
  border: 1px solid var(--vscode-widget-border);
  border-radius: 3px;
  cursor: pointer;
}

.shortcut-key.modified {
  border-color: var(--vscode-focusBorder);
}

.shortcut-key.unassigned {
  color: var(--vscode-descriptionForeground);
  font-style: italic;
  font-family: inherit;
}
</style>
