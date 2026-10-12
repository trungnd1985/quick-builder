// A Collection list's card (016 research TB-07): the blocks repeated once per listed item,
// bound to that item. It exists only inside its list, so it is never in the palette.
import type { BlockDefinition } from '../registry'
import ListCardBlock from './ListCardBlock.vue'

const definition: BlockDefinition = {
  type: 'list-card',
  labelKey: 'editor.blocks.listCard',
  icon: '▭',
  groups: [],
  initialProps: () => ({}),
  component: ListCardBlock,
  palette: false,
}

export default definition
