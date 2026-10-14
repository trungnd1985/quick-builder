<script setup lang="ts">
// Create or change one redirect (017 US2, FR-015, FR-017, FR-017a).
//
// The source is a path on the site, with a query for an exact redirect; the target is a page
// (followed when it moves), a path, an absolute address, or, for redirects an address change
// created, an item. Saving shows what the server said about it: a chain shortened, a query
// source over live content or a list's page parameter. Those are warnings, never refusals.
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { redirectsApi, type Redirect, type RedirectInput, type RedirectMatch, type RedirectTarget } from '../../api/redirects'
import type { PageNode } from '../../api/pages'
import { conflictingRedirectId, redirectErrorMessage } from './redirectErrors'

const props = defineProps<{ siteId: string; redirect: Redirect | null; pages: readonly PageNode[] }>()
const emit = defineEmits<{ saved: [redirect: Redirect]; deleted: [id: string]; cancel: [] }>()
const { t, te } = useI18n()

const dialogId = 'redirect-dialog'
const sourceInput = ref<HTMLInputElement | null>(null)

type Kind = RedirectTarget['kind']
const source = ref(props.redirect?.source ?? '')
const match = ref<RedirectMatch>(props.redirect?.match ?? 'exact')
const kind = ref<Kind>(props.redirect?.target.kind ?? 'path')
const pageId = ref(props.redirect?.target.kind === 'page' ? props.redirect.target.pageId : '')
const path = ref(props.redirect?.target.kind === 'path' ? props.redirect.target.path : '')
const url = ref(props.redirect?.target.kind === 'url' ? props.redirect.target.url : '')
const statusCode = ref<301 | 302>(props.redirect?.statusCode ?? 301)
const enabled = ref(props.redirect?.enabled ?? true)
const note = ref(props.redirect?.note ?? '')

const saving = ref(false)
const error = ref<string | null>(null)
const conflict = ref<string | null>(null)
const saved = ref<Redirect | null>(null)

const pageChoices = computed(() => props.pages.filter((p) => !p.path.includes('{')))
const hasQuery = computed(() => source.value.includes('?'))

function target(): RedirectTarget {
  switch (kind.value) {
    case 'page':
      return { kind: 'page', pageId: pageId.value }
    case 'url':
      return { kind: 'url', url: url.value.trim() }
    case 'item':
      return props.redirect!.target
    default:
      return { kind: 'path', path: path.value.trim() }
  }
}

async function save(): Promise<void> {
  saving.value = true
  error.value = null
  conflict.value = null
  const input: RedirectInput = {
    source: source.value.trim(),
    match: match.value,
    target: target(),
    statusCode: statusCode.value,
    enabled: enabled.value,
    note: note.value.trim() === '' ? null : note.value,
  }
  try {
    const result = props.redirect
      ? await redirectsApi.update(props.siteId, props.redirect.id, { ...input, version: props.redirect.version })
      : await redirectsApi.create(props.siteId, input)
    saved.value = result
    if (!result.shortened && (result.warnings ?? []).length === 0) emit('saved', result)
  } catch (caught) {
    error.value = redirectErrorMessage(t, te, caught)
    conflict.value = conflictingRedirectId(caught)
  } finally {
    saving.value = false
  }
}

async function remove(): Promise<void> {
  if (!props.redirect || !window.confirm(t('redirects.dialog.confirmDelete'))) return
  try {
    await redirectsApi.remove(props.siteId, props.redirect.id, props.redirect.version)
    emit('deleted', props.redirect.id)
  } catch (caught) {
    error.value = redirectErrorMessage(t, te, caught)
  }
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('cancel')
  }
}

onMounted(async () => {
  await nextTick()
  sourceInput.value?.focus()
})
</script>

<template>
  <div
    class="dialog-backdrop"
    @click.self="emit('cancel')"
  >
    <div
      class="card dialog"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="`${dialogId}-heading`"
      @keydown="onKeydown"
    >
      <form
        novalidate
        @submit.prevent="save"
      >
        <h2 :id="`${dialogId}-heading`">
          {{ redirect ? t('redirects.dialog.editTitle') : t('redirects.dialog.newTitle') }}
        </h2>

        <template v-if="!saved">
          <div class="field">
            <label :for="`${dialogId}-source`">{{ t('redirects.dialog.source') }}</label>
            <input
              :id="`${dialogId}-source`"
              ref="sourceInput"
              v-model="source"
              :aria-describedby="`${dialogId}-source-help`"
              placeholder="/old-page"
              required
            >
            <p
              :id="`${dialogId}-source-help`"
              class="hint"
            >
              {{ t('redirects.dialog.sourceHelp') }}
            </p>
          </div>

          <fieldset class="field">
            <legend>{{ t('redirects.dialog.match') }}</legend>
            <label class="radio">
              <input
                v-model="match"
                type="radio"
                value="exact"
              >
              {{ t('redirects.match.exact') }}
            </label>
            <label class="radio">
              <input
                v-model="match"
                type="radio"
                value="prefix"
                :disabled="hasQuery"
              >
              {{ t('redirects.match.prefix') }}
            </label>
          </fieldset>

          <div class="field">
            <label :for="`${dialogId}-kind`">{{ t('redirects.dialog.targetKind') }}</label>
            <select
              :id="`${dialogId}-kind`"
              v-model="kind"
            >
              <option value="page">
                {{ t('redirects.target.page') }}
              </option>
              <option value="path">
                {{ t('redirects.target.path') }}
              </option>
              <option
                value="url"
                :disabled="match === 'prefix'"
              >
                {{ t('redirects.target.url') }}
              </option>
              <option
                v-if="redirect?.target.kind === 'item'"
                value="item"
              >
                {{ t('redirects.target.item') }}
              </option>
            </select>
          </div>

          <div
            v-if="kind === 'page'"
            class="field"
          >
            <label :for="`${dialogId}-page`">{{ t('redirects.target.page') }}</label>
            <select
              :id="`${dialogId}-page`"
              v-model="pageId"
            >
              <option
                v-for="page in pageChoices"
                :key="page.id"
                :value="page.id"
              >
                {{ page.title }} ({{ page.path }})
              </option>
            </select>
          </div>
          <div
            v-else-if="kind === 'path'"
            class="field"
          >
            <label :for="`${dialogId}-path`">{{ t('redirects.target.path') }}</label>
            <input
              :id="`${dialogId}-path`"
              v-model="path"
              placeholder="/new-page"
            >
          </div>
          <div
            v-else-if="kind === 'url'"
            class="field"
          >
            <label :for="`${dialogId}-url`">{{ t('redirects.target.url') }}</label>
            <input
              :id="`${dialogId}-url`"
              v-model="url"
              type="url"
              placeholder="https://example.com/page"
            >
          </div>

          <fieldset class="field">
            <legend>{{ t('redirects.dialog.kind') }}</legend>
            <label class="radio">
              <input
                v-model="statusCode"
                type="radio"
                :value="301"
              >
              {{ t('redirects.kind.permanent') }}
            </label>
            <label class="radio">
              <input
                v-model="statusCode"
                type="radio"
                :value="302"
              >
              {{ t('redirects.kind.temporary') }}
            </label>
          </fieldset>

          <label class="checkbox">
            <input
              v-model="enabled"
              type="checkbox"
            >
            {{ t('redirects.dialog.enabled') }}
          </label>

          <div class="field">
            <label :for="`${dialogId}-note`">{{ t('redirects.dialog.note') }}</label>
            <input
              :id="`${dialogId}-note`"
              v-model="note"
              maxlength="500"
            >
          </div>

          <p
            v-if="error"
            class="error"
            role="alert"
          >
            {{ error }}
            <span v-if="conflict"> {{ t('redirects.dialog.conflictWith', { id: conflict.slice(0, 8) }) }}</span>
          </p>

          <div class="actions">
            <button
              v-if="redirect"
              type="button"
              class="danger"
              @click="remove"
            >
              {{ t('redirects.dialog.delete') }}
            </button>
            <button
              type="button"
              @click="emit('cancel')"
            >
              {{ t('redirects.dialog.cancel') }}
            </button>
            <button
              type="submit"
              class="primary"
              :disabled="saving"
            >
              {{ t('redirects.dialog.save') }}
            </button>
          </div>
        </template>

        <template v-else>
          <p role="status">
            {{ t('redirects.dialog.saved') }}
          </p>
          <p
            v-if="saved.shortened"
            class="warning"
            role="note"
          >
            {{ t('redirects.dialog.shortened', { address: saved.targetAddress ?? '' }) }}
          </p>
          <p
            v-for="warning in saved.warnings ?? []"
            :key="warning"
            class="warning"
            role="note"
          >
            {{ t(`redirects.warnings.${warning}`) }}
          </p>
          <div class="actions">
            <button
              type="button"
              class="primary"
              @click="emit('saved', saved)"
            >
              {{ t('redirects.dialog.done') }}
            </button>
          </div>
        </template>
      </form>
    </div>
  </div>
</template>
