<script setup lang="ts">
// The site-wide menu style (024 FR-017a to FR-017d; contracts/menu-style.md): a look and eight
// colours, each following the built-in style until chosen. Every Navigation block that does
// not override a value follows these, once the design is published. Colour pairs below the
// contrast minimums are named here; nothing is refused.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { MENU_COLOR_KEYS, type MenuColorKey, type MenuLook, type MenuStyle, type TokenSet } from '../../editor/tokens/defaultTokens'
import { effectiveMenuColors, menuContrastProblems } from '../../editor/tokens/contrast'

const props = defineProps<{ tokens: TokenSet; readOnly: boolean }>()
const emit = defineEmits<{ change: [style: MenuStyle | undefined] }>()
const { t } = useI18n()

const LOOKS: MenuLook[] = ['link', 'underline', 'pill', 'button']

const look = computed<MenuLook>(() => props.tokens.menuStyle?.look ?? 'link')
const colorOf = (key: MenuColorKey): string => props.tokens.menuStyle?.colors?.[key] ?? ''
const problems = computed(() => menuContrastProblems(effectiveMenuColors(props.tokens, {})))

/** The style with one change applied; parts left empty are dropped, so only choices are saved. */
function changed(next: { look?: MenuLook; key?: MenuColorKey; id?: string }): void {
  const current = props.tokens.menuStyle ?? {}
  const colors: Partial<Record<MenuColorKey, string>> = { ...current.colors }
  if (next.key) {
    if (next.id) colors[next.key] = next.id
    else delete colors[next.key]
  }
  const nextLook = next.look ?? current.look
  const style: MenuStyle = {
    ...(nextLook && nextLook !== 'link' ? { look: nextLook } : {}),
    ...(Object.keys(colors).length > 0 ? { colors } : {}),
  }
  emit('change', Object.keys(style).length > 0 ? style : undefined)
}

const swatch = (id: string): string => props.tokens.colors.find((c) => c.id === id)?.value ?? 'transparent'
</script>

<template>
  <fieldset class="design-group menu-style">
    <legend>{{ t('design.menus.title') }}</legend>
    <p class="help">
      {{ t('design.menus.help') }}
    </p>

    <fieldset class="menu-style__looks">
      <legend>{{ t('design.menus.look') }}</legend>
      <label
        v-for="option in LOOKS"
        :key="option"
        class="menu-style__look"
      >
        <input
          type="radio"
          name="design-menu-look"
          :value="option"
          :checked="look === option"
          :disabled="readOnly"
          @change="changed({ look: option })"
        >
        {{ t(`design.menus.looks.${option}`) }}
      </label>
    </fieldset>

    <div class="menu-style__colors">
      <div
        v-for="key in MENU_COLOR_KEYS"
        :key="key"
        class="menu-style__color"
      >
        <span
          class="design-swatch"
          :style="{ background: colorOf(key) ? swatch(colorOf(key)) : 'transparent' }"
          aria-hidden="true"
        />
        <label :for="`design-menu-${key}`">{{ t(`design.menus.colors.${key}`) }}</label>
        <select
          :id="`design-menu-${key}`"
          :value="colorOf(key)"
          :disabled="readOnly"
          @change="changed({ key, id: ($event.target as HTMLSelectElement).value })"
        >
          <option value="">
            {{ t('design.menus.siteDefault') }}
          </option>
          <option
            v-for="color in tokens.colors"
            :key="color.id"
            :value="color.id"
          >
            {{ color.name }}
          </option>
          <option
            v-if="colorOf(key) && !tokens.colors.some((c) => c.id === colorOf(key))"
            :value="colorOf(key)"
          >
            {{ t('design.menus.missingColor') }}
          </option>
        </select>
      </div>
    </div>

    <div
      v-if="problems.length > 0"
      role="status"
      class="menu-style__contrast"
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
  </fieldset>
</template>

<style scoped>
.menu-style__looks {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  border: 0;
  padding: 0;
  margin: 0;
}

.menu-style__looks > legend {
  font-weight: 600;
  margin-bottom: 0.25rem;
}

.menu-style__look {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
}

.menu-style__colors {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr));
  gap: 0.5rem 1rem;
}

.menu-style__color {
  display: grid;
  grid-template-columns: auto 1fr;
  grid-template-rows: auto auto;
  align-items: center;
  column-gap: 0.5rem;
}

.menu-style__color > .design-swatch {
  grid-row: span 2;
}

.menu-style__contrast {
  border-inline-start: 4px solid #b45309;
  padding-inline-start: 0.75rem;
}

.menu-style__contrast ul {
  margin: 0;
  padding-inline-start: 1.25rem;
}
</style>
