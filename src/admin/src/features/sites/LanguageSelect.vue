<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { sitesApi } from '../../api/sites'

// 009 research S-09: the server owns the list of supported languages; this shows each
// tag's name in the shell's own language with the browser's Intl.DisplayNames, so no
// table of language names ships with the app or needs translating.

defineProps<{ id: string; disabled?: boolean; describedby?: string }>()
const model = defineModel<string>({ required: true })

const { locale } = useI18n()
const tags = ref<string[]>([])

// One request per select; a failed one may be retried by the next mount.
let pending: Promise<string[]> | null = null
function loadTags(): Promise<string[]> {
  pending ??= sitesApi.languages().then((l) => (Array.isArray(l?.languages) ? l.languages : []))
  return pending
}

onMounted(async () => {
  try {
    tags.value = await loadTags()
  } catch {
    // Without the list, still offer the current value so the form stays usable.
    pending = null
    tags.value = []
  }
})

const names = computed(() => {
  let display: Intl.DisplayNames | null = null
  try {
    display = new Intl.DisplayNames([locale.value], { type: 'language' })
  } catch {
    display = null
  }

  return (tag: string): string => {
    const name = display?.of(tag)
    return name && name !== tag ? `${name} (${tag})` : tag
  }
})

const options = computed(() => {
  const all = tags.value.includes(model.value) || !model.value ? tags.value : [model.value, ...tags.value]
  return all
    .map((tag) => ({ tag, label: names.value(tag) }))
    .sort((a, b) => a.label.localeCompare(b.label, locale.value))
})
</script>

<template>
  <select
    :id="id"
    v-model="model"
    :disabled="disabled"
    :aria-describedby="describedby"
  >
    <option
      v-for="option in options"
      :key="option.tag"
      :value="option.tag"
    >
      {{ option.label }}
    </option>
  </select>
</template>
