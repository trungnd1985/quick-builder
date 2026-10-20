<script setup lang="ts">
// How a Navigation block's top-level entries look (024 FR-017b). Unset follows the site's
// menu style, which the first option names; choosing it again removes the override.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import FieldShell from './FieldShell.vue'
import { describedBy, type ControlProps } from './types'
import type { Json } from '../../schema/subsetValidator'
import { useEditorStore } from '../../state/editorStore'
import type { MenuLook } from '../../tokens/defaultTokens'

const props = defineProps<ControlProps>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()
const store = useEditorStore()

const LOOKS: MenuLook[] = ['link', 'underline', 'pill', 'button']
const current = computed(() => (typeof props.value === 'string' && (LOOKS as string[]).includes(props.value) ? props.value : ''))
const siteLook = computed(() => store.tokens.menuStyle?.look ?? 'link')

function onChange(event: Event): void {
  const look = (event.target as HTMLSelectElement).value
  emit('change', look ? look : undefined)
}
</script>

<template>
  <FieldShell
    :id="id"
    :prop="prop"
    :label="label"
    :error="error"
  >
    <select
      :id="id"
      :value="current"
      :aria-invalid="error ? 'true' : undefined"
      :aria-describedby="describedBy(id, error)"
      @change="onChange"
    >
      <option value="">
        {{ t('editor.navigation.siteLook', { look: t(`design.menus.looks.${siteLook}`) }) }}
      </option>
      <option
        v-for="look in LOOKS"
        :key="look"
        :value="look"
      >
        {{ t(`design.menus.looks.${look}`) }}
      </option>
    </select>
  </FieldShell>
</template>
