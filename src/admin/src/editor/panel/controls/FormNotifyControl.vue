<script setup lang="ts">
// Who is told about a submission, and how (018 FR-031 to FR-033, clarification 4): up to ten
// recipients, a subject, whether the email carries the submitted values, and which email field
// a reply goes to. A reply address needs the values in the email, so it is off while they are.
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ControlProps } from './types'
import { isObject, type Json, type JsonObject } from '../../schema/subsetValidator'
import { useEditorStore } from '../../state/editorStore'

const props = defineProps<ControlProps>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()
const store = useEditorStore()

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

const stored = computed<JsonObject>(() => (isObject(props.value) ? props.value : {}))
const recipientsText = ref('')
const subject = ref('')
const problem = ref<string | null>(null)

const sync = (value: JsonObject) => {
  recipientsText.value = Array.isArray(value.recipients) ? value.recipients.filter((r) => typeof r === 'string').join('\n') : ''
  subject.value = typeof value.subject === 'string' ? value.subject : ''
}
sync(stored.value)
watch(stored, sync)

const includeValues = computed(() => stored.value.includeValues !== false)
const replyFieldId = computed(() => (typeof stored.value.replyFieldId === 'string' ? stored.value.replyFieldId : ''))

/** The form's email fields: the only ones a reply can go to (rule 28.5). */
const emailFields = computed(() => {
  const fields = store.selectedNode?.props.fields
  return Array.isArray(fields) ? fields.filter(isObject).filter((f) => f.type === 'email') : []
})

const formName = computed(() => (typeof store.selectedNode?.props.name === 'string' ? store.selectedNode.props.name : ''))

function commit(next: Partial<{ recipients: string[]; subject: string; includeValues: boolean; replyFieldId: string }>): void {
  const recipients = next.recipients ?? (Array.isArray(stored.value.recipients) ? stored.value.recipients.filter((r): r is string => typeof r === 'string') : [])
  const nextSubject = (next.subject ?? subject.value).trim()
  const values = next.includeValues ?? includeValues.value
  const reply = values ? (next.replyFieldId ?? replyFieldId.value) : ''

  const notify: JsonObject = {}
  if (recipients.length > 0) notify.recipients = recipients
  if (nextSubject) notify.subject = nextSubject
  if (!values) notify.includeValues = false
  if (reply) notify.replyFieldId = reply
  emit('change', Object.keys(notify).length > 0 ? notify : undefined)
}

function commitRecipients(): void {
  const recipients = recipientsText.value.split(/[\n,;]+/).map((r) => r.trim()).filter(Boolean)
  const invalid = recipients.find((r) => r.length > 254 || !EMAIL.test(r))
  if (invalid) {
    problem.value = t('editor.form.notify.recipientsInvalid', { address: invalid })
    return
  }
  if (recipients.length > 10) {
    problem.value = t('editor.form.notify.tooMany')
    return
  }
  problem.value = null
  commit({ recipients })
}
</script>

<template>
  <fieldset
    class="editor-field"
    :data-prop="prop"
  >
    <legend class="editor-field__label">
      {{ label }}
    </legend>
    <div class="field">
      <label :for="`${id}-recipients`">{{ t('editor.form.notify.recipients') }}</label>
      <textarea
        :id="`${id}-recipients`"
        v-model="recipientsText"
        rows="3"
        :aria-invalid="problem ? 'true' : undefined"
        :aria-describedby="`${id}-recipients-help`"
        @change="commitRecipients"
      />
      <p
        :id="`${id}-recipients-help`"
        :class="problem ? 'field-error' : 'help'"
      >
        {{ problem ?? t('editor.form.notify.recipientsHelp') }}
      </p>
    </div>
    <div class="field">
      <label :for="`${id}-subject`">{{ t('editor.form.notify.subject') }}</label>
      <input
        :id="`${id}-subject`"
        v-model="subject"
        type="text"
        maxlength="200"
        :placeholder="t('editor.form.notify.subjectDefault', { name: formName })"
        @change="commit({ subject })"
      >
    </div>
    <label class="checkbox">
      <input
        type="checkbox"
        :checked="includeValues"
        :aria-describedby="`${id}-values-help`"
        @change="commit({ includeValues: ($event.target as HTMLInputElement).checked })"
      >
      {{ t('editor.form.notify.includeValues') }}
    </label>
    <p
      :id="`${id}-values-help`"
      class="help"
    >
      {{ t('editor.form.notify.includeValuesHelp') }}
    </p>
    <div class="field">
      <label :for="`${id}-reply`">{{ t('editor.form.notify.replyField') }}</label>
      <select
        :id="`${id}-reply`"
        :value="replyFieldId"
        :disabled="!includeValues"
        @change="commit({ replyFieldId: ($event.target as HTMLSelectElement).value })"
      >
        <option value="">
          {{ t('editor.form.notify.noReplyField') }}
        </option>
        <option
          v-for="field in emailFields"
          :key="String(field.id)"
          :value="String(field.id)"
        >
          {{ field.label }}
        </option>
      </select>
    </div>
    <p
      v-if="error"
      :id="`${id}-error`"
      class="field-error"
    >
      {{ error }}
    </p>
  </fieldset>
</template>
