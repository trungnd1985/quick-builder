import 'fake-indexeddb/auto'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { clearRecoveryForUser, findRecovery, recoveryOffer, removeRecovery, writeRecovery } from '../../src/editor/state/recovery'
import type { ContentDocument } from '../../src/editor/schema/schema'

// 011 clarification Q5, FR-030, research ED-19: unsaved changes are kept in the browser and
// offered on the next visit — restore when the draft is unchanged since, read-only copying
// when it has changed — never applied without asking, and gone on save, decline or sign-out.

const doc = (text: string): ContentDocument => ({
  schemaVersion: 1,
  root: { id: 'root', type: 'root', props: {}, children: [{ id: 's', type: 'section', props: {}, children: [{ id: 'h', type: 'heading', props: { text }, children: [] }] }] },
})

const key = { userId: 'u1', siteId: 's1', kind: 'page' as const, documentId: 'p1' }

describe('recovery copies', () => {
  beforeEach(async () => {
    await clearRecoveryForUser('u1')
    await clearRecoveryForUser('u2')
  })
  afterEach(async () => {
    await clearRecoveryForUser('u1')
  })

  it('keeps a copy and gives it back to the same user, for the same document', async () => {
    await writeRecovery({ ...key, document: doc('unsaved'), baseVersion: 7 })

    const found = await findRecovery(key)
    expect(found?.document).toEqual(doc('unsaved'))
    expect(found?.baseVersion).toBe(7)
    expect(await findRecovery({ ...key, userId: 'u2' })).toBeNull()
    expect(await findRecovery({ ...key, documentId: 'p2' })).toBeNull()
  })

  it('offers Restore when the saved draft has not changed since', async () => {
    await writeRecovery({ ...key, document: doc('unsaved'), baseVersion: 7 })

    expect(await recoveryOffer(key, { version: 7, root: doc('saved').root })).toMatchObject({ kind: 'restore' })
  })

  it('offers only read-only copying when the saved draft changed since', async () => {
    await writeRecovery({ ...key, document: doc('unsaved'), baseVersion: 7 })

    expect(await recoveryOffer(key, { version: 8, root: doc('theirs').root })).toMatchObject({ kind: 'read-only' })
  })

  it('offers nothing when the copy is what is already saved', async () => {
    await writeRecovery({ ...key, document: doc('same'), baseVersion: 7 })

    expect(await recoveryOffer(key, { version: 7, root: doc('same').root })).toBeNull()
    expect(await findRecovery(key)).toBeNull()
  })

  it('is removed when declined or saved', async () => {
    await writeRecovery({ ...key, document: doc('unsaved'), baseVersion: 7 })
    await removeRecovery(key)

    expect(await findRecovery(key)).toBeNull()
  })

  it('is removed for exactly one user on sign-out', async () => {
    await writeRecovery({ ...key, document: doc('mine'), baseVersion: 7 })
    await writeRecovery({ ...key, userId: 'u2', document: doc('theirs'), baseVersion: 7 })

    await clearRecoveryForUser('u1')

    expect(await findRecovery(key)).toBeNull()
    expect(await findRecovery({ ...key, userId: 'u2' })).not.toBeNull()
  })
})
