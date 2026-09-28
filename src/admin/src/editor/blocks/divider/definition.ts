import type { BlockDefinition } from '../registry'
import DividerBlock from './DividerBlock.vue'

const definition: BlockDefinition = {
  type: 'divider',
  labelKey: 'editor.blocks.divider',
  icon: '—',
  groups: [
    {
      labelKey: 'style',
      props: [
        { prop: 'thickness', control: 'choice' },
        { prop: 'color', control: 'colorToken' },
        { prop: 'spacing', control: 'spaceToken' },
      ],
    },
  ],
  initialProps: () => ({}),
  component: DividerBlock,
}

export default definition
