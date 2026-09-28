import type { BlockDefinition } from '../registry'
import ContainerBlock from './ContainerBlock.vue'

const definition: BlockDefinition = {
  type: 'container',
  labelKey: 'editor.blocks.container',
  icon: '▦',
  groups: [
    {
      labelKey: 'layout',
      props: [
        { prop: 'direction', control: 'choice' },
        { prop: 'stackOnMobile', control: 'toggle' },
        { prop: 'align', control: 'choice' },
        { prop: 'gap', control: 'spaceToken' },
        { prop: 'padding', control: 'spaceToken' },
      ],
    },
    { labelKey: 'style', props: [{ prop: 'background', control: 'colorToken' }] },
  ],
  initialProps: () => ({}),
  component: ContainerBlock,
}

export default definition
