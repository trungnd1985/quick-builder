<script setup lang="ts">
// Restoring a trash entry (021 FR-014 to FR-017, research TR-10, contracts/admin-trash.md §4):
// the plan is shown first — where it goes, a changed address, home or template effects — and
// the restore confirms the address the member saw. If something changed meanwhile the server
// answers trash.plan_changed with the new plan, which replaces the old one here.
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError } from '../../api/request'
import { mediaApi, type MediaFolder } from '../../api/media'
import { pagesApi, type PageNode } from '../../api/pages'
import { trashApi, type RestoreChoice, type RestorePlan, type TrashEntry } from '../../api/trash'

const props = defineProps<{ siteId: string; entry: TrashEntry }>()
const emit = defineEmits<{ restored: [published: boolean]; cancel: [] }>()
const { t } = useI18n()

const plan = ref<RestorePlan | null>(null)
const choice = ref<RestoreChoice>({})
const busy = ref(false)
const error = ref<string | null>(null)
const changed = ref(false)
const cancelButton = ref<HTMLButtonElement | null>(null)
// "Choose another place": the live pages, or the folders, to choose from.
const pages = ref<PageNode[]>([])
const folders = ref<MediaFolder[]>([])

const choosing = ref(false)
const needsChoice = computed(() => props.entry.place?.parentState === 'trashed' || props.entry.place?.parentState === 'gone')

async function loadPlan(): Promise<void> {
  error.value = null
  try {
    plan.value = await trashApi.restorePlan(props.siteId, props.entry.kind, props.entry.id, choice.value)
  } catch (caught) {
    plan.value = null
    error.value = messageOf(caught)
  }
}

onMounted(async () => {
  // Story 4: a page whose parent is in the trash or gone goes to the top level unless the member chooses.
  if (needsChoice.value) choice.value = { placement: 'top' }
  await loadPlan()
  await nextTick()
  cancelButton.value?.focus()
})

function messageOf(caught: unknown): string {
  const code = caught instanceof ApiError ? caught.problem?.errorCode : undefined
  switch (code) {
    case 'trash.not_in_trash':
      return t('trash.restore.notInTrash')
    case 'trash.placement_required':
      return t('trash.restore.placementRequired')
    case 'identity.forbidden':
      return t('trash.restore.forbidden')
    default:
      return caught instanceof ApiError && caught.problem?.detail ? caught.problem.detail : t('trash.restore.failed')
  }
}

async function restore(publish: boolean): Promise<void> {
  if (!plan.value) return
  busy.value = true
  error.value = null
  try {
    const result = await trashApi.restore(props.siteId, props.entry.kind, props.entry.id, plan.value.address, { ...choice.value, publish })
    emit('restored', result.published)
  } catch (caught) {
    const problem = caught instanceof ApiError ? caught.problem : undefined
    if (problem?.errorCode === 'trash.plan_changed') {
      // FR-016: never restore somewhere the member was not told about.
      changed.value = true
      const fresh = (problem as unknown as { plan?: RestorePlan }).plan
      if (fresh) plan.value = fresh
      else await loadPlan()
    } else {
      error.value = messageOf(caught)
    }
  } finally {
    busy.value = false
  }
}

async function choose(placement: string): Promise<void> {
  choice.value = { placement }
  if (placement === 'chosen') {
    // Nothing to plan until a place is picked.
    plan.value = plan.value ? { ...plan.value, placement: 'chosen' } : plan.value
    choosing.value = true
    if (props.entry.kind === 'file') folders.value = (await mediaApi.folders.list(props.siteId)).folders
    else pages.value = (await pagesApi.tree(props.siteId)).pages.filter((p) => !p.templateCollectionId)
    return
  }

  choosing.value = false
  await loadPlan()
}

async function pick(event: Event): Promise<void> {
  const id = (event.target as HTMLSelectElement).value
  if (!id) return
  choice.value = props.entry.kind === 'file' ? { placement: 'chosen', folderId: id } : { placement: 'chosen', parentId: id }
  choosing.value = false
  await loadPlan()
  choosing.value = true
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('cancel')
  }
}
</script>

<template>
  <Teleport to="body">
    <div
      class="dialog-backdrop"
      @click.self="emit('cancel')"
    >
      <div
        class="card dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="restore-title"
        aria-describedby="restore-body"
        @keydown="onKeydown"
      >
        <h2 id="restore-title">
          {{ t('trash.restore.title', { label: entry.label }) }}
        </h2>

        <div
          id="restore-body"
          aria-live="polite"
        >
          <p
            v-if="changed"
            role="status"
          >
            {{ t('trash.restore.changed') }}
          </p>

          <fieldset v-if="plan && (plan.placementOptions?.length ?? 0) > 1">
            <legend>{{ t('trash.restore.where') }}</legend>
            <label
              v-for="option in plan.placementOptions"
              :key="option"
            >
              <input
                type="radio"
                name="placement"
                :value="option"
                :checked="plan.placement === option"
                @change="choose(option)"
              >
              {{ t(`trash.restore.placement.${option}`) }}
            </label>
          </fieldset>

          <div
            v-if="choosing"
            class="field"
          >
            <label for="restore-place">{{ t('trash.restore.choosePlace') }}</label>
            <select
              id="restore-place"
              @change="pick"
            >
              <option value="">
                {{ t('trash.restore.choosePlaceFirst') }}
              </option>
              <template v-if="entry.kind === 'file'">
                <option
                  v-for="folder in folders"
                  :key="folder.id"
                  :value="folder.id"
                >
                  {{ folder.name }}
                </option>
              </template>
              <template v-else>
                <option
                  v-for="page in pages"
                  :key="page.id"
                  :value="page.id"
                >
                  {{ page.title }} ({{ page.path }})
                </option>
              </template>
            </select>
          </div>

          <template v-if="plan">
            <p v-if="entry.kind === 'page' && plan.target">
              {{ plan.target.parentId ? t('trash.restore.under', { parent: plan.target.label }) : t('trash.restore.topLevel') }}
            </p>
            <p v-if="plan.addressChanged">
              {{ t('trash.restore.addressChanged', { old: plan.previousAddress ?? '', new: plan.address }) }}
            </p>
            <p v-if="plan.becomesHome">
              {{ t('trash.restore.becomesHome') }}
            </p>
            <p v-if="plan.templateDropped">
              {{ t('trash.restore.templateDropped') }}
            </p>
            <p v-if="plan.folderGone">
              {{ t('trash.restore.folderGone') }}
            </p>
            <ul v-if="plan.parentsToRestore && plan.parentsToRestore.length > 0">
              <li
                v-for="parent in plan.parentsToRestore"
                :key="parent.id"
              >
                {{ t('trash.restore.alsoRestores', { label: parent.label, address: parent.address }) }}
              </li>
            </ul>
            <p v-if="plan.schemaIssues && plan.schemaIssues.length > 0">
              {{ t('trash.restore.needsAttention') }}
            </p>
            <ul v-if="plan.schemaIssues && plan.schemaIssues.length > 0">
              <li
                v-for="issue in plan.schemaIssues"
                :key="issue"
              >
                {{ issue }}
              </li>
            </ul>
            <p v-if="plan.wasLive">
              {{ t('trash.restore.wasLive') }}
            </p>
            <p v-else-if="plan.hadSchedule">
              {{ t('trash.restore.wasScheduled') }}
            </p>
          </template>
        </div>

        <p
          v-if="error"
          class="field-error"
          role="alert"
        >
          {{ error }}
        </p>

        <div class="dialog-actions">
          <button
            ref="cancelButton"
            type="button"
            @click="emit('cancel')"
          >
            {{ t('trash.restore.cancel') }}
          </button>
          <button
            v-if="plan?.canRestoreAndPublish"
            type="button"
            :disabled="busy"
            @click="restore(true)"
          >
            {{ t('trash.restore.andPublish') }}
          </button>
          <button
            type="button"
            class="primary"
            :disabled="busy || !plan || (choosing && !choice.parentId && !choice.folderId)"
            @click="restore(false)"
          >
            {{ t('trash.restore.confirm') }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
