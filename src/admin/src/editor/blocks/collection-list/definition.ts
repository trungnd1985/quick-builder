// The Collection list (016 research TB-07): a card repeated once per listed item, and an
// empty state. Its source, filters, sort keys and paging have their own controls.
import type { BlockDefinition } from '../registry'
import CollectionListBlock from './CollectionListBlock.vue'

const definition: BlockDefinition = {
  type: 'collection-list',
  labelKey: 'editor.blocks.collectionList',
  icon: '☰',
  groups: [
    {
      labelKey: 'content',
      props: [
        { prop: 'source', control: 'listSource' },
        { prop: 'filters', control: 'listFilters' },
        { prop: 'sort', control: 'listSort' },
        { prop: 'limit', control: 'number' },
        { prop: 'paging', control: 'listPaging' },
      ],
    },
    {
      labelKey: 'layout',
      props: [
        { prop: 'columns', control: 'number' },
        { prop: 'gap', control: 'spaceToken' },
      ],
    },
  ],
  // A list starts with no collection chosen; the panel asks for one before it can be saved.
  initialProps: () => ({ source: {} }),
  component: CollectionListBlock,
  // Inserted with its card and empty state by the list's own insertion (US4), not the palette.
  palette: false,
}

export default definition
