<script setup lang="ts">
// 017 US5 (FR-032): when a live page or item stops being live, its address can send visitors
// to a page of the site instead of the not-found page. Unticked by default: taking content
// down is often meant to make it disappear. The page itself is chosen with PagePicker (024).
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import PagePicker, { type PickablePage } from './PagePicker.vue'

export type { PickablePage }

const props = defineProps<{
  id: string
  pages: readonly PickablePage[]
  /** The page being taken down, which cannot be its own target. */
  exceptPageId?: string
}>()

/** The chosen page's id, or null when no redirect is wanted. */
const target = defineModel<string | null>({ required: true })
const { t } = useI18n()

const choices = computed(() => props.pages.filter((p) => p.id !== props.exceptPageId && !p.templateCollectionId))
const wanted = ref(target.value !== null)
const chosen = ref(target.value ?? choices.value[0]?.id ?? '')

watch([wanted, chosen], ([on, id]) => {
  target.value = on && id ? id : null
})
</script>

<template>
  <div
    v-if="choices.length > 0"
    class="redirect-picker"
  >
    <label class="checkbox">
      <input
        :id="`${id}-wanted`"
        v-model="wanted"
        type="checkbox"
      >
      {{ t('redirects.offer.toPage') }}
    </label>
    <PagePicker
      v-if="wanted"
      :id="`${id}-page`"
      v-model="chosen"
      :label="t('redirects.offer.page')"
      :pages="pages"
      :except-page-id="exceptPageId"
    />
  </div>
</template>
