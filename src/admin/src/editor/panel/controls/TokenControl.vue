<script setup lang="ts">
// A colour, spacing or text-size property: one of the site's tokens, stored as a reference
// ({ token: id }) so changing the token later changes this block (FR-020, FR-033). "Default"
// removes the property. A reference to a token the site deleted is marked, and the block uses
// its default meanwhile (clarification Q4).
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import FieldShell from './FieldShell.vue'
import { describedBy, type ControlProps } from './types'
import type { Json } from '../../schema/subsetValidator'
import { useEditorStore } from '../../state/editorStore'
import { isDeletedToken, tokenValue, type TokenKind } from '../../tokens/tokenCss'

const props = defineProps<ControlProps & { kind: TokenKind }>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()
const store = useEditorStore()

const DEFAULT = ''

const tokens = computed(() => {
  const set = store.tokens
  return props.kind === 'color' ? set.colors : props.kind === 'text' ? set.typography.sizes : set.spacing
})

const selectedId = (value: Json | undefined): string =>
  value && typeof value === 'object' && !Array.isArray(value) && typeof value.token === 'string' ? value.token : DEFAULT

const current = computed(() => selectedId(props.value))
const deleted = computed(() => isDeletedToken(props.value, props.kind, store.tokens))
const defaultName = computed(() => {
  const id = selectedId(props.defaultValue)
  return tokens.value.find((token) => token.id === id)?.name ?? t('editor.panel.token.none')
})
const swatch = computed(() => (props.kind === 'color' ? tokenValue(props.value, 'color', store.tokens, props.defaultValue, 'transparent') : null))

function onChange(event: Event): void {
  const id = (event.target as HTMLSelectElement).value
  emit('change', id === DEFAULT ? undefined : { token: id })
}
</script>

<template>
  <FieldShell
    :id="id"
    :prop="prop"
    :label="label"
    :error="error"
    :help="deleted ? t('editor.panel.token.deleted') : undefined"
  >
    <div class="editor-token">
      <span
        v-if="swatch"
        class="editor-token__swatch"
        :style="{ background: swatch }"
        aria-hidden="true"
      />
      <select
        :id="id"
        :value="deleted ? DEFAULT : current"
        :aria-invalid="error ? 'true' : undefined"
        :aria-describedby="describedBy(id, error, deleted ? 'deleted' : undefined)"
        @change="onChange"
      >
        <option :value="DEFAULT">
          {{ t('editor.panel.token.default', { name: defaultName }) }}
        </option>
        <option
          v-for="token in tokens"
          :key="token.id"
          :value="token.id"
        >
          {{ token.name }}
        </option>
      </select>
    </div>
  </FieldShell>
</template>
