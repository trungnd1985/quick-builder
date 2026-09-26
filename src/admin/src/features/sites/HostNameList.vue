<script setup lang="ts">
import { computed, nextTick, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { sitesApi, type HostName, type Site } from '../../api/sites'
import ConfirmDialog from './ConfirmDialog.vue'
import { isOutOfDate, siteErrorMessage } from './siteErrors'

// 009 US2: add and remove host names and choose the primary one. The primary and the
// only host name cannot be removed; the buttons say why before anyone tries, and the
// server enforces it regardless (FR-011).

const props = defineProps<{ site: Site }>()
const emit = defineEmits<{ updated: [site: Site]; reload: [] }>()

const { t, te } = useI18n()

const canManage = computed(() => props.site.permissions?.canManageHostNames === true)
const canChoosePrimary = computed(() => props.site.permissions?.canManageSettings === true)
const details = computed<HostName[]>(() => props.site.hostNameDetails ?? [])

const newHostName = ref('')
const adding = ref(false)
const addError = ref<string | null>(null)
const status = ref('')
const actionError = ref<string | null>(null)
const pendingRemoval = ref<HostName | null>(null)
const removing = ref(false)
const removeTrigger = ref<HTMLElement | null>(null)
const addInput = ref<HTMLInputElement | null>(null)

function removalBlockedReason(hostName: HostName): string | null {
  if (details.value.length <= 1) {
    return t('sites.hostNames.cannotRemoveLast')
  }

  return hostName.isPrimary ? t('sites.hostNames.cannotRemovePrimary') : null
}

async function add(): Promise<void> {
  adding.value = true
  addError.value = null
  status.value = ''

  try {
    const added = await sitesApi.addHostName(props.site.id, newHostName.value)
    newHostName.value = ''
    status.value = t('sites.hostNames.added', { host: added.displayName })
    emit('reload')
  } catch (caught) {
    addError.value = siteErrorMessage(t, te, caught)
  } finally {
    adding.value = false
  }
}

function askToRemove(hostName: HostName, event: Event): void {
  removeTrigger.value = event.currentTarget as HTMLElement
  actionError.value = null
  pendingRemoval.value = hostName
}

async function closeRemoval(): Promise<void> {
  pendingRemoval.value = null
  await nextTick()
  // The row may be gone; fall back to the add field so focus is never lost.
  if (removeTrigger.value?.isConnected) {
    removeTrigger.value.focus()
  } else {
    addInput.value?.focus()
  }
}

async function confirmRemoval(): Promise<void> {
  const target = pendingRemoval.value
  if (!target) {
    return
  }

  removing.value = true
  try {
    await sitesApi.removeHostName(props.site.id, target.hostName)
    status.value = t('sites.hostNames.removed', { host: target.displayName })
    emit('reload')
  } catch (caught) {
    actionError.value = siteErrorMessage(t, te, caught)
  } finally {
    removing.value = false
    await closeRemoval()
  }
}

async function makePrimary(hostName: HostName): Promise<void> {
  actionError.value = null
  status.value = ''

  try {
    const saved = await sitesApi.putSettings(props.site.id, {
      name: props.site.name,
      scheme: props.site.scheme,
      defaultLanguage: props.site.defaultLanguage,
      primaryHostName: hostName.hostName,
      version: props.site.version,
    })
    status.value = t('sites.hostNames.primaryChanged', { host: hostName.displayName })
    emit('updated', saved)
  } catch (caught) {
    actionError.value = siteErrorMessage(t, te, caught)
    if (isOutOfDate(caught)) {
      emit('reload')
    }
  }
}
</script>

<template>
  <section
    class="card"
    aria-labelledby="host-names-heading"
  >
    <h2 id="host-names-heading">
      {{ t('sites.hostNames.heading') }}
    </h2>
    <p class="help">
      {{ t('sites.hostNames.help') }}
    </p>

    <ul class="host-names">
      <li
        v-for="hostName in details"
        :key="hostName.hostName"
      >
        <span
          class="host-name"
          :title="hostName.hostName"
        >{{ hostName.displayName }}</span>
        <span
          v-if="hostName.isPrimary"
          class="badge"
        >{{ t('sites.hostNames.primary') }}</span>
        <span
          v-if="canManage || canChoosePrimary"
          class="actions"
        >
          <button
            v-if="canChoosePrimary && !hostName.isPrimary"
            type="button"
            @click="makePrimary(hostName)"
          >
            {{ t('sites.hostNames.makePrimary', { host: hostName.displayName }) }}
          </button>
          <button
            v-if="canManage"
            type="button"
            class="danger"
            :disabled="removalBlockedReason(hostName) !== null"
            :title="removalBlockedReason(hostName) ?? undefined"
            :aria-describedby="removalBlockedReason(hostName) ? `remove-why-${hostName.hostName}` : undefined"
            @click="askToRemove(hostName, $event)"
          >
            {{ t('sites.hostNames.remove', { host: hostName.displayName }) }}
          </button>
          <span
            v-if="canManage && removalBlockedReason(hostName)"
            :id="`remove-why-${hostName.hostName}`"
            class="visually-hidden"
          >{{ removalBlockedReason(hostName) }}</span>
        </span>
      </li>
    </ul>

    <form
      v-if="canManage"
      novalidate
      @submit.prevent="add"
    >
      <div class="field">
        <label for="host-name-add">{{ t('sites.hostNames.add') }}</label>
        <input
          id="host-name-add"
          ref="addInput"
          v-model="newHostName"
          type="text"
          autocomplete="off"
          :aria-invalid="addError !== null"
          aria-describedby="host-name-add-help host-name-add-error"
        >
        <p
          id="host-name-add-help"
          class="help"
        >
          {{ t('sites.hostNames.addHelp') }}
        </p>
        <p
          v-if="addError"
          id="host-name-add-error"
          class="error"
          role="alert"
        >
          {{ addError }}
        </p>
      </div>
      <div class="actions">
        <button
          type="submit"
          class="primary"
          :disabled="adding || newHostName.trim() === ''"
        >
          {{ adding ? t('sites.hostNames.adding') : t('sites.hostNames.addButton') }}
        </button>
      </div>
    </form>

    <p
      v-if="actionError"
      class="error"
      role="alert"
    >
      {{ actionError }}
    </p>
    <p
      class="status"
      role="status"
    >
      {{ status }}
    </p>

    <ConfirmDialog
      v-if="pendingRemoval"
      dialog-id="remove-host-name"
      :title="t('sites.hostNames.confirmTitle', { host: pendingRemoval.displayName })"
      :body="t('sites.hostNames.confirmBody')"
      :confirm-label="t('sites.hostNames.confirm')"
      :cancel-label="t('sites.hostNames.cancel')"
      :busy-label="t('sites.hostNames.removing')"
      :busy="removing"
      danger
      @confirm="confirmRemoval"
      @cancel="closeRemoval"
    />
  </section>
</template>
