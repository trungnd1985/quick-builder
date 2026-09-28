import type { BlockDefinition } from '../registry'
import SectionBlock from './SectionBlock.vue'

const definition: BlockDefinition = {
  type: 'section',
  labelKey: 'editor.blocks.section',
  icon: '▭',
  groups: [
    {
      labelKey: 'layout',
      props: [
        { prop: 'width', control: 'choice' },
        { prop: 'paddingY', control: 'spaceToken' },
      ],
    },
    { labelKey: 'style', props: [{ prop: 'background', control: 'colorToken' }] },
  ],
  initialProps: () => ({}),
  component: SectionBlock,
}

export default definition
