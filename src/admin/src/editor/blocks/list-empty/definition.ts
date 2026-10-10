// A Collection list's empty state (016 research TB-07): shown instead of cards when the list
// has nothing to show. It exists only inside its list, so it is never in the palette.
import type { BlockDefinition } from '../registry'
import ListEmptyBlock from './ListEmptyBlock.vue'

const definition: BlockDefinition = {
  type: 'list-empty',
  labelKey: 'editor.blocks.listEmpty',
  icon: '∅',
  groups: [],
  initialProps: () => ({}),
  component: ListEmptyBlock,
  palette: false,
}

export default definition
