import type { BlockDefinition } from '../registry'
import NavigationBlock from './NavigationBlock.vue'

// 024 FR-016, FR-017: a menu of the site, chosen by id. A new block starts with the site's
// first menu; a site without menus cannot have one yet (initialProps returns null).
const definition: BlockDefinition = {
  type: 'navigation',
  labelKey: 'editor.blocks.navigation',
  icon: '☰',
  groups: [
    {
      labelKey: 'content',
      props: [
        { prop: 'menuId', control: 'menu' },
        { prop: 'levels', control: 'number' },
        { prop: 'ariaLabel', control: 'text' },
      ],
    },
    {
      labelKey: 'layout',
      props: [
        { prop: 'layout', control: 'choice' },
        { prop: 'align', control: 'choice' },
        { prop: 'collapse', control: 'navCollapse' },
      ],
    },
    {
      // 024 FR-017a to FR-017c: every style prop is unset by default and follows the site.
      labelKey: 'style',
      props: [
        { prop: 'look', control: 'menuLook' },
        { prop: 'color', control: 'menuColor' },
        { prop: 'activeColor', control: 'menuColor' },
        { prop: 'background', control: 'menuColor' },
        { prop: 'hoverColor', control: 'menuColor' },
        { prop: 'hoverBackground', control: 'menuColor' },
        { prop: 'activeBackground', control: 'menuColor' },
        { prop: 'focusColor', control: 'menuColor' },
        { prop: 'panelBackground', control: 'menuColor' },
      ],
    },
  ],
  initialProps: (_t, context) => (context.menuIds?.[0] ? { menuId: context.menuIds[0] } : null),
  component: NavigationBlock,
  missing: 'no_menus',
}

export default definition
