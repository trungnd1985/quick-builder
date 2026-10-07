import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import SchemaHistoryPanel from '../../src/features/cms/SchemaHistoryPanel.vue'
import type { SchemaHistory } from '../../src/api/cms'
import { COLLECTION, en, i18n, SITE, stubFetch, violations } from './harness'

// 014 US6, FR-033: a collection's revisions as sentences, newest first, with older ones on request.

const page = (entries: SchemaHistory['entries'], nextBefore: number | null): SchemaHistory => ({ entries, nextBefore })

describe('SchemaHistoryPanel', () => {
  it('words each kind of change and loads older revisions', async () => {
    const calls = stubFetch((call) => ({
      status: 200,
      body: call.url.includes('before=3')
        ? page([{ revision: 1, changedAtUtc: '2026-10-01T08:00:00Z', changedBy: { id: 'u', displayName: 'Dana' }, changes: [{ kind: 'created' }] }], null)
        : page([
          {
            revision: 4,
            changedAtUtc: '2026-10-07T08:00:00Z',
            changedBy: { id: 'u', displayName: 'Dana' },
            changes: [
              { kind: 'typeChanged', name: 'Stock', from: 'text', to: 'number', counts: { cleared: 3 } },
              { kind: 'requiredChanged', name: 'Title', to: 'required' },
              { kind: 'fieldRenamed', name: 'Unit price', from: 'Price', to: 'Unit price' },
            ],
          },
          { revision: 3, changedAtUtc: '2026-10-06T08:00:00Z', changedBy: { id: 'u', displayName: 'Lee' }, changes: [{ kind: 'fieldsReordered' }] },
        ], 3),
    }))

    const { container } = render(SchemaHistoryPanel, { props: { siteId: SITE, collectionId: COLLECTION, revision: 4 }, global: { plugins: [i18n] } })

    expect(await screen.findByText('Changed Stock from Text to Number')).toBeTruthy()
    expect(screen.getByText('Made Title required')).toBeTruthy()
    expect(screen.getByText('Renamed Price to Unit price')).toBeTruthy()
    expect(screen.getByText(en.cms.history.kind.fieldsReordered)).toBeTruthy()
    expect(await violations(container)).toEqual([])

    await fireEvent.click(screen.getByRole('button', { name: en.cms.history.loadMore }))
    await waitFor(() => expect(screen.getByText(en.cms.history.kind.created)).toBeTruthy())
    expect(calls[1]?.url).toContain('before=3')
    expect(screen.queryByRole('button', { name: en.cms.history.loadMore })).toBeNull()
  })
})
