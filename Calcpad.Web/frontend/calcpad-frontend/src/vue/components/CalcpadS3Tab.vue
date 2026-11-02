<template>
  <div class="s3-tab">
    <section>
      <h4 class="sub-title">{{ t('Account') }}</h4>
      <p class="note">{{ t('Server') }}: <span class="mono">{{ s3Url }}</span></p>
      <template v-if="session.connected && session.user">
        <p class="note">{{ t('Signed in as') }} <strong>{{ session.user.username }}</strong> ({{ session.user.role }})</p>
        <div class="actions">
          <button class="secondary" :disabled="busy" @click="signOut">{{ t('Sign out') }}</button>
        </div>
      </template>
      <template v-else>
        <div class="field">
          <label for="s3Username">{{ t('Username') }}</label>
          <input id="s3Username" v-model="username" type="text" autocomplete="username" @keyup.enter="signIn" />
        </div>
        <div class="field">
          <label for="s3Password">{{ t('Password') }}</label>
          <input id="s3Password" v-model="password" type="password" autocomplete="current-password" @keyup.enter="signIn" />
        </div>
        <div class="actions">
          <button :disabled="busy || !username || !password" @click="signIn">{{ t('Sign in') }}</button>
        </div>
      </template>
      <p v-if="accountError" class="error">{{ accountError }}</p>
    </section>

    <template v-if="session.connected && session.user">
      <section>
        <h4 class="sub-title">{{ t('Buckets') }}</h4>
        <ul class="list">
          <li v-for="b in buckets" :key="b.name">
            <span>{{ b.name }}</span>
            <span class="badge">{{ b.kind }}</span>
            <span class="muted">{{ b.access }}</span>
          </li>
          <li v-if="!buckets.length" class="muted">{{ t('No buckets.') }}</li>
        </ul>
        <div v-if="isAdmin" class="field">
          <label for="s3NewBucket">{{ t('Create bucket') }}</label>
          <input id="s3NewBucket" v-model="newBucketName" type="text" :placeholder="t('Name')" @keyup.enter="createBucket" />
          <select v-model="newBucketKind">
            <option value="Library">Library</option>
            <option value="Project">Project</option>
          </select>
          <div class="actions">
            <button :disabled="busy || !newBucketName.trim()" @click="createBucket">{{ t('Create') }}</button>
          </div>
        </div>
        <p v-if="bucketError" class="error">{{ bucketError }}</p>
      </section>

      <section>
        <h4 class="sub-title">{{ t('Files') }}</h4>
        <div class="field">
          <label for="s3Bucket">{{ t('Bucket') }}</label>
          <select id="s3Bucket" v-model="selectedBucket" @change="loadObjects">
            <option value="">{{ t('(select)') }}</option>
            <option v-for="b in buckets" :key="b.name" :value="b.name">{{ b.name }}</option>
          </select>
        </div>
        <div v-if="selectedBucket" class="actions">
          <button class="secondary" @click="insert(`#LibraryPath {s3:${selectedBucket}}`)">#LibraryPath</button>
          <button class="secondary" @click="insert(`#ProjectPath {s3:${selectedBucket}}`)">#ProjectPath</button>
        </div>
        <div v-if="selectedBucket" class="field">
          <label for="s3Prefix">{{ t('Folder') }}</label>
          <input id="s3Prefix" v-model="prefix" type="text" placeholder="folder/" @keyup.enter="loadObjects" />
          <div class="actions">
            <button :disabled="busy" @click="loadObjects">{{ t('Refresh') }}</button>
          </div>
        </div>
        <div v-if="selectedBucket" class="field">
          <label for="s3Mode">{{ t('Reference by') }}</label>
          <select id="s3Mode" v-model="refMode">
            <option value="path">{{ t('Path') }}</option>
            <option value="latest">{{ t('ID (latest)') }}</option>
            <option value="pinned">{{ t('ID (pinned version)') }}</option>
          </select>
        </div>
        <ul v-if="selectedBucket" class="list">
          <li v-for="o in objects" :key="o.storageKey" class="object">
            <div class="object-name" :title="o.key">{{ o.key }}</div>
            <div class="muted">
              v{{ o.version }} · {{ shortId(o.baseId) }} · {{ shortId(o.id) }}<span v-if="!o.latest"> · {{ t('old') }}</span>
            </div>
            <div class="actions">
              <button class="secondary" @click="insert(includeText(o))">#include</button>
              <button class="secondary" @click="insert(readText(o))">#read</button>
            </div>
          </li>
          <li v-if="!objects.length" class="muted">{{ t('No files.') }}</li>
        </ul>
        <p v-if="filesError" class="error">{{ filesError }}</p>
      </section>

      <section>
        <h4 class="sub-title">{{ t('Upload active file') }}</h4>
        <p v-if="!hasDocument" class="note">{{ t('No document is open.') }}</p>
        <template v-else>
          <div class="field">
            <label for="s3UploadBucket">{{ t('Bucket') }}</label>
            <select id="s3UploadBucket" v-model="uploadBucket">
              <option value="">{{ t('(select)') }}</option>
              <option v-for="b in writableBuckets" :key="b.name" :value="b.name">{{ b.name }}</option>
            </select>
          </div>
          <div class="field">
            <label for="s3UploadKey">{{ t('Key') }}</label>
            <input id="s3UploadKey" v-model="uploadKey" type="text" @input="keyEdited = true" />
          </div>
          <div class="actions">
            <button :disabled="busy || !uploadBucket || !uploadKey.trim()" @click="upload(false)">{{ t('Upload') }}</button>
            <button v-if="canForce" class="secondary" :disabled="busy" @click="upload(true)">{{ t('Force overwrite') }}</button>
          </div>
          <p v-if="uploadError" class="error">{{ uploadError }}</p>
          <p v-if="uploadedInfo" class="note">
            {{ t('Uploaded') }}: v{{ uploadedInfo.version }} ({{ shortId(uploadedInfo.id) }})
          </p>
        </template>
      </section>
    </template>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import type { S3Bucket, S3BucketKind, S3ObjectInfo, S3Session } from '../../types/s3'

const { t } = useI18n()

interface Props {
  s3Url: string
  /** The active document's path or URI, which names the default upload key. */
  docKey?: string
  hasDocument?: boolean
}

const props = withDefaults(defineProps<Props>(), { docKey: '', hasDocument: false })

const emit = defineEmits<{
  'insert-text': [text: string]
  'request': [message: Record<string, unknown>]
}>()

interface Reply { status: number; data: any }

let requestSeq = 0
const pending = new Map<number, (reply: Reply) => void>()

const onMessage = (event: MessageEvent) => {
  const message = event.data
  if (message?.type !== 's3Response') return
  const resolve = pending.get(message.id)
  if (!resolve) return
  pending.delete(message.id)
  resolve({ status: message.status, data: message.data })
}

function send(message: Record<string, unknown>): Promise<Reply> {
  const id = ++requestSeq
  return new Promise(resolve => {
    pending.set(id, resolve)
    emit('request', { ...message, id })
  })
}

const call = (method: string, path: string, body?: unknown) => send({ type: 's3Request', method, path, body })

const errorOf = (reply: Reply): string =>
  reply.data?.error ? String(reply.data.error) : `${t('Request failed')} (${reply.status})`

const session = ref<S3Session>({ connected: false, url: null, user: null })
const username = ref('')
const password = ref('')
const busy = ref(false)
const accountError = ref('')
const buckets = ref<S3Bucket[]>([])
const bucketError = ref('')
const newBucketName = ref('')
const newBucketKind = ref<S3BucketKind>('Library')
const selectedBucket = ref('')
const prefix = ref('')
const objects = ref<S3ObjectInfo[]>([])
const filesError = ref('')
const refMode = ref<'path' | 'latest' | 'pinned'>('path')
const uploadBucket = ref('')
const uploadKey = ref('')
const keyEdited = ref(false)
const uploadError = ref('')
const uploadedInfo = ref<S3ObjectInfo | null>(null)
const canForce = ref(false)

const isAdmin = computed(() => session.value.user?.role === 'Admin')
const writableBuckets = computed(() => buckets.value.filter(b => b.access !== 'Viewer'))

const shortId = (id: string) => (id || '').slice(0, 8)

function defaultKey(): string {
  let decoded = props.docKey
  try {
    decoded = decodeURIComponent(props.docKey)
  } catch {
    // keep the raw key
  }
  const base = decoded.split(/[\\/]/).pop() ?? ''
  if (!/\.[^.]+$/.test(base)) return ''
  const folder = prefix.value.replace(/^\/+/, '')
  return folder && !folder.endsWith('/') ? `${folder}/${base}` : `${folder}${base}`
}

function refreshDefaultKey() {
  if (!keyEdited.value) uploadKey.value = defaultKey()
}

watch(() => props.docKey, () => {
  keyEdited.value = false
  refreshDefaultKey()
})
watch(prefix, refreshDefaultKey)

function applySession(data: S3Session | null) {
  session.value = data ?? { connected: false, url: null, user: null }
}

async function loadSession() {
  const reply = await call('GET', 'session')
  applySession(reply.status === 200 ? reply.data : null)
  if (session.value.connected) await loadBuckets()
}

async function signIn() {
  if (!username.value || !password.value) return
  busy.value = true
  accountError.value = ''
  const reply = await call('POST', 'session', { url: props.s3Url, username: username.value, password: password.value })
  busy.value = false
  if (reply.status !== 200) {
    accountError.value = errorOf(reply)
    return
  }
  password.value = ''
  applySession(reply.data)
  await loadBuckets()
}

async function signOut() {
  busy.value = true
  await call('DELETE', 'session')
  busy.value = false
  applySession(null)
  buckets.value = []
  objects.value = []
  selectedBucket.value = ''
}

async function loadBuckets() {
  bucketError.value = ''
  const reply = await call('GET', 'remote/buckets')
  if (reply.status !== 200) {
    bucketError.value = errorOf(reply)
    return
  }
  buckets.value = Array.isArray(reply.data) ? reply.data : []
}

async function createBucket() {
  const name = newBucketName.value.trim()
  if (!name) return
  busy.value = true
  bucketError.value = ''
  const reply = await call('POST', 'remote/buckets', { name, kind: newBucketKind.value })
  busy.value = false
  if (reply.status < 200 || reply.status >= 300) {
    bucketError.value = errorOf(reply)
    return
  }
  newBucketName.value = ''
  await loadBuckets()
}

async function loadObjects() {
  filesError.value = ''
  objects.value = []
  if (!selectedBucket.value) return
  busy.value = true
  const query = prefix.value ? `?prefix=${encodeURIComponent(prefix.value)}` : ''
  const reply = await call('GET', `remote/buckets/${encodeURIComponent(selectedBucket.value)}/objects${query}`)
  busy.value = false
  if (reply.status !== 200) {
    filesError.value = errorOf(reply)
    return
  }
  objects.value = Array.isArray(reply.data) ? reply.data : []
}

const folderOf = (key: string) => key.slice(0, key.lastIndexOf('/') + 1)

function reference(o: S3ObjectInfo): string {
  const root = `{s3:${selectedBucket.value}}/`
  if (refMode.value === 'path') return root + o.key
  return root + folderOf(o.key) + (refMode.value === 'latest' ? o.baseId : o.id)
}

const includeText = (o: S3ObjectInfo) => `#include ${reference(o)}`
const readText = (o: S3ObjectInfo) => `#read M from ${reference(o)}`

const insert = (text: string) => emit('insert-text', text + '\n')

async function upload(force: boolean) {
  busy.value = true
  uploadError.value = ''
  uploadedInfo.value = null
  const reply = await send({ type: 's3Upload', bucket: uploadBucket.value, key: uploadKey.value.trim(), force })
  busy.value = false
  if (reply.status === 200 && reply.data?.info) {
    canForce.value = false
    uploadedInfo.value = reply.data.info
    if (uploadBucket.value === selectedBucket.value) await loadObjects()
    return
  }
  uploadError.value = errorOf(reply)
  canForce.value = reply.status === 409
}

onMounted(() => {
  window.addEventListener('message', onMessage)
  refreshDefaultKey()
  void loadSession()
})

onUnmounted(() => window.removeEventListener('message', onMessage))
</script>

<style scoped>
.s3-tab section {
  margin-bottom: 16px;
}

.sub-title {
  margin: 12px 0 8px 0;
  font-weight: 600;
}

.note,
.muted {
  color: var(--vscode-descriptionForeground);
}

.note {
  margin: 0 0 8px 0;
  line-height: 1.5;
}

.error {
  margin: 8px 0 0 0;
  color: var(--vscode-errorForeground, #f48771);
}

.mono {
  font-family: var(--vscode-editor-font-family, monospace);
  overflow-wrap: anywhere;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 10px;
}

.field input,
.field select {
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
  padding: 4px 6px;
  font-family: var(--vscode-font-family);
  border-radius: 2px;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin: 8px 0;
}

.list {
  list-style: none;
  margin: 0 0 8px 0;
  padding: 0;
}

.list li {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  padding: 4px 0;
}

.list li.object {
  display: block;
  border-bottom: 1px solid var(--vscode-widget-border, transparent);
}

.object-name {
  overflow-wrap: anywhere;
}

.badge {
  font-size: 0.85em;
  padding: 0 6px;
  border-radius: 8px;
  background: var(--vscode-badge-background);
  color: var(--vscode-badge-foreground);
}
</style>
