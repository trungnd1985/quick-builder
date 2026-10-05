<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError, mediaApi, type MediaFolder } from '../../api/media'

// 013 US1: a site's folders as an ARIA tree (the treeview pattern: arrows move, Right opens
// or enters, Left closes or leaves, Home/End jump, Enter selects), with create, rename, move
// and delete for the selected folder (FR-001, FR-002, FR-004). The first item is the
// library's top level. Folder names are data: always rendered as text.

const props = defineProps<{
  siteId: string
  /** The selected folder; null is the top level. */
  selectedId: string | null
  /** Whether the member may change folders (the upload capability, MV-24). */
  canEdit?: boolean
}>()

const emit = defineEmits<{ select: [folderId: string | null]; changed: [] }>()

const { t, te } = useI18n()

interface Node {
  id: string | null
  name: string
  level: number
  hasChildren: boolean
}

const folders = ref<MediaFolder[]>([])
const expanded = ref<Set<string>>(new Set())
const focusedId = ref<string | null>(null)
const loading = ref(true)
const error = ref('')
const announcement = ref('')
const root = ref<HTMLElement | null>(null)

type Mode = 'none' | 'create' | 'rename' | 'move' | 'delete'
const mode = ref<Mode>('none')
const nameInput = ref('')
const moveTarget = ref<string>('')
const busy = ref(false)
const formError = ref('')
const nameField = ref<HTMLInputElement | null>(null)

const byParent = computed(() => {
  const map = new Map<string | null, MediaFolder[]>()
  for (const folder of folders.value) {
    const siblings = map.get(folder.parentId) ?? []
    siblings.push(folder)
    map.set(folder.parentId, siblings)
  }

  for (const siblings of map.values()) {
    siblings.sort((a, b) => a.name.localeCompare(b.name))
  }

  return map
})

/** The visible items, depth first, the top level first. */
const visible = computed<Node[]>(() => {
  const nodes: Node[] = [{ id: null, name: t('media.folders.topLevel'), level: 1, hasChildren: (byParent.value.get(null)?.length ?? 0) > 0 }]

  const walk = (parentId: string | null, level: number): void => {
    for (const folder of byParent.value.get(parentId) ?? []) {
      const hasChildren = (byParent.value.get(folder.id)?.length ?? 0) > 0
      nodes.push({ id: folder.id, name: folder.name, level, hasChildren })
      if (hasChildren && expanded.value.has(folder.id)) {
        walk(folder.id, level + 1)
      }
    }
  }

  walk(null, 2)
  return nodes
})

const selectedFolder = computed(() => folders.value.find((f) => f.id === props.selectedId) ?? null)

/** Folders the selected one may move into: not itself or anything inside it. */
const moveTargets = computed(() => {
  const selected = props.selectedId
  if (!selected) {
    return []
  }

  const inside = new Set<string>([selected])
  let grew = true
  while (grew) {
    grew = false
    for (const folder of folders.value) {
      if (folder.parentId && inside.has(folder.parentId) && !inside.has(folder.id)) {
        inside.add(folder.id)
        grew = true
      }
    }
  }

  return folders.value.filter((f) => !inside.has(f.id)).sort((a, b) => pathOf(a).localeCompare(pathOf(b)))
})

function pathOf(folder: MediaFolder): string {
  const names = [folder.name]
  let parent = folder.parentId
  while (parent) {
    const next = folders.value.find((f) => f.id === parent)
    if (!next) break
    names.unshift(next.name)
    parent = next.parentId
  }

  return names.join(' / ')
}

onMounted(load)
watch(() => props.siteId, load)

async function load(): Promise<void> {
  loading.value = true
  error.value = ''
  try {
    const tree = await mediaApi.folders.list(props.siteId)
    folders.value = tree.folders

    // Open the path to the selected folder, so it can be seen.
    let parent = folders.value.find((f) => f.id === props.selectedId)?.parentId ?? null
    while (parent) {
      expanded.value.add(parent)
      parent = folders.value.find((f) => f.id === parent)?.parentId ?? null
    }

    if (props.selectedId && !folders.value.some((f) => f.id === props.selectedId)) {
      // A remembered folder that no longer exists: back to the top level.
      emit('select', null)
    }
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : String(caught)
  } finally {
    loading.value = false
  }
}

function keyOf(id: string | null): string {
  return id ?? '__top__'
}

async function focusItem(id: string | null): Promise<void> {
  focusedId.value = id
  await nextTick()
  root.value?.querySelector<HTMLElement>(`[data-folder-key="${keyOf(id)}"]`)?.focus()
}

function select(id: string | null): void {
  focusedId.value = id
  emit('select', id)
}

async function onKeydown(event: KeyboardEvent, node: Node): Promise<void> {
  const index = visible.value.findIndex((n) => n.id === node.id)
  const move = async (target: Node | undefined): Promise<void> => {
    if (target) {
      event.preventDefault()
      await focusItem(target.id)
    }
  }

  switch (event.key) {
    case 'ArrowDown':
      await move(visible.value[index + 1])
      break
    case 'ArrowUp':
      await move(visible.value[index - 1])
      break
    case 'Home':
      await move(visible.value[0])
      break
    case 'End':
      await move(visible.value.at(-1))
      break
    case 'ArrowRight':
      if (node.id && node.hasChildren) {
        event.preventDefault()
        if (!expanded.value.has(node.id)) {
          expanded.value = new Set([...expanded.value, node.id])
        } else {
          await move(visible.value[index + 1])
        }
      }
      break
    case 'ArrowLeft':
      event.preventDefault()
      if (node.id && expanded.value.has(node.id)) {
        const next = new Set(expanded.value)
        next.delete(node.id)
        expanded.value = next
      } else if (node.id) {
        const parent = folders.value.find((f) => f.id === node.id)?.parentId ?? null
        await focusItem(parent)
      }
      break
    case 'Enter':
    case ' ':
      event.preventDefault()
      select(node.id)
      break
  }
}

function toggle(node: Node): void {
  if (!node.id) return
  const next = new Set(expanded.value)
  if (next.has(node.id)) next.delete(node.id)
  else next.add(node.id)
  expanded.value = next
}

async function begin(next: Mode): Promise<void> {
  mode.value = next
  formError.value = ''
  nameInput.value = next === 'rename' ? selectedFolder.value?.name ?? '' : ''
  moveTarget.value = ''
  await nextTick()
  nameField.value?.focus()
}

function cancel(): void {
  mode.value = 'none'
  formError.value = ''
  void focusItem(props.selectedId)
}

function reasonFor(caught: unknown): string {
  const code = caught instanceof ApiError ? caught.errorCode : undefined
  const key = code?.startsWith('media.') ? `media.folders.errors.${code.slice('media.'.length)}` : undefined
  return key && te(key) ? t(key) : t('media.folders.errors.unknown')
}

async function submit(): Promise<void> {
  busy.value = true
  formError.value = ''
  try {
    if (mode.value === 'create') {
      const created = await mediaApi.folders.create(props.siteId, nameInput.value, props.selectedId)
      if (props.selectedId) expanded.value = new Set([...expanded.value, props.selectedId])
      announcement.value = t('media.folders.created', { name: created.name })
      mode.value = 'none'
      await load()
      emit('changed')
      select(created.id)
      await focusItem(created.id)
    } else if (mode.value === 'rename' && props.selectedId) {
      const renamed = await mediaApi.folders.update(props.siteId, props.selectedId, { name: nameInput.value })
      announcement.value = t('media.folders.renamed', { name: renamed.name })
      mode.value = 'none'
      await load()
      emit('changed')
      await focusItem(renamed.id)
    } else if (mode.value === 'move' && props.selectedId) {
      const parentId = moveTarget.value === '' ? null : moveTarget.value
      const moved = await mediaApi.folders.update(props.siteId, props.selectedId, { parentId })
      if (parentId) expanded.value = new Set([...expanded.value, parentId])
      announcement.value = t('media.folders.moved', { name: moved.name })
      mode.value = 'none'
      await load()
      emit('changed')
      await focusItem(moved.id)
    } else if (mode.value === 'delete' && props.selectedId) {
      const name = selectedFolder.value?.name ?? ''
      const parent = selectedFolder.value?.parentId ?? null
      await mediaApi.folders.remove(props.siteId, props.selectedId)
      announcement.value = t('media.folders.deleted', { name })
      mode.value = 'none'
      await load()
      emit('changed')
      select(parent)
      await focusItem(parent)
    }
  } catch (caught) {
    formError.value = reasonFor(caught)
    if (caught instanceof ApiError && caught.errorCode === 'media.folder_conflict') {
      // Someone changed the folders meanwhile: show the current tree.
      await load()
    }
  } finally {
    busy.value = false
  }
}

defineExpose({ reload: load })
</script>

<template>
  <nav
    ref="root"
    class="folder-tree"
    :aria-label="t('media.folders.label')"
  >
    <p
      class="visually-hidden"
      role="status"
    >
      {{ announcement }}
    </p>

    <p
      v-if="loading"
      class="status"
      role="status"
    >
      {{ t('media.folders.loading') }}
    </p>
    <p
      v-else-if="error"
      class="error-card"
      role="alert"
    >
      {{ t('media.folders.loadFailed') }}
    </p>

    <ul
      v-else
      role="tree"
      :aria-label="t('media.folders.label')"
    >
      <li
        v-for="node in visible"
        :key="keyOf(node.id)"
        role="treeitem"
        :aria-level="node.level"
        :aria-selected="node.id === selectedId"
        :aria-expanded="node.id && node.hasChildren ? expanded.has(node.id) : undefined"
        :data-folder-key="keyOf(node.id)"
        :tabindex="node.id === (focusedId ?? selectedId) ? 0 : -1"
        :class="{ selected: node.id === selectedId }"
        :style="{ paddingInlineStart: `${0.5 + (node.level - 1)}rem` }"
        @click.self="select(node.id)"
        @keydown="onKeydown($event, node)"
      >
        <button
          v-if="node.id && node.hasChildren"
          type="button"
          class="link toggle"
          tabindex="-1"
          aria-hidden="true"
          @click="toggle(node)"
        >
          <span
            class="toggle-icon"
            :class="{ open: expanded.has(node.id) }"
          />
        </button>
        <span @click="select(node.id)">{{ node.name }}</span>
      </li>
    </ul>

    <div
      v-if="canEdit && !loading && !error"
      class="folder-actions"
    >
      <template v-if="mode === 'none'">
        <button
          type="button"
          @click="begin('create')"
        >
          {{ t('media.folders.new') }}
        </button>
        <template v-if="selectedFolder">
          <button
            type="button"
            @click="begin('rename')"
          >
            {{ t('media.folders.rename') }}
          </button>
          <button
            type="button"
            @click="begin('move')"
          >
            {{ t('media.folders.move') }}
          </button>
          <button
            type="button"
            class="danger"
            @click="begin('delete')"
          >
            {{ t('media.folders.delete') }}
          </button>
        </template>
      </template>

      <form
        v-else
        class="folder-form"
        @submit.prevent="submit"
        @keydown.esc.prevent="cancel"
      >
        <label
          v-if="mode === 'create' || mode === 'rename'"
          class="field"
        >
          <span>{{ mode === 'create' ? t('media.folders.newName') : t('media.folders.renameName') }}</span>
          <input
            ref="nameField"
            v-model="nameInput"
            type="text"
            maxlength="100"
            required
          >
        </label>

        <label
          v-else-if="mode === 'move'"
          class="field"
        >
          <span>{{ t('media.folders.moveTo', { name: selectedFolder?.name ?? '' }) }}</span>
          <select v-model="moveTarget">
            <option value="">{{ t('media.folders.topLevel') }}</option>
            <option
              v-for="folder in moveTargets"
              :key="folder.id"
              :value="folder.id"
            >{{ pathOf(folder) }}</option>
          </select>
        </label>

        <p v-else>
          {{ t('media.folders.deleteConfirm', { name: selectedFolder?.name ?? '' }) }}
        </p>

        <p
          v-if="formError"
          class="error"
          role="alert"
        >
          {{ formError }}
        </p>

        <div class="actions">
          <button
            type="button"
            :disabled="busy"
            @click="cancel"
          >
            {{ t('media.folders.cancel') }}
          </button>
          <button
            type="submit"
            :class="mode === 'delete' ? 'danger' : 'primary'"
            :disabled="busy"
          >
            {{ t(`media.folders.submit.${mode}`) }}
          </button>
        </div>
      </form>
    </div>
  </nav>
</template>

<style scoped>
/* The disclosure arrow is drawn, not written, so no text escapes the translation lookup. */
.toggle-icon {
  display: inline-block;
  width: 0;
  height: 0;
  border-block: 0.3em solid transparent;
  border-inline-start: 0.45em solid currentColor;
  transition: transform 0.1s;
}

.toggle-icon.open {
  transform: rotate(90deg);
}
</style>
