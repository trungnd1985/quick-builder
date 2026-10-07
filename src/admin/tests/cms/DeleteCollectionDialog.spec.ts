import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import DeleteCollectionDialog from '../../src/features/cms/DeleteCollectionDialog.vue'
import { COLLECTION, en, i18n, problem, SITE, stubFetch, violations } from './harness'

// 014 US5, FR-004: deleting a collection asks for its name, says how many items go with it and
// that it cannot be undone, and lists the fields that refer to it when refused.

function renderDialog() {
  return render(DeleteCollectionDialog, {
    props: { siteId: SITE, collectionId: COLLECTION, name: 'Job openings', revision: 4, itemCount: 14 },
    global: { plugins: [i18n] },
  })
}

describe('DeleteCollectionDialog', () => {
  it('enables delete only for the exact name and sends the revision', async () => {
    const calls = stubFetch(() => ({ status: 204 }))
    const { emitted, container } = renderDialog()

    expect(screen.getByText(/all 14 items, draft and published\. It cannot be undone\./)).toBeTruthy()
    expect(await violations(container)).toEqual([])
    const button = screen.getByRole('button', { name: en.cms.delete.confirm })
    expect(button).toHaveProperty('disabled', true)

    await fireEvent.update(screen.getByLabelText(en.cms.delete.typeName.replace('{name}', 'Job openings')), 'Job opening')
    expect(button).toHaveProperty('disabled', true)
    await fireEvent.update(screen.getByLabelText(en.cms.delete.typeName.replace('{name}', 'Job openings')), 'Job openings')
    await fireEvent.click(button)

    await waitFor(() => expect(emitted().deleted).toHaveLength(1))
    expect(calls[0]).toMatchObject({ method: 'DELETE', url: `/api/v1/cms/sites/${SITE}/collections/${COLLECTION}?revision=4` })
  })

  it('lists the reference fields that stand in the way', async () => {
    stubFetch(() => problem(409, 'cms.collection_referenced', {
      references: [{ collectionId: 'c2', collectionName: 'Team members', fieldId: 'f2', fieldName: 'Opening' }],
    }))
    const { emitted } = renderDialog()

    await fireEvent.update(screen.getByLabelText(en.cms.delete.typeName.replace('{name}', 'Job openings')), 'Job openings')
    await fireEvent.click(screen.getByRole('button', { name: en.cms.delete.confirm }))

    expect(await screen.findByText('Opening in Team members')).toBeTruthy()
    expect(screen.getByText(en.cms.delete.referenced)).toBeTruthy()
    expect(emitted().deleted).toBeUndefined()
  })
})
