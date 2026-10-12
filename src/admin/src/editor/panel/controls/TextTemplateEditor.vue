<script setup lang="ts">
// A text made of fixed text and bound values, such as "By {Author → Name}, {Published on}"
// (016 FR-015, research TB-14). Each part is a row: fixed text is a text box, a value a chip
// naming its field. Parts are added at the end, moved up or down and removed with buttons, so
// the whole template is built and reordered from the keyboard alone. Up to 20 parts, of which
// up to 10 values (binding-expression.md §2).
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { isObject, type Json, type JsonObject } from '../../schema/subsetValidator'

interface Choice {
  key: string
  label: string
  value: JsonObject
}

const props = defineProps<{
  id: string
  parts: Json[]
  groups: { label: string; choices: Choice[] }[]
}>()
const emit = defineEmits<{ change: [parts: Json[]] }>()
const { t } = useI18n()

const MAX_PARTS = 20
const MAX_VALUES = 10

const adding = ref('')
const valueCount = computed(() => props.parts.filter(isObject).length)
const canAddText = computed(() => props.parts.length < MAX_PARTS)
const canAddValue = computed(() => props.parts.length < MAX_PARTS && valueCount.value < MAX_VALUES)

/** A value part's key, as the choices write theirs: everything but its format. */
function keyOf(part: JsonObject): string {
  const { format: _format, ...rest } = part as Record<string, Json>
  void _format
  return JSON.stringify(rest)
}

function labelOf(part: Json): string {
  if (!isObject(part)) return ''
  const key = keyOf(part)
  return props.groups.flatMap((g) => g.choices).find((c) => c.key === key)?.label ?? t('editor.binding.unknownField')
}

function update(next: Json[]): void {
  emit('change', next)
}

function setText(index: number, text: string): void {
  const next = [...props.parts]
  next[index] = text
  update(next)
}

function move(index: number, by: -1 | 1): void {
  const target = index + by
  if (target < 0 || target >= props.parts.length) return
  const next = [...props.parts]
  ;[next[index], next[target]] = [next[target], next[index]]
  update(next)
}

function remove(index: number): void {
  update(props.parts.filter((_, i) => i !== index))
}

function addText(): void {
  if (canAddText.value) update([...props.parts, ''])
}

function addValue(): void {
  const choice = props.groups.flatMap((g) => g.choices).find((c) => c.key === adding.value)
  if (!choice || !canAddValue.value) return
  update([...props.parts, choice.value])
  adding.value = ''
}
</script>

<template>
  <div class="text-template">
    <ol
      class="text-template__parts"
      :aria-label="t('editor.binding.template.parts')"
    >
      <li
        v-for="(part, index) in parts"
        :key="index"
        class="text-template__part"
      >
        <template v-if="typeof part === 'string'">
          <label
            :for="`${id}-text-${index}`"
            class="visually-hidden"
          >{{ t('editor.binding.template.text', { n: index + 1 }) }}</label>
          <input
            :id="`${id}-text-${index}`"
            type="text"
            maxlength="500"
            :value="part"
            @change="setText(index, ($event.target as HTMLInputElement).value)"
          >
        </template>
        <span
          v-else
          class="chip"
          :aria-label="t('editor.binding.template.value', { n: index + 1, field: labelOf(part) })"
        >{{ labelOf(part) }}</span>
        <button
          type="button"
          class="link"
          :disabled="index === 0"
          :aria-label="t('editor.binding.template.moveUp', { n: index + 1 })"
          @click="move(index, -1)"
        >
          {{ t('editor.binding.template.up') }}
        </button>
        <button
          type="button"
          class="link"
          :disabled="index === parts.length - 1"
          :aria-label="t('editor.binding.template.moveDown', { n: index + 1 })"
          @click="move(index, 1)"
        >
          {{ t('editor.binding.template.down') }}
        </button>
        <button
          type="button"
          class="link"
          :aria-label="t('editor.binding.template.remove', { n: index + 1 })"
          @click="remove(index)"
        >
          {{ t('editor.binding.template.removeShort') }}
        </button>
      </li>
    </ol>

    <div class="text-template__add">
      <button
        type="button"
        :disabled="!canAddText"
        @click="addText"
      >
        {{ t('editor.binding.template.addText') }}
      </button>
      <label
        :for="`${id}-add`"
        class="visually-hidden"
      >{{ t('editor.binding.template.chooseValue') }}</label>
      <select
        :id="`${id}-add`"
        v-model="adding"
        :disabled="!canAddValue"
      >
        <option value="">
          {{ t('editor.binding.template.chooseValue') }}
        </option>
        <optgroup
          v-for="group in groups"
          :key="group.label"
          :label="group.label"
        >
          <option
            v-for="choice in group.choices"
            :key="choice.key"
            :value="choice.key"
          >
            {{ choice.label }}
          </option>
        </optgroup>
      </select>
      <button
        type="button"
        :disabled="!adding || !canAddValue"
        @click="addValue"
      >
        {{ t('editor.binding.template.addValue') }}
      </button>
    </div>
  </div>
</template>
