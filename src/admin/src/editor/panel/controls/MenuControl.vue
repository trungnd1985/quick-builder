<script setup lang="ts">
// Which of the site's menus a Navigation block shows (024 FR-017, FR-018). A menu that no longer
// exists stays selected and is named as missing, so the designer sees why publishing is refused.
// Members who may change menus get a link to edit the chosen one.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import FieldShell from './FieldShell.vue'
import { describedBy, type ControlProps } from './types'
import type { Json } from '../../schema/subsetValidator'
import { useSiteMenus } from '../../state/useSiteMenus'

const props = defineProps<ControlProps>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()

const menus = useSiteMenus(props.siteId)
const current = computed(() => (typeof props.value === 'string' ? props.value : ''))
const items = computed(() => menus.value?.items ?? [])
const missing = computed(() => current.value !== '' && menus.value !== null && !items.value.some((m) => m.id === current.value))
const canManage = computed(() => menus.value?.permissions.canManage === true)

function onChange(event: Event): void {
  const id = (event.target as HTMLSelectElement).value
  if (id) emit('change', id)
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
      :aria-invalid="error || missing ? 'true' : undefined"
      :aria-describedby="describedBy(id, error, missing ? 'missing' : undefined)"
      @change="onChange"
    >
      <option
        v-if="missing"
        :value="current"
      >
        {{ t('editor.navigation.missingOption') }}
      </option>
      <option
        v-for="menu in items"
        :key="menu.id"
        :value="menu.id"
      >
        {{ menu.publication.state === 'draft' ? t('editor.navigation.draftOption', { name: menu.name }) : menu.name }}
      </option>
    </select>
    <p
      v-if="missing"
      :id="`${id}-help`"
      class="field-error"
    >
      {{ t('editor.navigation.missing') }}
    </p>
    <RouterLink
      v-if="canManage && current && !missing"
      class="button-link"
      :to="{ name: 'site-menu', params: { siteId, menuId: current } }"
    >
      {{ t('editor.navigation.openMenu') }}
    </RouterLink>
  </FieldShell>
</template>
