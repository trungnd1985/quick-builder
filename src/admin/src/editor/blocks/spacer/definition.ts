import type { BlockDefinition } from '../registry'
import SpacerBlock from './SpacerBlock.vue'

const definition: BlockDefinition = {
  type: 'spacer',
  labelKey: 'editor.blocks.spacer',
  icon: '↕',
  groups: [{ labelKey: 'layout', props: [{ prop: 'height', control: 'spaceToken' }] }],
  initialProps: () => ({}),
  component: SpacerBlock,
}

export default definition
