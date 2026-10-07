<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError, cmsApi, type CollectionReference } from '../../api/cms'
import { cmsErrorMessage } from './cmsErrors'

// 014 US5, FR-004: deleting a collection is permanent (clarification Q5), so the dialog says
// how many items go with it and asks for the collection's name before the button works. A
// refusal because other collections refer to it lists those fields (research CS-11).

const props = defineProps<{
  siteId: string
  collectionId: string
  name: string
  revision: number
  itemCount: number
}>()
const emit = defineEmits<{ deleted: []; cancel: [] }>()
const { t, te } = useI18n()

const typed = ref('')
const busy = ref(false)
const error = ref<string | null>(null)
const references = ref<CollectionReference[]>([])
const input = ref<HTMLInputElement | null>(null)

const matches = computed(() => typed.value.trim() === props.name)

onMounted(async () => {
  await nextTick()
  input.value?.focus()
})

async function confirm(): Promise<void> {
  if (!matches.value) return

  busy.value = true
  error.value = null
  references.value = []

  try {
    await cmsApi.remove(props.siteId, props.collectionId, props.revision)
    emit('deleted')
  } catch (caught) {
    error.value = cmsErrorMessage(t, te, caught)
    if (caught instanceof ApiError && caught.errorCode === 'cms.collection_referenced') {
      references.value = ((caught.problem as unknown as { references?: CollectionReference[] })?.references) ?? []
    }
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div
    class="dialog-backdrop"
    @click.self="emit('cancel')"
  >
    <form
      class="card dialog"
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="collection-delete-title"
      aria-describedby="collection-delete-body"
      novalidate
      @submit.prevent="confirm"
      @keydown.esc.prevent="emit('cancel')"
    >
      <h2 id="collection-delete-title">
        {{ t('cms.delete.title', { name }) }}
      </h2>
      <p id="collection-delete-body">
        {{ t('cms.delete.body', { count: itemCount }, itemCount) }}
      </p>
      <div class="field">
        <label for="collection-delete-name">{{ t('cms.delete.typeName', { name }) }}</label>
        <input
          id="collection-delete-name"
          ref="input"
          v-model="typed"
          type="text"
          autocomplete="off"
        >
      </div>
      <div
        v-if="error"
        class="error"
        role="alert"
      >
        <p>{{ references.length > 0 ? t('cms.delete.referenced') : error }}</p>
        <ul v-if="references.length > 0">
          <li
            v-for="reference in references"
            :key="`${reference.collectionId}-${reference.fieldId}`"
          >
            {{ t('cms.delete.reference', { field: reference.fieldName, collection: reference.collectionName }) }}
          </li>
        </ul>
      </div>
      <div class="actions">
        <button
          type="button"
          :disabled="busy"
          @click="emit('cancel')"
        >
          {{ t('cms.delete.cancel') }}
        </button>
        <button
          type="submit"
          class="danger"
          :disabled="busy || !matches"
        >
          {{ busy ? t('cms.delete.deleting') : t('cms.delete.confirm') }}
        </button>
      </div>
    </form>
  </div>
</template>
