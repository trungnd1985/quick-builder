<script setup lang="ts">
// A Navigation block's colour pairs below the contrast minimums (024 FR-017d), from its
// effective colours: its own overrides, else the site's menu style, else the built-in style.
// A warning only: nothing is refused.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ContentNode } from '../../schema/schema'
import { useEditorStore } from '../../state/editorStore'
import { effectiveMenuColors, menuContrastProblems } from '../../tokens/contrast'

const props = defineProps<{ node: ContentNode }>()
const { t } = useI18n()
const store = useEditorStore()

const problems = computed(() => menuContrastProblems(effectiveMenuColors(store.tokens, props.node.props as Record<string, unknown>)))
</script>

<template>
  <div
    v-if="problems.length > 0"
    role="status"
    class="editor-panel__warning"
  >
    <p>{{ t('design.menus.contrast.heading') }}</p>
    <ul>
      <li
        v-for="problem in problems"
        :key="problem.pair"
      >
        {{ t(`design.menus.contrast.pairs.${problem.pair}`) }}:
        {{ t('design.menus.contrast.ratio', { ratio: problem.ratio.toFixed(2), minimum: problem.minimum }) }}
      </li>
    </ul>
  </div>
</template>
