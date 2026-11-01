<script setup lang="ts">
// Who else has this document open, who is editing it, and what this member can do about it
// (023 US2 and US3, contracts/admin-editing.md §2 and §4).
//
// One polite live region announces changes, so a screen reader hears when someone starts or
// stops editing without having to look (research CE-19). Times are the server's.
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Presence } from '../api/editing'
import type { EditingStatus } from './useEditingSession'
import './editing.css'

const props = defineProps<{
  kind: 'page' | 'component' | 'item'
  status: EditingStatus
  presence: Presence | null
  busy?: boolean
}>()
const emit = defineEmits<{ takeOver: []; moveHere: []; edit: []; reload: [] }>()
const { t, locale } = useI18n()

const what = computed(() => t(`editing.kinds.${props.kind}`))
const time = (iso: string | undefined) =>
  iso ? new Intl.DateTimeFormat(locale.value, { timeStyle: 'short' }).format(new Date(iso)) : ''

const holder = computed(() => props.presence?.lock?.holder.displayName ?? '')
const since = computed(() => time(props.presence?.lock?.since))
const inactiveMinutes = computed(() => Math.floor((props.presence?.lock?.inactiveSeconds ?? 0) / 60))
const viewers = computed(() =>
  [...new Set((props.presence?.others ?? []).filter((o) => !o.holdsLock).map((o) => o.member.displayName))].join(', '),
)

const message = computed(() => {
  switch (props.status) {
    case 'locked':
      return t('editing.presence.locked', { name: holder.value, what: what.value, since: since.value })
    case 'locked-stale':
      return t('editing.presence.stale', { name: holder.value, what: what.value, since: since.value, minutes: inactiveMinutes.value })
    case 'locked-self':
      return t('editing.presence.self', { what: what.value })
    case 'taken-over':
      return props.presence?.lostLock
        ? t('editing.presence.takenOver', { name: props.presence.lostLock.by.displayName, time: time(props.presence.lostLock.at) })
        : t('editing.presence.takenOverByUnknown', { name: holder.value || t('editing.presence.someoneElse') })
    case 'moved':
      return t('editing.presence.moved', { what: what.value })
    case 'view-only':
      return holder.value
        ? t('editing.presence.viewOnlyHeld', { what: what.value, name: holder.value, since: since.value })
        : t('editing.presence.viewOnly', { what: what.value })
    case 'free':
      return t('editing.presence.free', { what: what.value })
    default:
      return viewers.value ? t('editing.presence.alsoViewing', { names: viewers.value }) : ''
  }
})

const blocked = computed(() => props.status === 'locked' || props.status === 'locked-stale' || props.status === 'locked-self')

// Announced a moment after it changes, so the same text twice is read out again.
const announcement = ref('')
watch(message, (next) => {
  announcement.value = ''
  setTimeout(() => (announcement.value = next), 30)
})

function takeOver(): void {
  if (window.confirm(t('editing.presence.confirmTakeOver', { name: holder.value }))) emit('takeOver')
}
</script>

<template>
  <div class="editing-presence">
    <div
      v-if="message"
      class="editing-presence__banner"
      :class="{ 'editing-presence__banner--blocked': blocked || status === 'taken-over' || status === 'moved' }"
    >
      <p>{{ message }}</p>
      <p
        v-if="blocked"
        class="help"
      >
        {{ t('editing.presence.readOnlyHelp') }}
      </p>
      <div class="editing-presence__actions">
        <button
          v-if="status === 'locked-stale'"
          type="button"
          :disabled="busy"
          @click="takeOver"
        >
          {{ t('editing.presence.takeOver') }}
        </button>
        <button
          v-if="status === 'locked-self'"
          type="button"
          :disabled="busy"
          @click="emit('moveHere')"
        >
          {{ t('editing.presence.editHere') }}
        </button>
        <button
          v-if="status === 'free'"
          type="button"
          class="primary"
          :disabled="busy"
          @click="emit('edit')"
        >
          {{ t('editing.presence.edit') }}
        </button>
        <button
          v-if="status === 'taken-over' || status === 'moved'"
          type="button"
          :disabled="busy"
          @click="emit('reload')"
        >
          {{ t('editing.presence.reload') }}
        </button>
        <slot name="actions" />
      </div>
    </div>
    <p
      class="visually-hidden"
      aria-live="polite"
    >
      {{ announcement }}
    </p>
  </div>
</template>
