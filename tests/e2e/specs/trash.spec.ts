// US2 scenario 2, FR-013: a deleted page comes back from the trash under its old parent, with
// its draft intact.
import { headingDocument } from '../fixtures/api.js'
import { test } from '../fixtures/index.js'
import { PageTreeScreen } from '../pages/page-tree.js'
import { TrashScreen } from '../pages/trash.js'

test('a deleted page is restored from the trash with its draft', async ({ page, kind, scan, seed }) => {
  const site = await seed.site('Trash')
  const team = await seed.page(site, { title: 'Team', slug: 'team' })
  const alice = await seed.page(site, { title: 'Alice', slug: 'alice', parentId: team.id })
  await seed.draft(alice, headingDocument('Alice draft'))

  const tree = new PageTreeScreen(page, kind, scan)
  await tree.open(site)
  await tree.deletePage('Alice')

  const trash = new TrashScreen(page, kind, scan)
  await trash.open(site)
  await trash.restore('Alice')

  await tree.open(site)
  await tree.expectPage('Alice', 'Team')
  const editor = await tree.openInEditor('Alice')
  await editor.expectOnCanvas('Alice draft')
})
