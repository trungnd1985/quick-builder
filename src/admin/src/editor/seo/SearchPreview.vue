<script setup lang="ts">
// How the page may look in a search listing (017 FR-004): title, address and description,
// shortened roughly as search engines shorten them. A sketch, not a promise.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RECOMMENDED_LENGTH } from './seoFields'

const props = defineProps<{ title: string; address: string; description: string; id: string }>()
const { t } = useI18n()

const shorten = (value: string, limit: number) => {
  const chars = Array.from(value)
  if (chars.length <= limit) return value
  const cut = chars.slice(0, limit - 1).join('')
  const space = cut.lastIndexOf(' ')
  return `${(space > limit / 2 ? cut.slice(0, space) : cut).trimEnd()}…`
}

const shownTitle = computed(() => shorten(props.title, RECOMMENDED_LENGTH.title!))
const shownDescription = computed(() => shorten(props.description, RECOMMENDED_LENGTH.description!))
</script>

<template>
  <section
    class="seo-preview seo-preview--search"
    :aria-labelledby="`${id}-heading`"
    role="region"
  >
    <h3 :id="`${id}-heading`">
      {{ t('editor.seo.searchPreview') }}
    </h3>
    <p class="seo-preview__address">
      {{ address }}
    </p>
    <p class="seo-preview__title">
      {{ shownTitle }}
    </p>
    <p
      v-if="shownDescription"
      class="seo-preview__description"
    >
      {{ shownDescription }}
    </p>
    <p
      v-else
      class="seo-preview__description seo-preview__description--none"
    >
      {{ t('editor.seo.noDescription') }}
    </p>
  </section>
</template>
