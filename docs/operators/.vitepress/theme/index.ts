import { h } from 'vue'
import DefaultTheme from 'vitepress/theme'
import VersionPicker from './VersionPicker.vue'

// 026 contracts/docs-publishing.md §4: the version picker in the navigation bar.
export default {
  extends: DefaultTheme,
  Layout: () => h(DefaultTheme.Layout, null, { 'nav-bar-content-after': () => h(VersionPicker) }),
}
