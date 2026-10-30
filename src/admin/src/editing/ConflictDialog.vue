<script setup lang="ts">
// A save was refused because someone else changed the document first (023 FR-006 to FR-008,
// research CE-10, contracts/admin-editing.md §3).
//
// Says who and when, and that nothing was overwritten. Offers to reload the latest version
// (after a warning), to keep viewing this version read-only to copy from it, or, to members
// who may create one, to save it as a new copy (FR-020a). There is no way to overwrite the
// other change.
import { computed, nextTick, onMounted, ref } from 'vue'
import type { RouteLocationRaw } from 'vue-router'
import { useI18n } from 'vue-i18n'
import type { ConflictDetails } from './conflict'
import './editing.css'

const props = defineProps<{
  kind: 'page' | 'component' | 'item'
  conflict: ConflictDetails | null
  reloading: boolean
  /** For items: the labels of the fields the other change altered (FR-007). */
  changedFields?: string[]
  /** FR-020a: whether this member may create a document of this kind. */
  canSaveCopy?: boolean
  copying?: boolean
  /** After saving a copy: where it is. */
  copy?: { to: RouteLocationRaw; label: string } | null
  /** For items: the labels of fields whose values the copy could not keep (FR-022). */
  droppedFields?: string[]
}>()
const emit = defineEmits<{ reload: []; keepViewing: []; saveCopy: [] }>()
const { t, locale } = useI18n()

const first = ref<HTMLButtonElement | null>(null)
// After the next tick: the dialog may be mounted before it is in the document.
onMounted(() => void nextTick(() => first.value?.focus()))

const what = computed(() => t(`editing.kinds.${props.kind}`))
const when = computed(() =>
  props.conflict?.changedAtUtc
    ? new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(props.conflict.changedAtUtc))
    : '',
)
const who = computed(() => {
  if (props.conflict?.changeKind === 'schema_change') return t('editing.conflict.byFieldChange', { when: when.value })
  if (props.conflict?.changedBy) return t('editing.conflict.byMember', { name: props.conflict.changedBy.displayName, what: what.value, when: when.value })
  return t('editing.conflict.byUnknown', { what: what.value })
})

function reload(): void {
  if (window.confirm(t('editing.conflict.confirmReload'))) emit('reload')
}
</script>

<template>
  <div
    class="editing-conflict"
    role="alertdialog"
    aria-modal="true"
    aria-labelledby="editing-conflict-title"
    aria-describedby="editing-conflict-body"
  >
    <h2 id="editing-conflict-title">
      {{ t('editing.conflict.title', { what }) }}
    </h2>
    <div id="editing-conflict-body">
      <p>{{ who }} {{ t('editing.conflict.nothingOverwritten') }}</p>
      <template v-if="changedFields && changedFields.length > 0">
        <p id="editing-conflict-fields">
          {{ t('editing.conflict.changedFields') }}
        </p>
        <ul :aria-label="t('editing.conflict.changedFields')">
          <li
            v-for="label in changedFields"
            :key="label"
          >
            {{ label }}
          </li>
        </ul>
      </template>
      <p>{{ t('editing.conflict.choices') }}</p>
      <p v-if="!canSaveCopy">
        {{ t('editing.copy.notAllowed') }}
      </p>
      <p
        v-if="copy"
        role="status"
      >
        {{ t('editing.copy.saved') }}
        <RouterLink :to="copy.to">
          {{ copy.label }}
        </RouterLink>
        <template v-if="droppedFields && droppedFields.length > 0">
          {{ t('editing.copy.dropped', { fields: droppedFields.join(', ') }) }}
        </template>
      </p>
    </div>
    <div class="editing-conflict__actions">
      <button
        ref="first"
        type="button"
        class="primary"
        :disabled="reloading"
        @click="reload"
      >
        {{ reloading ? t('editing.conflict.reloading') : t('editing.conflict.reload') }}
      </button>
      <button
        type="button"
        @click="emit('keepViewing')"
      >
        {{ t('editing.conflict.keepViewing') }}
      </button>
      <button
        v-if="canSaveCopy && !copy"
        type="button"
        :disabled="copying"
        @click="emit('saveCopy')"
      >
        {{ copying ? t('editing.copy.saving') : t('editing.copy.save') }}
      </button>
    </div>
  </div>
</template>
