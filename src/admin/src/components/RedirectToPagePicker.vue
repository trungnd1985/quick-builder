<script setup lang="ts">
// 017 US5 (FR-032): when a live page or item stops being live, its address can send visitors
// to a page of the site instead of the not-found page. Unticked by default: taking content
// down is often meant to make it disappear.
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

export interface PickablePage {
  id: string
  title: string
  path: string
  templateCollectionId?: string | null
}

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
    <div
      v-if="wanted"
      class="field"
    >
      <label :for="`${id}-page`">{{ t('redirects.offer.page') }}</label>
      <select
        :id="`${id}-page`"
        v-model="chosen"
      >
        <option
          v-for="page in choices"
          :key="page.id"
          :value="page.id"
        >
          {{ page.title }} ({{ page.path }})
        </option>
      </select>
    </div>
  </div>
</template>
